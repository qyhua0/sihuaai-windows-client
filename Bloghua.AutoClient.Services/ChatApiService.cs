using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Bloghua.AutoClient.Core.Models;
using Bloghua.AutoClient.Infrastructure.Data; // 需引用
using Bloghua.AutoClient.Infrastructure.Utils; // 需引用
using Bloghua.AutoClient.Desktop; // 引用 ServiceLocator
using Newtonsoft.Json;

namespace Bloghua.AutoClient.Services
{
    public class ChatApiService
    {
        private static readonly HttpClient _httpClient;

        // 缓存 Token
        private static string _accessToken = null;
        private static DateTime _tokenExpireTime = DateTime.MinValue;

        static ChatApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // 大模型响应慢
        }

        public ChatApiService()
        {
            // 构造函数
        }

        /// <summary>
        /// 测试登录 (用于设置界面测试按钮)
        /// </summary>
        public async Task<(bool success, string message)> TestConnectionAsync(string baseUrl, string username, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    return (false, "参数不能为空");
                }

                var loginData = new TokenRequest { username = username, password = password };
                var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");

                // 发起请求
                var response = await _httpClient.PostAsync($"{baseUrl}/token", content);

                if (response.IsSuccessStatusCode)
                {
                    var resStr = await response.Content.ReadAsStringAsync();
                    var tokenRes = JsonConvert.DeserializeObject<TokenResponse>(resStr);

                    if (!string.IsNullOrEmpty(tokenRes?.access_token))
                    {
                        return (true, "连接成功！Token 获取正常。");
                    }
                }

                return (false, $"连接失败: HTTP {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, $"异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 对外公开的调用方法 (保持签名不变)
        /// </summary>
        public async Task<string> GetReplyAsync(string sessionKey, string content, bool isImage = false)
        {
            if (isImage) return "[图片暂不支持]"; // 新API暂时未提及图片处理

            try
            {
                // 1. 获取配置
                var db = ServiceLocator.Db;
                string baseUrl = db.GetSetting("ApiBaseUrl", "http://127.0.0.1:8000/api/open");
                string appId = db.GetSetting("AppId", "");
                string appSecret = db.GetSetting("AppSecret", "");
                string aesKey = db.GetSetting("AesKey", "");
                string aesIv = db.GetSetting("AesIv", "");

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(aesKey))
                {
                    return "【系统提示】API 配置未完成，请在设置页完善 AppID/Key 等信息。";
                }

                // 2. 确保有 Token
                await EnsureTokenAsync(baseUrl, db);

                // 3. 准备业务数据并加密
                var businessData = new ChatDataPayload
                {
                    content = content,
                    session_key = sessionKey
                };
                string jsonBusiness = JsonConvert.SerializeObject(businessData);
                string encryptedData = SecurityHelper.AesEncrypt(jsonBusiness, aesKey, aesIv);

                // 4. 准备信封参数
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string nonce = Guid.NewGuid().ToString("N"); // ret 字段

                var envelopeParams = new Dictionary<string, string>
                {
                    { "app_id", appId },
                    { "time", timestamp.ToString() },
                    { "data", encryptedData },
                    { "ret", nonce }
                };

                // 5. 签名
                string sign = SecurityHelper.GenerateSign(envelopeParams, appSecret);

                // 6. 组装最终请求体
                var requestBody = new ApiEnvelopeRequest
                {
                    app_id = appId,
                    time = timestamp,
                    data = encryptedData,
                    ret = nonce,
                    sign = sign
                };

                // 7. 发送请求
                var jsonBody = JsonConvert.SerializeObject(requestBody);
                var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // 设置 Bearer Token
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var response = await _httpClient.PostAsync($"{baseUrl}/chat", httpContent);

                if (response.IsSuccessStatusCode)
                {
                    string resString = await response.Content.ReadAsStringAsync();
                    var apiRes = JsonConvert.DeserializeObject<ApiEnvelopeResponse>(resString);

                    if (apiRes.code == 200 && apiRes.payload != null)
                    {
                        // 8. 解密响应数据
                        try
                        {
                            string decryptedJson = SecurityHelper.AesDecrypt(apiRes.payload.data, aesKey, aesIv);
                            var replyData = JsonConvert.DeserializeObject<ChatReplyPayload>(decryptedJson);
                            return replyData?.reply;
                        }
                        catch
                        {
                            return "【系统提示】响应解密失败，请检查 AES Key/IV。";
                        }
                    }
                    else
                    {
                        // 业务错误 (如401, 403业务码)
                        // 如果是 Token 过期 (比如服务端返回特定code)，这里可以做一次重试逻辑
                        return $"【API错误】{apiRes.msg} ({apiRes.code})";
                    }
                }
                else
                {
                    // HTTP 错误
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _accessToken = null; // Token 失效，清空
                        return "【系统提示】Token 已过期，请重试。";
                    }
                    return $"【网络错误】HTTP {(int)response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return $"【异常】{ex.Message}";
            }
        }

        private async Task EnsureTokenAsync(string baseUrl, DatabaseService db)
        {
            // 如果 Token 存在且未过期 (提前60秒刷新)
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.Now < _tokenExpireTime.AddSeconds(-60))
            {
                return;
            }

            string username = db.GetSetting("ApiUser", "");
            string password = db.GetSetting("ApiPwd", "");

            if (string.IsNullOrEmpty(username)) throw new Exception("未配置 API 用户名");

            var loginData = new TokenRequest { username = username, password = password };
            var content = new StringContent(JsonConvert.SerializeObject(loginData), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{baseUrl}/token", content);
            if (response.IsSuccessStatusCode)
            {
                var resStr = await response.Content.ReadAsStringAsync();
                var tokenRes = JsonConvert.DeserializeObject<TokenResponse>(resStr);

                _accessToken = tokenRes.access_token;
                _tokenExpireTime = DateTime.Now.AddSeconds(tokenRes.expires_in);
            }
            else
            {
                throw new Exception($"获取 Token 失败: {response.StatusCode}");
            }
        }


        /// <summary>
        /// API 状态枚举
        /// </summary>
        public enum ApiStatus
        {
            Normal,         // 正常 (200)
            ConfigError,    // 配置错误 (401/403)
            Unreachable,    // 无法连接 (404/500/Timeout)
            Unknown
        }

        /// <summary>
        /// 检查服务器连接状态
        /// </summary>
        public async Task<ApiStatus> CheckHealthAsync(string baseUrl, string user, string pwd)
        {
            try
            {
                // 如果没有配置，直接返回配置错误
                if (string.IsNullOrEmpty(baseUrl)) return ApiStatus.ConfigError;

                // 尝试获取 Token 作为探针
                // 如果有缓存且未过期，说明连接是通的
                if (!string.IsNullOrEmpty(_accessToken) && DateTime.Now < _tokenExpireTime)
                {
                    return ApiStatus.Normal;
                }

                // 尝试用错误凭证请求一次，看能不能连通服务器
                // 或者直接调用 EnsureTokenAsync 的逻辑
                // 这里为了简单，复用 TestConnectionAsync 的核心逻辑
                var result = await TestConnectionAsync(baseUrl, user, pwd);
                if (result.success) return ApiStatus.Normal;

                // 根据错误消息判断
                if (result.message.Contains("401") || result.message.Contains("403"))
                    return ApiStatus.ConfigError;

                return ApiStatus.Unreachable;
            }
            catch
            {
                return ApiStatus.Unreachable;
            }
        }
    }
}