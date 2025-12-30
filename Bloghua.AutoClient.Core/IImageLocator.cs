using System.Drawing;
using System.Windows;


namespace Bloghua.AutoClient.Core.Interfaces
{
    public interface IImageLocator
    {
        // 图像匹配：在大图中找小图，返回中心点坐标
        System.Drawing.Point? FindImageCenter(Bitmap source, string templatePath, double threshold = 0.8);
    }
}