using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bloghua.AutoClient.Core.Interfaces;
using Bloghua.AutoClient.Core.Models; // 引用 ChatRequest, ChatResponse
using Newtonsoft.Json;                // 需要 NuGet: Newtonsoft.Json

namespace Bloghua.AutoClient.Services
{
    public class ChatApiService
    {
        // 使用静态 HttpClient 以避免套接字耗尽问题
        private static readonly HttpClient _httpClient = new HttpClient();

        // 后端 API 地址
        private readonly string _apiUrl = "http://192.168.10.86:8000/api/process_chat";

        public ChatApiService()
        {
            

            // 【关键修改】设置超时时间为 5 分钟 (300秒)
            // 根据大模型的实际响应时间灵活调整，建议比模型最大生成时间多一点
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// 发送聊天内容到 AI 后端并获取回复
        /// </summary>
        /// <param name="sessionKey">会话标识 (如: 客户名_业务ID)</param>
        /// <param name="content">聊天内容</param>
        /// <param name="isImage">内容是否为图片(OCR后通常传false)</param>
        /// <returns>AI 的回复内容，如果失败则返回 null</returns>
        public async Task<string> GetReplyAsync(string sessionKey, string content, ILoggerService logger,bool isImage = false)
        {
            // 1. 构建请求体
            var requestPayload = new ChatRequest
            {
                session_key = sessionKey,
                content = content,
                is_image = isImage
            };

            try
            {
                string json = JsonConvert.SerializeObject(requestPayload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                // 2. 发送 POST 请求
                var response = await _httpClient.PostAsync(_apiUrl, httpContent);
            

                // 3. 处理响应
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"API Error: {responseString}");

                    if (logger != null)
                    {
                        logger.Log($"API Error: {responseString}");
                    }

                    // 解析响应 JSON: { "reply": "..." }
                    var responseObj = JsonConvert.DeserializeObject<ChatResponse>(responseString);

                    return responseObj?.reply;
                }
                else
                {
                    // 可以通过日志记录失败状态码
                    Console.WriteLine($"API Error: {response.StatusCode}");
                    if (logger != null)
                    {
                        logger.Log($"API Error: {response.StatusCode}");
                    }
                    return null;
                }
            }
    
            catch (TaskCanceledException ex)
            {
                // 超时会抛出 TaskCanceledException
                Console.WriteLine("API 请求超时 (超过5分钟)");
                if (logger != null)
                {
                    logger.Log($"API 请求超时 (超过5分钟): {ex.Message}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"API 异常: {ex.Message}");
                if (logger != null)
                {
                    logger.Log($"API Exception: {ex.Message}");
                }
                return null;
            }
        }
    }
}