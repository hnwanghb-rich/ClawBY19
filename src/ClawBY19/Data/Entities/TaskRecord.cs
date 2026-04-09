using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

/// <summary>
/// 操作任务记录：记录 Git 上传 / 云端部署每次操作的完整执行信息
/// </summary>
public class TaskRecord
{
    [Key] public int Id { get; set; }

    /// <summary>12位短任务ID，供界面展示</summary>
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..12].ToUpper();

    /// <summary>任务类型：GitUpload | CloudDeploy</summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>摘要说明（仓库名或服务器IP）</summary>
    public string Summary { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }

    /// <summary>运行状态：running | success | failed</summary>
    public string Status { get; set; } = "running";

    // ── Git 上传专用字段 ────────────────────────────────────────────────
    public string? LocalPath { get; set; }
    public string? RemoteUrl { get; set; }
    public string? Branch { get; set; }
    public string? CommitMessage { get; set; }

    // ── 云端部署专用字段 ────────────────────────────────────────────────
    public string? ServerIp { get; set; }
    public string? DeployPath { get; set; }
    public string? OnlineUrl { get; set; }

    // ── 日志与分析 ──────────────────────────────────────────────────────
    /// <summary>逐步执行日志：JSON 数组 [{Step,Output,Success,Timestamp}]</summary>
    public string? StepLogsJson { get; set; }

    /// <summary>大模型失败原因分析</summary>
    public string? AiAnalysis { get; set; }

    /// <summary>错误特征提取（用于学习库匹配）</summary>
    public string? ErrorFeature { get; set; }
}
