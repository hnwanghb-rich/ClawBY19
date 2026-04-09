using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

/// <summary>
/// 自动化测试日志：记录每次测试的入口-功能-操作-结果
/// </summary>
public class TestLog
{
    [Key] public int Id { get; set; }

    /// <summary>测试会话ID（一次测试任务的唯一标识）</summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>测试目标：URL 或 EXE 路径</summary>
    public string TestTarget { get; set; } = string.Empty;

    /// <summary>测试类型：Web | Exe</summary>
    public string TestType { get; set; } = string.Empty;

    /// <summary>菜单入口（测试功能所在菜单路径）</summary>
    public string MenuPath { get; set; } = string.Empty;

    /// <summary>被测功能名称</summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>测试操作（点击、输入等）</summary>
    public string TestAction { get; set; } = string.Empty;

    /// <summary>测试结果：Pass | Exception</summary>
    public string TestResult { get; set; } = "Pass";

    /// <summary>异常详情（仅 TestResult == "Exception" 时有值）</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>记录时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
