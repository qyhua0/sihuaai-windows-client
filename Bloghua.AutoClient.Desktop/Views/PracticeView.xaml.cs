using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Bloghua.AutoClient.Desktop;
using Bloghua.AutoClient.Services;
using Bloghua.AutoClient.Core.Models;

namespace Bloghua.AutoClient.Desktop.Views
{
    public partial class PracticeView : Page
    {
        // 缓存原始 JSON
        private string _cachedRawJson = "";

        public PracticeView()
        {
            InitializeComponent();
            this.Loaded += PracticeView_Loaded;
        }

        private void PracticeView_Loaded(object sender, RoutedEventArgs e)
        {
            var roles = ServiceLocator.Db.GetAllRoles();
            cmbRoles.ItemsSource = roles;

            // 选中默认
            string globalRole = ServiceLocator.Db.GetSetting("GlobalDefaultRole", "");
            if (!string.IsNullOrEmpty(globalRole)) cmbRoles.SelectedValue = globalRole;
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            string question = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(question)) return;

            // UI Reset
            btnGenerate.IsEnabled = false;
            loadingRing.IsActive = true;
            lblStatus.Text = "请求中...";
            lblEmpty.Visibility = Visibility.Collapsed;
            lblPersona.Text = "";

            // 默认切回卡片视图
            tsViewMode.IsOn = false;
            viewRendered.Visibility = Visibility.Visible;
            viewRaw.Visibility = Visibility.Collapsed;
            viewRaw.Text = "";

            try
            {
                string roleCode = cmbRoles.SelectedValue as string;
                var api = new ChatApiService();

                string rawReply = await api.GetReplyAsync("MANUAL_TEST", question, roleCode);

                ProcessReply(rawReply);
                lblStatus.Text = "生成完成";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "错误";
                MessageBox.Show(ex.Message);
            }
            finally
            {
                btnGenerate.IsEnabled = true;
                loadingRing.IsActive = false;
            }
        }

        private void ProcessReply(string rawReply)
        {
            if (string.IsNullOrWhiteSpace(rawReply)) return;

            // 1. 保存原始数据用于 Raw 视图显示
            _cachedRawJson = rawReply;
            viewRaw.Text = rawReply;

            try
            {
                List<SuggestionItem> items = new List<SuggestionItem>();

                // 2. 【调用强力清洗】
                string jsonStr = CleanJsonString(rawReply);

                // 尝试在 Raw 视图里显示格式化后的 JSON，方便调试
                try
                {
                    dynamic parsedJson = JsonConvert.DeserializeObject(jsonStr);
                    viewRaw.Text = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
                }
                catch { /* 忽略格式化错误，显示原始内容 */ }

                bool parseSuccess = false;
                try
                {
                    // 3. 反序列化
                    var response = JsonConvert.DeserializeObject<SuggestionResponse>(jsonStr);
                    if (response != null)
                    {
                        // 显示人设标签
                        lblPersona.Text = !string.IsNullOrEmpty(response.persona)
                            ? $"当前人设: {response.persona}"
                            : "";

                        if (response.suggestions != null && response.suggestions.Count > 0)
                        {
                            // 4. 【关键】手动分配 UI 属性 (标题和颜色)
                            int index = 1;
                            foreach (var item in response.suggestions)
                            {
                                // 如果 API 没返回 type，默认为 "suggestion"
                                if (string.IsNullOrEmpty(item.type)) item.type = "suggestion";

                                // 强制覆盖标题为 "建议 1", "建议 2"...
                                item.DisplayTitle = $"建议 {index}";

                                // 强制分配颜色 (循环使用)
                                item.ColorCode = GetColorByIndex(index);

                                index++;
                            }

                            items = response.suggestions;
                            parseSuccess = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 记录一下具体解析哪里错了，方便调试
                    System.Diagnostics.Debug.WriteLine($"JSON解析失败: {ex.Message}");
                }

                // 5. 兜底处理 (如果解析失败，把整段文本作为一条建议显示)
                if (!parseSuccess)
                {
                    string type = jsonStr.Trim().StartsWith("{") ? "parse_error" : "general";

                    // 如果清洗后的字符串还是很长且不像 JSON，可能就是纯文本回复
                    items.Add(new SuggestionItem
                    {
                        DisplayTitle = "通用回复",
                        ColorCode = "#666666", // 灰色
                        content = rawReply // 显示原始内容
                    });
                }

                listResults.ItemsSource = items;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"处理结果时发生未知错误: {ex.Message}");
            }
        }

        // 颜色分配辅助方法
        private string GetColorByIndex(int index)
        {
            switch ((index - 1) % 4)
            {
                case 0: return "#0078D7"; // 蓝
                case 1: return "#107C10"; // 绿
                case 2: return "#FFB900"; // 橙
                case 3: return "#881798"; // 紫
                default: return "Gray";
            }
        }

       

        // 切换视图事件
        private void ViewMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (tsViewMode.IsOn)
            {
                // 显示 Raw JSON
                viewRendered.Visibility = Visibility.Collapsed;
                viewRaw.Visibility = Visibility.Visible;
            }
            else
            {
                // 显示卡片列表
                viewRendered.Visibility = Visibility.Visible;
                viewRaw.Visibility = Visibility.Collapsed;
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag == null) return;
            try { Clipboard.SetText(btn.Tag.ToString()); lblStatus.Text = "已复制"; } catch { }
        }

        /// <summary>
        /// 强力清洗 JSON 字符串，兼容各种 Markdown 和前缀后缀
        /// </summary>
        private string CleanJsonString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = raw.Trim();

            // 1. 尝试移除 Markdown 代码块标记 (```json ... ```)
            // [\s\S]*? 匹配包括换行符在内的任意字符
            var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                text = match.Groups[1].Value.Trim();
            }

            // 2. 暴力提取：寻找第一个 '{' 和最后一个 '}'
            // 这能处理 "Here is the json: { ... }" 这种情况
            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                // 只保留 { ... } 中间的部分
                text = text.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return text;
        }
    }
}