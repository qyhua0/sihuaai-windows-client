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

namespace Bloghua.AutoClient.Desktop
{
    public partial class MainWindow : Window
    {
        // 页面缓存：显式指定 System.Windows.Controls.Page
        private Dictionary<string, System.Windows.Controls.Page> _pageCache = new Dictionary<string, System.Windows.Controls.Page>();

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
    }
}