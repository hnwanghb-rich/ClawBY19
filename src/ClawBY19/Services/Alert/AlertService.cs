using ClawBY19.Config;
using ClawBY19.Services.AI;
using ClawBY19.Services.Cost;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Alert;

public enum AlertLevel { None, Warning, Critical }
public enum AlertPeriodType { Daily, Weekly, Monthly, Cumulative }

public class AlertTriggeredEventArgs : EventArgs
{
    public AlertLevel Level { get; init; }
    public AlertPeriodType Period { get; init; }
    public decimal CurrentCost { get; init; }
    public decimal Threshold { get; init; }
    public decimal Percentage { get; init; }
    public string Message => $"{Period}费用预警（{Level}）：¥{CurrentCost:F2}/¥{Threshold:F2}（{Percentage:F1}%）";
}

public class AlertService
{
    private readonly ConfigService _config;
    private readonly CostTracker _tracker;
    private readonly ILogger<AlertService> _logger;

    public event EventHandler<AlertTriggeredEventArgs>? AlertTriggered;
    public bool IsPaused { get; private set; }

    public AlertService(ConfigService config, CostTracker tracker, ILogger<AlertService> logger)
    {
        _config = config;
        _tracker = tracker;
        _logger = logger;
        _tracker.CostUpdated += OnCostUpdated;
    }

    public void Resume() => IsPaused = false;

    // ── 内部 ──────────────────────────────────────────────────────────
    private async void OnCostUpdated(object? sender, CostUpdatedEventArgs e) =>
        await CheckAllAsync(e.Summary);

    private Task CheckAllAsync(CostSummary s)
    {
        var rules = _config.AlertRules;
        Check(s.Today, rules.Daily, AlertPeriodType.Daily);
        Check(s.Week, rules.Weekly, AlertPeriodType.Weekly);
        Check(s.Month, rules.Monthly, AlertPeriodType.Monthly);
        Check(s.Total, rules.Cumulative, AlertPeriodType.Cumulative);
        return Task.CompletedTask;
    }

    private void Check(decimal cost, AlertPeriodConfig cfg, AlertPeriodType period)
    {
        if (!cfg.Enabled || cfg.ThresholdCny <= 0) return;
        var pct = cost / cfg.ThresholdCny * 100m;

        AlertLevel level;
        if (pct >= cfg.CriticalAtPercent)      level = AlertLevel.Critical;
        else if (pct >= cfg.WarningAtPercent)  level = AlertLevel.Warning;
        else return;

        var args = new AlertTriggeredEventArgs
        {
            Level = level,
            Period = period,
            CurrentCost = cost,
            Threshold = cfg.ThresholdCny,
            Percentage = pct
        };

        _logger.LogWarning("费用预警：{Msg}", args.Message);
        AlertTriggered?.Invoke(this, args);

        if (level == AlertLevel.Critical && _config.AlertRules.Action.AutoPauseOnCritical)
        {
            IsPaused = true;
            _logger.LogCritical("已自动暂停所有AI调用（{Period}费用达到{Pct:F0}%）", period, pct);
        }
    }
}
