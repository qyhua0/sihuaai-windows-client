using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Infrastructure.Data;
using Bloghua.AutoClient.Services;

namespace Bloghua.AutoClient.Desktop
{
    public static class ServiceLocator
    {
        public static DatabaseService Db { get; set; }
        public static ILoggerService Logger { get; set; }
        public static WeChatVisualService AutoService { get; set; }
    }
}