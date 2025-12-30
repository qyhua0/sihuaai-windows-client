using System;
using System.IO;
using Bloghua.AutoClient.Core.Interfaces;

namespace Bloghua.AutoClient.Infrastructure.Services
{
    public class FileLoggerService : ILoggerService
    {
        private readonly string _logPath;
        private readonly object _lock = new object();

        public FileLoggerService()
        {
            // 日志保存在程序运行目录下的 debug_log.txt
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt");
        }

        public void Log(string message)
        {
            WriteToFile($"[INFO] {message}");
        }

        public void Error(string message, Exception ex = null)
        {
            string errorDetails = ex != null ? $" | Exception: {ex.Message}" : "";
            WriteToFile($"[ERROR] {message}{errorDetails}");
        }

        private void WriteToFile(string content)
        {
            try
            {
                lock (_lock)
                {
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {content}{Environment.NewLine}";
                    File.AppendAllText(_logPath, line);
                }
            }
            catch
            {
                // 忽略日志写入本身的错误，避免死循环
            }
        }
    }
}