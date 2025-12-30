using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bloghua.AutoClient.Infrastructure.ImageProcessing
{
    public static class BitmapFilter
    {
        /// <summary>
        /// 针对微信浅色文字 (#eaeaea) 进行专项加黑
        /// </summary>
        public static Bitmap DarkenSpecificGray(Bitmap original)
        {
            // 复制一份，不修改原图
            Bitmap bmp = new Bitmap(original);

            // 锁定整个位图的像素数据
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

            // 获取图像的总字节数
            int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
            byte[] rgbValues = new byte[bytes];

            // 将数据复制到托管字节数组
            Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

            // 【修改点】手动获取每个像素的字节数 (步长)
            // 微信截图通常是 32位 (4字节) 或 24位 (3字节)
            int pixelSize = 4; // 默认为 32位
            if (bmp.PixelFormat == PixelFormat.Format24bppRgb)
            {
                pixelSize = 3;
            }
            else if (bmp.PixelFormat == PixelFormat.Format32bppArgb ||
                     bmp.PixelFormat == PixelFormat.Format32bppRgb ||
                     bmp.PixelFormat == PixelFormat.Format32bppPArgb)
            {
                pixelSize = 4;
            }
            else
            {
                // 如果遇到索引颜色(8位)等特殊格式，直接返回原图，避免报错
                bmp.UnlockBits(bmpData);
                return bmp;
            }

            // 目标颜色: #eaeaea (234, 234, 234)
            // 捕捉范围: 220 ~ 248
            byte minThreshold = 220;
            byte maxThreshold = 248;

            for (int i = 0; i < bytes - pixelSize; i += pixelSize)
            {
                // Windows 位图通常是 BGR 顺序
                byte b = rgbValues[i];
                byte g = rgbValues[i + 1];
                byte r = rgbValues[i + 2];

                // 1. 判断是否是灰色 (R, G, B 数值非常接近)
                // 容差设为 10，越小越严格
                bool isGray = (Math.Abs(r - g) < 10) && (Math.Abs(g - b) < 10);

                // 2. 判断亮度是否在我们的目标范围内 (#eaeaea 附近)
                bool inRange = (r > minThreshold && r < maxThreshold);

                if (isGray && inRange)
                {
                    // 命中目标！涂成纯黑
                    rgbValues[i] = 0;     // B
                    rgbValues[i + 1] = 0; // G
                    rgbValues[i + 2] = 0; // R

                    // 如果是 32 位图，把 Alpha 通道设为 255 (完全不透明)
                    if (pixelSize == 4)
                    {
                        rgbValues[i + 3] = 255;
                    }
                }
            }

            // 将修改后的数据复制回位图
            Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
            bmp.UnlockBits(bmpData);

            return bmp;
        }
    }
}