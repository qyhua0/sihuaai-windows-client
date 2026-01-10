using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Bloghua.AutoClient.Core.Enums;
using Newtonsoft.Json; // 必须引用
using Bloghua.AutoClient.Core.Models; // 引用 SuggestionItem
using System.Collections.Generic;
using System.Text.RegularExpressions;


namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class MonitoringView : Page
    {
        private DispatcherTimer _loopTimer;

        public MonitoringView()
        {
            InitializeComponent();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainWindow.InitAutoService();

                // 1. 订阅状态
                ServiceLocator.AutoService.OnStatusChanged -= OnStatusChanged;
                ServiceLocator.AutoService.OnStatusChanged += OnStatusChanged;

                // 2. 【核心】订阅 AI 建议事件
                ServiceLocator.AutoService.OnSuggestionReady -= OnSuggestionReceived;
                ServiceLocator.AutoService.OnSuggestionReady += OnSuggestionReceived;

                // 订阅新事件
                ServiceLocator.AutoService.OnChatTargetChanged -= OnChatTargetChanged;
                ServiceLocator.AutoService.OnChatTargetChanged += OnChatTargetChanged;

                if (_loopTimer == null)
                {
                    _loopTimer = new DispatcherTimer();
                    _loopTimer.Interval = TimeSpan.FromSeconds(3); // 3秒检查一次当前窗口
                    _loopTimer.Tick += LoopTimer_Tick;
                }

                _loopTimer.Start();
                lblStatus.Text = "监听中 (辅助模式)";
                lblStatus.Foreground = Brushes.Green;
                lightStatus.Fill = Brushes.Green;
                LogToUI("服务已启动，请打开微信并点击任意授权好友的聊天窗口。");
            }
            catch (Exception ex)
            {
                LogToUI($"启动异常: {ex.Message}");
            }
        }

        // 1. 处理标题更新
        private void OnChatTargetChanged(string title)
        {
            Dispatcher.Invoke(() =>
            {
                // 如果标题为空或者未识别，显示"未知"
                lblCurrentChat.Text = string.IsNullOrEmpty(title) ? "未检测到活动窗口" : title;
            });
        }

        // 2. 处理 AI 建议 (JSON 解析)
        private void OnSuggestionReceived(string rawReply, string originalQuery)
        {
            Dispatcher.Invoke(() =>
            {
                // 1. 更新界面上的"本次读取内容" (需求2)
                if (!string.IsNullOrEmpty(originalQuery))
                {
                    // 如果内容太长，截断显示 (虽然 XAML 的 TextTrimming 也能做，这里双重保险)
                    string displayQuery = originalQuery.Length > 100
                        ? originalQuery.Substring(0, 100) + "..."
                        : originalQuery;

                    lblOriginalQuery.Text = displayQuery;
                    lblOriginalQuery.ToolTip = originalQuery; // 鼠标悬停显示全文

                    // 让这块区域可见
                    borderOriginalQuery.Visibility = Visibility.Visible;
                }

                if (string.IsNullOrWhiteSpace(rawReply)) return;

                try
                {
                    List<SuggestionItem> items = new List<SuggestionItem>();

                    // 2. 【核心修复】清洗 JSON 字符串 (需求1)
                    string jsonStr = CleanJsonString(rawReply);

                    // 3. 尝试解析
                    bool parseSuccess = false;
                    try
                    {
                        var response = JsonConvert.DeserializeObject<SuggestionResponse>(jsonStr);
                        if (response != null && response.suggestions != null)
                        {
                            items = response.suggestions;
                            parseSuccess = true;
                        }
                    }
                    catch
                    {
                        // 解析失败，可能是纯文本建议
                    }

                    // 4. 兜底处理
                    if (!parseSuccess)
                    {
                        // 如果看起来像 JSON 但没解析出来，可能是格式错乱，显示错误提示
                        // 如果看起来不像 JSON (比如本地库返回的纯文本)，直接显示内容
                        string type = jsonStr.Trim().StartsWith("{") ? "parse_error" : "general";

                        items.Add(new SuggestionItem
                        {
                            type = type,
                            content = rawReply // 显示原始回复供参考
                        });
                    }

                    // 5. 绑定列表
                    listSuggestions.ItemsSource = items;
                    lblNoSuggestion.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    LogToUI($"渲染异常: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 清洗大模型返回的 Markdown 格式
        /// </summary>
        private string CleanJsonString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            // 1. 尝试提取 ```json ... ``` 或者是 ``` ... ``` 中间的内容
            // RegexOptions.Singleline 让 . 匹配换行符
            var match = Regex.Match(raw, @"```(?:json)?\s*(.*?)```", RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // 2. 如果没有 markdown 标记，尝试寻找最外层的 { ... }
            int firstBrace = raw.IndexOf('{');
            int lastBrace = raw.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return raw.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return raw.Trim();
        }



        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_loopTimer != null) _loopTimer.Stop();

            // 取消订阅，防止内存泄漏
            if (ServiceLocator.AutoService != null)
            {
                ServiceLocator.AutoService.OnSuggestionReady -= OnSuggestionReceived;
                ServiceLocator.AutoService.OnStatusChanged -= OnStatusChanged;
            }

            lblStatus.Text = "已暂停";
            lblStatus.Foreground = Brushes.Gray;
            lightStatus.Fill = Brushes.Gray;
            LogToUI("服务已停止");
        }

        // 定时器：只负责驱动 Service 读屏
        private async void LoopTimer_Tick(object sender, EventArgs e)
        {
            _loopTimer.Stop();
            try
            {
                if (ServiceLocator.AutoService != null)
                {
                    await ServiceLocator.AutoService.RunCycleAsync();
                }
            }
            catch { }
            finally
            {
                if (lblStatus.Text.Contains("监听中")) _loopTimer.Start();
            }
        }

   

        // 【按钮】用户点击粘贴
        /*
        private async void Paste_Click(object sender, RoutedEventArgs e)
        {
            string content = txtSuggestion.Text;
            if (string.IsNullOrWhiteSpace(content))
            {
                MessageBox.Show("建议内容为空");
                return;
            }

            LogToUI("正在填入微信输入框...");

            // 调用 Service 的粘贴方法
            await ServiceLocator.AutoService.PasteSuggestionToWeChat(content);
        }
        */
        // 3. 点击建议卡片 -> 自动填入
        private async void SuggestionItem_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag == null) return;

            string contentToPaste = btn.Tag.ToString();

            LogToUI($"用户选择建议: {contentToPaste.Substring(0, Math.Min(10, contentToPaste.Length))}...");

            // 调用 Service 填入微信
            await ServiceLocator.AutoService.PasteSuggestionToWeChat(contentToPaste);
        }


        // 状态灯更新
        private void OnStatusChanged(WorkStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                if (status == WorkStatus.Processing)
                {
                    lightStatus.Fill = Brushes.Orange;
                    lblStatus.Text = "思考中...";
                }
                else if (status == WorkStatus.Scanning)
                {
                    lightStatus.Fill = Brushes.Blue;
                    lblStatus.Text = "读取中...";
                }
                else
                {
                    lightStatus.Fill = Brushes.Green;
                    lblStatus.Text = "监听中";
                }
            });
        }

        private void LogToUI(string msg)
        {
            txtConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            txtConsole.ScrollToEnd();
        }

        // 【新增】复制按钮逻辑
        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag == null) return;

            string content = btn.Tag.ToString();
            try
            {
                Clipboard.SetText(content);
                LogToUI("内容已复制到剪贴板");
            }
            catch (Exception ex)
            {
                LogToUI($"复制失败: {ex.Message}");
            }
        }
    }
}