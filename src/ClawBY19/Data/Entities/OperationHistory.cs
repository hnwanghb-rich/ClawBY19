using System.ComponentModel.DataAnnotations;

namespace ClawBY19.Data.Entities;

/// <summary>
/// 操作历史：记录 GitHub 同步 / 云端部署等敏感操作的历史配置
/// 密码字段经 AES 加密后存储，解密由 OperationHistoryService 负责
/// </summary>
public class OperationHistory
{
    [Key] public int Id { get; set; }

    /// <summary>操作类型：GitHub | CloudDeploy</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>目标名称，如仓库名或平台名</summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>目标地址，如 https://github.com/user/repo 或服务器 IP</summary>
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>账号（明文存储，非敏感信息）</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>密码（AES 加密后的 Base64 字符串）</summary>
    public string PasswordEncrypted { get; set; } = string.Empty;

    /// <summary>额外参数（JSON，如分支、端口、路径等）</summary>
    public string? ExtraJson { get; set; }

    /// <summary>首次记录时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最近使用时间</summary>
    public DateTime LastUsedAt { get; set; } = DateTime.Now;

    /// <summary>使用次数</summary>
    public int UseCount { get; set; } = 1;

    /// <summary>备注（用户自定义说明）</summary>
    public string? Remark { get; set; }
}
