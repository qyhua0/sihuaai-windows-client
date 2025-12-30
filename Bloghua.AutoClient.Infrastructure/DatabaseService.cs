using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Needed for ToList()
using Bloghua.AutoClient.Core.Entities;
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

            // 初始化默认配置
            InitDefaults();
        }

        private void InitDefaults()
        {
            // 如果配置为空，插入默认值: 自动发送=True
            if (_db.Find<AppSetting>("IsAutoSend") == null)
            {
                SaveSetting("IsAutoSend", "true");
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
    }
}