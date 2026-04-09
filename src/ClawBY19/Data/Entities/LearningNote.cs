using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

/// <summary>学习笔记：记录每次任务的完整生命周期</summary>
public class LearningNote
{
    [Key] public int Id { get; set; }
    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? TaskType { get; set; }
    public string UserPrompt { get; set; } = string.Empty;
    public string? ExecutionPlan { get; set; }
    public string? ExecutionLog { get; set; }
    public string? FinalResult { get; set; }
    public string Status { get; set; } = "running";   // running|success|failed|revised

    // 人机交互反馈
    public string? UserFeedback { get; set; }          // JSON 数组
    public int RevisionCount { get; set; }

    // 成功执行四维总结
    public string? SummaryProblem { get; set; }
    public string? SummarySolution { get; set; }
    public string? SummaryKeySteps { get; set; }        // JSON 数组
    public string? SummaryAvoidError { get; set; }

    // 向量嵌入（JSON float[]，用于余弦相似度计算）
    public string? EmbeddingJson { get; set; }

    // 模型消耗
    public string? ModelUsed { get; set; }
    public int TokensInput { get; set; }
    public int TokensOutput { get; set; }
    public decimal CostCny { get; set; }

    public ICollection<UserRevision> Revisions { get; set; } = new List<UserRevision>();
}

/// <summary>用户修改意见明细</summary>
public class UserRevision
{
    [Key] public int Id { get; set; }
    public int LearningNoteId { get; set; }
    public LearningNote LearningNote { get; set; } = null!;
    public int RevisionNo { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string UserOpinion { get; set; } = string.Empty;
    public string? BeforeAction { get; set; }
    public string? AfterAction { get; set; }
    public bool Applied { get; set; } = true;
}

/// <summary>核心经验库：高置信度经验，每次任务自动注入 System Prompt</summary>
public class CoreExperience
{
    [Key] public int Id { get; set; }
    public string ExpCode { get; set; } = string.Empty;     // EXP-001
    public string Rule { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int SourceCount { get; set; } = 1;
    public double Confidence { get; set; } = 0.5;
    public string? Category { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool Active { get; set; } = true;
}
