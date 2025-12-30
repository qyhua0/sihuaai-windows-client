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


        // 【新增】用户忙碌状态锁
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
            DatabaseService db)
        {
            _uia = uia; _ocr = ocr; _input = input; _cv = cv; _logger = logger;
            _api = new ChatApiService();

            _db = db;
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

    

        public async Task RunCycleAsync()
        {
            try
            {
                if (!_uia.AttachToWeChat()) return;
                var winRect = _uia.GetWindowBounds();

                // 1. 获取原始全屏截图
                using (Bitmap fullWindow = CaptureScreen(winRect))
                {
                    // 2. 【关键】动态寻找分割线 X 坐标
                    int splitX = VisualHelper.FindVerticalSplitLine(fullWindow);
                    _logger.Log($"检测到分割线位置: X={splitX}");

                    // 3. 截取“纯净”的标题区 (分割线右侧，顶部)
                    // 标题通常在分割线右侧 + 20px 开始
                    Rectangle rectTitle = new Rectangle(splitX + 10, 0, fullWindow.Width - splitX - 200, 60);
                    string currentTitle = await CropAndOcr(fullWindow, rectTitle, false); // false=不需要滤镜

                    _logger.Log($"当前标题: {currentTitle}");

                    // 4. 判断是否在目标房间
                    AuthorizedTarget activeUser = CheckAuthUser(currentTitle);

                    if (activeUser != null)
                    {
                        // === 情况 A: 已在房间，只读分割线右侧的消息 ===
                        await ProcessChatContent(fullWindow, winRect, splitX, activeUser);
                    }
                    else
                    {
                        // === 情况 B: 不在房间，扫描左侧列表 ===
                        // 传入 splitX，确保只扫描分割线左侧
                        await ScanUserListAndClick(fullWindow, winRect, splitX);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("循环异常", ex);
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

        private Point GetCenter(Rectangle r)
        {
            return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        private bool IsPointInZone(Point p, int minX, int maxX)
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

                    // 4. 裁剪出气泡内容 (对方发的白色气泡)
                    // 注意：bubble.Rect 是相对于 msgAreaBmp 的坐标
                    using (Bitmap bubbleBmp = msgAreaBmp.Clone(bubble.Rect, msgAreaBmp.PixelFormat))
                    {
                        // 调试：保存气泡图 (这张图就是纯白背景+黑字，OCR 识别率极高)
                        string bubblePath = "debug_last_bubble.png";
                        bubbleBmp.Save(bubblePath);

                        // 5. OCR 识别
                        string msgContent = await _ocr.RecognizeTextAsync(bubbleBmp);

                        // 清洗内容 (去除空行)
                        msgContent = msgContent?.Trim();

                        if (string.IsNullOrWhiteSpace(msgContent)) return;

                    // 6. 防重与回复 (逻辑不变)
                    if (IsDuplicateMsg(user.Name, msgContent)) return;

                        _logger.Log($"[{user.Name}] 捕获完整消息: {msgContent}");

                    _logger.Log($"[{user.Name}] 捕获新消息，准备请求 API...");

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
                        }
                        else
                        {
                            // 2. 本地没找到，调用云端 API
                            _logger.Log("本地未匹配，请求云端 API...");
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
    }
}