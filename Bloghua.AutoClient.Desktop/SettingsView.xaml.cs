using System;
using System.Windows;
using System.Windows.Controls;
using Bloghua.AutoClient.Core.Entities;

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class SettingsView : Page
    {

        private int _clickCount = 0; // 点击计数器

        public SettingsView()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            var db = ServiceLocator.Db;
            // 基础配置
            sliderScan.Value = double.Parse(db.GetSetting("ScanInterval", "6"));
            nbWaitMin.Value = double.Parse(db.GetSetting("ReplyWaitMin", "2"));
            nbWaitMax.Value = double.Parse(db.GetSetting("ReplyWaitMax", "20"));
            tsAutoSend.IsOn = db.IsAutoSend();

            string autoActive = db.GetSetting("AutoActiveWindow", "true");
            tsAutoActive.IsOn = (autoActive == "true");

            // API 配置
            txtApiBaseUrl.Text = db.GetSetting("ApiBaseUrl", "http://127.0.0.1:8000/api/open");
            txtAppId.Text = db.GetSetting("AppId", "");
            txtAesKey.Text = db.GetSetting("AesKey", "");
            txtAesIv.Text = db.GetSetting("AesIv", "");
            txtApiUser.Text = db.GetSetting("ApiUser", "");

            // 【注意】PasswordBox 赋值方式不同
            pbAppSecret.Password = db.GetSetting("AppSecret", "");
            pbApiPwd.Password = db.GetSetting("ApiPwd", "");

            // 列表
            gridUsers.ItemsSource = db.GetAllTargets();
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var db = ServiceLocator.Db;

            // 保存基础配置
            db.SaveSetting("ScanInterval", sliderScan.Value.ToString());
            db.SaveSetting("ReplyWaitMin", nbWaitMin.Value.ToString());
            db.SaveSetting("ReplyWaitMax", nbWaitMax.Value.ToString());
            db.SaveSetting("IsAutoSend", tsAutoSend.IsOn ? "true" : "false");

            // 保存新配置
            db.SaveSetting("AutoActiveWindow", tsAutoActive.IsOn ? "true" : "false");

            // 保存 API 配置
            db.SaveSetting("ApiBaseUrl", txtApiBaseUrl.Text.Trim());
            db.SaveSetting("AppId", txtAppId.Text.Trim());
            db.SaveSetting("AesKey", txtAesKey.Text.Trim());
            db.SaveSetting("AesIv", txtAesIv.Text.Trim());
            db.SaveSetting("ApiUser", txtApiUser.Text.Trim());

            // 【注意】PasswordBox 取值方式不同
            db.SaveSetting("AppSecret", pbAppSecret.Password.Trim());
            db.SaveSetting("ApiPwd", pbApiPwd.Password.Trim());

            MessageBox.Show("配置已保存。");
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
            gridUsers.ItemsSource = ServiceLocator.Db.GetAllTargets();
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

        // 【新增】彩蛋触发逻辑
        private void SecretTitle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _clickCount++;
            if (_clickCount >= 5)
            {
                // 激活隐藏功能
                panelSecretAuto.Visibility = Visibility.Visible;
                panelSecretTargets.Visibility = Visibility.Visible;

                MessageBox.Show("开发者模式已激活！\n请谨慎操作全自动与授权管理功能。", "系统提示");
                _clickCount = 0; // 重置
            }
        }
    }
}