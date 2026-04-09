using ClawBY19.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawBY19.Data;

public class ClawDbContext : DbContext
{
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ApiUsageLog> ApiUsageLogs => Set<ApiUsageLog>();
    public DbSet<LearningNote> LearningNotes => Set<LearningNote>();
    public DbSet<UserRevision> UserRevisions => Set<UserRevision>();
    public DbSet<CoreExperience> CoreExperiences => Set<CoreExperience>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<OperationHistory> OperationHistories => Set<OperationHistory>();
    public DbSet<TestLog> TestLogs => Set<TestLog>();
    public DbSet<TaskRecord> TaskRecords => Set<TaskRecord>();
    public DbSet<FailedPath> FailedPaths => Set<FailedPath>();

    public ClawDbContext(DbContextOptions<ClawDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserRevision>()
            .HasOne(r => r.LearningNote)
            .WithMany(n => n.Revisions)
            .HasForeignKey(r => r.LearningNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CoreExperience>()
            .HasIndex(e => e.ExpCode)
            .IsUnique();

        modelBuilder.Entity<ApiUsageLog>()
            .HasIndex(l => l.Timestamp);

        modelBuilder.Entity<LearningNote>()
            .HasIndex(n => n.TaskId)
            .IsUnique();

        modelBuilder.Entity<OperationHistory>()
            .HasIndex(o => new { o.OperationType, o.TargetUrl });
        modelBuilder.Entity<OperationHistory>()
            .HasIndex(o => o.LastUsedAt);

        // SQLite WAL 模式：通过连接字符串参数设置
        // EF Core 会在 OnConfiguring 或 DbContextOptions 中处理

        base.OnModelCreating(modelBuilder);
    }

    // 获取今日费用合计
    public async Task<decimal> GetTodayCostAsync()
    {
        var today = DateTime.Today;
        return await ApiUsageLogs
            .Where(l => l.Timestamp >= today && l.Status == "success")
            .SumAsync(l => l.CostCny);
    }

    // 获取本月费用合计
    public async Task<decimal> GetMonthCostAsync()
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return await ApiUsageLogs
            .Where(l => l.Timestamp >= first && l.Status == "success")
            .SumAsync(l => l.CostCny);
    }

    // 获取累计费用
    public async Task<decimal> GetTotalCostAsync() =>
        await ApiUsageLogs.Where(l => l.Status == "success").SumAsync(l => l.CostCny);

    // 按小时聚合（用于柱状图）
    public async Task<List<HourlyCostItem>> GetHourlyCostAsync(DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        return await ApiUsageLogs
            .Where(l => l.Timestamp >= start && l.Timestamp < end && l.Status == "success")
            .GroupBy(l => new { l.Timestamp.Hour, l.Model })
            .Select(g => new HourlyCostItem
            {
                Hour = g.Key.Hour,
                Model = g.Key.Model,
                TotalCost = g.Sum(x => x.CostCny),
                TotalTokens = g.Sum(x => x.TokensTotal),
                RequestCount = g.Count()
            })
            .ToListAsync();
    }

    // 获取所有使用过的模型列表
    public async Task<List<string>> GetUsedModelsAsync()
    {
        return await ApiUsageLogs
            .Where(l => l.Status == "success")
            .Select(l => l.Model)
            .Distinct()
            .OrderBy(m => m)
            .ToListAsync();
    }

    // 按时间维度聚合Token流量（支持小时/天/月）
    public async Task<List<ChartDataItem>> GetTokenFlowAsync(string period, DateTime startDate, string? modelFilter = null)
    {
        var query = ApiUsageLogs.Where(l => l.Status == "success" && l.Timestamp >= startDate);

        if (!string.IsNullOrEmpty(modelFilter))
            query = query.Where(l => l.Model == modelFilter);

        return period switch
        {
            "hour" => await query
                .GroupBy(l => new { l.Timestamp.Hour, l.Model })
                .Select(g => new ChartDataItem
                {
                    TimeUnit = g.Key.Hour,
                    Model = g.Key.Model,
                    TotalTokens = g.Sum(x => x.TokensTotal),
                    TotalCost = g.Sum(x => x.CostCny)
                })
                .OrderBy(x => x.TimeUnit)
                .ToListAsync(),

            "day" => await query
                .GroupBy(l => new { Day = l.Timestamp.Day, l.Model })
                .Select(g => new ChartDataItem
                {
                    TimeUnit = g.Key.Day,
                    Model = g.Key.Model,
                    TotalTokens = g.Sum(x => x.TokensTotal),
                    TotalCost = g.Sum(x => x.CostCny)
                })
                .OrderBy(x => x.TimeUnit)
                .ToListAsync(),

            "month" => await query
                .GroupBy(l => new { Month = l.Timestamp.Month, l.Model })
                .Select(g => new ChartDataItem
                {
                    TimeUnit = g.Key.Month,
                    Model = g.Key.Model,
                    TotalTokens = g.Sum(x => x.TokensTotal),
                    TotalCost = g.Sum(x => x.CostCny)
                })
                .OrderBy(x => x.TimeUnit)
                .ToListAsync(),

            _ => new List<ChartDataItem>()
        };
    }
}

public record HourlyCostItem
{
    public int Hour { get; init; }
    public string Model { get; init; } = string.Empty;
    public decimal TotalCost { get; init; }
    public int TotalTokens { get; init; }
    public int RequestCount { get; init; }
}

public record ChartDataItem
{
    public int TimeUnit { get; init; }  // 小时/天/月
    public string Model { get; init; } = string.Empty;
    public int TotalTokens { get; init; }
    public decimal TotalCost { get; init; }
}
