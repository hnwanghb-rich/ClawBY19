namespace ClawBY19.Config;

public class AppSettings
{
    public string AppName { get; set; } = "ClawBY19";
    public string Version { get; set; } = "2.0";
    public string DataDirectory { get; set; } = "data";
    public string LogDirectory { get; set; } = "logs";
    public string WorkspaceDirectory { get; set; } = "data/workspace";
    public string SkillsDirectory { get; set; } = "skills";
    public Dictionary<string, string> ApiKeys { get; set; } = new();
    public Dictionary<string, string> ApiModelIds { get; set; } = new();  // 模型ID覆盖（如豆包EndpointID）
    public string Theme { get; set; } = "Dark";
    public string NickName { get; set; } = string.Empty;   // 用户给 ClawBY19 起的昵称
    public AgentSettings Agent { get; set; } = new();
    public RagSettings Rag { get; set; } = new();
}

public class AgentSettings
{
    public int MaxIterations { get; set; } = 20;
    public int TimeoutSeconds { get; set; } = 300;
    public bool ConfirmBeforeExecution { get; set; } = true;
    public bool StreamOutput { get; set; } = true;
}

public class RagSettings
{
    public int TopK { get; set; } = 5;
    public double MinSimilarity { get; set; } = 0.3;
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public bool UseLocalEmbedding { get; set; } = false;
}
