using Bloghua.AutoClient.Core.Models;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace Bloghua.AutoClient.Core.Interfaces
{
    public interface IOcrService
    {
     

        // 原有的纯文本接口 (用于长文本识别)
        Task<string> RecognizeTextAsync(Bitmap image);

        // [新增] 包含坐标的检测接口 (用于 UI 定位)
        List<OcrResultItem> DetectText(Bitmap image);
    }
}