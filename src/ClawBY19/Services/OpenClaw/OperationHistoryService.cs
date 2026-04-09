using System.Security.Cryptography;
using System.Text;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.OpenClaw;

/// <summary>操作类型常量</summary>
public static class OpType
{
    public const string GitHub     = "GitHub";
    public const string CloudDeploy = "CloudDeploy";
}

/// <summary>
/// 操作历史服务：管理 GitHub/云端操作的历史记录与凭证加解密
/// 凭证使用 AES-256 + 机器唯一密钥加密，防止明文落盘
/// </summary>
public class OperationHistoryService
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly ILogger<OperationHistoryService> _logger;
    private readonly byte[] _aesKey;

    public OperationHistoryService(
        IDbContextFactory<ClawDbContext> dbFactory,
        ILogger<OperationHistoryService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _aesKey = DeriveKey();
    }

    // ── 查询 ─────────────────────────────────────────────────────────

    /// <summary>获取指定操作类型最近一次记录（按 LastUsedAt 降序）</summary>
    public async Task<OperationHistory?> GetLastAsync(string opType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OperationHistories
            .Where(o => o.OperationType == opType)
            .OrderByDescending(o => o.LastUsedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>获取指定操作类型的全部历史记录，按最近使用排序</summary>
    public async Task<List<OperationHistory>> GetAllAsync(string opType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OperationHistories
            .Where(o => o.OperationType == opType)
            .OrderByDescending(o => o.LastUsedAt)
            .ToListAsync();
    }

    // ── 保存 / 更新 ──────────────────────────────────────────────────

    /// <summary>
    /// 保存或更新操作记录。
    /// 若已有相同 OperationType + TargetUrl 的记录则更新，否则新增。
    /// password 传 null 表示不修改已存密码。
    /// </summary>
    public async Task<OperationHistory> SaveAsync(
        string opType,
        string targetName,
        string targetUrl,
        string account,
        string? password)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.OperationHistories
            .FirstOrDefaultAsync(o => o.OperationType == opType && o.TargetUrl == targetUrl);

        if (existing is not null)
        {
            existing.TargetName  = targetName;
            existing.Account     = account;
            existing.LastUsedAt  = DateTime.Now;
            existing.UseCount++;
            if (password is not null)
                existing.PasswordEncrypted = EncryptPassword(password);
        }
        else
        {
            existing = new OperationHistory
            {
                OperationType      = opType,
                TargetName         = targetName,
                TargetUrl          = targetUrl,
                Account            = account,
                PasswordEncrypted  = EncryptPassword(password ?? string.Empty),
                CreatedAt          = DateTime.Now,
                LastUsedAt         = DateTime.Now,
                UseCount           = 1
            };
            db.OperationHistories.Add(existing);
        }

        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>更新最近使用时间 + 使用次数（每次使用现有记录时调用）</summary>
    public async Task TouchAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rec = await db.OperationHistories.FindAsync(id);
        if (rec is null) return;
        rec.LastUsedAt = DateTime.Now;
        rec.UseCount++;
        await db.SaveChangesAsync();
    }

    // ── 凭证解密（供 Agent 执行时使用）──────────────────────────────

    /// <summary>解密密码，返回明文。失败则返回空字符串。</summary>
    public string DecryptPassword(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return string.Empty;
        try
        {
            var combined = Convert.FromBase64String(encryptedBase64);
            // 前16字节为 IV
            var iv         = combined[..16];
            var cipherText = combined[16..];
            using var aes = Aes.Create();
            aes.Key = _aesKey;
            aes.IV  = iv;
            using var decryptor = aes.CreateDecryptor();
            var plain = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解密密码失败");
            return string.Empty;
        }
    }

    // ── 私有工具 ─────────────────────────────────────────────────────

    private string EncryptPassword(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        using var aes = Aes.Create();
        aes.Key = _aesKey;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plain  = Encoding.UTF8.GetBytes(plainText);
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        // IV + 密文 → Base64
        var combined = new byte[aes.IV.Length + cipher.Length];
        aes.IV.CopyTo(combined, 0);
        cipher.CopyTo(combined, aes.IV.Length);
        return Convert.ToBase64String(combined);
    }

    /// <summary>从机器唯一信息派生 256-bit AES 密钥（SHA-256）</summary>
    private static byte[] DeriveKey()
    {
        var seed = $"ClawBY19:{Environment.MachineName}:{Environment.UserName}:OpHistKey";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }
}
