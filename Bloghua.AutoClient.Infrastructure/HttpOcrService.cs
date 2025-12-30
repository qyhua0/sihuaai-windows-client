using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core.Models;
using Newtonsoft.Json.Linq;

namespace Bloghua.AutoClient.Infrastructure.Services
{
    public class HttpOcrService : IOcrService
    {
        private readonly HttpClient _client;
        private readonly string _ocrApiUrl = "http://192.168.10.86:8000/api/ocr";

        public HttpOcrService()
        {
            _client = new HttpClient();
        }

        public List<OcrResultItem> DetectText(Bitmap image)
        {
            throw new System.NotImplementedException();
        }

        public async Task<string> RecognizeTextAsync(Bitmap image)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, ImageFormat.Png);
                byte[] imgBytes = stream.ToArray();

                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(imgBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    content.Add(fileContent, "file", "screenshot.png");

                    try
                    {
                        var response = await _client.PostAsync(_ocrApiUrl, content);
                        if (response.IsSuccessStatusCode)
                        {
                            var jsonString = await response.Content.ReadAsStringAsync();
                            var json = JObject.Parse(jsonString);
                            return json["text"]?.ToString().Trim();
                        }
                    }
                    catch { 
                        
                    }
                }
            }
            return string.Empty;
        }
    }
}