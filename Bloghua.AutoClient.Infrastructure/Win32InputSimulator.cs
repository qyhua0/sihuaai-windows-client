using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing; // 用于 Point
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core;
using System.Windows.Forms; // 需要引用 System.Windows.Forms (用于 SendKeys)

namespace Bloghua.AutoClient.Infrastructure.Input
{
    public class Win32InputSimulator : IInputSimulator
    {
        // 导入必要的 Win32 API
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        static extern void mouse_event(int flags, int dX, int dY, int buttons, int extraInfo);

        // 鼠标事件常量
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_WHEEL = 0x0800;



        /// <summary>
        /// 模拟一次标准的鼠标左键单击
        /// </summary>
        public void Click(int x, int y)
        {
            // 1. 先把鼠标瞬移过去
            SetCursorPos(x, y);

            // 2. 【关键】停顿 150ms
            // 作用：让微信列表感知到鼠标悬停(Hover)，有些UI需要先Hover才能Click
            // 同时也是为了消除移动惯性，防止被误判为拖拽
            Thread.Sleep(150);

            // 3. 按下 (Down)
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);

            // 4. 【关键】极短延时 10ms
            // 作用：模拟真实的快速轻击。如果这里超过 200ms，就会变成“长按”或“拖动准备”
            Thread.Sleep(10);

            // 5. 抬起 (Up)
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

            // 6. 点击后稍微等待一下，给 UI 反应时间
            Thread.Sleep(50);
        }

        public void InputText(string text)
        {
            // 使用剪贴板粘贴是输入大量文字最稳的方法
            // 需引用 System.Windows.Forms
            System.Windows.Forms.Clipboard.SetText(text);
            Thread.Sleep(50);
            System.Windows.Forms.SendKeys.SendWait("^v"); // Ctrl+V
        }

        public void PasteText(string text)
        {
            InputText(text);
        }

        public void SendEnter()
        {
            System.Windows.Forms.SendKeys.SendWait("{ENTER}");
        }

        // 右键点击
        public void RightClick(int x, int y)
        {
            SetCursorPos(x, y);
            Thread.Sleep(150); // 停顿，防止惯性
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            Thread.Sleep(10);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            Thread.Sleep(500); // 右键菜单弹出需要时间，多等一会儿
        }

        // 模拟按键 (用于按 ESC 关闭菜单)
        public void SendKey(string key)
        {
            SendKeys.SendWait(key);
            Thread.Sleep(100);
        }

        // 实现滚轮方法
        public void ScrollMouseWheel(int delta)
        {
            // delta > 0: 向上滚
            // delta < 0: 向下滚
            // -120 约等于一次标准的滚轮“咔嗒”
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, delta, 0);
            Thread.Sleep(200); // 滚动后等待UI响应
        }
    }
}