using System.Windows;

namespace Bloghua.AutoClient.Core.Interfaces
{
    public interface IUIAutomationService
    {
        // 1. 查找并锁定微信窗口
        bool AttachToWeChat();

        // 2. 获取窗口在屏幕上的绝对坐标
        Rect GetWindowBounds();

        // 3. 强制调整窗口大小 (确保OCR和截图坐标稳定)
        void ResizeWindow(int width, int height);

        void MoveWindow(int x, int y, int width, int height);
        bool IsWeChatActive();
    }
}