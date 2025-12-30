using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing; // 用于 Point
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core;

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
    }
}