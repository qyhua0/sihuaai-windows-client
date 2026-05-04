# 斯华AI · 客服助手（Windows Client）

面向微信与桌面客服场景的 AI 智能辅助客户端。  
通过 **截图识别（OCR）+ 上下文整理 + 智能回复建议**，帮助客服团队在不改变原有工作习惯的前提下，快速获得高质量回复建议，提升响应效率与转化率。

> 官网：[斯华AI官网](https://www.sihuaai.com/?utm_source=chatgpt.com)  
> 产品主页：:contentReference[oaicite:1]{index=1}  
> 在线体验：[访问官网](https://www.sihuaai.com/?utm_source=chatgpt.com)

---

## 项目简介

斯华AI · 客服助手（Windows Client）是 :contentReference[oaicite:3]{index=3} 的桌面辅助客户端，专为客服、销售、私域运营等高频对话场景设计。

它并不替代现有聊天工具，而是作为一个轻量级桌面辅助终端运行在本地：

- 截取当前聊天窗口内容
- 自动识别对话文本（OCR）
- 结构化整理上下文
- 调用斯华AI服务生成回复建议
- 一键插入到输入框，提升客服响应效率

适用于：

- 微信客服 / 企业微信客服
- 电商售前 / 售后接待
- 私域社群运营
- 销售咨询接待
- 企业内部问答辅助

---

## 核心能力

### 1. 截图识别驱动
无需复制聊天记录，直接截图即可识别当前对话内容。

- 支持区域截图
- 支持多屏幕环境
- 自动识别聊天内容区域
- 本地预处理图像，减少识别噪声

### 2. OCR 文本结构化
并非简单 OCR 识别，而是对截图内容进行结构化整理：

- 文本块排序
- 对话角色识别
- 噪声字符清洗
- 上下文重组

让模型理解“谁在说什么”，而不是只看到一堆文本。

### 3. 智能回复建议
自动结合上下文生成更贴近业务场景的回复建议：

- 客服接待
- 销售跟进
- 催付转化
- 售后安抚
- 风格化话术生成（商务 / 亲和 / 活泼等）

### 4. 一键辅助回复
无需切换窗口，直接完成辅助回复流程：

- 全局快捷键唤起
- 一键生成
- 一键复制
- 一键插入输入框

### 5. 本地优先
客户端仅负责：

- 截图采集
- OCR预处理
- 上下文整理
- 快捷交互

不存储敏感业务数据，支持企业私有化部署。:contentReference[oaicite:4]{index=4}

---

## 使用场景

### 微信客服辅助
在微信聊天窗口中快速截图当前对话，自动生成更自然、更高转化的回复建议。

### 电商售前接待
自动识别买家咨询内容，快速生成商品推荐、催付、优惠解释等回复。

### 售后处理
针对退款、投诉、物流等高频问题，快速生成更稳妥的回复方案。

### 私域销售转化
根据用户当前意图，生成更具引导性的销售回复。

---

## 客户端架构

```text
┌─────────────────────────────┐
│      Desktop Client (WPF)   │
├─────────────────────────────┤
│ Screenshot Capture          │
│ OCR Preprocessing           │
│ Context Structuring         │
│ Local Session Cache         │
│ Hotkey / Clipboard Control  │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│      Sihua AI Server API    │
├─────────────────────────────┤
│ Prompt Engine               │
│ Knowledge Retrieval         │
│ Response Strategy           │
│ Model Scheduling            │
└─────────────────────────────┘
```

客户端负责本地交互与数据整理，服务端负责智能推理与业务策略。([斯华AI][1])

---

## 技术栈

* **Framework**: .NET / C# / WPF
* **OCR**: Third-party OCR Engine
* **UI**: WPF Desktop
* **Communication**: REST API
* **Architecture**: Local Client + Remote AI Service

---

## 开源说明

本仓库开源的是 **Windows 客户端（桌面辅助层）**，主要包含：

* 截图采集
* OCR结果预处理
* 本地会话整理
* 快捷交互逻辑
* 客户端任务调度

不包含以下内容：

* 服务端核心引擎
* Prompt 策略系统
* RAG 知识库引擎
* 模型调度系统
* 商业版控制台
* 企业私有化部署模块

上述能力属于 斯华AI 商业服务的一部分。([斯华AI][1])

---

## 商业版 / 私有化部署

如果你需要：

* 企业私有化部署
* 微信原生深度接入
* 知识库增强（RAG）
* 多客服协作
* SaaS 控制台
* 企业级安全部署

请访问官网：[预约私有化演示](https://www.sihuaai.com/?utm_source=chatgpt.com)

---

## 安装

```bash
# clone
git clone https://github.com/qyhua0/sihuaai-windows-client.git

# open
使用 Visual Studio 2022 打开解决方案

# build
Build -> Release
```

运行环境：

* Windows 10 / 11
* .NET 6.0+
* WebView2 Runtime（如启用嵌入模式）

---

## Roadmap

* [ ] 多窗口识别优化
* [ ] 企业微信场景增强
* [ ] 本地 OCR 插件可替换
* [ ] 自定义快捷键
* [ ] 多语言支持
* [ ] 插件化扩展能力

---

## License

本项目基于 **Apache License 2.0** 开源。
你可以自由使用、修改和分发，但需保留原始版权声明与许可证。

详见 [LICENSE](./LICENSE)

---

## 关于斯华AI

斯华AI 致力于为企业提供新一代智能客服系统与企业 AI 解决方案，支持：

* SaaS 快速接入
* 企业私有化部署
* AI 客服助手
* 网站智能客服
* RAG 知识库增强
* 企业级 AI 定制开发

官网：[www.sihuaai.com](https://www.sihuaai.com)
