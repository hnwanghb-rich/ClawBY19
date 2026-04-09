namespace ClawBY19.Services.AI;

/// <summary>
/// 全量模型注册表：42款模型，中国开源优先排列。
/// 所有模型均兼容 OpenAI Chat Completions API 格式。
/// </summary>
public class ModelRegistry
{
    private readonly Dictionary<string, ModelInfo> _models;

    public ModelRegistry()
    {
        var list = BuildRegistry();
        _models = list.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    }

    public ModelInfo? Get(string id) =>
        _models.TryGetValue(id, out var m) ? m : null;

    public IReadOnlyList<ModelInfo> All() =>
        _models.Values.OrderBy(m => (int)m.Category).ThenBy(m => m.Id).ToList();

    public IReadOnlyList<ModelInfo> ByCategory(ModelCategory cat) =>
        _models.Values.Where(m => m.Category == cat).ToList();

    public bool Contains(string id) => _models.ContainsKey(id);

    // ──────────────────────────────────────────────────────────────────
    private static List<ModelInfo> BuildRegistry() =>
    [
        // ═══════════════════════════════════════════
        // A类：中国开源模型（最优先）
        // ═══════════════════════════════════════════
        new() {
            Id = "deepseek-v3", FullName = "DeepSeek-V3", Provider = "深度求索",
            ApiProvider = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            ApiModelId = "deepseek-chat",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 128_000,
            IsOpenSource = true,
            Description = "综合能力强，极高性价比，理解+代码+推理均衡优秀，开源旗舰"
        },
        new() {
            Id = "deepseek-r1", FullName = "DeepSeek-R1", Provider = "深度求索",
            ApiProvider = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            ApiModelId = "deepseek-reasoner",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 128_000,
            IsOpenSource = true,
            Description = "强化学习推理专项，数学/逻辑/代码推理标杆，开源推理之王"
        },
        new() {
            Id = "deepseek-r1-distill-qwen-32b",
            FullName = "DeepSeek-R1-Distill-Qwen-32B", Provider = "深度求索",
            ApiProvider = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            ApiModelId = "deepseek-r1-distill-qwen-32b",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 128_000,
            IsOpenSource = true,
            Description = "R1蒸馏版，推理能力强，体积适中，本地部署首选"
        },
        new() {
            Id = "qwen3-235b", FullName = "Qwen3-235B-A22B", Provider = "阿里通义",
            ApiProvider = "Qwen",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "qwen3-235b-a22b",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 128_000,
            IsOpenSource = true, SupportsVision = true,
            Description = "混合思考/直答双模式，超大MoE，多语言，综合性能顶级"
        },
        new() {
            Id = "qwen2.5-72b", FullName = "Qwen2.5-72B-Instruct", Provider = "阿里通义",
            ApiProvider = "Qwen",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "qwen2.5-72b-instruct",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 128_000,
            IsOpenSource = true,
            Description = "中文/代码/数学综合最优均衡，开源全能旗舰"
        },
        new() {
            Id = "qwq-32b", FullName = "QwQ-32B", Provider = "阿里通义",
            ApiProvider = "Qwen",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "qwq-32b",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 32_000,
            IsOpenSource = true,
            Description = "深度推理思考链，媲美o3-mini，数学竞赛级"
        },
        new() {
            Id = "qwen2.5-coder-32b", FullName = "Qwen2.5-Coder-32B-Instruct", Provider = "阿里通义",
            ApiProvider = "Qwen",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "qwen2.5-coder-32b-instruct",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 128_000,
            IsOpenSource = true,
            Description = "代码专项旗舰，支持92种编程语言"
        },
        new() {
            Id = "glm-4-9b", FullName = "GLM-4-9B-Chat", Provider = "智谱AI",
            ApiProvider = "ZhipuAI",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            ApiModelId = "glm-4-9b",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 128_000,
            IsOpenSource = true,
            Description = "轻量开源中文模型，工具调用优化，Agent友好"
        },
        new() {
            Id = "internlm3-20b", FullName = "InternLM3-20B-Instruct", Provider = "上海AI实验室",
            ApiProvider = "Qwen",   // 通过百炼平台接入
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "internlm3-20b-instruct",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 1_000_000,
            IsOpenSource = true,
            Description = "工具调用原生支持，超长上下文，Agent编排最优"
        },
        new() {
            Id = "yi-34b", FullName = "Yi-34B-Chat", Provider = "零一万物",
            ApiProvider = "ZhipuAI",
            BaseUrl = "https://api.lingyiwanwu.com/v1",
            ApiModelId = "yi-34b-chat",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 200_000,
            IsOpenSource = true,
            Description = "中英双语均衡，长上下文理解强"
        },
        new() {
            Id = "baichuan2-13b", FullName = "Baichuan2-13B-Chat", Provider = "百川智能",
            ApiProvider = "Baichuan",
            BaseUrl = "https://api.baichuan-ai.com/v1",
            ApiModelId = "Baichuan2-13B-Chat",
            Category = ModelCategory.ChineseOpenSource,
            ContextWindow = 4_000,
            IsOpenSource = true,
            Description = "中文文化理解深，知识丰富，轻量可本地"
        },

        // ═══════════════════════════════════════════
        // B类：中国商业API
        // ═══════════════════════════════════════════
        new() {
            Id = "qwen-max", FullName = "Qwen-Max", Provider = "阿里云百炼",
            ApiProvider = "Qwen",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "qwen-max",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 128_000,
            SupportsVision = true,
            Description = "阿里商业旗舰，多轮对话、工具调用成熟，企业级SLA"
        },
        new() {
            Id = "qwen-plus", FullName = "Qwen-Plus", Provider = "阿里云百炼",
            ApiProvider = "Qwen",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "qwen-plus",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 128_000,
            Description = "性价比次旗舰，均衡快速"
        },
        new() {
            Id = "qwen-turbo", FullName = "Qwen-Turbo", Provider = "阿里云百炼",
            ApiProvider = "Qwen",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiModelId = "qwen-turbo",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 128_000,
            Description = "极速响应，最低成本"
        },
        new() {
            Id = "glm-4-plus", FullName = "GLM-4-Plus", Provider = "智谱AI",
            ApiProvider = "ZhipuAI",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            ApiModelId = "glm-4-plus",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 128_000,
            Description = "中文深度理解，多模态，函数调用成熟"
        },
        new() {
            Id = "glm-z1-flash", FullName = "GLM-Z1-Flash", Provider = "智谱AI",
            ApiProvider = "ZhipuAI",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            ApiModelId = "glm-z1-flash",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 64_000,
            Description = "推理增强型，深度思考链，快速输出"
        },
        new() {
            Id = "moonshot-v1-128k", FullName = "Kimi (Moonshot-v1-128k)", Provider = "月之暗面",
            ApiProvider = "Moonshot",
            BaseUrl = "https://api.moonshot.cn/v1",
            ApiModelId = "moonshot-v1-128k",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 128_000,
            Description = "超长文档理解专项，文件分析、书籍摘要场景最优"
        },
        new() {
            Id = "minimax-text-01", FullName = "MiniMax-Text-01", Provider = "MiniMax",
            ApiProvider = "MiniMax",
            BaseUrl = "https://api.minimax.chat/v1",
            ApiModelId = "MiniMax-Text-01",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 1_000_000,
            Description = "100万Token超长上下文，全球最长，适合全书处理"
        },
        new() {
            Id = "spark4.0-ultra", FullName = "Spark4.0-Ultra", Provider = "科大讯飞",
            ApiProvider = "iFlytek",
            BaseUrl = "https://spark-api-open.xf-yun.com/v1",
            ApiModelId = "4.0Ultra",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 128_000,
            Description = "语音文本深度融合，教育医疗行业专项优化"
        },
        new() {
            Id = "ernie-4.5", FullName = "ERNIE-4.5", Provider = "百度文心",
            ApiProvider = "Baidu",
            BaseUrl = "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop",
            ApiModelId = "ernie-4.5-8k",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 128_000,
            Description = "中文生态深度整合，搜索增强，内容创作"
        },
        new() {
            Id = "doubao-pro-32k", FullName = "Doubao-Pro-32k", Provider = "字节豆包",
            ApiProvider = "Doubao",
            BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            ApiModelId = "ep-20241230154618-2tkgv",  // 用户需替换为自己的endpoint
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 32_000,
            Description = "极低成本高效响应，日常任务性价比最高"
        },
        new() {
            Id = "doubao-seedream-5.0-lite", FullName = "Doubao-Seedream-5.0-Lite", Provider = "字节豆包",
            ApiProvider = "Doubao",
            BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            ApiModelId = "doubao-seedream-5-0-lite",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 0,
            SupportsImageGeneration = true,
            Description = "豆包文生图模型（文字→图片生成），不支持图片输入识别，需配置对应 Endpoint ID"
        },
        new() {
            Id = "doubao-seedance-1.5-pro", FullName = "Doubao-Seedance-1.5-Pro", Provider = "字节豆包",
            ApiProvider = "Doubao",
            BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            ApiModelId = "doubao-seedance-1-5-pro",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 0,
            SupportsVideoGeneration = true, SupportsVideoUnderstanding = true,
            Description = "豆包视频生成旗舰（文字→视频生成），需配置对应 Endpoint ID"
        },
        new() {
            Id = "doubao-seedance-1.0-pro-fast", FullName = "Doubao-Seedance-1.0-Pro-Fast", Provider = "字节豆包",
            ApiProvider = "Doubao",
            BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            ApiModelId = "doubao-seedance-1-0-pro-fast",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 0,
            SupportsVideoGeneration = true,
            Description = "豆包视频生成高速版（文字→视频生成），需配置对应 Endpoint ID"
        },
        new() {
            Id = "hunyuan-turbo", FullName = "Hunyuan-Turbo", Provider = "腾讯混元",
            ApiProvider = "Tencent",
            BaseUrl = "https://api.hunyuan.cloud.tencent.com/v1",
            ApiModelId = "hunyuan-turbo",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 256_000,
            Description = "腾讯生态整合，办公/搜索场景，长上下文"
        },
        new() {
            Id = "baichuan4-turbo", FullName = "Baichuan4-Turbo", Provider = "百川智能",
            ApiProvider = "Baichuan",
            BaseUrl = "https://api.baichuan-ai.com/v1",
            ApiModelId = "Baichuan4-Turbo",
            Category = ModelCategory.ChineseCommercial,
            ContextWindow = 32_000,
            Description = "中文文化理解最深，文学创作、古文处理"
        },

        // ═══════════════════════════════════════════
        // C类：美国主流模型
        // ═══════════════════════════════════════════
        new() {
            Id = "claude-sonnet-4-6", FullName = "Claude Sonnet 4.6", Provider = "Anthropic",
            ApiProvider = "Anthropic",
            BaseUrl = "https://api.anthropic.com/v1",
            ApiModelId = "claude-sonnet-4-6",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 200_000,
            Description = "代码/长文本最强，Agent执行能力顶级，安全性高"
        },
        new() {
            Id = "claude-opus-4-6", FullName = "Claude Opus 4.6", Provider = "Anthropic",
            ApiProvider = "Anthropic",
            BaseUrl = "https://api.anthropic.com/v1",
            ApiModelId = "claude-opus-4-6",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 200_000,
            Description = "Anthropic旗舰，复杂推理、科研写作"
        },
        new() {
            Id = "claude-haiku-4-5", FullName = "Claude Haiku 4.5", Provider = "Anthropic",
            ApiProvider = "Anthropic",
            BaseUrl = "https://api.anthropic.com/v1",
            ApiModelId = "claude-haiku-4-5-20251001",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 200_000,
            Description = "轻量极速版，低延迟简单任务"
        },
        new() {
            Id = "gpt-4o", FullName = "GPT-4o", Provider = "OpenAI",
            ApiProvider = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            ApiModelId = "gpt-4o",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 128_000,
            SupportsVision = true,
            Description = "多模态旗舰，工具调用最成熟，生态最广"
        },
        new() {
            Id = "o3", FullName = "o3", Provider = "OpenAI",
            ApiProvider = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            ApiModelId = "o3",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 200_000,
            Description = "顶级推理，数学/科学竞赛级，博士难题"
        },
        new() {
            Id = "o4-mini", FullName = "o4-mini", Provider = "OpenAI",
            ApiProvider = "OpenAI",
            BaseUrl = "https://api.openai.com/v1",
            ApiModelId = "o4-mini",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 200_000,
            Description = "高效推理，o3能力70%，价格10%，性价比推理首选"
        },
        new() {
            Id = "gemini-2.5-pro", FullName = "Gemini 2.5 Pro", Provider = "Google",
            ApiProvider = "Google",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            ApiModelId = "gemini-2.5-pro",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 1_000_000,
            SupportsVision = true, SupportsVideoUnderstanding = true,
            Description = "超长上下文+多模态，代码强，图像/视频识别"
        },
        new() {
            Id = "gemini-2.0-flash", FullName = "Gemini 2.0 Flash", Provider = "Google",
            ApiProvider = "Google",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            ApiModelId = "gemini-2.0-flash",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 1_000_000,
            SupportsVision = true, SupportsVideoUnderstanding = true,
            Description = "极速轻量，高吞吐，图像/视频识别，低成本"
        },
        new() {
            Id = "grok-3", FullName = "Grok-3", Provider = "xAI",
            ApiProvider = "xAI",
            BaseUrl = "https://api.x.ai/v1",
            ApiModelId = "grok-3",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 128_000,
            Description = "实时互联网数据接入，深度推理，X平台生态"
        },
        new() {
            Id = "mistral-large-3", FullName = "Mistral Large 3", Provider = "Mistral AI",
            ApiProvider = "Mistral",
            BaseUrl = "https://api.mistral.ai/v1",
            ApiModelId = "mistral-large-latest",
            Category = ModelCategory.UsCommercial,
            ContextWindow = 128_000,
            Description = "欧洲产，多语言优秀，GDPR合规，隐私友好"
        },

        // ═══════════════════════════════════════════
        // D类：本地Ollama（零成本）
        // ═══════════════════════════════════════════
        new() {
            Id = "ollama/deepseek-r1:8b", FullName = "DeepSeek-R1-8B", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "deepseek-r1:8b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 32_000,
            IsLocal = true, IsOpenSource = true,
            Description = "本地最佳中文推理，思考链可见，需8GB内存"
        },
        new() {
            Id = "ollama/deepseek-r1:1.5b", FullName = "DeepSeek-R1-1.5B", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "deepseek-r1:1.5b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 32_000,
            IsLocal = true, IsOpenSource = true,
            Description = "极轻量推理，低配设备可用，需2GB内存"
        },
        new() {
            Id = "ollama/qwen2.5:7b", FullName = "Qwen2.5-7B-Instruct", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "qwen2.5:7b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 32_000,
            IsLocal = true, IsOpenSource = true,
            Description = "本地中文综合最优，日常任务，需6GB内存"
        },
        new() {
            Id = "ollama/qwen2.5-coder:7b", FullName = "Qwen2.5-Coder-7B", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "qwen2.5-coder:7b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 32_000,
            IsLocal = true, IsOpenSource = true,
            Description = "本地代码补全/生成专项，需6GB内存"
        },
        new() {
            Id = "ollama/qwq:32b", FullName = "QwQ-32B-Q4", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "qwq:32b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 32_000,
            IsLocal = true, IsOpenSource = true,
            Description = "本地深度推理，高配专用，需20GB内存"
        },
        new() {
            Id = "ollama/glm4:9b", FullName = "GLM-4-9B-Chat", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "glm4:9b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 128_000,
            IsLocal = true, IsOpenSource = true,
            Description = "本地中文对话，工具调用支持，需8GB内存"
        },
        new() {
            Id = "ollama/llama3.3:70b", FullName = "Llama 3.3-70B-Q4", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "llama3.3:70b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 128_000,
            IsLocal = true, IsOpenSource = true,
            Description = "Meta开源旗舰英文，高配专用，需40GB内存"
        },
        new() {
            Id = "ollama/gemma3:4b", FullName = "Gemma3-4B", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "gemma3:4b",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 32_000,
            IsLocal = true, IsOpenSource = true,
            Description = "Google轻量本地，英文优秀，需6GB内存"
        },
        new() {
            Id = "ollama/phi4-mini", FullName = "Phi-4-mini", Provider = "本地Ollama",
            ApiProvider = "Ollama",
            BaseUrl = "http://localhost:11434/v1",
            ApiModelId = "phi4-mini",
            Category = ModelCategory.LocalOllama,
            ContextWindow = 16_000,
            IsLocal = true, IsOpenSource = true,
            Description = "微软轻量代码，数学能力强，需4GB内存"
        },
    ];
}
