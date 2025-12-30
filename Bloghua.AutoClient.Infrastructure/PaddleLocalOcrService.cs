using System;
using System.Collections.Generic;
using System.Drawing; // 必须引用: 用于 Bitmap 和 Rectangle
using System.Linq;
using System.Threading.Tasks;
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core.Models;
using PaddleOCRSharp; // 引用 NuGet 包

namespace Bloghua.AutoClient.Infrastructure.Services
{
    public class PaddleLocalOcrService : IOcrService
    {
        private PaddleOCREngine _engine;

        public PaddleLocalOcrService()
        {
            // 【关键修复】
            // v6.x 构造函数签名通常是: PaddleOCREngine(OCRModelConfig config, OCRParameter parameter)
            // 1. 第一个参数传 null：使用默认模型配置 (会自动在运行目录查找或下载模型)
            // 2. 第二个参数传 new OCRParameter()：使用默认推理参数

            var ocrParam = new OCRParameter();

            // 如果想要提高准确率，可以调整参数 (可选)
             ocrParam.cls = false; // 是否启用方向分类
            // ocrParam.use_gpu = false; // 默认使用 CPU

            // _engine = new PaddleOCREngine(null, ocrParam);



   

            OCRModelConfig config = new OCRModelConfig();
            string root = EngineBase.GetRootDirectory();
            string modelPathroot = root + @"\modelx\inference_v3";
            config.det_infer = modelPathroot + @"\ch_PP-OCRv3_det_infer";
            config.cls_infer = modelPathroot + @"\ch_ppocr_mobile_v2.0_cls_infer";
            config.rec_infer = modelPathroot + @"\ch_PP-OCRv3_rec_infer";
            config.keys = modelPathroot + @"\ppocr_keys.txt";
            //初始化OCR引擎
            _engine = new PaddleOCREngine(config, "");
        }

        public List<OcrResultItem> DetectText(Bitmap image)
        {
            var list = new List<OcrResultItem>();
            try
            {
                if (image == null) return list;

                // 调用识别
                OCRResult result = _engine.DetectText(image);

                if (result.TextBlocks == null) return list;

                foreach (var block in result.TextBlocks)
                {
                    // 动态获取坐标，避免类型冲突
                    var rect = GetBoundingRect(block.BoxPoints);

                    list.Add(new OcrResultItem
                    {
                        Text = block.Text,
                        Rect = rect
                    });
                }
            }
            catch (Exception ex)
            {
                // 调试时可以将错误打印出来
                System.Diagnostics.Debug.WriteLine("OCR 识别异常: " + ex.Message);
            }
            return list;
        }

        public Task<string> RecognizeTextAsync(Bitmap image)
        {
            var results = DetectText(image);
            // 将所有识别到的文字拼接，用于简单文本获取
            string fullText = string.Join("\n", results.Select(r => r.Text));
            return Task.FromResult(fullText);
        }

        // 通用坐标转换 (防止 PaddleOCRSharp.Point 与 System.Drawing.Point 冲突)
        private Rectangle GetBoundingRect(dynamic points)
        {
            if (points == null) return Rectangle.Empty;

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            int count = 0;
            foreach (var p in points)
            {
                // 这里 p 是动态类型，会自动访问 X 和 Y 属性
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                count++;
            }

            if (count == 0) return Rectangle.Empty;

            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }
    }
}