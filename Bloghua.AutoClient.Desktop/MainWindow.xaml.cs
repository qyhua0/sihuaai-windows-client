using System;
using System.Collections.Generic;
using System.Windows;
using ModernWpf.Controls; // NavigationView 在这里
// 注意：不要引用 System.Windows.Controls，避免 Page 冲突，或者在代码中显式指定

using Bloghua.AutoClient.Desktop.Views;
using Bloghua.AutoClient.Infrastructure.Automation;
using Bloghua.AutoClient.Infrastructure.Data;
using Bloghua.AutoClient.Infrastructure.Image;
using Bloghua.AutoClient.Infrastructure.Input;
using Bloghua.AutoClient.Infrastructure.Services;
using Bloghua.AutoClient.Services;
using Bloghua.AutoClient.Core.Interfaces;

// 引入 Forms 和 Drawing 命名空间，为了避免冲突，使用别名
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Bloghua.AutoClient.Desktop
{
    public partial class MainWindow : Window
    {
        // 页面缓存：显式指定 System.Windows.Controls.Page
        private Dictionary<string, System.Windows.Controls.Page> _pageCache = new Dictionary<string, System.Windows.Controls.Page>();
        private WinForms.NotifyIcon _notifyIcon;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                // 初始化全局服务
                ServiceLocator.Db = new DatabaseService();
                ServiceLocator.Logger = new FileLoggerService();

                // 默认选中第一个菜单项
                NavView.SelectedItem = NavView.MenuItems[0];

                // 【新增】订阅用户信息更新事件
                ServiceLocator.OnUserInfoUpdated += UpdateAppTitle;

                // 【新增】初始化时立即更新一次标题
                UpdateAppTitle();


            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}");
            }


            // 监听加载完成事件，设置窗口位置
            this.Loaded += MainWindow_Loaded;

            try
            {
                ServiceLocator.Db = new DatabaseService();
                ServiceLocator.Logger = new FileLoggerService();
                NavView.SelectedItem = NavView.MenuItems[0];
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}");
            }

            // 【新增】初始化托盘图标
            InitSystemTray();
        }

        private void UpdateAppTitle()
        {
            // 必须在 UI 线程执行
            Dispatcher.Invoke(() =>
            {
                string username = ServiceLocator.Db.GetSetting("ApiUser", "");

                if (!string.IsNullOrEmpty(username))
                {
                    this.Title = $"AI 客服助手 - [ {username} ]";
                }
                else
                {
                    // this.Title = "AI 客服助手 - [ 未登录 ]";
                    this.Title = "AI 客服助手";

                }
            });
        }

        // 设置主窗口位置：屏幕右下角
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取屏幕工作区 (排除任务栏)
            var workArea = SystemParameters.WorkArea;

            // 计算 Left: 屏幕最右边 - 窗口宽度 - 5px
            this.Left = workArea.Right - this.Width - 5;

            // 计算 Top: 屏幕最下边 - 窗口高度
            // 这样刚好坐在任务栏上方，不会遮挡
            this.Top = workArea.Bottom - this.Height-5;
        }

        public static void InitAutoService()
        {
            if (ServiceLocator.AutoService != null) return;

            try
            {
                var uia = new UiaService();
                var ocr = new PaddleLocalOcrService();
                var input = new Win32InputSimulator();
                IImageLocator cv = new OpenCvLocator();

                ServiceLocator.AutoService = new WeChatVisualService(
                    uia, ocr, input, cv, ServiceLocator.Logger, ServiceLocator.Db,null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"组件加载失败: {ex.Message}");
                throw;
            }
        }

        // 【关键修复】补回了这个丢失的方法
        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                NavigateToPage("Settings");
            }
            else
            {
                var item = args.SelectedItem as NavigationViewItem;
                if (item == null || item.Tag == null) return;

                NavigateToPage(item.Tag.ToString());
            }
        }

        // 统一导航逻辑
        private void NavigateToPage(string tag)
        {
            System.Windows.Controls.Page page = null;

            // 1. 检查缓存
            if (_pageCache.ContainsKey(tag))
            {
                page = _pageCache[tag];
            }
            else
            {
                // 2. 创建新页面
                switch (tag)
                {
                    case "Monitoring":
                        page = new MonitoringView();
                        break;
                    case "Practice": // 【新增】
                        page = new PracticeView();
                        break;
                    case "Logs":
                        page = new LogView();
                        break;
                    case "QA":
                        page = new QAView();
                        break;
                    case "Settings":
                        page = new SettingsView();
                        break;
                    case "About": 
                        page = new AboutView();
                        break;
                }

                if (page != null)
                {
                    _pageCache[tag] = page;
                }
            }

            // 3. 执行跳转
            if (page != null)
            {
                ContentFrame.Navigate(page);
            }
        }




        private void InitSystemTray()
        {
            _notifyIcon = new WinForms.NotifyIcon();

            // 尝试获取当前程序的图标作为托盘图标
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                _notifyIcon.Icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch
            {
                // 如果获取失败，使用系统默认图标兜底
                _notifyIcon.Icon = Drawing.SystemIcons.Application;
            }

            _notifyIcon.Text = "斯华AI客服助手";
            _notifyIcon.Visible = true;

            // 双击托盘图标 -> 恢复窗口
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

            // 右键菜单
            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("显示主界面", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("-"); // 分割线
            contextMenu.Items.Add("退出程序", null, (s, e) => ExitApplication());

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        // 窗口状态改变事件 (最小化时隐藏)
        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide(); // 隐藏任务栏图标

                // 可选：弹个气泡提示
                // _notifyIcon.ShowBalloonTip(2000, "提示", "程序已最小化到托盘", WinForms.ToolTipIcon.Info);
            }
            base.OnStateChanged(e);
        }

        // 窗口关闭事件 (拦截关闭按钮，改为最小化)
        // 如果您希望点 X 是彻底退出，可以去掉这个重写方法
        // 如果您希望点 X 是最小化到托盘，请保留
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 这里的逻辑是：点击关闭按钮 -> 最小化到托盘
            // 只有通过托盘菜单的"退出"或者代码显式 Shutdown 才能真退出
            e.Cancel = true;
            this.WindowState = WindowState.Minimized;
        }

        private void ShowWindow()
        {
            this.Show(); // 显示窗口
            this.WindowState = WindowState.Normal; // 恢复大小
            this.Activate(); // 激活焦点
        }

        private void ExitApplication()
        {
            // 1. 销毁托盘图标 (否则退出后托盘里会有个残影)
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            

            // 3. 强制退出
            Application.Current.Shutdown();
        }

    }
}