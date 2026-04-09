using System.Text;
using System.Text.Json;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Rag;

/// <summary>
/// RAG学习笔记库：
/// 1. 记录任务执行全过程
/// 2. 语义检索相关历史经验（余弦相似度，嵌入向量存储于 SQLite BLOB）
/// 3. 将历史经验注入 System Prompt
/// </summary>
public class RagService
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly AI.IAiClient _aiClient;
    private readonly AI.ConfigService _config;
    private readonly AI.ModelRegistry _registry;
    private readonly ILogger<RagService> _logger;

    public RagService(IDbContextFactory<ClawDbContext> dbFactory, AI.IAiClient aiClient,
        AI.ConfigService config, AI.ModelRegistry registry, ILogger<RagService> logger)
    {
        _dbFactory = dbFactory;
        _aiClient = aiClient;
        _config = config;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>为新任务检索 Top-K 相关历史经验，返回注入文本</summary>
    public async Task<string> BuildContextAsync(string userPrompt, int topK = 5)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var successNotes = await db.LearningNotes
            .Where(n => n.Status == "success" && n.EmbeddingJson != null)
            .ToListAsync();

        if (successNotes.Count == 0) return string.Empty;

        var queryEmb = await GetEmbeddingAsync(userPrompt);
        if (queryEmb is null) return BuildKeywordContext(userPrompt, successNotes, topK);

        var ranked = successNotes
            .Select(n =>
            {
                float[]? emb = null;
                try { emb = JsonSerializer.Deserialize<float[]>(n.EmbeddingJson!); } catch { }
                return (Note: n, Score: emb is null ? 0f : CosineSimilarity(queryEmb, emb));
            })
            .OrderByDescending(x => x.Score)
            .Where(x => x.Score >= (float)_config.AppSettings.Rag.MinSimilarity)
            .Take(topK)
            .ToList();

        return BuildContextText(ranked.Select(x => x.Note).ToList());
    }

    /// <summary>保存任务笔记（开始时调用）</summary>
    public async Task<string> CreateNoteAsync(string userPrompt, string? taskType = null)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var emb = await GetEmbeddingAsync(userPrompt);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.LearningNotes.Add(new LearningNote
        {
            TaskId = taskId,
            UserPrompt = userPrompt,
            TaskType = taskType,
            Status = "running",
            EmbeddingJson = emb is null ? null : JsonSerializer.Serialize(emb)
        });
        await db.SaveChangesAsync();
        return taskId;
    }

    /// <summary>记录用户修改意见</summary>
    public async Task AddRevisionAsync(string taskId, string opinion, string? before, string? after)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var note = await db.LearningNotes.FirstOrDefaultAsync(n => n.TaskId == taskId);
        if (note is null) return;

        note.RevisionCount++;
        db.UserRevisions.Add(new UserRevision
        {
            LearningNoteId = note.Id,
            RevisionNo = note.RevisionCount,
            UserOpinion = opinion,
            BeforeAction = before,
            AfterAction = after
        });
        await db.SaveChangesAsync();
    }

    /// <summary>任务成功：生成四维总结</summary>
    public async Task MarkSuccessAsync(string taskId, string executionLog, string finalResult,
        string modelId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var note = await db.LearningNotes.Include(n => n.Revisions)
            .FirstOrDefaultAsync(n => n.TaskId == taskId);
        if (note is null) return;

        note.ExecutionLog = executionLog;
        note.FinalResult = finalResult;
        note.Status = "success";
        note.ModelUsed = modelId;

        var summary = await GenerateSummaryAsync(note, modelId);
        if (summary is not null)
        {
            note.SummaryProblem = summary.Problem;
            note.SummarySolution = summary.Solution;
            note.SummaryKeySteps = JsonSerializer.Serialize(summary.KeySteps);
            note.SummaryAvoidError = summary.AvoidError;
        }
        await db.SaveChangesAsync();
        _logger.LogInformation("任务 {TaskId} 已成功归档，四维总结已生成", taskId);
    }

    /// <summary>任务失败</summary>
    public async Task MarkFailedAsync(string taskId, string? errorMessage = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var note = await db.LearningNotes.FirstOrDefaultAsync(n => n.TaskId == taskId);
        if (note is null) return;
        note.Status = "failed";
        if (errorMessage is not null) note.ExecutionLog = errorMessage;
        await db.SaveChangesAsync();
    }

    // ── 私有方法 ────────────────────────────────────────────────────────
    private Task<float[]?> GetEmbeddingAsync(string text)
    {
        if (_config.AppSettings.Rag.UseLocalEmbedding) return Task.FromResult((float[]?)null);

        // 尝试用 deepseek-v3 调 embedding（如支持）
        // 简化：直接返回 null，退回关键词检索
        return Task.FromResult((float[]?)null);
    }

    private static string BuildKeywordContext(string prompt, List<LearningNote> notes, int topK)
    {
        var words = prompt.Split(' ', '，', '。', '？', '！')
            .Where(w => w.Length >= 2).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ranked = notes
            .Select(n => (Note: n, Score: words.Count(w =>
                (n.UserPrompt + " " + n.SummaryProblem).Contains(w, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Note)
            .ToList();

        return BuildContextText(ranked);
    }

    private static string BuildContextText(List<LearningNote> notes)
    {
        if (notes.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine("【历史经验参考（共检索到以下相关任务经验）】");
        foreach (var n in notes)
        {
            sb.AppendLine($"- 原任务：{n.UserPrompt.Truncate(60)}");
            if (n.SummarySolution is not null)
                sb.AppendLine($"  解决方案：{n.SummarySolution.Truncate(100)}");
            if (n.SummaryAvoidError is not null)
                sb.AppendLine($"  避错要点：{n.SummaryAvoidError.Truncate(100)}");
        }
        return sb.ToString();
    }

    private async Task<SummaryResult?> GenerateSummaryAsync(LearningNote note, string modelId)
    {
        var model = _registry.Get(modelId);
        if (model is null) return null;

        var revisions = note.Revisions.Any()
            ? string.Join("; ", note.Revisions.Select(r => r.UserOpinion))
            : "无";

        var prompt = $$"""
你是ClawBY19的自我复盘引擎。请根据以下任务的执行记录生成结构化复盘总结。

任务指令：{{note.UserPrompt}}
用户修改意见：{{revisions}}
最终结果：{{note.FinalResult?.Truncate(500)}}

请严格按JSON格式输出：
{
  "problem": "难点说明",
  "solution": "解决方法",
  "key_steps": ["步骤1", "步骤2", "步骤3"],
  "avoid_error": "下次避错建议"
}
""";

        try
        {
            var apiKey = _config.GetApiKey(model.ApiProvider);
            var (content, _, _) = await _aiClient.CompleteAsync(
                model.BaseUrl, model.ApiModelId, apiKey,
                [new AI.ApiMessage("user", prompt)],
                temperature: 0.3);

            // 从返回中提取 JSON
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0) return null;
            var json = content[jsonStart..(jsonEnd + 1)];
            return JsonSerializer.Deserialize<SummaryResult>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "四维总结生成失败");
            return null;
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0 || normB == 0) ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private class SummaryResult
    {
        public string Problem { get; set; } = string.Empty;
        public string Solution { get; set; } = string.Empty;
        public List<string> KeySteps { get; set; } = [];
        public string AvoidError { get; set; } = string.Empty;
    }
}

internal static class StringExtensions
{
    public static string Truncate(this string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";
}
