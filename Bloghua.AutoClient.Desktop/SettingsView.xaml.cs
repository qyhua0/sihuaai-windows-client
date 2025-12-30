using System;
using System.Windows;
using System.Windows.Controls;
using Bloghua.AutoClient.Core.Entities;

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class SettingsView : Page
    {
        public SettingsView()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            // 加载配置
            var db = ServiceLocator.Db;
            sliderScan.Value = double.Parse(db.GetSetting("ScanInterval", "6"));
            nbWaitMin.Value = double.Parse(db.GetSetting("ReplyWaitMin", "2"));
            nbWaitMax.Value = double.Parse(db.GetSetting("ReplyWaitMax", "20"));
            tsAutoSend.IsOn = db.IsAutoSend();

            // 加载列表
            gridUsers.ItemsSource = db.GetAllTargets();
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var db = ServiceLocator.Db;
            db.SaveSetting("ScanInterval", sliderScan.Value.ToString());
            db.SaveSetting("ReplyWaitMin", nbWaitMin.Value.ToString());
            db.SaveSetting("ReplyWaitMax", nbWaitMax.Value.ToString());
            db.SaveSetting("IsAutoSend", tsAutoSend.IsOn ? "true" : "false");

            MessageBox.Show("配置已保存，下次扫描生效。");
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtAddName.Text)) return;

            var target = new AuthorizedTarget
            {
                Name = txtAddName.Text,
                BusinessId = txtAddBizId.Text,
                Platform = cmbPlatform.Text,
                Type = "User",
                IsEnabled = true
            };
            ServiceLocator.Db.AddOrUpdateTarget(target);
            gridUsers.ItemsSource = ServiceLocator.Db.GetAllTargets(); // 刷新
            txtAddName.Clear();
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            var target = (sender as Button).DataContext as AuthorizedTarget;
            if (target != null)
            {
                ServiceLocator.Db.DeleteTarget(target.Id);
                gridUsers.ItemsSource = ServiceLocator.Db.GetAllTargets();
            }
        }
    }
}