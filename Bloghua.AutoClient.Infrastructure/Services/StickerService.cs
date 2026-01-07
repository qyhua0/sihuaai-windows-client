using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Bloghua.AutoClient.Infrastructure.Services
{
    public class StickerService
    {
        private readonly Dictionary<string, ulong> _stickerHashes = new Dictionary<string, ulong>();
        private readonly string _stickerPath;

        public StickerService()
        {
            _stickerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Stickers");
            if (!Directory.Exists(_stickerPath)) Directory.CreateDirectory(_stickerPath);
            LoadStickers();
        }

        public void LoadStickers()
        {
            _stickerHashes.Clear();
            var files = Directory.GetFiles(_stickerPath, "*.*")
                .Where(s => s.EndsWith(".jpg") || s.EndsWith(".png") || s.EndsWith(".bmp"));

            foreach (var file in files)
            {
                using (var bmp = new Bitmap(file))
                {
                    ulong hash = CalculateDHash(bmp);
                    // 文件名即语义，如 "微笑.jpg" -> Key="[微笑]"
                    string name = $"[{Path.GetFileNameWithoutExtension(file)}]";
                    _stickerHashes[name] = hash;
                }
            }
        }

        /// <summary>
        /// 尝试匹配表情包
        /// </summary>
        /// <param name="targetBmp">聊天气泡截图</param>
        /// <returns>表情含义 (如 [微笑])，未匹配到返回 null</returns>
        public string MatchSticker(Bitmap targetBmp)
        {
            ulong targetHash = CalculateDHash(targetBmp);

            // 寻找汉明距离最小的 (差异越小越相似)
            int minDistance = int.MaxValue;
            string bestMatch = null;

            foreach (var kvp in _stickerHashes)
            {
                int distance = CalcHammingDistance(targetHash, kvp.Value);

                // 阈值：dHash 距离小于 5 通常认为是同一张图
                // 稍微放宽到 10 以适应微信截图的压缩噪点
                if (distance < 10 && distance < minDistance)
                {
                    minDistance = distance;
                    bestMatch = kvp.Key;
                }
            }

            return bestMatch;
        }

        // --- dHash 算法实现 (纯C#，无需 OpenCV) ---

        private ulong CalculateDHash(Bitmap image)
        {
            // 1. 缩放到 9x8 (差异哈希需要 N+1 列)
            using (Bitmap resized = ResizeImage(image, 9, 8))
            {
                ulong hash = 0;
                // 2. 遍历像素，比较左边和右边的亮度
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        var pLeft = resized.GetPixel(x, y);
                        var pRight = resized.GetPixel(x + 1, y);

                        // 简化亮度计算
                        float brightLeft = pLeft.GetBrightness();
                        float brightRight = pRight.GetBrightness();

                        if (brightLeft > brightRight)
                        {
                            hash |= (1UL << (y * 8 + x));
                        }
                    }
                }
                return hash;
            }
        }

        private Bitmap ResizeImage(Bitmap image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            using (var graphics = Graphics.FromImage(destImage))
            {
                // 高质量缩放
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }
            return destImage;
        }

        private int CalcHammingDistance(ulong a, ulong b)
        {
            ulong x = a ^ b; // 异或，不同位为1
            int dist = 0;
            while (x > 0)
            {
                dist++;
                x &= x - 1; // 清除最低位的1
            }
            return dist;
        }
    }
}