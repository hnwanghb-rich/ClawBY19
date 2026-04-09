using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

/// <summary>
/// 失败路径学习库：记录已尝试但失败的解决方案，供下次自动规避
/// </summary>
public class FailedPath
{
    [Key] public int Id { get; set; }

    /// <summary>关联任务类型：GitUpload | CloudDeploy</summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>错误特征签名（用于相似错误匹配）</summary>
    public string ErrorFeature { get; set; } = string.Empty;

    /// <summary>已尝试但失败的方案描述</summary>
    public string FailedSolution { get; set; } = string.Empty;

    /// <summary>该方案被尝试的次数</summary>
    public int AttemptCount { get; set; } = 1;

    public DateTime LastAttemptAt { get; set; } = DateTime.Now;

    /// <summary>最终成功的方案（如有）</summary>
    public string? SuccessSolution { get; set; }

    /// <summary>是否已通过某成功方案解决</summary>
    public bool IsResolved { get; set; }
}
