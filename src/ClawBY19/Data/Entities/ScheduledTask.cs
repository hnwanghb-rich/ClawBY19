using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

public class ScheduledTask
{
    [Key] public int Id { get; set; }
    public string Description { get; set; } = string.Empty;  // 用户可读描述
    public string CronExpr    { get; set; } = string.Empty;  // "0 9 * * *"
    public string Prompt      { get; set; } = string.Empty;  // 到时发给 AI 的指令
    public bool   IsActive    { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastRun  { get; set; }
    public DateTime? NextRun  { get; set; }
    public string? LastResult { get; set; }                   // 最近一次执行结果摘要
}
