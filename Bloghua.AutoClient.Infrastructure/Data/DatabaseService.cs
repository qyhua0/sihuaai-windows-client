using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Needed for ToList()
using Bloghua.AutoClient.Core.Entities;
using Bloghua.AutoClient.Core.Models;
using SQLite;

namespace Bloghua.AutoClient.Infrastructure.Data
{
    public class DatabaseService
    {
        private readonly SQLiteConnection _db;

        public DatabaseService()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bloghua_bot.db");
            _db = new SQLiteConnection(dbPath);

            // 自动建表 (如果不存在)
            _db.CreateTable<AppSetting>();
            _db.CreateTable<AuthorizedTarget>();
            _db.CreateTable<ChatLog>();
            _db.CreateTable<QuestionAnswer>();

            _db.CreateTable<AiRole>();
            _db.CreateTable<UserRoleConfig>();

            // 初始化默认配置
            InitDefaults();
        }

        private void InitDefaults()
        {
            // 如果配置为空，插入默认值: 自动发送=True
            if (_db.Find<AppSetting>("IsAutoSend") == null)
            {
                SaveSetting("IsAutoSend", "false");
            }

            if (_db.Find<AppSetting>("ScanInterval") == null) SaveSetting("ScanInterval", "6");
            if (_db.Find<AppSetting>("ReplyWaitMin") == null) SaveSetting("ReplyWaitMin", "2");
            if (_db.Find<AppSetting>("ReplyWaitMax") == null) SaveSetting("ReplyWaitMax", "20");

            // 默认开启主动激活，用户可在设置里关掉
            if (_db.Find<AppSetting>("AutoActiveWindow") == null)
            {
                SaveSetting("AutoActiveWindow", "true");
            }
        }

        // --- 配置相关 ---
        public void SaveSetting(string key, string value)
        {
            var setting = new AppSetting { Key = key, Value = value };
            _db.InsertOrReplace(setting);
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            var s = _db.Find<AppSetting>(key);
            return s != null ? s.Value : defaultValue;
        }

        public bool IsAutoSend()
        {
            return GetSetting("IsAutoSend", "true").ToLower() == "true";
        }

        // --- 授权用户相关 ---
        public List<AuthorizedTarget> GetActiveTargets(string platform)
        {
            return _db.Table<AuthorizedTarget>()
                      .Where(t => t.IsEnabled && t.Platform == platform)
                      .ToList();
        }

        public void AddOrUpdateTarget(AuthorizedTarget target)
        {
            if (target.Id != 0) _db.Update(target);
            else _db.Insert(target);
        }

        public void DeleteTarget(int id)
        {
            _db.Delete<AuthorizedTarget>(id);
        }

        public List<AuthorizedTarget> GetAllTargets()
        {
            return _db.Table<AuthorizedTarget>().ToList();
        }

        // --- 日志相关 ---
        public void LogChat(string name, string platform, string req, string reply, double ms)
        {
            var log = new ChatLog
            {
                SessionName = name,
                Platform = platform,
                RequestContent = req,
                ReplyContent = reply,
                TimeTakenMs = ms,
                CreatedAt = DateTime.Now
            };
            _db.Insert(log);
        }

        public List<ChatLog> SearchLogs(string nameKeyword, string contentKeyword, DateTime start, DateTime end)
        {
            // SQLite-net-pcl 的简单查询
            var query = _db.Table<ChatLog>().Where(l => l.CreatedAt >= start && l.CreatedAt <= end);

            if (!string.IsNullOrEmpty(nameKeyword))
            {
                query = query.Where(l => l.SessionName.Contains(nameKeyword));
            }
            if (!string.IsNullOrEmpty(contentKeyword))
            {
                query = query.Where(l => l.RequestContent.Contains(contentKeyword) || l.ReplyContent.Contains(contentKeyword));
            }

            return query.OrderByDescending(l => l.CreatedAt).ToList();
        }

        public void DeleteLogs(List<int> logIds)
        {
            _db.Table<ChatLog>().Where(l => logIds.Contains(l.Id)).Delete();
        }
        public void ClearAllLogs()
        {
            _db.DeleteAll<ChatLog>();
        }


        // 问答管理 CRUD 
        public List<QuestionAnswer> SearchQAs(string keyword, string platform)
        {
            var query = _db.Table<QuestionAnswer>().Where(q => q.Platform == platform);
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(q => q.Question.Contains(keyword) || q.Answer.Contains(keyword));
            }
            return query.OrderByDescending(q => q.Priority).ToList();
        }
        public void SaveQA(QuestionAnswer qa)
        {
            if (qa.Id == 0) _db.Insert(qa);
            else _db.Update(qa);
        }
        public void DeleteQA(int id)
        {
            _db.Delete<QuestionAnswer>(id);
        }

        /// <summary>
        /// 查找最佳匹配的本地问答
        /// </summary>
        /// <param name="userMessage">用户发送的消息</param>
        /// <param name="platform">平台 (WeChat)</param>
        /// <returns>匹配到的回答，如果没有则返回 null</returns>
        public string FindBestMatchAnswer(string userMessage, string platform)
        {
            if (string.IsNullOrEmpty(userMessage)) return null;

            // 获取该平台下所有问答
            // 注意：如果数据量特别大(几万条)，建议用 SQL LIKE 查询，但本地几百条数据内存过滤更快且支持更灵活的逻辑
            var allQAs = _db.Table<QuestionAnswer>()
                            .Where(q => q.Platform == platform)
                            .OrderByDescending(q => q.Priority) // 优先匹配高优先级的
                            .ToList();

            foreach (var qa in allQAs)
            {
                // 匹配逻辑：
                // 1. 如果 QA 的关键词包含在用户消息中 (例如 QA="价格", User="请问价格是多少")
                // 2. 或者用户消息包含在 QA 问题中 (模糊匹配)
                //if (userMessage.Contains(qa.Question))
                if(userMessage.Equals(qa.Question))
                {
                    return qa.Answer;
                }
            }

            return null;
        }




        // --- 角色相关 ---

        // 保存拉取到的角色列表
        public void SaveRoles(List<PromptItem> roles)
        {
            _db.RunInTransaction(() =>
            {
                _db.DeleteAll<AiRole>(); // 先清空旧的
                foreach (var r in roles)
                {
                    _db.Insert(new AiRole { Code = r.code, Name = r.name, IsDefault = r.is_default });
                }
            });
        }

        // 获取所有角色 (供下拉框使用)
        public List<AiRole> GetAllRoles()
        {
            return _db.Table<AiRole>().ToList();
        }

        // 获取默认角色 Code
        public string GetDefaultRoleCode()
        {
            var def = _db.Table<AiRole>().FirstOrDefault(r => r.IsDefault);
            return def?.Code ?? "";
        }

        // --- 用户配置相关 ---

        // 保存用户与角色的绑定
        public void SaveUserRole(string sessionKey, string roleCode)
        {
            var config = new UserRoleConfig { SessionKey = sessionKey, RoleCode = roleCode };
            _db.InsertOrReplace(config);
        }


        // 获取某个用户绑定的角色
        public string GetUserRole(string sessionKey)
        {
            // 1. 尝试获取针对该用户的特定配置
            var config = _db.Find<UserRoleConfig>(sessionKey);
            if (config != null && !string.IsNullOrEmpty(config.RoleCode))
            {
                return config.RoleCode;
            }

            // 2. 【新增】尝试获取系统设置的"全局默认角色"
            string globalDefault = GetSetting("GlobalDefaultRole", "");
            if (!string.IsNullOrEmpty(globalDefault))
            {
                return globalDefault;
            }

            // 3. 最后兜底：获取 API 返回的 is_default=true 的角色
            return GetDefaultRoleCode();
        }

    }
}