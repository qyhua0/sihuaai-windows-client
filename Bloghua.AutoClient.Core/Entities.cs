using System;
using SQLite; // 引用 sqlite-net-pcl

namespace Bloghua.AutoClient.Core.Entities
{
    // 配置表：存储如 "AutoSendMode" 等全局设置
    public class AppSetting
    {
        [PrimaryKey]
        public string Key { get; set; }
        public string Value { get; set; }
    }

    // 授权目标表：替代原有的 AuthorizedUsers/Groups
    public class AuthorizedTarget
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public string Name { get; set; }       // 微信昵称或群名
        public string BusinessId { get; set; } // 业务ID
        public string Platform { get; set; }   // 平台: WeChat, QQ, Facebook
        public string Type { get; set; }       // 类型: User, Group
        public bool IsEnabled { get; set; } = true;
    }

    // 会话日志表
    public class ChatLog
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public string SessionName { get; set; } // 对方名字
        public string Platform { get; set; }    // 平台
        public string RequestContent { get; set; } // 识别到的内容
        public string ReplyContent { get; set; }   // API 回复的内容
        public double TimeTakenMs { get; set; }    // API 耗时(毫秒)
        public DateTime CreatedAt { get; set; }    // 创建时间
    }

    // 问答表
    public class QuestionAnswer
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public int Priority { get; set; } = 0; // 优先级，数字越大越优先
        public string Platform { get; set; }   // 平台
    }


    // 1. 角色缓存表 (存储从服务端拉取的角色)
    public class AiRole
    {
        [PrimaryKey]
        public string Code { get; set; } // tech_expert
        public string Name { get; set; } // 技术专家
        public bool IsDefault { get; set; }
    }

    // 2. 用户角色配置表 (记录哪个微信好友用哪个角色)
    public class UserRoleConfig
    {
        [PrimaryKey]
        public string SessionKey { get; set; } // 微信昵称_业务ID
        public string RoleCode { get; set; }   // 绑定的角色Code
    }
}