using Newtonsoft.Json;

namespace Bloghua.AutoClient.Core.Models
{
    // === 1. 认证相关 ===
    public class TokenRequest
    {
        public string username { get; set; }
        public string password { get; set; }
    }

    public class TokenResponse
    {
        public string access_token { get; set; }
        public int expires_in { get; set; }
    }

    // === 2. 业务数据 (加密前/解密后) ===
    public class ChatDataPayload
    {
        public string content { get; set; }
        public string session_key { get; set; }
    }

    public class ChatReplyPayload
    {
        public string reply { get; set; }
        public int usage { get; set; }
        public string source { get; set; } // qa, rag, llm
    }

    // === 3. 信封结构 (传输层) ===
    public class ApiEnvelopeRequest
    {
        public string app_id { get; set; }
        public long time { get; set; }
        public string data { get; set; } // Base64 密文
        public string ret { get; set; }
        public string sign { get; set; }
    }

    public class ApiEnvelopeResponse
    {
        public int code { get; set; }
        public string msg { get; set; }
        public EnvelopePayload payload { get; set; }

        public class EnvelopePayload
        {
            public string app_id { get; set; }
            public long time { get; set; }
            public string data { get; set; } // 响应密文
            public string sign { get; set; }
        }
    }
}