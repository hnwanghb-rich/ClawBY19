using ClawBY19.Data;
using ClawBY19.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Cost;

/// <summary>记录 API 用量、聚合统计、触发预警检查</summary>
public class CostTracker
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly PricingEngine _pricing;
    private readonly ILogger<CostTracker> _logger;

    public event EventHandler<CostUpdatedEventArgs>? CostUpdated;

    public CostTracker(IDbContextFactory<ClawDbContext> dbFactory, PricingEngine pricing,
        ILogger<CostTracker> logger)
    {
        _dbFactory = dbFactory;
        _pricing = pricing;
        _logger = logger;
    }

    /// <summary>记录一次 API 调用</summary>
    public async Task<decimal> RecordAsync(ApiUsageLog log)
    {
        if (log.CostCny == 0m)
            log.CostCny = _pricing.Calculate(log.Model, log.TokensInput, log.TokensOutput);
        log.TokensTotal = log.TokensInput + log.TokensOutput;
        if (string.IsNullOrEmpty(log.Provider))
            log.Provider = _pricing.GetProvider(log.Model);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.ApiUsageLogs.Add(log);
        await db.SaveChangesAsync();

        _logger.LogInformation("API调用记录：{Model} 输入{In}/输出{Out} Token，费用¥{Cost:F4}",
            log.Model, log.TokensInput, log.TokensOutput, log.CostCny);

        var stats = await GetSummaryAsync();
        CostUpdated?.Invoke(this, new CostUpdatedEventArgs(stats));
        return log.CostCny;
    }

    /// <summary>获取费用汇总（今日/本周/本月/累计）</summary>
    public async Task<CostSummary> GetSummaryAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.Now;
        var today = now.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var todayLogs = await db.ApiUsageLogs
            .Where(l => l.Timestamp >= today)
            .ToListAsync();

        var avgLatency = todayLogs.Where(l => l.LatencyMs > 0).Select(l => (double)l.LatencyMs).DefaultIfEmpty(0).Average();
        var successRate = todayLogs.Count > 0
            ? (double)todayLogs.Count(l => l.Status == "success") / todayLogs.Count * 100
            : 100.0;

        return new CostSummary
        {
            Today = await db.ApiUsageLogs
                .Where(l => l.Timestamp >= today && l.Status == "success")
                .SumAsync(l => l.CostCny),
            Week = await db.ApiUsageLogs
                .Where(l => l.Timestamp >= weekStart && l.Status == "success")
                .SumAsync(l => l.CostCny),
            Month = await db.ApiUsageLogs
                .Where(l => l.Timestamp >= monthStart && l.Status == "success")
                .SumAsync(l => l.CostCny),
            Total = await db.ApiUsageLogs
                .Where(l => l.Status == "success")
                .SumAsync(l => l.CostCny),
            TodayRequests = todayLogs.Count,
            TodaySuccessRequests = todayLogs.Count(l => l.Status == "success"),
            TodayTokens = todayLogs.Where(l => l.Status == "success").Sum(l => (long)l.TokensTotal),
            MonthTokens = await db.ApiUsageLogs
                .Where(l => l.Timestamp >= monthStart && l.Status == "success")
                .SumAsync(l => (long)l.TokensTotal),
            AvgLatencyMs = avgLatency,
            SuccessRate = successRate
        };
    }

    /// <summary>获取每小时费用（用于柱状图）</summary>
    public async Task<List<HourlyCostItem>> GetHourlyCostAsync(DateTime date)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.GetHourlyCostAsync(date);
    }
}

public class CostSummary
{
    public decimal Today { get; init; }
    public decimal Week { get; init; }
    public decimal Month { get; init; }
    public decimal Total { get; init; }
    public int TodayRequests { get; init; }
    public int TodaySuccessRequests { get; init; }
    public long TodayTokens { get; init; }
    public long MonthTokens { get; init; }
    public double AvgLatencyMs { get; init; }
    public double SuccessRate { get; init; }
}

public class CostUpdatedEventArgs(CostSummary summary) : EventArgs
{
    public CostSummary Summary { get; } = summary;
}
