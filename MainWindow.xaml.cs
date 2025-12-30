using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading; // 用于 DispatcherTimer
using Microsoft.Win32;          // 用于 SaveFileDialog

// 引入项目依赖 (请确保命名空间与您的项目一致)
using Bloghua.AutoClient.Core.Entities;
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Infrastructure.Automation;
using Bloghua.AutoClient.Infrastructure.Data;
using Bloghua.AutoClient.Infrastructure.Image;
using Bloghua.AutoClient.Infrastructure.Input;
using Bloghua.AutoClient.Infrastructure.Services;
using Bloghua.AutoClient.Services;
using MiniExcelLibs; // NuGet: MiniExcel

namespace Bloghua.AutoClient.Desktop
{
    public partial class MainWindow : Window
    {
        // 核心服务
        private WeChatVisualService _service;
        private DatabaseService _db;
        private ILoggerService _logger;

        // 定时器
        private DispatcherTimer _timer;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                // 1. 初始化数据库与日志服务
                _db = new DatabaseService();
                _logger = new FileLoggerService(); // 或者也可以把 _db 传进去做数据库日志

                // 2. 加载 UI 初始状态
                InitializeUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"程序初始化失败: {ex.Message}\n请检查运行环境。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        /// <summary>
        /// 初始化界面数据
        /// </summary>
        private void InitializeUI()
        {
            // 加载全局配置
            chkAutoSend.IsChecked = _db.IsAutoSend();

            // 加载授权用户列表
            RefreshUserGrid();

            // 设置日期选择器默认值 (今天)
            dpStart.SelectedDate = DateTime.Today;
            dpEnd.SelectedDate = DateTime.Today;

            // 初始化日志显示
            AppendLog("程序已就绪，等待启动...");
        }

        /// <summary>
        /// 组装自动化服务 (懒加载，点击启动时才初始化)
        /// </summary>
        private void InitializeAutomationService()
        {
            if (_service != null) return;

            AppendLog("正在初始化自动化组件 (OCR引擎加载中，请稍候)...");

            try
            {
                // 实例化底层设施
                var uia = new UiaService();

                // 注意：PaddleOCR 首次初始化可能需要几秒钟
                var ocr = new PaddleLocalOcrService();

                var input = new Win32InputSimulator();

                // OpenCV 图像定位器 (如果业务逻辑还需要用到图片模板匹配)
                // 如果 WeChatVisualService 已完全改为纯 OCR 方案，这里传 null 并在 Service 里处理即可
                // 这里假设传 null，或者您保留了 OpenCvLocator
                IImageLocator cv = new OpenCvLocator();

                // 注入所有依赖，包括数据库服务 _db
                _service = new WeChatVisualService(uia, ocr, input, cv, _logger, _db);

                // 设置定时器 (每 3 秒执行一次循环)
                _timer = new DispatcherTimer();
                _timer.Interval = TimeSpan.FromSeconds(3);
                _timer.Tick += Timer_Tick;

                AppendLog("组件初始化完成。");
            }
            catch (Exception ex)
            {
                AppendLog($"[致命错误] 组件加载失败: {ex.Message}");
                MessageBox.Show($"组件加载失败: {ex.Message}");
                _service = null; // 重置以便重试
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            // 停止计时器防止重入 (如果在一次 Tick 中耗时超过 Interval)
            // 但由于我们在 Service 内部用了锁机制处理 API 等待，这里可以不暂停，或者为了稳妥暂停一下
            _timer.Stop();

            try
            {
                if (_service != null)
                {
                    await _service.RunCycleAsync();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[循环异常] {ex.Message}");
            }
            finally
            {
                // 任务结束后重启计时器 (实现间隔执行)
                if (lblStatus.Content.ToString() == "运行中")
                {
                    _timer.Start();
                }
            }
        }

        #region --- 运行监控 ---

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_service == null)
            {
                InitializeAutomationService();
                if (_service == null) return; // 初始化失败
            }

            lblStatus.Content = "运行中";
            lblStatus.Foreground = System.Windows.Media.Brushes.Green;
            AppendLog(">>> 服务已启动 <<<");

            _timer.Start();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
            }

            lblStatus.Content = "已停止";
            lblStatus.Foreground = System.Windows.Media.Brushes.Red;
            AppendLog(">>> 服务已停止 <<<");
        }

        // 简单的界面日志追加
        private void AppendLog(string msg)
        {
            txtConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            txtConsole.ScrollToEnd();
        }

        #endregion

        #region --- 会话记录 (查询与导出) ---

        private void SearchLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取时间范围，结束时间加一天以包含当天
                DateTime start = dpStart.SelectedDate ?? DateTime.MinValue;
                DateTime end = dpEnd.SelectedDate.HasValue ? dpEnd.SelectedDate.Value.AddDays(1) : DateTime.MaxValue;

                string nameKey = txtSearchName.Text?.Trim();
                string contentKey = txtSearchContent.Text?.Trim();

                var logs = _db.SearchLogs(nameKey, contentKey, start, end);
                gridLogs.ItemsSource = logs;

                AppendLog($"查询完成，共找到 {logs.Count} 条记录。");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询出错: {ex.Message}");
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var logs = gridLogs.ItemsSource as List<ChatLog>;
            if (logs == null || logs.Count == 0)
            {
                MessageBox.Show("当前表格没有数据，请先执行查询。");
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出聊天记录",
                Filter = "Excel 文件|*.xlsx",
                FileName = $"ChatLogs_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    // 使用 MiniExcel 快速导出
                    MiniExcel.SaveAs(sfd.FileName, logs);
                    MessageBox.Show($"导出成功！\n路径: {sfd.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}\n请确保文件未被占用。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region --- 系统配置 (用户管理) ---

        // 自动发送开关变更
        private void Config_Changed(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;
            // 将 bool 转换为 string 存入数据库
            _db.SaveSetting("IsAutoSend", chkAutoSend.IsChecked == true ? "true" : "false");
            AppendLog($"配置更新: 自动发送 = {chkAutoSend.IsChecked}");
        }

        // 刷新用户列表表格
        private void RefreshUserGrid()
        {
            if (_db == null) return;
            gridUsers.ItemsSource = _db.GetAllTargets();
        }

        // 添加或更新用户
        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            string name = txtAddName.Text?.Trim();
            string bizId = txtAddBizId.Text?.Trim();
            string platform = cmbPlatform.Text;

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入名称 (微信昵称/群名)。");
                return;
            }

            try
            {
                var target = new AuthorizedTarget
                {
                    Name = name,
                    BusinessId = string.IsNullOrEmpty(bizId) ? "Default" : bizId,
                    Platform = platform,
                    Type = "User", // 暂时默认为 User，后续可加下拉框区分 Group
                    IsEnabled = true
                };

                // 这里逻辑是简单的添加，如果要做修改逻辑，需要获取 Grid 选中的 ID
                // 为了简单演示，我们假设名字重复就是更新，或者直接由 DB Service 处理
                // 实际项目中建议先判断是否存在

                _db.AddOrUpdateTarget(target);

                RefreshUserGrid();

                txtAddName.Clear();
                txtAddBizId.Clear();
                AppendLog($"已添加授权目标: {name} ({platform})");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}");
            }
        }

        // 删除用户
        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要删除该授权配置吗？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            try
            {
                // 获取按钮所在的行数据
                var btn = sender as Button;
                var target = btn.DataContext as AuthorizedTarget;

                if (target != null)
                {
                    _db.DeleteTarget(target.Id);
                    RefreshUserGrid();
                    AppendLog($"已删除授权目标: {target.Name}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}");
            }
        }

        #endregion
    }
}