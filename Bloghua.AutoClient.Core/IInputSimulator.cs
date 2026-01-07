
namespace Bloghua.AutoClient.Core
{
    public interface IInputSimulator
    {
        void Click(int x, int y);
        void InputText(string text);
        void SendEnter();
        void PasteText(string text); // 微信通常禁止直接模拟按键输入大量文本，建议用剪贴板粘贴
  
        void RightClick(int x, int y);
        void SendKey(string key);

        // 【新增】模拟鼠标滚輪滾動
        void ScrollMouseWheel(int delta);
    }
}