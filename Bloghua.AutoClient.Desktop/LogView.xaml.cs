using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Bloghua.AutoClient.Core.Entities;
using Microsoft.Win32;
using MiniExcelLibs;

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class LogView : Page
    {
        public LogView()
        {
            InitializeComponent();
            dpStart.SelectedDate = DateTime.Today;
            dpEnd.SelectedDate = DateTime.Today;
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            DateTime start = dpStart.SelectedDate ?? DateTime.MinValue;
            DateTime end = dpEnd.SelectedDate.HasValue ? dpEnd.SelectedDate.Value.AddDays(1) : DateTime.MaxValue;

            // 使用 ServiceLocator 访问数据库
            var logs = ServiceLocator.Db.SearchLogs(txtSearchName.Text, txtSearchContent.Text, start, end);
            gridLogs.ItemsSource = logs;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var logs = gridLogs.ItemsSource as List<ChatLog>;
            if (logs == null || logs.Count == 0) return;

            SaveFileDialog sfd = new SaveFileDialog { Filter = "Excel|*.xlsx", FileName = $"Logs_{DateTime.Now:MMdd}.xlsx" };
            if (sfd.ShowDialog() == true)
            {
                MiniExcel.SaveAs(sfd.FileName, logs);
                MessageBox.Show("导出成功");
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定清空所有历史记录吗？", "警告", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                ServiceLocator.Db.ClearAllLogs(); // 需在 DatabaseService 实现此方法
                gridLogs.ItemsSource = null;
            }
        }
    }
}