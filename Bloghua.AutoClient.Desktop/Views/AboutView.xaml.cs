using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class AboutView : Page
    {
        public AboutView()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // 调用系统默认浏览器打开链接
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}