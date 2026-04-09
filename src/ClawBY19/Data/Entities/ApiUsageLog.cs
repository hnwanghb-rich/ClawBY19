using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

public class ApiUsageLog
{
    [Key] public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? TaskId { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string SelectionMode { get; set; } = "user_specified"; // user_specified | claw_auto
    public int TokensInput { get; set; }
    public int TokensOutput { get; set; }
    public int TokensTotal { get; set; }
    public decimal CostCny { get; set; }
    public int LatencyMs { get; set; }
    public int TtftMs { get; set; }
    public string Status { get; set; } = "success";   // success | error | timeout
    public string? ErrorMessage { get; set; }
}
