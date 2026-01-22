using System;
using System.Windows;
using System.Windows.Controls;
using Bloghua.AutoClient.Core.Entities;
using System.Windows.Media; // 用于设置测试结果颜色
using Bloghua.AutoClient.Services;
using System.Windows.Navigation; // 必须引用，用于 OnNavigatedTo


namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class SettingsView : Page
    {

        private int _clickCount = 0; // 点击计数器

        public SettingsView()
        {
            InitializeComponent();
            // LoadData();
            // 【核心修复】使用 Loaded 事件代替 OnNavigatedTo
            // 每次页面显示（包括从缓存切回来）都会触发 Loaded
            this.Loaded += SettingsView_Loaded;
        }

  

        // 页面加载完成时触发
        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
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
           // pbAppSecret.Password = db.GetSetting("AppSecret", "");
          //  pbApiPwd.Password = db.GetSetting("ApiPwd", "");

            // PasswordBox 必须每次手动回填
            // 从数据库读取保存的密码
            string savedSecret = db.GetSetting("AppSecret", "");
            string savedPwd = db.GetSetting("ApiPwd", "");

            // 只有当数据库里有值时才回填，避免覆盖用户正在输入的内容(虽然OnNavigatedTo通常是在切换进来时)
            if (!string.IsNullOrEmpty(savedSecret))
            {
                pbAppSecret.Password = savedSecret;
            }

            if (!string.IsNullOrEmpty(savedPwd))
            {
                pbApiPwd.Password = savedPwd;
            }

            // 列表
            gridUsers.ItemsSource = db.GetAllTargets();

            // API 基础
            txtApiBaseUrl.Text = db.GetSetting("ApiBaseUrl", "http://127.0.0.1:8000/api/open");
            txtAppId.Text = db.GetSetting("AppId", "");
            txtAesKey.Text = db.GetSetting("AesKey", "");
            txtAesIv.Text = db.GetSetting("AesIv", "");


            // --- 加载角色列表到下拉框 ---
            var roles = db.GetAllRoles();
            cmbGlobalRole.ItemsSource = roles;

            // --- 回显选中的默认角色 ---
            string globalRole = db.GetSetting("GlobalDefaultRole", "");

            // 如果还没设置过全局默认，尝试选一个 API 标记为默认的
            if (string.IsNullOrEmpty(globalRole))
            {
                globalRole = db.GetDefaultRoleCode();
            }

            cmbGlobalRole.SelectedValue = globalRole;

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

            // --- 保存全局默认角色 ---
            if (cmbGlobalRole.SelectedValue != null)
            {
                db.SaveSetting("GlobalDefaultRole", cmbGlobalRole.SelectedValue.ToString());
            }


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

        // 彩蛋触发逻辑
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



        // 测试登录逻辑
        private async void TestLogin_Click(object sender, RoutedEventArgs e)
        {
            lblTestResult.Text = "正在连接...";
            lblTestResult.Foreground = Brushes.Gray;

            string url = txtApiBaseUrl.Text.Trim();
            string user = txtApiUser.Text.Trim();
            string pwd = pbApiPwd.Password.Trim(); // 取当前输入框的值，不是数据库的

            var api = new ChatApiService();
            var result = await api.TestConnectionAsync(url, user, pwd);

            if (result.success)
            {
                lblTestResult.Text = "✔ 测试通过";
                lblTestResult.Foreground = Brushes.Green;
            }
            else
            {
                lblTestResult.Text = "❌ " + result.message;
                lblTestResult.Foreground = Brushes.Red;
            }
        }


        private async void SyncRoles_Click(object sender, RoutedEventArgs e)
        {
            // 先保存当前配置，确保 API 能读取到最新的 Key
            SaveSettings_Click(null, null);

            lblTestResult.Text = "正在拉取角色...";
            lblTestResult.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                var api = new ChatApiService();
                var roles = await api.GetPromptsAsync();

                if (roles != null && roles.Count > 0)
                {
                    ServiceLocator.Db.SaveRoles(roles);

                    // 刷新下拉框数据源
                    cmbGlobalRole.ItemsSource = ServiceLocator.Db.GetAllRoles();

                    lblTestResult.Text = $"✔ 成功同步 {roles.Count} 个角色";
                    lblTestResult.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    lblTestResult.Text = "❌ 未获取到角色或列表为空";
                    lblTestResult.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                lblTestResult.Text = "❌ 同步失败";
                MessageBox.Show(ex.Message);
            }
        }
    }
}