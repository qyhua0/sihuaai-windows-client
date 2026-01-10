using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Bloghua.AutoClient.Infrastructure.Utils
{
    public static class SecurityHelper
    {
        // =======================
        // 1. AES-128-CBC 加解密
        // =======================

        /// <summary>
        /// AES 加密
        /// </summary>
        public static string AesEncrypt(string plainText, string key, string iv)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            using (var aes = Aes.Create())
            {
                // 文档要求: AES-128-CBC, PKCS7
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = 128;
                aes.BlockSize = 128;

                // 密钥和IV直接使用UTF8字节 (文档说是16位字符串)
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = Encoding.UTF8.GetBytes(iv);

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                    byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                    // 输出 Base64
                    return Convert.ToBase64String(encryptedBytes);
                }
            }
        }

        /// <summary>
        /// AES 解密
        /// </summary>
        public static string AesDecrypt(string cipherText, string key, string iv)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = 128;
                aes.BlockSize = 128;

                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = Encoding.UTF8.GetBytes(iv);

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                {
                    byte[] cipherBytes = Convert.FromBase64String(cipherText);
                    byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        // =======================
        // 2. MD5 签名算法
        // =======================

        /// <summary>
        /// 生成签名
        /// </summary>
        /// <param name="parameters">信封参数 (data, time, app_id 等)</param>
        /// <param name="appSecret">应用密钥</param>
        public static string GenerateSign(Dictionary<string, string> parameters, string appSecret)
        {
            // 1. 参数排序 (ASCII 码从小到大)
            var sortedParams = parameters
                .Where(p => !string.IsNullOrEmpty(p.Value) && p.Key != "sign") // 排除空值和sign本身
                .OrderBy(p => p.Key);

            // 2. 拼接字符串: key1=value1&key2=value2...
            StringBuilder sb = new StringBuilder();
            foreach (var param in sortedParams)
            {
                if (sb.Length > 0) sb.Append("&");
                sb.Append($"{param.Key}={param.Value}");
            }

            // 3. 拼接密钥
            sb.Append($"&secret={appSecret}");

            // 4. MD5 运算并转大写
            using (var md5 = MD5.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                byte[] hash = md5.ComputeHash(bytes);

                StringBuilder result = new StringBuilder();
                foreach (byte b in hash)
                {
                    result.Append(b.ToString("X2")); // X2 = 大写十六进制
                }
                return result.ToString();
            }
        }
    }
}