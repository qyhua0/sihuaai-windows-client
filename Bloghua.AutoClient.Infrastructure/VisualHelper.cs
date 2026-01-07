using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Bloghua.AutoClient.Infrastructure.Image
{
    public static class VisualHelper
    {
        /// <summary>
        /// 寻找第二栏(列表)和第三栏(聊天)之间的垂直分割线 X 坐标
        /// </summary>
        public static int FindVerticalSplitLine(Bitmap bmp)
        {
            // 锁定内存加快速度
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);

            // 扫描范围：假设分割线在 X=250 到 X=660 之间
            int startX = 250;
            int endX = 660;
            int splitX = -1;

            int height = bmp.Height;
            int stride = Math.Abs(bmpData.Stride);
            int pixelSize = (bmp.PixelFormat == PixelFormat.Format24bppRgb) ? 3 : 4;

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;

                // 按列扫描 X
                for (int x = startX; x < endX; x++)
                {
                    int grayCount = 0;

                    // 按行扫描 Y (采样扫描，不必每行都扫，步长为5)
                    for (int y = 50; y < height - 50; y += 5)
                    {
                        // 计算像素位置
                        int index = (y * stride) + (x * pixelSize);

                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        // 微信的分隔线特征：
                        // 1. 它是灰色的 (R=G=B)
                        // 2. 它的颜色通常在 #e0e0e0 到 #f0f0f0 之间 (224 - 240)
                        // 3. 或者是深色模式下的特定深灰

                        bool isGray = (Math.Abs(r - g) < 5) && (Math.Abs(g - b) < 5);
                        // 针对浅色模式的分割线判定
                        bool isLineColor = (r > 210 && r < 245);

                        if (isGray && isLineColor)
                        {
                            grayCount++;
                        }
                    }

                    // 如果这一列 80% 以上的像素都是这个颜色，那它就是分割线
                    if (grayCount > (height - 100) / 5 * 0.8)
                    {
                        splitX = x;
                        break;
                    }
                }
            }

            bmp.UnlockBits(bmpData);

            // 如果没找到，返回默认值 300
            return splitX > 0 ? splitX : 300;
        }

        /// <summary>
        /// 列表专用滤镜：只保留深色文字(名字)，过滤浅色文字(预览消息)
        /// </summary>
        public static Bitmap KeepDarkTextOnly(Bitmap original)
        {
            Bitmap bmp = new Bitmap(original);
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

            int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
            byte[] rgbValues = new byte[bytes];
            Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

            int pixelSize = 4; // 假设32位
            if (bmp.PixelFormat == PixelFormat.Format24bppRgb) pixelSize = 3;

            for (int i = 0; i < bytes - pixelSize; i += pixelSize)
            {
                byte b = rgbValues[i];
                byte g = rgbValues[i + 1];
                byte r = rgbValues[i + 2];

                // 计算亮度 (简单的平均值)
                int brightness = (r + g + b) / 3;

                // 【核心逻辑】
                // 微信好友名字是黑色 (亮度 < 50)
                // 预览消息是灰色 (亮度 > 150)
                // 背景是白色 (亮度 > 240)

                // 阈值设为 150：比这个亮的(灰色/白色)全部涂白，比这个暗的(黑色名字)保留
                if (brightness > 150)
                {
                    rgbValues[i] = 255;     // B
                    rgbValues[i + 1] = 255; // G
                    rgbValues[i + 2] = 255; // R
                }
            }

            Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
            bmp.UnlockBits(bmpData);
            return bmp;
        }


        /// <summary>
        /// 在指定 X 范围内，寻找水平分割线 Y 坐标
        /// </summary>
        /// <param name="bmp">全屏截图</param>
        /// <param name="startX">扫描区域左边界 (即垂直分割线位置)</param>
        /// <param name="minY">扫描起始高度</param>
        /// <param name="maxY">扫描结束高度</param>
        /// <returns>找到的 Y 坐标，未找到返回 -1</returns>
        public static int FindHorizontalLine1(Bitmap bmp, int startX, int minY, int maxY)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);

            int width = bmp.Width;
            int stride = Math.Abs(bmpData.Stride);
            int pixelSize = (bmp.PixelFormat == PixelFormat.Format24bppRgb) ? 3 : 4;

            // 扫描结束 X (留出右侧滚动条的空间，减去 20px)
            int endX = width - 20;
            int targetY = -1;

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;

                // 按行扫描 Y
                for (int y = minY; y < maxY; y++)
                {
                    int grayCount = 0;
                    int totalSampled = 0;

                    // 按列扫描 X (为了性能，每隔 5 个像素采样一次)
                    for (int x = startX + 10; x < endX; x += 5)
                    {
                        totalSampled++;
                        int index = (y * stride) + (x * pixelSize);

                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        // 灰线特征判定 (颜色范围 220-245，且 RGB 接近)
                        bool isGray = (Math.Abs(r - g) < 5) && (Math.Abs(g - b) < 5);
                        bool isLineColor = (r > 210 && r < 248);

                        if (isGray && isLineColor)
                        {
                            grayCount++;
                        }
                    }

                    // 如果这一行超过 70% 的像素符合灰线特征，且是一条长线
                    if (totalSampled > 0 && grayCount > (totalSampled * 0.8))
                    {
                        targetY = y;
                        break; // 找到第一条符合的线就停止
                    }
                }
            }

            bmp.UnlockBits(bmpData);
            return targetY;
        }


        /// <summary>
        /// 寻找水平分割线 (带调试图保存)
        /// </summary>
        /// <param name="bmp">原始截图</param>
        /// <param name="startX">垂直分割线位置 (扫描该线右侧)</param>
        /// <param name="minY">扫描起始Y</param>
        /// <param name="maxY">扫描结束Y</param>
        /// <param name="debugName">调试文件名标识 (如 "top_line" 或 "bottom_line")</param>
        public static int FindHorizontalLineDebug(Bitmap bmp, int startX, int minY, int maxY, string debugName)
        {
            // 范围校验
            if (minY < 0) minY = 0;
            if (maxY >= bmp.Height) maxY = bmp.Height - 1;
            int width = bmp.Width;
            int endX = width - 30; // 避开右侧滚动条

            // ==========================================
            // 准备调试画布 (复制一份原图)
            // ==========================================
            Bitmap debugBmp = new Bitmap(bmp);

            // 锁定原图 (只读)
            BitmapData srcData = bmp.LockBits(new Rectangle(0, 0, width, height: bmp.Height), ImageLockMode.ReadOnly, bmp.PixelFormat);

            // 锁定调试图 (读写，用于画红线)
            BitmapData debugData = debugBmp.LockBits(new Rectangle(0, 0, width, height: bmp.Height), ImageLockMode.ReadWrite, debugBmp.PixelFormat);

            int stride = Math.Abs(srcData.Stride);
            int pixelSize = (bmp.PixelFormat == PixelFormat.Format24bppRgb) ? 3 : 4;

            int foundY = -1;

            unsafe
            {
                byte* srcPtr = (byte*)srcData.Scan0;
                byte* debugPtr = (byte*)debugData.Scan0;

                // 目标颜色: #d4d4d4 (212)
                // 背景颜色: #ededed (237)
                // 策略: 严格只允许 200 - 225 之间的颜色
                byte targetMin = 200;
                byte targetMax = 225;

                for (int y = minY; y < maxY; y++)
                {
                    int matchPixelCount = 0;
                    int totalChecked = 0;

                    // 按列扫描 X
                    for (int x = startX + 10; x < endX; x++)
                    {
                        totalChecked++;
                        int index = (y * stride) + (x * pixelSize);

                        byte b = srcPtr[index];
                        byte g = srcPtr[index + 1];
                        byte r = srcPtr[index + 2];

                        // 1. 必须是灰色 (RGB 差异极小)
                        bool isGray = (Math.Abs(r - g) < 5) && (Math.Abs(g - b) < 5);

                        // 2. 【核心】严格的亮度范围
                        // 212 会命中，237 会失败
                        bool isTargetColor = (r >= targetMin && r <= targetMax);

                        if (isGray && isTargetColor)
                        {
                            matchPixelCount++;

                            // 【调试】把符合条件的像素点涂成红色 (R=255, G=0, B=0)
                            // 这样打开图一看就知道程序有没有找对点
                            debugPtr[index] = 0;     // B
                            debugPtr[index + 1] = 0; // G
                            debugPtr[index + 2] = 255; // R
                        }
                    }

                    // 判定条件：这一行超过 60% 的像素符合要求
                    if (totalChecked > 0 && matchPixelCount > (totalChecked * 0.6))
                    {
                        // 再次确认：这是一条细线，它的上一行应该是背景色(>230)
                        // 取上一行中间的一个点检查
                        int prevY = y - 1;
                        if (prevY >= 0)
                        {
                            int prevIndex = (prevY * stride) + ((startX + 50) * pixelSize);
                            byte prevR = srcPtr[prevIndex + 2];

                            // 如果上一行是浅色背景(>230)，说明这是边界，找到了！
                            if (prevR > 230)
                            {
                                foundY = y;

                                // 【调试】把找到的这整行涂成绿色 (R=0, G=255, B=0)
                                for (int dx = startX; dx < endX; dx++)
                                {
                                    int dIndex = (y * stride) + (dx * pixelSize);
                                    debugPtr[dIndex] = 0;
                                    debugPtr[dIndex + 1] = 255;
                                    debugPtr[dIndex + 2] = 0;
                                }
                                break; // 停止扫描
                            }
                        }
                    }
                }
            }

            bmp.UnlockBits(srcData);
            debugBmp.UnlockBits(debugData);

            // 保存调试图片
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"debug_scan_{debugName}.png");
                debugBmp.Save(path, ImageFormat.Png);
                // System.Diagnostics.Debug.WriteLine($"调试图已保存: {path}");
            }
            catch { }
            finally
            {
                debugBmp.Dispose();
            }

            return foundY;
        }


        /// <summary>
        /// 气泡类型枚举
        /// </summary>
        public enum BubbleType
        {
            None,
            Received, // 白色气泡 (对方)
            Sent      // 绿色气泡 (自己)
        }

        public class BubbleResult
        {
            public Rectangle Rect;
            public BubbleType Type;
        }

        /// <summary>
        /// 从底部向上查找最后一个聊天气泡的位置
        /// </summary>
        public static BubbleResult FindLastBubble(Bitmap bmp)
        {
            // 锁定内存
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);

            int stride = Math.Abs(bmpData.Stride);
            int pixelSize = (bmp.PixelFormat == PixelFormat.Format24bppRgb) ? 3 : 4;
            int width = bmp.Width;
            int height = bmp.Height;

            int foundBottomY = -1;
            int foundTopY = -1;
            int foundMinX = width;
            int foundMaxX = 0;
            BubbleType foundType = BubbleType.None;

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;

                // 1. 从下往上扫描 Y，寻找气泡的“底部边缘”
                // 留出底部 5px 的边距，防止贴底
                for (int y = height - 5; y >= 5; y--)
                {
                    // 按行扫描 X
                    // 为了性能，水平步长设为 4
                    for (int x = 10; x < width - 30; x += 4)
                    {
                        int index = (y * stride) + (x * pixelSize);
                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        // 检查白色气泡 (255, 255, 255)
                        // 给一点容差，防止压缩导致的非纯白
                        bool isWhite = (r > 250 && g > 250 && b > 250);

                        // 检查绿色气泡 (#95EC69 -> R149 G236 B105)
                        // 容差 ±15
                        bool isGreen = (Math.Abs(r - 149) < 20) &&
                                       (Math.Abs(g - 236) < 20) &&
                                       (Math.Abs(b - 105) < 20);

                        if (isWhite || isGreen)
                        {
                            // 找到了气泡的最底部像素！
                            foundBottomY = y;
                            foundType = isWhite ? BubbleType.Received : BubbleType.Sent;
                            break; // 停止当前行的扫描，准备向上扩散
                        }
                    }
                    if (foundBottomY != -1) break; // 停止 Y 轴扫描
                }

                // 如果没找到任何气泡
                if (foundBottomY == -1)
                {
                    bmp.UnlockBits(bmpData);
                    return null;
                }

                // 2. 已锁定底部 Y，现在向上寻找顶部 Y，并确定左右边界 X
                // 从底部向上连续扫描，直到颜色不再是气泡色
                for (int y = foundBottomY; y >= 0; y--)
                {
                    bool lineHasBubbleColor = false;

                    // 扫描这一行的左右边界
                    for (int x = 0; x < width - 10; x++)
                    {
                        int index = (y * stride) + (x * pixelSize);
                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        bool match = false;
                        if (foundType == BubbleType.Received)
                            match = (r > 250 && g > 250 && b > 250);
                        else
                            match = (Math.Abs(r - 149) < 20) && (Math.Abs(g - 236) < 20);

                        if (match)
                        {
                            lineHasBubbleColor = true;
                            if (x < foundMinX) foundMinX = x;
                            if (x > foundMaxX) foundMaxX = x;
                        }
                    }

                    if (!lineHasBubbleColor)
                    {
                        // 这一行完全没有气泡色了，说明到达了气泡上方
                        foundTopY = y + 1; // 上一行是空的，所以 Top 是 y+1
                        break;
                    }
                }

                // 兜底：如果一直扫到顶部
                if (foundTopY == -1) foundTopY = 0;
            }

            bmp.UnlockBits(bmpData);

            // 构造矩形
            int w = foundMaxX - foundMinX;
            int h = foundBottomY - foundTopY;

            // 过滤极小噪点 (例如宽高小于 5px)
            if (w < 5 || h < 5) return null;

            // 为了 OCR 更准，四周留一点白边 padding
            int padding = 2;
            return new BubbleResult
            {
                Rect = new Rectangle(
                    Math.Max(0, foundMinX - padding),
                    Math.Max(0, foundTopY - padding),
                    Math.Min(bmp.Width - foundMinX, w + padding * 2),
                    Math.Min(bmp.Height - foundTopY, h + padding * 2)
                ),
                Type = foundType
            };
        }


        /// <summary>
        /// 检测指定区域是否包含明显的绿色 (用于判断微信Tab状态)
        /// </summary>
        public static bool HasGreenColor(Bitmap bmp, Rectangle rect)
        {
            // 边界校验
            if (rect.X < 0) rect.X = 0;
            if (rect.Y < 0) rect.Y = 0;
            if (rect.Right > bmp.Width) rect.Width = bmp.Width - rect.X;
            if (rect.Bottom > bmp.Height) rect.Height = bmp.Height - rect.Y;

            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
            int stride = Math.Abs(bmpData.Stride);
            int pixelSize = (bmp.PixelFormat == PixelFormat.Format24bppRgb) ? 3 : 4;

            bool hasGreen = false;

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;
                int rows = rect.Height;
                int cols = rect.Width;

                // 为了性能，跳跃扫描
                for (int y = 0; y < rows; y += 2)
                {
                    for (int x = 0; x < cols; x += 2)
                    {
                        int index = (y * stride) + (x * pixelSize);
                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        // 微信绿特征: R=7, G=193, B=96
                        // 宽泛判定: G 分量明显大于 R 和 B，且 G 足够亮
                        if (g > 140 && g > r + 40 && g > b + 40)
                        {
                            hasGreen = true;
                            break;
                        }
                    }
                    if (hasGreen) break;
                }
            }

            bmp.UnlockBits(bmpData);
            return hasGreen;
        }


        /// <summary>
        /// 判断气泡内容是否为纯文本 (改进版：基于饱和度和边缘检测)
        /// </summary>
        public static bool IsTextBubble(Bitmap bmp)
        {
            // 1. 尺寸防御：太小的图（如标点符号）直接算文本
            if (bmp.Width < 20 || bmp.Height < 20) return true;

            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
            int stride = Math.Abs(bmpData.Stride);
            int pixelSize = (bmp.PixelFormat == PixelFormat.Format24bppRgb) ? 3 : 4;

            int colorPixelCount = 0; // 彩色像素数
            int totalPixels = 0;

            // 记录边缘行的白色情况
            int topRowsWhiteCount = 0;
            int bottomRowsWhiteCount = 0;
            int marginCheckHeight = 5; // 检查上下各 5 像素的高度

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;

                for (int y = 0; y < bmp.Height; y++)
                {
                    bool isTopMargin = y < marginCheckHeight;
                    bool isBottomMargin = y >= bmp.Height - marginCheckHeight;

                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int index = (y * stride) + (x * pixelSize);
                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        // ==========================================
                        // 逻辑 A: 颜色饱和度检测
                        // ==========================================
                        // 计算 RGB 的最大差值。如果差值大，说明是彩色。
                        // 黑、白、灰的 RGB 差值通常 < 10
                        int maxDiff = Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(r - b), Math.Abs(g - b)));

                        // 如果差值 > 20，认为是“彩色像素”
                        if (maxDiff > 20)
                        {
                            colorPixelCount++;
                        }

                        totalPixels++;

                        // ==========================================
                        // 逻辑 B: 边缘纯净度检测 (检查是不是白底)
                        // ==========================================
                        // 宽松的白色定义：亮度 > 230 且是灰阶
                        bool isWhiteish = (r > 230 && g > 230 && b > 230) && (maxDiff < 15);

                        if (isWhiteish)
                        {
                            if (isTopMargin) topRowsWhiteCount++;
                            if (isBottomMargin) bottomRowsWhiteCount++;
                        }
                    }
                }
            }
            bmp.UnlockBits(bmpData);

            // ==========================================
            // 综合判定
            // ==========================================

            // 1. 彩色判定：如果彩色像素占比超过 5%，肯定是图片/表情包
            // (文本只有黑白灰，彩色占比应该是 0%)
            double colorRatio = (double)colorPixelCount / totalPixels;
            if (colorRatio > 0.05)
            {
                // System.Diagnostics.Debug.WriteLine($"判定为图片：彩色像素占比 {colorRatio:P1}");
                return false;
            }

            // 2. 边缘判定：文本气泡上下边缘必须是白色的
            // 如果顶部或底部的 5 行里，白色像素少于 50%，说明图片撑满了边缘，不是文本
            int totalMarginPixels = bmp.Width * marginCheckHeight;
            double topWhiteRatio = (double)topRowsWhiteCount / totalMarginPixels;
            double bottomWhiteRatio = (double)bottomRowsWhiteCount / totalMarginPixels;

            if (topWhiteRatio < 0.5 || bottomWhiteRatio < 0.5)
            {
                // System.Diagnostics.Debug.WriteLine($"判定为图片：边缘白色占比过低 Top:{topWhiteRatio:P1} Bottom:{bottomWhiteRatio:P1}");
                return false;
            }

            // 通过所有检查，认为是文本
            return true;
        }
    }
}