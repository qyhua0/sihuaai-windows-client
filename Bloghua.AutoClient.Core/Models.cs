using System.Collections.Generic;
using System.Drawing;

namespace Bloghua.AutoClient.Core.Models
{
    public class AppConfig
    {
        public List<AuthorizedItem> AuthorizedUsers { get; set; } = new List<AuthorizedItem>();
        public List<AuthorizedItem> AuthorizedGroups { get; set; } = new List<AuthorizedItem>();
    }

    public class AuthorizedItem
    {
        public string Name { get; set; }       // 匹配名称
        public string BusinessId { get; set; } // 业务ID
    }

    public class ChatRequest
    {
        public string session_key { get; set; }
        public string content { get; set; }
        public bool is_image { get; set; } = false;
    }

    public class ChatResponse
    {
        public string reply { get; set; }
    }



    // OCR 识别单元，包含文字和位置
    public class OcrResultItem
    {
        public string Text { get; set; }
        public Rectangle Rect { get; set; } // 文字在图片中的绝对坐标
    }
}