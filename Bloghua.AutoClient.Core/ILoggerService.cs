using System;

namespace Bloghua.AutoClient.Core.Interfaces
{
    public interface ILoggerService
    {
        void Log(string message);
        void Error(string message, Exception ex = null);
    }
}