namespace ClawBY19.Services.AI;

/// <summary>模型分类</summary>
public enum ModelCategory
{
    ChineseOpenSource,   // A类：中国开源（最优先）
    ChineseCommercial,   // B类：中国商业API
    UsCommercial,        // C类：美国主流
    LocalOllama          // D类：本地Ollama（零成本）
}

/// <summary>单个模型的完整元数据</summary>
public class ModelInfo
{
    public string Id { get; init; } = string.Empty;           // 配置文件中使用的ID
    public string FullName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ApiProvider { get; init; } = string.Empty;  // API密钥配置键
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiModelId { get; init; } = string.Empty;   // 实际传给API的model字段
    public ModelCategory Category { get; init; }
    public int ContextWindow { get; init; }                    // Token数
    public string Description { get; init; } = string.Empty;
    public bool IsLocal { get; init; }                         // 是否本地Ollama
    public bool IsOpenSource { get; init; }
    public bool SupportsVision { get; init; }                  // 图像识别（理解输入图片）
    public bool SupportsImageGeneration { get; init; }         // 图像生成（文生图）
    public bool SupportsVideoUnderstanding { get; init; }      // 视频识别（理解输入视频）
    public bool SupportsVideoGeneration { get; init; }         // 视频生成（文生视频）
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";
}
