
using System.Drawing;
using Bloghua.AutoClient.Core.Interfaces;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace Bloghua.AutoClient.Infrastructure.Image
{
    public class OpenCvLocator : IImageLocator
    {
        public System.Drawing.Point? FindImageCenter(Bitmap source, string templatePath, double threshold = 0.8)
        {
            try
            {
                using (var matSource = BitmapConverter.ToMat(source))
                using (var matTemplate = new Mat(templatePath))
                using (var result = new Mat())
                {
                    // 模板匹配
                    Cv2.MatchTemplate(matSource, matTemplate, result, TemplateMatchModes.CCoeffNormed);

                    double minVal, maxVal;
                    OpenCvSharp.Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

                    if (maxVal >= threshold)
                    {
                        return new System.Drawing.Point(
                            maxLoc.X + matTemplate.Width / 2,
                            maxLoc.Y + matTemplate.Height / 2
                        );
                    }
                }
            }
            catch
            {
                // 日志记录：模板图片不存在或OpenCV错误
            }
            return null;
        }
    }
}
