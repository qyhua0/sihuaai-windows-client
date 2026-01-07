namespace Bloghua.AutoClient.Core.Enums
{
    public enum WorkStatus
    {
        Idle,       // 空闲/等待
        Scanning,   // 正在获取/OCR扫描
        Processing, // 正在调用API或本地库 (思考中)
        Sending     // 正在输入/发送回复
    }
}