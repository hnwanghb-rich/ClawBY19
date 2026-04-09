using System.Text;
using System.Text.Json;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using ClawBY19.Services.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.TaskLog;

/// <summary>单步执行日志条目</summary>
public record StepLogEntry(string Step, string Output, bool Success, string Timestamp);

/// <summary>
/// 操作任务日志服务：负责 Git 上传 / 云端部署任务的完整生命周期记录与 AI 失败分析
/// </summary>
public class TaskLogService
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly IAiClient _aiClient;
    private readonly ConfigService _config;
    private readonly ModelRegistry _registry;
    private readonly ILogger<TaskLogService> _logger;

    public TaskLogService(
        IDbContextFactory<ClawDbContext> dbFactory,
        IAiClient aiClient,
        ConfigService config,
        ModelRegistry registry,
        ILogger<TaskLogService> logger)
    {
        _dbFactory = dbFactory;
        _aiClient  = aiClient;
        _config    = config;
        _registry  = registry;
        _logger    = logger;
    }

    // ── 任务生命周期 ──────────────────────────────────────────────────────

    /// <summary>创建运行中的任务记录，返回数据库主键 ID</summary>
    public async Task<int> StartTaskAsync(
        string taskType, string summary,
        string? localPath = null, string? remoteUrl = null,
        string? branch = null, string? commitMessage = null,
        string? serverIp = null, string? deployPath = null, string? onlineUrl = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rec = new TaskRecord
        {
            TaskType      = taskType,
            Summary       = summary,
            LocalPath     = localPath,
            RemoteUrl     = remoteUrl,
            Branch        = branch,
            CommitMessage = commitMessage,
            ServerIp      = serverIp,
            DeployPath    = deployPath,
            OnlineUrl     = onlineUrl
        };
        db.TaskRecords.Add(rec);
        await db.SaveChangesAsync();
        _logger.LogInformation("任务开始 [{Type}] id={Id} {Summary}", taskType, rec.Id, summary);
        return rec.Id;
    }

    /// <summary>完成任务：解析步骤日志，更新状态</summary>
    public async Task CompleteTaskAsync(int taskRecordId, bool success, string rawLog)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rec = await db.TaskRecords.FindAsync(taskRecordId);
        if (rec is null) return;

        rec.FinishedAt   = DateTime.Now;
        rec.Status       = success ? "success" : "failed";
        rec.StepLogsJson = JsonSerializer.Serialize(ParseStepLogs(rawLog, success));

        if (!success)
            rec.ErrorFeature = ExtractErrorFeature(rawLog);

        await db.SaveChangesAsync();
        _logger.LogInformation("任务完成 id={Id} success={S}", taskRecordId, success);
    }

    // ── AI 失败分析 ────────────────────────────────────────────────────────

    /// <summary>
    /// 将失败日志发给大模型分析原因与解决方案；
    /// 自动查询学习库中相似失败路径以避免重复建议
    /// </summary>
    public async Task<string> AnalyzeFailureAsync(int taskRecordId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rec = await db.TaskRecords.FindAsync(taskRecordId);
        if (rec is null) return string.Empty;

        // 查询已知失败路径，提示 AI 排除这些方案
        var knownFailed = await db.FailedPaths
            .Where(f => f.TaskType == rec.TaskType && !f.IsResolved)
            .OrderByDescending(f => f.LastAttemptAt)
            .Take(5)
            .ToListAsync(ct);

        var knownSection = knownFailed.Any()
            ? "\n\n【已知失败方案，请勿重复建议】\n" +
              string.Join("\n", knownFailed.Select(f => $"- {f.FailedSolution}（尝试 {f.AttemptCount} 次）"))
            : string.Empty;

        // 如有已成功方案，优先推荐
        var successPath = await db.FailedPaths
            .Where(f => f.TaskType == rec.TaskType && f.IsResolved && f.SuccessSolution != null)
            .OrderByDescending(f => f.LastAttemptAt)
            .FirstOrDefaultAsync(ct);

        var successHint = successPath is not null
            ? $"\n\n【历史成功方案参考】\n{successPath.SuccessSolution}"
            : string.Empty;

        // 构造失败日志摘要
        var steps = rec.StepLogsJson is not null
            ? JsonSerializer.Deserialize<List<StepLogEntry>>(rec.StepLogsJson) ?? []
            : [];
        var failLines = steps
            .Where(s => !s.Success)
            .Select(s => $"[失败] {s.Step}: {s.Output}")
            .ToList();
        var logSummary = failLines.Any()
            ? string.Join("\n", failLines)
            : rec.ErrorFeature ?? "（未能提取详细错误信息）";

        var modelId = _config.ModelCfg.ClawAuto.DefaultModel;
        var model   = _registry.Get(modelId);
        if (model is null) return "（AI 分析不可用：未配置模型）";
        var apiKey = _config.GetApiKey(model.ApiProvider);
        if (string.IsNullOrEmpty(apiKey)) return "（AI 分析不可用：未配置 API Key）";

        var messages = new List<ApiMessage>
        {
            new("system",
                "你是一位专业 DevOps 工程师。请分析失败原因，输出格式严格按以下两段：\n" +
                "【原因诊断】\n（明确说明失败根因：认证失败/网络超时/文件冲突/权限不足等）\n\n" +
                "【解决方案】\n（给出 3 步以内的具体操作步骤，不要废话）"),
            new("user",
                $"任务类型：{rec.TaskType}\n" +
                $"目标：{rec.Summary}\n\n" +
                $"失败步骤：\n{logSummary}" +
                knownSection + successHint)
        };

        var sb = new StringBuilder();
        try
        {
            await foreach (var token in _aiClient.StreamAsync(
                model.BaseUrl, model.ApiModelId, apiKey, messages, ct: ct))
                sb.Append(token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 失败分析异常");
            sb.Append($"AI 分析失败：{ex.Message}");
        }

        var analysis = sb.ToString().Trim();

        // 保存分析结果
        await using var db2 = await _dbFactory.CreateDbContextAsync();
        var rec2 = await db2.TaskRecords.FindAsync(taskRecordId);
        if (rec2 is not null)
        {
            rec2.AiAnalysis = analysis;
            await db2.SaveChangesAsync();
        }

        return analysis;
    }

    /// <summary>将某方案标记为失败，写入学习库</summary>
    public async Task RecordFailedPathAsync(int taskRecordId, string failedSolution)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rec = await db.TaskRecords.FindAsync(taskRecordId);
        if (rec is null) return;

        var existing = await db.FailedPaths.FirstOrDefaultAsync(
            f => f.TaskType == rec.TaskType && f.ErrorFeature == rec.ErrorFeature);

        if (existing is not null)
        {
            existing.AttemptCount++;
            existing.LastAttemptAt  = DateTime.Now;
            existing.FailedSolution = failedSolution;
        }
        else
        {
            db.FailedPaths.Add(new FailedPath
            {
                TaskType       = rec.TaskType,
                ErrorFeature   = rec.ErrorFeature ?? string.Empty,
                FailedSolution = failedSolution
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>将某方案标记为成功，更新学习库</summary>
    public async Task RecordSuccessPathAsync(int taskRecordId, string successSolution)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rec = await db.TaskRecords.FindAsync(taskRecordId);
        if (rec is null) return;

        var existing = await db.FailedPaths.FirstOrDefaultAsync(
            f => f.TaskType == rec.TaskType && f.ErrorFeature == rec.ErrorFeature);

        if (existing is not null)
        {
            existing.SuccessSolution = successSolution;
            existing.IsResolved      = true;
            existing.LastAttemptAt   = DateTime.Now;
        }
        await db.SaveChangesAsync();
    }

    // ── 查询 ──────────────────────────────────────────────────────────────

    public async Task<List<TaskRecord>> GetTasksAsync(
        string? typeFilter   = null,
        string? statusFilter = null,
        int take = 200)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var q = db.TaskRecords.AsQueryable();
        if (!string.IsNullOrEmpty(typeFilter)   && typeFilter   != "全部") q = q.Where(r => r.TaskType == typeFilter);
        if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "全部") q = q.Where(r => r.Status   == statusFilter);
        return await q.OrderByDescending(r => r.StartedAt).Take(take).ToListAsync();
    }

    // ── 内部工具 ──────────────────────────────────────────────────────────

    private static List<StepLogEntry> ParseStepLogs(string rawLog, bool overallSuccess)
    {
        var steps = new List<StepLogEntry>();
        if (string.IsNullOrWhiteSpace(rawLog)) return steps;

        foreach (var raw in rawLog.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            bool success = line.StartsWith("✅") || (!line.StartsWith("❌") && overallSuccess);
            var  clean   = line.TrimStart('✅', '❌', ' ');
            var  colon   = clean.IndexOf(':');
            var  step    = colon > 0 ? clean[..colon].Trim() : clean;
            var  output  = colon > 0 ? clean[(colon + 1)..].Trim() : string.Empty;

            steps.Add(new StepLogEntry(step, output, success, DateTime.Now.ToString("HH:mm:ss")));
        }
        return steps;
    }

    private static string ExtractErrorFeature(string rawLog)
    {
        var keywords = new[]
        {
            "authentication failed", "rejected", "timeout", "permission denied",
            "not found", "conflict", "merge conflict",
            "认证失败", "权限不足", "超时", "拒绝", "冲突", "找不到"
        };
        var lower = rawLog.ToLowerInvariant();
        var hits  = keywords.Where(k => lower.Contains(k)).Take(3).ToArray();
        return hits.Length > 0
            ? string.Join(",", hits)
            : rawLog[..Math.Min(80, rawLog.Length)].Trim();
    }
}
