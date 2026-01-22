using System;
using System.Collections.Generic;
using System.Drawing; // 用于 Bitmap, Rectangle
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Bloghua.AutoClient.Core.Entities;
using Bloghua.AutoClient.Core.Enums;
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core.Models;
using Bloghua.AutoClient.Infrastructure.Data;
using Bloghua.AutoClient.Infrastructure.Image;
using Bloghua.AutoClient.Infrastructure.Services;

// 解决 Point 和 Rect 的命名空间冲突
using WinRect = System.Windows.Rect;
using DrawingPoint = System.Drawing.Point;
using Bloghua.AutoClient.Core;
using System.Windows;

namespace Bloghua.AutoClient.Services
{
    public class WeChatVisualService
    {
        #region Fields & Dependencies

        private readonly IUIAutomationService _uia;
        private readonly IOcrService _ocr;
        private readonly IInputSimulator _input;
        private readonly IImageLocator _cv;
        private readonly ILoggerService _logger;
        private readonly ChatApiService _api;
        private readonly DatabaseService _db;
        private readonly StickerService _stickerService;
        private readonly WeChatAutoPilot _autoPilot; // 全自动模块

        // 【新增】全局忙碌锁：true = 正在等待 API，暂停一切扫描
        private bool _isProcessing = false;

        // 【新增】当前正在服务的对象名称 (用于粘贴时的安全校验)
        private string _processingUserName = "";

        // 状态缓存：用于防止同一条消息重复请求 API (SessionKey -> LastMsgContent)
        // 即使程序重启，建议也从数据库加载一部分历史防止重复，这里仅做内存级防抖
        private Dictionary<string, string> _history = new Dictionary<string, string>();

        // 瞬时状态：用于极短时间内的循环防抖
        private string _lastProcessedUser = "";
        private string _lastProcessedMsg = "";

        // 缓存防止 UI 频繁刷新建议
        private string _lastSuggestedMsgContent = "";

        #endregion

        #region Events

        // 【修改】事件签名改为两个参数：(API回复, 用户原始消息)
        public event Action<string, string> OnSuggestionReady;



        // 状态变更 (UI 更新红绿灯)
        //public event Action<WorkStatus> OnStatusChanged;
        // 增加 string 参数用于传递上下文内容
        public event Action<WorkStatus, string> OnStatusChanged;

        // 聊天对象变更 (UI 更新当前窗口标题)
        public event Action<string> OnChatTargetChanged;

        // 【新增】专门用于 UI 显示的日志事件
        public event Action<string> OnLog;

        #endregion

        #region Constructor

        public WeChatVisualService(
            IUIAutomationService uia,
            IOcrService ocr,
            IInputSimulator input,
            IImageLocator cv,
            ILoggerService logger,
            DatabaseService db,
            StickerService stickerService)
        {
            _uia = uia;
            _ocr = ocr;
            _input = input;
            _cv = cv;
            _logger = logger;
            _db = db;
            _stickerService = stickerService ?? new StickerService();
            _api = new ChatApiService();

            // 初始化全自动驾驶员 (虽然主要用辅助模式，但保留入口)
            _autoPilot = new WeChatAutoPilot(input, ocr, logger, uia);
        }

        #endregion

        #region Public Methods (Main Loop)

        /// <summary>
        /// 主循环：由 UI 定时器调用
        /// </summary>
        public async Task RunCycleAsync()
        {

            //【核心修复 1】如果正在忙（等待API），直接退出，绝不扫描新窗口
            if (_isProcessing)
            {
                // 可选：通知 UI 正在忙
                // OnStatusChanged?.Invoke(WorkStatus.Processing);
                return;
            }

            try
            {
                // 读取配置：是否主动激活窗口
                bool autoActive = _db.GetSetting("AutoActiveWindow", "true") == "true";
                WinRect winRect;

                // 1. 窗口状态检查
                if (autoActive)
                {
                    // A. 主动模式：强制激活并归位
                    if (!_uia.AttachToWeChat())
                    {
                        SetStatus(WorkStatus.Idle);
                        return;
                    }
                    await Task.Delay(50); // 等待重绘
                    winRect = _uia.GetWindowBounds();
                }
                else
                {
                    // B. 被动模式：只读状态
                    bool isAvailable = _uia.RefreshWindowStatus(out winRect);
                    if (!isAvailable)
                    {
                        SetStatus(WorkStatus.Idle);
                        OnChatTargetChanged?.Invoke("微信未启动或已最小化");
                        return;
                    }
                }

                // 2. 视觉分析
                using (Bitmap fullWindow = CaptureScreen(winRect))
                {
                    // A. 检查是否在"聊天"Tab
                    Rectangle sidebarRect = new Rectangle(26, 95, 3, 3);
                    if (!VisualHelper.HasGreenColor(fullWindow, sidebarRect))
                    {
                        SetStatus(WorkStatus.Idle);
                        if (autoActive)
                        {
                            // 主动归位
                            _input.Click((int)winRect.X + 26, (int)winRect.Y + 95);
                            _input.Click((int)winRect.X + 900, (int)winRect.Y + 750);
                            return;
                        }
                        else
                        {
                            OnChatTargetChanged?.Invoke("⚠️ 聊天界面被遮挡或未激活");
                            return;
                        }
                    }

                    // B. 分析布局
                    int splitX = VisualHelper.FindVerticalSplitLine(fullWindow);

                    // C. 识别标题
                    Rectangle rectTitle = new Rectangle(splitX + 10, 0, fullWindow.Width - splitX - 200, 60);
                    string currentTitle = await CropAndOcr(fullWindow, rectTitle, false);
                    currentTitle = currentTitle?.Replace("\n", "")?.Trim();

                    OnChatTargetChanged?.Invoke(currentTitle);

                    if (string.IsNullOrEmpty(currentTitle))
                    {
                        SetStatus(WorkStatus.Idle);
                        return;
                    }

                    // 3. 权限与模式判断
                    bool isAutoMode = _db.IsAutoSend();
                    AuthorizedTarget targetUser = CheckAuthUser(currentTitle);

                    if (isAutoMode)
                    {
                        // --- 全自动模式 ---
                        if (targetUser != null)
                        {
                            // 已在目标窗口，处理消息
                            SetStatus(WorkStatus.Scanning);
                            await ProcessChatContent(fullWindow, winRect, splitX, targetUser);
                        }
                        else
                        {
                            // 陌生人或非目标，全自动模式下尝试去列表找人
                            // (此处调用 AutoPilot，如果不想启用巡逻，可直接 return)
                            _logger.Log($"[全自动] 当前非目标 [{currentTitle}]，呼叫巡逻...");
                            SetStatus(WorkStatus.Scanning);
                            await _autoPilot.FindAndClickTargetAsync(winRect, splitX, _db.GetActiveTargets("WeChat"));
                            return;
                        }
                    }
                    else
                    {
                       
                        // --- 辅助模式 (默认) ---
                        // 任何窗口都处理，但在库里的优先使用库配置(BizId)
                        if (targetUser == null)
                        {
                            targetUser = new AuthorizedTarget
                            {
                                Name = currentTitle,
                                BusinessId = "" // 陌生人
                            };
                        }

                        // 简单的黑名单过滤
                        if (IsSystemEntrySimplified(targetUser.Name))
                        {
                            _logger.Log($"辅助模式: {targetUser.Name}是黑名单");

                        }
                        else
                        {
                            SetStatus(WorkStatus.Scanning);
                            await ProcessChatContent(fullWindow, winRect, splitX, targetUser);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("辅助循环异常", ex);
            }
            finally
            {
                SetStatus(WorkStatus.Idle);
            }
        }

        /// <summary>
        /// 将建议粘贴到微信 (UI 调用)
        /// </summary>
        /// <summary>
        /// 安全粘贴：粘贴前再次确认当前窗口是不是那个人
        /// </summary>
        public async Task PasteSuggestionToWeChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 1. 激活窗口
            if (!_uia.AttachToWeChat()) return;
            var winRect = _uia.GetWindowBounds();

            // 2. 【核心修复 4】安全校验：当前窗口是谁？
            // 必须重新截图 OCR 标题，防止用户切到了别的窗口
            using (Bitmap bmp = CaptureScreen(winRect))
            {
                int splitX = VisualHelper.FindVerticalSplitLine(bmp);
                Rectangle rectTitle = new Rectangle(splitX + 10, 0, bmp.Width - splitX - 200, 60);
                string currentTitle = await CropAndOcr(bmp, rectTitle, false);
                currentTitle = currentTitle?.Replace("\n", "")?.Trim();

                // 只有当 当前标题 包含 之前请求时的名字 (或反之) 时，才允许粘贴
                // 比如请求时是 "张三"，现在标题是 "张三(手机在线)"，允许通过
                if (string.IsNullOrEmpty(currentTitle) ||
                   (!currentTitle.Contains(_processingUserName) && !_processingUserName.Contains(currentTitle)))
                {
                    _logger.Log($"[安全拦截] 禁止填入！当前窗口[{currentTitle}] 与建议对象[{_processingUserName}] 不一致。");

                    // 这里可以抛个事件通知 UI 弹窗警告，或者直接返回
                    // 为了简单，我们让日志记录并在 UI 显示（UI 需要订阅 Log）
                    MessageBox.Show($"严重警告：\n当前聊天对象是【{currentTitle}】\n但这条建议是给【{_processingUserName}】的！\n\n已拦截粘贴操作。", "安全保护");
                    return;
                }

                // 校验通过，执行粘贴
                await SendReplyInternal(bmp, winRect, text, false);
                _logger.Log($"已填入回复: {text.Substring(0, Math.Min(10, text.Length))}...");
            }
        }


        #endregion


        // 辅助方法：统一日志出口
        private void LogInfo(string msg)
        {
            // 1. 写文件
            _logger.Log(msg);
            // 2. 通知 UI
            OnLog?.Invoke(msg);
        }

        #region Private Core Logic

        private async Task ProcessChatContent(Bitmap fullBmp, WinRect winRect, int splitX, AuthorizedTarget user)
        {
            // 1. 定位消息区域
            int topY = VisualHelper.FindHorizontalLineDebug(fullBmp, splitX, 50, 90, "TOP");
            if (topY == -1) topY = 60;



            // =============================================================
            // 2. 【核心修复】定位底部横线 (输入框上沿)
            // =============================================================
            int bottomY = -1;

            // 策略 A: 优先使用"笑脸图标"定位 (最稳健)
            // 只要找到了笑脸，分割线肯定在笑脸上面一点点
            var smileLoc = _cv.FindImageCenter(fullBmp, "Images/icon_smile.png");

            if (smileLoc.HasValue)
            {
                // 经过测量，分割线通常在笑脸中心上方约 22-28 像素处
                // 我们取 25
                bottomY = smileLoc.Value.Y - 25;
                 _logger.Log($"通过锚点定位底部线: {bottomY}");
            }
            else
            {
                // 策略 B: 如果没找到笑脸 (比如被遮挡)，采用大范围"从下往上"扫描
                // 既然输入框大小可变，我们就在 splitX 右侧，从底部向上扫，找第一条贯穿的长灰线

                // 扫描范围：从底部-100 开始，一直扫到顶部+100
                int scanStart = fullBmp.Height - 80; // 避开最底部的发送按钮区
                int scanEnd = topY + 50;

                // 我们需要一个新的 Helper 方法：FindHorizontalLineFromBottom (从下往上)
                // 如果没有这个方法，暂时用原来的，但扩大范围
                bottomY = VisualHelper.FindHorizontalLineDebug(fullBmp, splitX, 200, fullBmp.Height - 100, "BOTTOM_WIDE");
                _logger.Log($"通过FindHorizontalLineFromBottom定位底部线: {bottomY}");

            }

            // 兜底：如果还是没找到，或者位置极其不合理
            if (bottomY == -1 || bottomY < topY + 50 || bottomY > fullBmp.Height - 50)
            {
                // 这种情况下通常是输入框拉得太高或太低，或者截图失败
                // 默认给一个大概值，或者直接 return 避免乱发
                _logger.Log("⚠️ 无法确定消息区底部边界，使用默认值。");
                bottomY = fullBmp.Height - 360;
            }


            // int searchStart = fullBmp.Height - 450;
            // int searchEnd = fullBmp.Height - 150;

            // =============================================================
            // 3. 计算矩形
            // =============================================================
            int msgX = splitX + 60;
            int msgW = fullBmp.Width - msgX - 60;
            int msgH = bottomY - topY - 5; // 稍微留点空隙

            if (msgH < 20) return; // 区域太小

            Rectangle rectMsg = new Rectangle(msgX, topY + 5, msgW, msgH);


           

            // 4. 提取最后一条气泡
            string msgContent = "";

            using (Bitmap msgAreaBmp = fullBmp.Clone(rectMsg, fullBmp.PixelFormat))
            {

                // 保存这张图，看看截到了什么
                try { msgAreaBmp.Save("debug_chat_area.png"); } catch { }


                var bubble = VisualHelper.FindLastBubble(msgAreaBmp);

                // 没气泡或是我发的(绿色)，跳过
                if (bubble == null || bubble.Type == VisualHelper.BubbleType.Sent) return;

                using (Bitmap bubbleBmp = msgAreaBmp.Clone(bubble.Rect, msgAreaBmp.PixelFormat))
                {
                    // 图片/表情包识别
                    if (!VisualHelper.IsTextBubble(bubbleBmp))
                    {
                        string stickerMeaning = _stickerService.MatchSticker(bubbleBmp);
                        if (!string.IsNullOrEmpty(stickerMeaning))
                            msgContent = stickerMeaning;
                        else
                            return; // 未知图片跳过
                    }
                    else
                    {
                        // 文本识别
                        msgContent = await _ocr.RecognizeTextAsync(bubbleBmp);
                        msgContent = msgContent?.Trim();
                        if (string.IsNullOrWhiteSpace(msgContent) ||
                           (msgContent.Length < 2 && !char.IsLetterOrDigit(msgContent[0]))) return;
                    }
                }
            }

            // 【需求2】系统日志必须显示读取到的内容 (移到最前面)
            // 只要识别出来了，哪怕是重复的，建议也打个日志(Debug级)，证明程序活着
            // 但为了清爽，我们只在"非重复"时打 Info 日志
            if (_lastProcessedUser != user.Name || _lastProcessedMsg != msgContent)
            {
                _logger.Log($"[读取消息] 对象:{user.Name} 内容:{msgContent}");
            }


            // 3. 严格防重检查
            // 如果内存中记录的上一条就是这个，直接返回 (防止每3秒刷一次API)
            if (_lastProcessedUser == user.Name && _lastProcessedMsg == msgContent) return;

            // 检查历史记录 (是否已回复过)
            if (IsDuplicateMsg(user.Name, msgContent))
            {
                // 虽然已回复，但如果是新扫描到的，打印一下日志证明读到了
                // _logger.Log($"[已处理] {msgContent}");

                // 更新一下瞬时状态，防止下个循环继续进这里检查
                _lastProcessedUser = user.Name;
                _lastProcessedMsg = msgContent;
                return;
            }

            // =========================================================
            // 【核心修复 2】上锁！停止一切扫描
            // =========================================================
            _isProcessing = true;
            _processingUserName = user.Name; // 记住我们正在为谁服务

            _logger.Log($"[OCR读取] {user.Name}: {msgContent}");
            LogInfo($"[获取消息] 对象: {user.Name}, 内容: {msgContent}");

            SetStatus(WorkStatus.Processing,msgContent);

            try
            {
                // 4. 生成回复 (DB -> API)
                string replyContent = _db.FindBestMatchAnswer(msgContent, "WeChat");
                string source = "本地库";

                if (string.IsNullOrEmpty(replyContent))
                {
                    source = "AI大模型";
                    LogInfo($"[处理中] 正在请求 API 处理...");
                    _logger.Log($"[DEBUG] 准备调用 API. AppId: ... Session: {user.Name}");


                    // 【核心修改】获取该用户绑定的角色
                    string sessionKey = $"{user.Name}_{user.BusinessId}";
                    string roleCode = _db.GetUserRole(sessionKey);

                    // 将 roleCode 传给 API
                    replyContent = await _api.GetReplyAsync(sessionKey, msgContent, roleCode);

                    _logger.Log($"[DEBUG] API原始响应: {replyContent}");

                }

                if (string.IsNullOrEmpty(replyContent)) return;

                // 5. 更新状态
                _lastProcessedUser = user.Name;
                _lastProcessedMsg = msgContent;

                // 记录日志 (不管发没发，建议都生成了)
                _db.LogChat(user.Name, "WeChat", msgContent, replyContent, 0);

                // 6. 执行动作
                bool isAutoMode = _db.IsAutoSend();
                if (isAutoMode)
                {
                    SetStatus(WorkStatus.Sending);
                    _logger.Log($"[{source}] 自动回复中...");
                    await SendReplyInternal(fullBmp, winRect, replyContent, true);
                    RecordMsg(user.Name, msgContent); // 标记为已完成
                }
                else
                {
                    // 辅助模式
                    _logger.Log($"[{source}] 建议已生成 (所属对象: {user.Name})");
                    //OnSuggestionReady?.Invoke(replyContent);
                    // 【修改】调用 Invoke 时传入两个参数：回复内容，用户提问内容
                    OnSuggestionReady?.Invoke(replyContent, msgContent);

                    LogInfo($"[处理完成] 建议已生成");
                }

            }
            finally
            {
                // 【核心修复 3】无论成功失败，必须解锁，否则程序死锁
                _isProcessing = false;
                SetStatus(WorkStatus.Idle);
            }
        }

        #endregion

        #region Helper Methods (Fixed CS0103 Errors)

        // 【修复】检查用户是否在授权名单
        private AuthorizedTarget CheckAuthUser(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            var targets = _db.GetActiveTargets("WeChat");
            return targets.FirstOrDefault(u => title.Contains(u.Name));
        }

        // 【修复】检查消息是否已处理 (防重)
        private bool IsDuplicateMsg(string user, string msg)
        {
            if (_history.ContainsKey(user) && _history[user] == msg) return true;
            return false;
        }

        // 【修复】记录消息已处理
        private void RecordMsg(string user, string msg)
        {
            if (_history.ContainsKey(user))
                _history[user] = msg;
            else
                _history.Add(user, msg);
        }

        // 【修复】内部发送逻辑 (点击 -> 粘贴 -> 可选回车)
        private async Task SendReplyInternal(Bitmap fullBmp, WinRect winRect, string text, bool sendEnter)
        {
            // 尝试找笑脸作为锚点
            var smileLoc = _cv.FindImageCenter(fullBmp, "Images/icon_smile.png");

            int inputX = (int)winRect.X + 600; // 默认位置
            int inputY = (int)winRect.Y + 700;

            if (smileLoc.HasValue)
            {
                inputX = (int)winRect.X + smileLoc.Value.X + 80;
                inputY = (int)winRect.Y + smileLoc.Value.Y;
            }

            _input.Click(inputX, inputY);
            await Task.Delay(50);
            _input.PasteText(text);

            if (sendEnter)
            {
                await Task.Delay(100);
                _input.SendEnter();
            }

            // 移开鼠标
            _input.Click((int)winRect.X + 900, (int)winRect.Y + 750);
        }

        // 简单的系统号过滤
        private bool IsSystemEntrySimplified(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            string[] blackList = { "客服消息", "服务通知", "订阅号", "文件传输助手", "微信团队", "微信支付", "服务号", "公众号"};
            return blackList.Any(k => name.Contains(k));
        }

        private void SetStatus(WorkStatus status, string context = "")
        {
            OnStatusChanged?.Invoke(status, context);
        }

        private async Task<string> CropAndOcr(Bitmap original, Rectangle roi, bool useDarkFilter)
        {
            if (roi.Width <= 0 || roi.Height <= 0) return "";
            using (Bitmap crop = original.Clone(roi, original.PixelFormat))
            {
                if (useDarkFilter)
                {
                    using (Bitmap filtered = VisualHelper.KeepDarkTextOnly(crop))
                    {
                        return await _ocr.RecognizeTextAsync(filtered);
                    }
                }
                return await _ocr.RecognizeTextAsync(crop);
            }
        }

        private Bitmap CaptureScreen(WinRect rect)
        {
            var bmp = new Bitmap((int)rect.Width, (int)rect.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, bmp.Size);
            }
            return bmp;
        }

        private DrawingPoint GetCenter(Rectangle r)
        {
            return new DrawingPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        #endregion
    }
}