using System.IO;
using System.Text;
using System.Text.Json;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using ClawBY19.Services.AI;
using ClawBY19.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Learning;

/// <summary>升级学习：遍历RAG库生成"学习升级总结.md"</summary>
public class LearningService
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly IAiClient _aiClient;
    private readonly ConfigService _config;
    private readonly ModelRegistry _registry;
    private readonly ILogger<LearningService> _logger;

    public event EventHandler<LearningProgressEventArgs>? ProgressChanged;

    public LearningService(IDbContextFactory<ClawDbContext> dbFactory, IAiClient aiClient,
        ConfigService config, ModelRegistry registry, ILogger<LearningService> logger)
    {
        _dbFactory = dbFactory;
        _aiClient = aiClient;
        _config = config;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>执行升级学习，输出总结 MD 文件，返回文件路径</summary>
    public async Task<string> RunUpgradeAsync(CancellationToken ct = default)
    {
        Report("开始遍历 RAG 学习笔记库...");
        idx_counter = 1;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var notes = await db.LearningNotes
            .Include(n => n.Revisions)
            .Where(n => n.Status == "success")
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

        var failed = await db.LearningNotes.CountAsync(n => n.Status == "failed", ct);
        var revised = await db.LearningNotes.CountAsync(n => n.RevisionCount > 0, ct);

        Report($"成功任务 {notes.Count} 条，失败 {failed} 条，用户修正 {revised} 条");

        // 按任务类型分组
        var groups = notes
            .GroupBy(n => n.TaskType ?? "未分类")
            .ToDictionary(g => g.Key, g => g.ToList());

        // 选择分析用模型
        var modelId = _config.ModelCfg.ClawAuto.DefaultModel;
        var model = _registry.Get(modelId)!;
        var apiKey = _config.GetApiKey(model.ApiProvider);

        var sections = new StringBuilder();
        int idx = 0;
        foreach (var (type, typeNotes) in groups)
        {
            Report($"分析任务类型：{type}（共 {typeNotes.Count} 条）...");
            var section = await AnalyzeTypeAsync(type, typeNotes, model.BaseUrl, model.ApiModelId, apiKey, ct);
            sections.AppendLine(section);
            idx++;
            ProgressChanged?.Invoke(this, new LearningProgressEventArgs(idx, groups.Count, type));
        }

        // 生成核心经验
        Report("更新核心经验库...");
        await UpdateCoreExperiencesAsync(db, notes, model.BaseUrl, model.ApiModelId, apiKey, ct);

        // 生成 MD 报告
        var periodStart = notes.Min(n => n.CreatedAt);
        var total = notes.Count + failed;
        var successRate = total > 0 ? (double)notes.Count / total * 100 : 0;

        var md = $"""
# ClawBY19 学习升级总结

> 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}
> 统计周期：{periodStart:yyyy-MM-dd} ~ {DateTime.Now:yyyy-MM-dd}
> 总任务数：{total}次 | 成功：{notes.Count}次 | 失败：{failed}次 | 用户修正：{revised}次
> 成功率：{successRate:F1}%

---

## ���、各类任务的经验总结

{sections}

---

## 二、核心经验库更新

见 SQLite 数据库 core_experience 表（已自动更新）。

---

*由 ClawBY19 自学习引擎自动生成*
""";

        var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "学习升级总结.md");
        await File.WriteAllTextAsync(outputPath, md, ct);
        Report($"升级总结已输出至：{outputPath}");
        return outputPath;
    }

    private async Task<string> AnalyzeTypeAsync(string type, List<LearningNote> notes,
        string baseUrl, string apiModelId, string apiKey, CancellationToken ct)
    {
        var sampleProblems = string.Join("\n", notes
            .Where(n => n.SummaryProblem != null)
            .Take(5)
            .Select(n => $"- {n.SummaryProblem}"));

        var topRevisions = notes
            .SelectMany(n => n.Revisions)
            .GroupBy(r => r.UserOpinion)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"- {g.Key}（{g.Count()}次）");

        var prompt = $"""
以下是 "{type}" 类型任务的执行数据摘要（共{notes.Count}条成功记录）：

高频问题：
{sampleProblems}

用户偏好（最常见修改意见）：
{string.Join("\n", topRevisions)}

请生成该类型任务的经验总结，包含：共性问题、最佳实践、用户偏好、高频错误与规避（Markdown格式）。
""";

        var sb = new StringBuilder();
        sb.AppendLine($"### {idx_counter++}. {type}类（共{notes.Count}次）\n");
        try
        {
            await foreach (var token in _aiClient.StreamAsync(baseUrl, apiModelId, apiKey,
                [new ApiMessage("user", prompt)], ct: ct))
            {
                sb.Append(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "分析任务类型 {Type} 失败", type);
            sb.AppendLine($"（分析失败：{ex.Message}）");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private int idx_counter = 1;

    private async Task UpdateCoreExperiencesAsync(ClawDbContext db, List<LearningNote> notes,
        string baseUrl, string apiModelId, string apiKey, CancellationToken ct)
    {
        // 收集高频规避要点
        var avoidErrors = notes
            .Where(n => n.SummaryAvoidError != null)
            .GroupBy(n => n.SummaryAvoidError!)
            .Where(g => g.Count() >= 2)  // 出现≥2次才升为核心经验
            .Select(g => (Rule: g.Key, Count: g.Count()));

        foreach (var (rule, count) in avoidErrors)
        {
            var existing = await db.CoreExperiences
                .FirstOrDefaultAsync(e => e.Rule == rule, ct);
            if (existing is not null)
            {
                existing.SourceCount = Math.Max(existing.SourceCount, count);
                existing.Confidence = Math.Min(0.99, existing.Confidence + 0.05);
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                var code = $"EXP-{(await db.CoreExperiences.CountAsync(ct) + 1):D3}";
                db.CoreExperiences.Add(new CoreExperience
                {
                    ExpCode = code,
                    Rule = rule.Truncate(200),
                    SourceCount = count,
                    Confidence = Math.Min(0.5 + count * 0.1, 0.95)
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private void Report(string msg) =>
        _logger.LogInformation("[升级学习] {Msg}", msg);
}

public class LearningProgressEventArgs(int current, int total, string currentType) : EventArgs
{
    public int Current { get; } = current;
    public int Total { get; } = total;
    public string CurrentType { get; } = currentType;
    public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;
}
