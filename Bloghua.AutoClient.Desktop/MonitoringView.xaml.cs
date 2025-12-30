using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class MonitoringView : Page
    {
        private DispatcherTimer _loopTimer;

        public MonitoringView()
        {
            InitializeComponent();
            // 从文件加载最近一点日志
            txtConsole.Text = "准备就绪...\n";
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 初始化服务
                MainWindow.InitAutoService();

                // 启动循环定时器
                if (_loopTimer == null)
                {
                    _loopTimer = new DispatcherTimer();
                    // 读取配置的间隔，默认6秒
                    int interval = int.Parse(ServiceLocator.Db.GetSetting("ScanInterval", "6"));
                    _loopTimer.Interval = TimeSpan.FromSeconds(interval);
                    _loopTimer.Tick += LoopTimer_Tick;
                }

                _loopTimer.Start();

                lblStatus.Text = "运行中";
                lblStatus.Foreground = System.Windows.Media.Brushes.Green;
                LogToUI(">>> 服务已启动 <<<");
            }
            catch (Exception ex)
            {
                LogToUI($"启动失败: {ex.Message}");
            }
        }

        private async void LoopTimer_Tick(object sender, EventArgs e)
        {
            _loopTimer.Stop(); // 暂停防止重入
            try
            {
                if (ServiceLocator.AutoService != null)
                {
                    LogToUI("开始扫描周期...");
                    await ServiceLocator.AutoService.RunCycleAsync();
                }
            }
            catch (Exception ex)
            {
                LogToUI($"循环异常: {ex.Message}");
            }
            finally
            {
                if (lblStatus.Text == "运行中") _loopTimer.Start();
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_loopTimer != null) _loopTimer.Stop();
            lblStatus.Text = "已停止";
            lblStatus.Foreground = System.Windows.Media.Brushes.Red;
            LogToUI(">>> 服务已停止 <<<");
        }

        private void LogToUI(string msg)
        {
            string log = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            txtConsole.AppendText(log);
            txtConsole.ScrollToEnd();
            // 同时写文件日志
            ServiceLocator.Logger?.Log(msg);
        }
    }
}