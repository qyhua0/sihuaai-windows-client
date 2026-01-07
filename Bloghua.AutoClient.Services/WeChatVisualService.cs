using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core.Models;
using Bloghua.AutoClient.Core;
using Bloghua.AutoClient.Infrastructure.Image; 
using System.Collections.Concurrent;

using System.Diagnostics; // 用于 Stopwatch
using Bloghua.AutoClient.Infrastructure.Data; // 引用数据库服务
using Bloghua.AutoClient.Core.Entities;
using Bloghua.AutoClient.Core.Enums; // 引用枚举
using Bloghua.AutoClient.Infrastructure.Services;
using System.Windows;
using System.Collections.Generic; // 用于 HashSet



namespace Bloghua.AutoClient.Services
{
    public class WeChatVisualService
    {
        private readonly IUIAutomationService _uia;
        private readonly IOcrService _ocr;
        private readonly IInputSimulator _input;
        private readonly IImageLocator _cv;
        private readonly ILoggerService _logger;
        private readonly ChatApiService _api;
       // private AppConfig _config;
        private readonly DatabaseService _db;
        private readonly StickerService _stickerService; // 新增



        // 用户忙碌状态锁
        // Key: 用户名_业务ID (SessionKey), Value: 是否正在处理中
        private static ConcurrentDictionary<string, bool> _processingFlags = new ConcurrentDictionary<string, bool>();

        // ==========================================
        // 区域划分定义 (基于 1000x800 窗口)
        // ==========================================
        // 列表区：左边栏(60) ~ 中栏结束(350)
        private const int ZONE_LIST_MIN_X = 60;
        private const int ZONE_LIST_MAX_X = 350;

        // 标题区：右侧顶部 (放大一点范围，防止名字太长被切)
        private readonly Rectangle Rect_Title = new Rectangle(350, 0, 600, 80);

        // 消息扫描区：右侧中间 (避开顶栏和底部输入框)
        // 底部留 200px 给输入框，顶部留 80px 给标题
        private readonly Rectangle Rect_MsgArea = new Rectangle(350, 80, 640, 520);

        public WeChatVisualService(
            IUIAutomationService uia,
            IOcrService ocr,
            IInputSimulator input,
            IImageLocator cv,
            ILoggerService logger,
            DatabaseService db, StickerService stickerService)
        {
            _uia = uia; _ocr = ocr; _input = input; _cv = cv; _logger = logger;
            _api = new ChatApiService();

            _db = db;
            _stickerService = stickerService ?? new StickerService();
            // LoadConfig();

        }


        /*
        private void LoadConfig()
        {
            if (File.Exists("config.json"))
            {
                _config = JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText("config.json"));
            }
            else
            {
                _config = new AppConfig();
            }

        }*/

        // 状态变更事件
        public event Action<WorkStatus> OnStatusChanged;

        // 辅助方法：触发状态
        private void SetStatus(WorkStatus status)
        {
            OnStatusChanged?.Invoke(status);
        }


       

       


        public async Task RunCycleAsync()
        {
            try
            {
                // 设置状态为扫描中
                SetStatus(WorkStatus.Scanning);

                if (!_uia.AttachToWeChat())
                {
                    SetStatus(WorkStatus.Idle);
                    return;
                }

                // 等待一下窗口刷新
                await Task.Delay(100);

                // 获取窗口位置
                var winRect = _uia.GetWindowBounds();

                // 1. 获取原始全屏截图
                using (Bitmap fullWindow = CaptureScreen(winRect))
                {
                    // ========================================================
                    // 检查左侧 "聊天" 图标是否被选中 (保留您调试好的坐标)
                    // ========================================================
                    Rectangle sidebarRect = new Rectangle(26, 95, 3, 3);

                    bool isChatTabSelected = VisualHelper.HasGreenColor(fullWindow, sidebarRect);

                    if (!isChatTabSelected)
                    {
                        _logger.Log("检测到 [聊天] 图标未选中，执行点击归位。");

                        // 点击侧边栏第一个图标
                        int chatIconX = (int)winRect.X + 26;
                        int chatIconY = (int)winRect.Y + 95;

                        _input.Click(chatIconX, chatIconY);
                        _input.Click((int)winRect.X + 900, (int)winRect.Y + 750); // 移开鼠标

                        // 点击后直接返回，等待下一轮
                        return;
                    }

                    // ========================================================
                    // 正常的业务流程
                    // ========================================================

                    // 2. 动态寻找分割线
                    int splitX = VisualHelper.FindVerticalSplitLine(fullWindow);
                    // _logger.Log($"检测到分割线位置: X={splitX}");

                    // 3. 截取标题区
                    // 注意：这里的坐标如果之前测试准确，请保持；如果标题太长被切，可适当加宽
                    Rectangle rectTitle = new Rectangle(splitX + 10, 0, fullWindow.Width - splitX - 200, 60);
                    string currentTitle = await CropAndOcr(fullWindow, rectTitle, false);

                    _logger.Log($"当前标题: {currentTitle}");

                    // 4. 判断是否在目标房间
                    AuthorizedTarget activeUser = CheckAuthUser(currentTitle);

                    if (activeUser != null)
                    {
                        // === 情况 A: 已在房间，只读消息 ===
                        await ProcessChatContent(fullWindow, winRect, splitX, activeUser);
                    }
                    else
                    {
                        // === 情况 B: 不在房间，执行【滚动扫描】 ===
                        // 替换掉了原来的 ScanUserListAndClick

                        _logger.Log("当前窗口非目标，开始滚动扫描列表...");

                        // 调用滚动扫描方法，获取所有可见用户
                        var allVisibleUsers = await ScanFullUserListWithScrolling(winRect, splitX);

                        // 从数据库获取需要处理的目标
                        var targetsToServe = _db.GetActiveTargets("WeChat");

                        // 在扫描结果中寻找匹配项
                        foreach (var dbUser in targetsToServe)
                        {
                            // 查找 OCR 结果中是否包含数据库里的名字
                            var foundMatch = allVisibleUsers.FirstOrDefault(kvp => kvp.Key.Contains(dbUser.Name));

                            if (!string.IsNullOrEmpty(foundMatch.Key))
                            {
                                _logger.Log($"在列表中找到目标 [{dbUser.Name}]，执行点击。");

                                var userRect = foundMatch.Value.Rect; // 这是屏幕绝对坐标(已在Scan方法里转换)

                                // 点击中心点
                                int clickX = userRect.X + userRect.Width / 2;
                                int clickY = userRect.Y + userRect.Height / 2;

                                _input.Click(clickX, clickY);
                                _input.Click((int)winRect.X + 900, (int)winRect.Y + 750); // 移开鼠标

                                // 点击后立刻返回，等待下个周期
                                return;
                            }
                        }

                        _logger.Log("本轮扫描未找到任何待处理的目标用户。");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("循环异常", ex);
            }
            finally
            {
                SetStatus(WorkStatus.Idle);
            }
        }



        // 扫描好友列表 (左侧)
        private async Task ScanUserListAndClick(Bitmap fullBmp, System.Windows.Rect winRect, int splitX)
        {
            // 定义列表区：X=60 (避开左侧图标栏) ~ X=splitX (分割线)
            int listX = 120;
            int listW = splitX - listX;
            Rectangle rectList = new Rectangle(listX, 60, listW, fullBmp.Height - 60);

            _logger.Log("进入好友列表");


            // 【关键步骤】
            // 1. 裁剪出列表区
            using (Bitmap listBmp = fullBmp.Clone(rectList, fullBmp.PixelFormat))
            {
                // 2. 应用“深色滤镜”：抹除灰色的预览消息，只留黑色的名字
                using (Bitmap filteredListBmp = VisualHelper.KeepDarkTextOnly(listBmp))
                {
                    // 调试：保存看看是不是只剩名字了
                    filteredListBmp.Save("debug_list_filtered.png");

                    // 3. 对过滤后的图片进行 OCR
                    var ocrResults = _ocr.DetectText(filteredListBmp);

                   // _logger.Log("进入好友列表");
                  //  _logger.Log("-----------");
                  //  foreach (var item in ocrResults)
                  //  {
                 //       _logger.Log(item.Text);

                 //   }
                 //   _logger.Log("-----------");

                    // 从数据库获取当前启用的微信目标
                    var activeTargets = _db.GetActiveTargets("WeChat");

                    foreach (var targetUser in activeTargets)
                    {
                        var foundItem = ocrResults.FirstOrDefault(t => t.Text.Contains(targetUser.Name));

                        if (foundItem != null)
                        {
                            _logger.Log($"列表定位到 [{targetUser.Name}]");

                            // 计算点击坐标
                            // 注意：foundItem.Rect 是相对于 listBmp 的坐标，需要还原到全屏
                            int clickX = (int)winRect.X + listX + foundItem.Rect.X + (foundItem.Rect.Width / 2);
                            int clickY = (int)winRect.Y + 60 + foundItem.Rect.Y + (foundItem.Rect.Height / 2);

                            _logger.Log($"单击 [{targetUser.Name}]");
                            _input.Click(clickX, clickY);

                            Random random = new Random();
                            int delayMs = random.Next(1000, 2001); // [1000, 2000] 毫秒（包含 1000，不包含 2001）
                            await Task.Delay(delayMs);

                            // 移开鼠标到输入区
                            _input.Click((int)winRect.X + fullBmp.Width-120, (int)winRect.Y +fullBmp.Height-70);
                            _logger.Log($"移动单击输入区 [{targetUser.Name}]");

                            return; // 找到一个就处理，处理完退出
                        }
                    }
                }
            }
        }

        // 辅助方法：裁剪并OCR
        private async Task<string> CropAndOcr(Bitmap original, Rectangle roi, bool useDarkFilter)
        {
            if (roi.Width <= 0 || roi.Height <= 0) return "";
            using (Bitmap crop = original.Clone(roi, original.PixelFormat))
            {
                // 保存到运行目录，方便查看
                string debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_chat_title.png");
                try
                {
                    crop.Save(debugPath);
                     _logger.Log($"已保存调试截图: {debugPath}");
                }
                catch { }

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

        private async Task ProcessCurrentChat(Bitmap fullBmp, System.Windows.Rect winRect, List<OcrResultItem> allTexts, AuthorizedItem user)
        {
            // 1. 在消息区找最后一条消息
            // 过滤掉 "昨天"、"12:30" 这种时间戳 (简单的长度过滤或正则，这里先略过)
            var lastMsgItem = allTexts
                .Where(t => Rect_MsgArea.Contains(GetCenter(t.Rect)))
                .OrderByDescending(t => t.Rect.Y) // 取 Y 最大的（最下面的）
                .FirstOrDefault();

            if (lastMsgItem == null) return;

            string msg = lastMsgItem.Text;

            // 2. 防重 (非常重要)
            // 如果最后一条消息是我们刚刚回复过的，或者是我们自己发的（通常自己发的在右边，对方发的在左边，可以根据 X 坐标判断，这里暂用内容判断）
            if (IsDuplicateMsg(user.Name, msg)) return;

            _logger.Log($"[{user.Name}] 收到新消息: {msg}");

            // 3. 调用 API
            string reply = await _api.GetReplyAsync($"{user.Name}_{user.BusinessId}", msg,_logger);

            _logger.Log($"[{user.Name}] AI回复: {reply}");

            // 4. 回复
            if (!string.IsNullOrEmpty(reply))
            {
                await SendReply(fullBmp, winRect, reply);

                // 记录这次处理，防止死循环回复
                // 注意：这里记录的是“收到的消息”，意味着“这条消息我已经回过了”
                RecordMsg(user.Name, msg);
            }
        }

        private async Task SendReply(Bitmap fullBmp, System.Windows.Rect winRect, string text)
        {
            // 定位输入框：尝试找笑脸
            var smileLoc = _cv.FindImageCenter(fullBmp, "Images/icon_smile.png");
            int inputX, inputY;

            if (smileLoc.HasValue)
            {
                inputX = (int)winRect.X + smileLoc.Value.X + 80; // 笑脸右侧 80px
                inputY = (int)winRect.Y + smileLoc.Value.Y;
            }
            else
            {
                // 兜底坐标
                inputX = (int)winRect.X + 600;
                inputY = (int)winRect.Y + 700;
            }

            _logger.Log($"执行回复: {text}");
            _input.Click(inputX, inputY);
            await Task.Delay(50);
            _input.PasteText(text);
            await Task.Delay(100);



            // 读取配置：是否自动发送
            bool isAuto = _db.IsAutoSend();

            if (isAuto)
            {
                _input.SendEnter(); // 自动模式：按回车
                _logger.Log($"[自动] 已发送回复: {text}");
            }
            else
            {
                // 半自动模式：只粘贴，不回车
                _logger.Log($"[半自动] 内容已填入，等待人工确认发送。");
            }

            // 发完消息后，最好把鼠标移开，避免遮挡
            _input.Click((int)winRect.X + 900, (int)winRect.Y + 750);
        }

        // ================= Helpers =================

        private System.Drawing.Point GetCenter(Rectangle r)
        {
            return new System.Drawing.Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        private bool IsPointInZone(System.Drawing.Point p, int minX, int maxX)
        {
            return p.X >= minX && p.X <= maxX;
        }

        private Dictionary<string, string> _history = new Dictionary<string, string>();

        private bool IsDuplicateMsg(string user, string msg)
        {
            // 如果字典里记录的最后一条消息和现在的一样，说明对方没发新话
            if (_history.ContainsKey(user) && _history[user] == msg) return true;
            return false;
        }

        private void RecordMsg(string user, string msg)
        {
            if (_history.ContainsKey(user)) _history[user] = msg;
            else _history.Add(user, msg);
        }

        private Bitmap CaptureScreen(System.Windows.Rect rect)
        {
            var bmp = new Bitmap((int)rect.Width, (int)rect.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, bmp.Size);
            }
            return bmp;
        }



        private AuthorizedTarget CheckAuthUser(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;

            // 从数据库获取所有 "WeChat" 平台的启用目标
            var targets = _db.GetActiveTargets("WeChat");

            return targets.FirstOrDefault(u => title.Contains(u.Name));
        }


        private async Task ProcessChatContent(Bitmap fullBmp, System.Windows.Rect winRect, int splitX, AuthorizedTarget user)
        {


            // 1. 生成唯一的会话 Key
            string sessionKey = $"{user.Name}_{user.BusinessId}";

            // 【关键逻辑 1】检查是否忙碌
            // 如果该用户正在等待大模型回复，直接跳过本次循环
            if (_processingFlags.TryGetValue(sessionKey, out bool isBusy) && isBusy)
            {
                _logger.Log($"[{user.Name}] 上一条消息正在处理中，跳过...");
                return;
            }


            // =============================================================
            // 1. 定位顶部横线 (标题栏下沿)
            // =============================================================
            // 范围：Y 在 50 到 100 之间扫描
            int topY = VisualHelper.FindHorizontalLineDebug(fullBmp, splitX, 50, 90, "TOP");
            _logger.Log($"第三栏的聊天分割线1：{topY}");

            // 兜底：如果找不到线，默认设为 60
            if (topY == -1)
            {
                _logger.Log("⚠️ 未检测到顶部横线，使用默认值 60");
                topY = 60;
            }

            // =============================================================
            // 2. 定位底部横线 (输入框上沿)
            // =============================================================
            // 范围：Y 在 (Height - 400) 到 (Height - 300) 之间扫描
            // 根据您的描述，输入框高 340-360，所以线大概在 Height - 360 附近
            int searchStart = 220; //fullBmp.Height - 420;
            int searchEnd = fullBmp.Height -138;
            int bottomY = VisualHelper.FindHorizontalLineDebug(fullBmp, splitX, searchStart, searchEnd, "BOTTOM");


            _logger.Log($"{searchStart}-{searchEnd}第三栏的聊天分割线2：{bottomY}");


            // 兜底：如果找不到线，默认设为 Height - 360
            if (bottomY == -1)
            {
                _logger.Log("⚠️ 未检测到底部横线，使用默认值 Height-360");
                bottomY = fullBmp.Height - 360;
            }

            // _logger.Log($"布局定位: 垂直={splitX}, 顶部线={topY}, 底部线={bottomY}");

            // =============================================================
            // 3. 计算“纯净消息区”矩形
            // =============================================================
            int msgX = splitX + 60; // 分割线往右60 不要头像
            int msgW = fullBmp.Width - msgX - 68; // 宽度到右边缘 60 不要头像
            int msgY = topY + 5;   // 顶线往下一点
            int msgH = bottomY-75; // 底线往上一点，算出高度

            Rectangle rectMsg = new Rectangle(msgX, msgY, msgW, msgH);

            // =============================================================
            // 4. 【关键】调试：保存截图
            // =============================================================
            // 裁剪图片
            using (Bitmap msgAreaBmp = fullBmp.Clone(rectMsg, fullBmp.PixelFormat))
            {
                // 保存到运行目录，方便查看
                string debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_chat_area.png");
                try
                {
                    msgAreaBmp.Save(debugPath);
                     _logger.Log($"已保存调试截图: {debugPath}");
                }
                catch { }

                // =============================================================
                // 5. OCR 识别
                // =============================================================
                // 直接识别这块纯净区域
                //string allMsgText = await _ocr.RecognizeTextAsync(msgAreaBmp);

                //if (string.IsNullOrWhiteSpace(allMsgText)) return;

                // 获取最后一行文字
                // string lastMsg = GetLastValidMessage(allMsgText);

          
                    // 调试：保存整个消息区
                  // msgAreaBmp.Save("debug_chat_area.png");

                    // 2. 【核心】寻找最后一个气泡
                    var bubble = VisualHelper.FindLastBubble(msgAreaBmp);

                    if (bubble == null)
                    {
                        _logger.Log("未检测到任何消息气泡 (可能是空聊天或背景干扰)");
                        return;
                    }

                    // 3. 判断是谁发的
                    if (bubble.Type == VisualHelper.BubbleType.Sent)
                    {
                        _logger.Log($"检测到最后一条是【我发送的】(绿色气泡)，跳过回复。");
                        return;
                    }

                Rectangle bubbleRelRect = bubble.Rect; // 相对气泡图
                int bubbleCenterX = (int)(winRect.X + rectMsg.X + bubbleRelRect.X + bubbleRelRect.Width / 2);
                int bubbleCenterY = (int)(winRect.Y + rectMsg.Y + bubbleRelRect.Y + bubbleRelRect.Height / 2);


                // 4. 裁剪出气泡内容 (对方发的白色气泡)
                // 注意：bubble.Rect 是相对于 msgAreaBmp 的坐标
                using (Bitmap bubbleBmp = msgAreaBmp.Clone(bubble.Rect, msgAreaBmp.PixelFormat))
                    {
                        // 调试：保存气泡图 (这张图就是纯白背景+黑字，OCR 识别率极高)
                        string bubblePath = "debug_last_bubble.png";
                        bubbleBmp.Save(bubblePath);




                    // =========================================================
                    // 图片/表情包过滤
                    // =========================================================
                    string msgContent = "";
                    bool isImage = false;

                    // =========================================================
                    // 【核心升级】类型判定策略
                    // =========================================================

                    // 策略 A: 先看是不是明显的非文本（比如五颜六色的图）
                    if (!VisualHelper.IsTextBubble(bubbleBmp))
                    {
                        isImage = true; // 肯定是图片
                    }
                    else
                    {
                        // 策略 B: 即便是白底，也可能是表情包 (如“微笑”)
                        // 此时调用 "右键菜单法" 进行最终裁决
                        _logger.Log("检测到白底内容，执行 [右键验身]...");

                        bool hasSaveOption = CheckIfImageByRightClick(bubbleCenterX, bubbleCenterY);

                        if (hasSaveOption)
                        {
                            isImage = true;
                        }
                    }

                    // =========================================================
                    // 【分支处理】
                    // =========================================================

                    if (isImage)
                    {
                        // 尝试匹配本地表情库
                        string stickerMeaning = _stickerService.MatchSticker(bubbleBmp);

                        if (!string.IsNullOrEmpty(stickerMeaning))
                        {
                            msgContent = stickerMeaning; // 匹配成功！如 "[微笑]"
                            _logger.Log($"表情包匹配成功: {msgContent}");
                        }
                        else
                        {
                            _logger.Log("检测到未知图片/表情，准备调用多模态 API (功能待实现)");
                            // TODO: 这里将来调用 api/ocr 或 api/analyze_image
                            // 目前暂且跳过，或标记为 [图片]
                            msgContent = "[图片]";

                            // 临时策略：如果是未知的 [图片]，目前先不回，防止乱回
                            return;
                        }
                    }
                    else
                    {
                        // 是纯文本，执行 OCR
                        msgContent = await _ocr.RecognizeTextAsync(bubbleBmp);
                    }






                    // 5. OCR 识别
                   // string msgContent = await _ocr.RecognizeTextAsync(bubbleBmp);

                        // 清洗内容 (去除空行)
                        msgContent = msgContent?.Trim();

                        if (string.IsNullOrWhiteSpace(msgContent)) return;

                    // 6. 防重与回复 (逻辑不变)
                    if (IsDuplicateMsg(user.Name, msgContent)) return;

                        _logger.Log($"[{user.Name}] 捕获完整消息: {msgContent}");

             


                    // 状态：开始处理 (亮起第二个灯)
                    SetStatus(WorkStatus.Processing);
                    // =======================================================
                    // 【关键逻辑 2】开始处理流程
                    // =======================================================

                    // A. 上锁 (标记为忙碌)
                    _processingFlags[sessionKey] = true;

                    // 开始计时
                    Stopwatch sw = Stopwatch.StartNew();

                    try
                    {


                        // =========================================================
                        //  核心逻辑升级：本地优先 + 统一延迟
                        // =========================================================

                        string replyContent = null;
                        string replySource = ""; // 用于日志记录来源

                        // 1. 优先查找本地问答库
                        replyContent = _db.FindBestMatchAnswer(msgContent, "WeChat");

                        if (!string.IsNullOrEmpty(replyContent))
                        {
                            replySource = "本地知识库";
                            _logger.Log("本地匹配，使用规则知识库");
                        }
                        else
                        {
                            _logger.Log($"[{user.Name}] 捕获新消息，准备请求 API...");
                            // 2. 本地没找到，调用云端 API
                         
                            replySource = "云端API";
                            replyContent = await _api.GetReplyAsync($"{user.Name}_{user.BusinessId}", msgContent, _logger);
                        }


                        sw.Stop();
                        double elapsedMs = sw.Elapsed.TotalMilliseconds/1000;


                       

                        // 记录到数据库
                        _db.LogChat(user.Name, "WeChat", msgContent, replyContent ?? "(无回复)", elapsedMs);
            


                        // C. 处理回复
                        if (!string.IsNullOrEmpty(replyContent))
                        {
                       


                            // 读取配置：回复等待时间
                            int minWait = int.Parse(_db.GetSetting("ReplyWaitMin", "2"));
                            int maxWait = int.Parse(_db.GetSetting("ReplyWaitMax", "20"));
                            // 在最小等待时间内，随机等待一下，模拟人类思考
                            if (elapsedMs < minWait * 1000)
                            {
                                int delay = new Random().Next((int)(minWait * 1000 - elapsedMs), (int)(maxWait * 1000 - elapsedMs));
                                await Task.Delay(delay);
                            }

                            // 状态：开始发送 (亮起第三个灯)
                            SetStatus(WorkStatus.Sending);
                            // 此时界面可能已经变了(用户可能发了新消息)，但我们回复的是针对 msgContent 的
                            // 视觉自动化通常回复完后，用户看到回复自然会继续发问
                            await SendReply(fullBmp, winRect, replyContent);

                            // 记录已处理
                            RecordMsg(user.Name, msgContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"处理消息异常: {ex.Message}", ex);
                    }
                    finally
                    {
                        // D. 【至关重要】无论成功还是失败，必须解锁！
                        // 否则这个用户永远会被卡在“忙碌”状态
                        _processingFlags[sessionKey] = false;
                        _logger.Log($"[{user.Name}] 处理完成，解锁状态。");
                    }




                }


            }
        }

        // 辅助方法：简单清洗 OCR 结果，取最后一条有效内容
        private string GetLastValidMessage(string fullText)
        {
            if (string.IsNullOrEmpty(fullText)) return null;

            var lines = fullText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;

            // 倒序遍历
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();

                // 过滤掉时间戳 (例如 "12:30", "昨天")
                // 这里写个简单逻辑：如果包含冒号且长度很短，大概率是时间
                if (line.Contains(":") && line.Length < 6) continue;
                if (line.Contains("昨天") && line.Length < 4) continue;

                return line;
            }
            return lines.LastOrDefault(); // 兜底
        }


        /// <summary>
        /// 【核心黑科技】通过右键菜单判断是文本还是图片
        /// </summary>
        private bool CheckIfImageByRightClick(int x, int y)
        {
            // 1. 右键点击气泡中心
            _input.RightClick(x, y);

            // 2. 截取鼠标附近的菜单区域
            // 右键菜单通常出现在鼠标右下方，截一个 200x300 的图足够覆盖菜单了
            // 注意：要确保不越界
            int screenW = (int)SystemParameters.PrimaryScreenWidth;
            int screenH = (int)SystemParameters.PrimaryScreenHeight;

            int menuW = 200;
            int menuH = 300;
            if (x + menuW > screenW) x = x - menuW; // 如果靠右边缘，菜单会往左弹
            if (y + menuH > screenH) y = y - menuH;

            // 稍微偏移一点截取，避开鼠标指针本身
            Rectangle menuRect = new Rectangle(x + 20, y + 20, menuW - 10, menuH - 10);

            bool isImage = false;

            using (Bitmap menuBmp = CaptureScreen(new Rect(menuRect.X, menuRect.Y, menuRect.Width, menuRect.Height)))
            {
                // 3. OCR 识别菜单文字
                // 菜单对比度很高，PaddleOCR 识别非常准
                string menuText = _ocr.RecognizeTextAsync(menuBmp).Result; // 同步调用即可

                // 4. 关键词判断
                // 图片/表情包特征: "另存为", "添加", "表情"
                // 文本特征: "复制", "搜一搜" (文本也有复制，但图片通常有另存为)

                if (menuText.Contains("另存为") || menuText.Contains("添加") || menuText.Contains("表情"))
                {
                    isImage = true;
                }

                // 调试日志
                // _logger.Log($"右键菜单识别: {menuText.Replace("\n", " ")} => {(isImage ? "图片" : "文本")}");
            }

            // 5. 【非常重要】关闭菜单
            // 模拟按下 ESC 键
            _input.SendKey("{ESC}");

            // 等待菜单消失
            System.Threading.Thread.Sleep(200);

            return isImage;
        }



        private async Task<Dictionary<string, OcrResultItem>> ScanFullUserListWithScrolling(System.Windows.Rect winRect, int splitX)
        {
            var allFoundUsers = new Dictionary<string, OcrResultItem>();
            var processedNames = new HashSet<string>();

            // =========================================================
            // 1. 【安全检查】启动前确认窗口状态
            // =========================================================
            if (!_uia.IsWeChatActive())
            {
                _logger.Log("⚠️ 警告：微信窗口失去焦点或已退出，停止扫描。");
                return allFoundUsers;
            }

            // =========================================================
            // 2. 归位逻辑 (回到顶部)
            // =========================================================
            int listX = 60;
            int listW = splitX - listX;
            if (listW < 50) listW = 200;

            // 移动鼠标到列表区
            int hoverX = (int)winRect.X + listX + listW / 2;
            int hoverY = (int)winRect.Y + 300;

            // 【安全点击】
            if (!_uia.IsWeChatActive()) return allFoundUsers;
            _input.Click(hoverX, hoverY);

            // 向上滚动归位
            for (int k = 0; k < 5; k++)
            {
                // 【熔断】每次动作前检查
                if (!_uia.IsWeChatActive()) break;

                // 减小力度，防止极速滚动触发风控
                _input.ScrollMouseWheel(300);
                await Task.Delay(100); // 增加间隔
            }
            await Task.Delay(800);

            // =========================================================
            // 3. 扫描循环
            // =========================================================
            int maxScrolls = 15;

            for (int i = 0; i < maxScrolls; i++)
            {
                // 【熔断】截图前检查
                if (!_uia.IsWeChatActive())
                {
                    _logger.Log("⚠️ 扫描中途窗口丢失，紧急停止。");
                    break;
                }

                // 重新截取当前屏幕
                using (Bitmap currentFrameBmp = CaptureScreen(winRect))
                {
                    Rectangle rectList = new Rectangle(listX, 60, listW, currentFrameBmp.Height - 60);

                    using (Bitmap listBmp = currentFrameBmp.Clone(rectList, currentFrameBmp.PixelFormat))
                    using (Bitmap filteredListBmp = VisualHelper.KeepDarkTextOnly(listBmp))
                    {
                        var ocrResults = _ocr.DetectText(filteredListBmp);
                        bool hasNewUserInThisPage = false;

                        if (ocrResults != null)
                        {
                            foreach (var item in ocrResults)
                            {
                                string name = item.Text.Trim();
                                if (name.Length < 1) continue;
                                if (IsSystemEntry(name)) continue;

                                if (processedNames.Add(name))
                                {
                                    hasNewUserInThisPage = true;
                                    int absX = (int)winRect.X + listX + item.Rect.X;
                                    int absY = (int)winRect.Y + 60 + item.Rect.Y;
                                    var screenRect = new Rectangle(absX, absY, item.Rect.Width, item.Rect.Height);

                                    allFoundUsers[name] = new OcrResultItem { Text = name, Rect = screenRect };
                                }
                            }
                        }

                        if (!hasNewUserInThisPage && i > 0)
                        {
                            _logger.Log("列表扫描结束 (到达底部)。");
                            break;
                        }
                    }
                }

                // 【熔断】滚动前检查
                if (!_uia.IsWeChatActive()) break;

                // 确保鼠标还在列表区 (防止用户移走鼠标导致滚错地方)
                _input.Click(hoverX, hoverY);

                // 向下滚动
                _input.ScrollMouseWheel(-300);

                // 【关键】增加随机等待，模拟人类，减少崩溃概率
                // 机器滚太快会导致微信UI线程死锁
                int randomDelay = new Random().Next(600, 900);
                await Task.Delay(randomDelay);
            }

            return allFoundUsers;
        }

        /// <summary>
        /// 判断是否为微信的系统文件夹入口（点击会改变列表结构的特殊项）
        /// </summary>
        private bool IsSystemEntry(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            // 常见的系统入口名称
            string[] blackList = new[]
            {
                "服务号",
                "客服消息",
                "服务通知",
                "订阅号消息",
                "订阅号",
                "文件传输助手",
                "微信团队"
            };

            foreach (var key in blackList)
            {
                if (name.Contains(key)) return true;
            }
            return false;
        }



    }
}