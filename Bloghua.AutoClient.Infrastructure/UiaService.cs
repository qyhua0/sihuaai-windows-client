using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation; //需引用 UIAutomationClient 和 UIAutomationTypes
using Bloghua.AutoClient.Core.Interfaces;
using System.Windows.Forms;

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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        // 引入判断窗口最小化的 API
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);



        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);



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
                           // Thread.Sleep(200); // 等待窗口恢复动画
                           // MoveWindow(_hWnd, 0, 0, 1000, 800, true);
                            //Thread.Sleep(200); // 等待重绘



                            // 获取主屏幕高度
                            int screenHeight = Screen.PrimaryScreen.Bounds.Height;

                            int targetX = 0;              // 靠左
                            int targetY = 90;             // 距上 90px
                            int targetW = 1000;           // 宽度保持 1000 不变(保证OCR布局)
                            int targetH = screenHeight - 180; // 高度 = 总高 - 上90 - 下90

                            Thread.Sleep(200);
                            MoveWindow(_hWnd, targetX, targetY, targetW, targetH, true);
                            Thread.Sleep(200);




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
            // 获取实际位置，而不是写死
            if (_weChatWindow != null)
            {
                try { return _weChatWindow.Current.BoundingRectangle; } catch { }
            }
            // 兜底返回计算值
            int h = Screen.PrimaryScreen.Bounds.Height - 180;
            return new Rect(0, 90, 1000, h);
        }


        public void ResizeWindow(int width, int height)
        {
            if (_hWnd != IntPtr.Zero)
            {
                MoveWindow(_hWnd, 0, 0, width, height, true);
            }
        }

        public void MoveWindow(int x, int y, int width, int height)
        {
            if (_hWnd != IntPtr.Zero)
            {
                MoveWindow(_hWnd, x, y, width, height, true);
            }
        }

        /// <summary>
        /// 快速检查微信窗口是否处于激活状态且有效
        /// </summary>
        public bool IsWeChatActive()
        {
            if (_hWnd == IntPtr.Zero) return false;

            // 1. 检查句柄是否还且有效
            // IsWindow 是 user32.dll 的 API，需引入
            if (!IsWindow(_hWnd)) return false;

            // 2. 检查当前前台窗口是不是微信
            // 如果用户切换了窗口，或者微信崩了，前台窗口就不是它了
            IntPtr foregroundWnd = GetForegroundWindow();
            if (foregroundWnd != _hWnd) return false;

            return true;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);



        /// <summary>
        /// 【新增】尝试刷新句柄并获取位置，但不抢占焦点，不移动窗口
        /// </summary>
        /// <returns>如果窗口存在且未最小化，返回 true</returns>
        public bool RefreshWindowStatus(out Rect bounds)
        {
            bounds = Rect.Empty;

            // 1. 如果句柄丢失，尝试重新查找
            if (_hWnd == IntPtr.Zero || !IsWindow(_hWnd))
            {
                Process[] processes = Process.GetProcessesByName("WeChat");
                if (processes.Length == 0) processes = Process.GetProcessesByName("Weixin");

                foreach (var proc in processes)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        _hWnd = proc.MainWindowHandle;
                        break;
                    }
                }
            }

            if (_hWnd == IntPtr.Zero) return false;

            // 2. 检查是否最小化
            if (IsIconic(_hWnd)) return false;

            // 3. 获取当前位置 (不移动)
            // 使用 UIA 或 Win32 API 获取 Rect
            try
            {
                // 如果 AutomationElement 还没初始化
                if (_weChatWindow == null) _weChatWindow = AutomationElement.FromHandle(_hWnd);

                bounds = _weChatWindow.Current.BoundingRectangle;

                // 简单的有效性检查 (宽或高太小说明不正常)
                if (bounds.Width < 100 || bounds.Height < 100) return false;

                return true;
            }
            catch
            {
                // 如果 UIA 失败，尝试用 Win32 GetWindowRect (需引入) 兜底，这里简化处理返回 false
                return false;
            }
        }

    }
}
