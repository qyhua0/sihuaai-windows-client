

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation; //需引用 UIAutomationClient 和 UIAutomationTypes
using Bloghua.AutoClient.Core.Interfaces;

namespace Bloghua.AutoClient.Infrastructure.Automation
{
    public class UiaService : IUIAutomationService
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
       // [DllImport("user32.dll")]
      //  private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        // 引入 MoveWindow 也就是调整窗口位置和大小的 API
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);


        private AutomationElement _weChatWindow;
        private IntPtr _hWnd;


        // 补充需要的 Win32 API 引用
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public bool AttachToWeChat()
        {
            // 1. 国际PC版微信进程名通常是 "WeChat"，
            // 2. 大陆版是 "Weixin"
            string processName = "WeChat";
            Process[] processes = Process.GetProcessesByName(processName);

            // 如果找不到 "WeChat"，尝试用户提到的 "Weixin"
            if (processes.Length == 0)
            {
                processName = "Weixin";
                processes = Process.GetProcessesByName(processName);
            }

            if (processes.Length == 0)
            {
                // 日志建议：未找到微信进程
                return false;
            }

            // 2. 关键修改：遍历所有进程，寻找那个带窗口句柄的
            foreach (var proc in processes)
            {
                // 刷新进程状态，确保句柄信息最新
                proc.Refresh();

                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    _hWnd = proc.MainWindowHandle;

                    // 找到句柄后，尝试获取 UIA 元素
                    try
                    {
                        _weChatWindow = AutomationElement.FromHandle(_hWnd);

                        // 再次确认：有时候有句柄但没名字的可能是托盘图标，加个名字判断更保险（可选）
                        if (_weChatWindow != null)
                        {
                            // 1. 恢复窗口（防止最小化）
                            ShowWindow(_hWnd, 9); // SW_RESTORE

                            // 2. 激活置顶
                            SetForegroundWindow(_hWnd);

                            // 3. 【核心修正】强制移动到屏幕 (0,0) 并重置大小为 1000x800
                            // 这样我们可以保证后续的 OCR 坐标绝对准确，不再受上次关闭位置影响
                            Thread.Sleep(200); // 等待窗口恢复动画
                            MoveWindow(_hWnd, 0, 0, 1000, 800, true);
                            Thread.Sleep(200); // 等待重绘


                            try
                            {
                                _weChatWindow = AutomationElement.FromHandle(_hWnd);
                                return true;
                            }
                            catch { continue; }
                        }
                    }
                    catch
                    {
                        // 忽略这个进程，继续找下一个
                        continue;
                    }
                }
            }

            return false;
        }





        public Rect GetWindowBounds()
        {
            if (_weChatWindow == null) AttachToWeChat();
           // return _weChatWindow.Current.BoundingRectangle;

            // 要强制重置，所以直接返回我们设定的标准尺寸即可
            // 实际截图时会截取 0,0,1000,800 的屏幕区域
            return new Rect(0, 0, 1000, 800);
        }


        public void ResizeWindow(int width, int height)
        {
            if (_hWnd != IntPtr.Zero)
            {
                MoveWindow(_hWnd, 0, 0, width, height, true);
            }
        }
    }
}
