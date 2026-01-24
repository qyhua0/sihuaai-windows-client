using System;
using System.Threading;
using System.Windows;

namespace Bloghua.AutoClient.Desktop
{
    public partial class App : Application
    {
        /*
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 手动实例化并显示主窗口
            // 这样如果类名或命名空间不对，编译时就会直接报错，而不是运行时才崩溃
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }*/


        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "Bloghua.AutoClient.UniqueId_v1"; // 唯一标识符
            bool createdNew;

            // 尝试创建一个命名的互斥体
            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // 如果 createdNew 为 false，说明已经有一个实例在运行
                MessageBox.Show("程序已经在运行中，请检查任务栏或系统托盘。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                // 退出当前尝试启动的实例
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);

            // 手动启动主窗口 (保持之前的逻辑)
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 释放互斥体
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Close();
            }
            base.OnExit(e);
        }
    }
}