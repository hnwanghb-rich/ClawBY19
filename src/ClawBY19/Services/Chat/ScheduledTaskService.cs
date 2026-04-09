using Cronos;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using ClawBY19.Services.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Chat;

public class TaskResultEventArgs(int taskId, string description, string result) : EventArgs
{
    public int TaskId { get; } = taskId;
    public string Description { get; } = description;
    public string Result { get; } = result;
}

/// <summary>
/// 定时任务调度服务：
/// 每分钟检查到期任务 → 调用 AI → 触发 TaskCompleted 事件（ChatViewModel 监听后显示结果）
/// </summary>
public class ScheduledTaskService : IDisposable
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly IAiClient _aiClient;
    private readonly ModelSelectionService _modelSelector;
    private readonly ModelRegistry _registry;
    private readonly ConfigService _config;
    private readonly ILogger<ScheduledTaskService> _logger;
    private readonly Timer _timer;

    public event EventHandler<TaskResultEventArgs>? TaskCompleted;

    public ScheduledTaskService(
        IDbContextFactory<ClawDbContext> dbFactory,
        IAiClient aiClient,
        ModelSelectionService modelSelector,
        ModelRegistry registry,
        ConfigService config,
        ILogger<ScheduledTaskService> logger)
    {
        _dbFactory = dbFactory;
        _aiClient = aiClient;
        _modelSelector = modelSelector;
        _registry = registry;
        _config = config;
        _logger = logger;

        // 每60秒检查一次
        _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
    }

    /// <summary>保存新定时任务到 DB，计算首次执行时间</summary>
    public async Task AddTaskAsync(ExtractedTask extracted)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        DateTime? nextRun = null;
        try
        {
            var expr = CronExpression.Parse(extracted.CronExpr);
            nextRun = expr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local)?.LocalDateTime;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cron 表达式解析失败：{Cron}", extracted.CronExpr);
        }

        db.ScheduledTasks.Add(new ScheduledTask
        {
            Description = extracted.Description,
            CronExpr    = extracted.CronExpr,
            Prompt      = extracted.Prompt,
            NextRun     = nextRun
        });
        await db.SaveChangesAsync();
        _logger.LogInformation("定时任务已保存：{Desc}，下次执行：{Next}", extracted.Description, nextRun);
    }

    public async Task<List<ScheduledTask>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ScheduledTasks.OrderByDescending(t => t.CreatedAt).ToListAsync();
    }

    // ── 定时执行 ────────────────────────────────────────────────────

    private async void OnTick(object? _)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var now = DateTime.Now;
            var due = await db.ScheduledTasks
                .Where(t => t.IsActive && t.NextRun != null && t.NextRun <= now)
                .ToListAsync();

            foreach (var task in due)
                await ExecuteTaskAsync(db, task);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScheduledTaskService OnTick 出错");
        }
    }

    private async Task ExecuteTaskAsync(ClawDbContext db, ScheduledTask task)
    {
        _logger.LogInformation("执行定时任务：{Desc}", task.Description);

        var (modelId, _, _) = _modelSelector.SelectModel(task.Prompt, 0m);
        var model = _registry.Get(modelId);
        if (model is null) return;

        var apiKey = _config.GetApiKey(model.ApiProvider);
        var messages = new List<ApiMessage>
        {
            new("system", "你是 ClawBY19，请执行用户定时安排的任务，给出结果。"),
            new("user", task.Prompt)
        };

        var (result, _, _) = await _aiClient.CompleteAsync(
            model.BaseUrl, model.ApiModelId, apiKey, messages);

        // 更新执行记录
        task.LastRun = DateTime.Now;
        task.LastResult = result.Length > 500 ? result[..500] + "…" : result;

        // 计算下次执行时间
        try
        {
            var expr = CronExpression.Parse(task.CronExpr);
            task.NextRun = expr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local)?.LocalDateTime;
        }
        catch { task.NextRun = null; }

        await db.SaveChangesAsync();

        // 通知 UI
        TaskCompleted?.Invoke(this, new TaskResultEventArgs(task.Id, task.Description, result));
    }

    public void Dispose() => _timer.Dispose();
}
