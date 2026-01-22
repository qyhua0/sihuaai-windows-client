using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Infrastructure.Data;
using Bloghua.AutoClient.Services;
using System;

namespace Bloghua.AutoClient.Desktop
{
    public static class ServiceLocator
    {
        public static DatabaseService Db { get; set; }
        public static ILoggerService Logger { get; set; }
        public static WeChatVisualService AutoService { get; set; }

        // 【新增】用户信息更新事件
        public static event Action OnUserInfoUpdated;

        // 【新增】触发更新的方法
        public static void NotifyUserInfoUpdated()
        {
            OnUserInfoUpdated?.Invoke();
        }
    }
}