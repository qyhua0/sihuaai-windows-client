using System.Windows.Controls;
using Bloghua.AutoClient.Desktop.ViewModels; // 引用下面的 ViewModel

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class QAView : Page
    {
        public QAView()
        {
            InitializeComponent();
            // 绑定数据上下文
            this.DataContext = new QAViewModel();
        }
    }
}