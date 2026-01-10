using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Bloghua.AutoClient.Core;
using Bloghua.AutoClient.Core.Entities;
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core.Models;
using Bloghua.AutoClient.Infrastructure.Image;

namespace Bloghua.AutoClient.Services
{
    /// <summary>
    /// 全自动驾驶模块：负责列表扫描、滚动、黑名单过滤、自动点击
    /// </summary>
    public class WeChatAutoPilot
    {
        private readonly IInputSimulator _input;
        private readonly IOcrService _ocr;
        private readonly ILoggerService _logger;
        private readonly IUIAutomationService _uia; // 用于熔断检查

        public WeChatAutoPilot(
            IInputSimulator input,
            IOcrService ocr,
            ILoggerService logger,
            IUIAutomationService uia)
        {
            _input = input;
            _ocr = ocr;
            _logger = logger;
            _uia = uia;
        }

        /// <summary>
        /// 执行全自动寻人逻辑：滚动列表，直到找到数据库中的授权用户并点击
        /// </summary>
        public async Task<bool> FindAndClickTargetAsync(System.Windows.Rect winRect, int splitX, List<AuthorizedTarget> targets)
        {
            if (targets == null || targets.Count == 0) return false;

            // =========================================================
            // 1. 前置动作：强制回到列表顶部
            // =========================================================
            int listX = 60;
            int listW = splitX - listX;
            if (listW < 50) listW = 200;

            int hoverX = (int)winRect.X + listX + listW / 2;
            int hoverY = (int)winRect.Y + 300;

            // 激活并归位
            if (!_uia.IsWeChatActive()) return false;
            _input.Click(hoverX, hoverY);

            // 向上滚动归位 (5次大幅度)
            for (int k = 0; k < 5; k++)
            {
                _input.ScrollMouseWheel(500);
                await Task.Delay(50);
            }
            await Task.Delay(800);

            // =========================================================
            // 2. 滚动扫描循环
            // =========================================================
            var processedNames = new HashSet<string>();
            int maxScrolls = 15;

            for (int i = 0; i < maxScrolls; i++)
            {
                // 熔断
                if (!_uia.IsWeChatActive()) return false;

                // 截图 (这里需要调用外部传入的截图方法，或者简单起见，这里局部截图)
                // 为了代码解耦，我们假设这里有能力截图。
                // 实际工程中最好传入 Func<Bitmap> captureFunc，这里简化处理，直接依赖 UIA 获取范围
                using (Bitmap currentFrame = CaptureScreen(winRect))
                {
                    Rectangle rectList = new Rectangle(listX, 60, listW, currentFrame.Height - 60);

                    using (Bitmap listBmp = currentFrame.Clone(rectList, currentFrame.PixelFormat))
                    using (Bitmap filteredBmp = VisualHelper.KeepDarkTextOnly(listBmp))
                    {
                        var ocrResults = _ocr.DetectText(filteredBmp);
                        bool hasNewUser = false;

                        if (ocrResults != null)
                        {
                            foreach (var item in ocrResults)
                            {
                                string name = item.Text.Trim();
                                if (name.Length < 1 || IsSystemEntry(name)) continue;

                                if (processedNames.Add(name))
                                {
                                    hasNewUser = true;

                                    // Check: 是不是我们要找的人？
                                    var target = targets.FirstOrDefault(t => name.Contains(t.Name));
                                    if (target != null)
                                    {
                                        _logger.Log($"[全自动] 发现目标 [{target.Name}]，执行点击。");

                                        // 计算绝对坐标并点击
                                        int clickX = (int)winRect.X + listX + item.Rect.X + item.Rect.Width / 2;
                                        int clickY = (int)winRect.Y + 60 + item.Rect.Y + item.Rect.Height / 2;

                                        _input.Click(clickX, clickY);
                                        _input.Click((int)winRect.X + 900, (int)winRect.Y + 750); // 移开鼠标
                                        return true; // 任务完成
                                    }
                                }
                            }
                        }

                        // 到底检测
                        if (!hasNewUser && i > 0)
                        {
                            _logger.Log("[全自动] 列表已扫完，未发现待处理目标。");
                            return false;
                        }
                    }
                }

                // 没找到，继续滚
                if (!_uia.IsWeChatActive()) return false;
                _input.Click(hoverX, hoverY);
                _input.ScrollMouseWheel(-300); // 向下

                int delay = new Random().Next(600, 900);
                await Task.Delay(delay);
            }

            return false;
        }

        private bool IsSystemEntry(string name)
        {
            string[] blackList = { "客服消息", "服务通知", "订阅号", "文件传输助手", "微信团队", "折叠的群聊" };
            return blackList.Any(k => name.Contains(k));
        }

        // 私有截图辅助
        private Bitmap CaptureScreen(System.Windows.Rect rect)
        {
            var bmp = new Bitmap((int)rect.Width, (int)rect.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, bmp.Size);
            }
            return bmp;
        }
    }
}