using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media; // 用于 SolidColorBrush
using Bloghua.AutoClient.Core.Enums;

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class MonitoringView : Page
    {
        private DispatcherTimer _loopTimer;

        // 定义状态灯颜色
        private readonly SolidColorBrush _colorIdle = new SolidColorBrush(Color.FromRgb(224, 224, 224)); // #E0E0E0 灰
        private readonly SolidColorBrush _colorScan = new SolidColorBrush(Color.FromRgb(0, 120, 215));   // #0078D7 蓝
        private readonly SolidColorBrush _colorProcess = new SolidColorBrush(Color.FromRgb(255, 185, 0)); // #FFB900 黄
        private readonly SolidColorBrush _colorSend = new SolidColorBrush(Color.FromRgb(16, 124, 16));   // #107C10 绿

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

                // 订阅状态事件
                ServiceLocator.AutoService.OnStatusChanged -= OnServiceStatusChanged; // 先减后加防止重复
                ServiceLocator.AutoService.OnStatusChanged += OnServiceStatusChanged;


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

               // lblStatus.Text = "运行中";
               // lblStatus.Foreground = System.Windows.Media.Brushes.Green;

                UpdateMainStatus("运行中", true);

                LogToUI(">>> 服务已启动 <<<");
            }
            catch (Exception ex)
            {
                LogToUI($"启动失败: {ex.Message}");
            }
        }

       
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_loopTimer != null) _loopTimer.Stop();
           // lblStatus.Text = "已停止";
            //lblStatus.Foreground = System.Windows.Media.Brushes.Red;

            UpdateMainStatus("已停止", false);
            ResetLights(); // 熄灯
            LogToUI(">>> 服务已停止 <<<");
        }

        // 【核心】定时器逻辑：确保串行执行
        private async void LoopTimer_Tick(object sender, EventArgs e)
        {
            // 1. 暂停计时器 (暂停扫描)
            _loopTimer.Stop();

            try
            {
                if (ServiceLocator.AutoService != null)
                {

                    

                    // 2. 等待整个 RunCycleAsync 执行完毕
                    // 只有这里 await 返回了，才会执行下面的 finally
                    await ServiceLocator.AutoService.RunCycleAsync();
                }
            }
            catch (Exception ex)
            {
                LogToUI($"循环异常: {ex.Message}");
            }
            finally
            {
                // 3. 任务处理完后，恢复计时器 (恢复扫描)
                // 这样就绝对保证了：处理一条消息期间，不会触发新的扫描
                if (lblStatus.Text == "运行中")
                {
                    _loopTimer.Start();
                }
            }
        }

        // 【新增】处理状态灯变化
        private void OnServiceStatusChanged(WorkStatus status)
        {
            // 必须在 UI 线程执行
            Dispatcher.Invoke(() =>
            {
                ResetLights(); // 先全灰

                switch (status)
                {
                    case WorkStatus.Scanning:
                        lightScan.Fill = _colorScan; // 亮第一个
                        break;
                    case WorkStatus.Processing:
                        lightScan.Fill = _colorScan;    // 保持第一个亮
                        lightProcess.Fill = _colorProcess; // 亮第二个
                        break;
                    case WorkStatus.Sending:
                        lightScan.Fill = _colorScan;
                        lightProcess.Fill = _colorProcess;
                        lightSend.Fill = _colorSend;    // 亮第三个
                        break;
                    case WorkStatus.Idle:
                        // 全灰 (ResetLights已处理)
                        break;
                }
            });
        }

        private void ResetLights()
        {
            lightScan.Fill = _colorIdle;
            lightProcess.Fill = _colorIdle;
            lightSend.Fill = _colorIdle;
        }

        private void UpdateMainStatus(string text, bool isRunning)
        {
            lblStatus.Text = text;
            lblStatus.Foreground = isRunning ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
            btnStart.IsEnabled = !isRunning;
            btnStop.IsEnabled = isRunning;
        }

        private void LogToUI(string msg)
        {
            string log = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            txtConsole.AppendText(log);
            txtConsole.ScrollToEnd();
            ServiceLocator.Logger?.Log(msg);
        }

    }
}