using System.Collections.Generic;

namespace Bloghua.AutoClient.Core.Models
{
    public class SuggestionItem
    {
        public string type { get; set; }    // professional, friendly, closing
        public string content { get; set; } // 具体话术

        // 辅助属性，用于 UI 显示不同颜色/图标 (非 API 返回)
        public string TypeDisplayName
        {
            get
            {
                switch (type)
                {
                    case "professional": return "专业的";
                    case "friendly": return "亲切的";
                    case "closing": return "结束语";
                    default: return "建议";
                }
            }
        }

        public string ColorCode
        {
            get
            {
                switch (type)
                {
                    case "professional": return "#0078D7"; // 蓝
                    case "friendly": return "#107C10";     // 绿
                    case "closing": return "#FFB900";      // 黄
                    default: return "Gray";
                }
            }
        }
    }

    public class SuggestionResponse
    {
        public List<SuggestionItem> suggestions { get; set; }
    }
}