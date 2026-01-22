using System.Collections.Generic;

namespace Bloghua.AutoClient.Core.Models
{
    public class SuggestionItem
    {
        public string type { get; set; }    // professional, friendly, closing
        public string content { get; set; } // 具体话术

        // 【新增】UI 辅助字段：显示标题 (如 "建议 1")
        public string DisplayTitle { get; set; }




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

        // 背景色
        // 逻辑：优先返回手动设置的颜色；如果没有设置，则根据 type 返回默认色
        private string _colorCode;
        public string ColorCode
        {
            get
            {
                // 1. 如果手动设置过(例如在演练界面按顺序分配)，直接返回
                if (!string.IsNullOrEmpty(_colorCode)) return _colorCode;

                // 2. 否则根据 type 返回默认颜色
                switch (type)
                {
                    case "professional": return "#0078D7"; // 蓝
                    case "friendly": return "#107C10";     // 绿
                    case "closing": return "#FFB900";      // 黄
                    default: return "#666666";             // 灰
                }
            }
            set
            {
                _colorCode = value; // 允许外部手动修改颜色
            }
        
        }
}

    public class SuggestionResponse
    {
        public List<SuggestionItem> suggestions { get; set; }

        public string persona { get; set; }
        public string scene { get; set; }
    }
}