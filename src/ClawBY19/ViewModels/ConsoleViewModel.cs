using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClawBY19.Services.Alert;
using ClawBY19.Services.Cost;
using ClawBY19.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace ClawBY19.ViewModels;

/// <summary>一条柱状图数据点</summary>
public record ChartBar(int Hour, string Model, decimal Cost);

/// <summary>图表数据点（用于流量/费用分析）</summary>
public class ChartPoint
{
    public int TimeUnit { get; set; }  // 横轴：小时/天/月
    public string Label { get; set; } = string.Empty;  // 显示标签
    public double Value { get; set; }  // 纵轴值（Token数或费用）
    public string ValueText { get; set; } = string.Empty;  // 柱上显示的数值文本
    public string Model { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;  // 悬停提示
}

/// <summary>调用记录行（用于表格展示）</summary>
public record LogRow(
    string Time,
    string Model,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    decimal Cost,
    int LatencyMs,
    string Status)
{
    public string CostStr    => Cost > 0 ? $"¥{Cost:F4}" : "-";
    public string LatencyStr => LatencyMs > 0 ? $"{LatencyMs}ms" : "-";
    public string StatusIcon => Status == "success" ? "✅" : "❌";
}

public partial class ConsoleViewModel : ViewModelBase
{
    private readonly CostTracker _tracker;
    private readonly AlertService _alert;
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Services.AI.ConfigService? _configService;

    // ── 统计数值 ──
    [ObservableProperty] private decimal _todayCost;
    [ObservableProperty] private decimal _weekCost;
    [ObservableProperty] private decimal _monthCost;
    [ObservableProperty] private decimal _totalCost;
    [ObservableProperty] private int _todayRequests;
    [ObservableProperty] private int _todaySuccessRequests;
    [ObservableProperty] private long _todayTokens;
    [ObservableProperty] private long _monthTokens;
    [ObservableProperty] private double _avgLatencyMs;
    [ObservableProperty] private double _successRate;

    // ── 预警 ──
    [ObservableProperty] private AlertLevel _alertLevel = AlertLevel.None;
    [ObservableProperty] private string _alertMessage = string.Empty;
    [ObservableProperty] private bool _isAlertVisible;
    [ObservableProperty] private bool _isFlashOn;

    // ── 柱状图 ──
    public ObservableCollection<ChartBar> HourlyBars { get; } = new();

    // ── 最近调用记录 ──
    public ObservableCollection<LogRow> RecentLogs { get; } = new();

    // ── Tab切换 ──
    [ObservableProperty] private int _selectedTabIndex = 0;  // 0=流量分析, 1=费用分析

    // ── 时间维度 ──
    [ObservableProperty] private string _selectedPeriod = "today";  // today/week/month/year

    // ── 模型过滤 ──
    [ObservableProperty] private string? _selectedModelFilter;  // null=全部模型
    public ObservableCollection<string> AvailableModels { get; } = new();

    // ── 图表数据 ──
    public ObservableCollection<ChartPoint> ChartData { get; } = new();
    [ObservableProperty] private string _chartTitle = "当天Token使用量";
    [ObservableProperty] private string _yAxisLabel = "Token数";
    [ObservableProperty] private double _maxYValue = 100;

    // ── Y轴刻度 ──
    public ObservableCollection<double> YAxisTicks { get; } = new();

    private DispatcherTimer? _flashTimer;
    private string? _currentModelId;  // 当前使用的模型ID

    public ConsoleViewModel(CostTracker tracker, AlertService alert,
        IDbContextFactory<ClawDbContext> dbFactory,
        Services.AI.ConfigService? configService = null)
    {
        _tracker = tracker;
        _alert = alert;
        _dbFactory = dbFactory;
        _configService = configService;

        _tracker.CostUpdated += OnCostUpdated;
        _alert.AlertTriggered += OnAlertTriggered;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _ = RefreshAsync();
        _ = LoadCurrentModelAsync();
        _ = LoadAvailableModelsAsync();
    }

    partial void OnSelectedPeriodChanged(string value) => _ = RefreshChartDataAsync();
    partial void OnSelectedModelFilterChanged(string? value) => _ = RefreshChartDataAsync();
    partial void OnSelectedTabIndexChanged(int value) => _ = RefreshChartDataAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var s = await _tracker.GetSummaryAsync();
            TodayCost    = s.Today;
            WeekCost     = s.Week;
            MonthCost    = s.Month;
            TotalCost    = s.Total;
            TodayRequests        = s.TodayRequests;
            TodaySuccessRequests = s.TodaySuccessRequests;
            TodayTokens  = s.TodayTokens;
            MonthTokens  = s.MonthTokens;
            AvgLatencyMs = s.AvgLatencyMs;
            SuccessRate  = s.SuccessRate;

            await RefreshChartAsync(DateTime.Today);
            await RefreshRecentLogsAsync();
            await RefreshChartDataAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task ChangePeriodAsync(string period)
    {
        SelectedPeriod = period;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ChangeModelFilterAsync(string? model)
    {
        SelectedModelFilter = model;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LoadChartAsync(string period)
    {
        var date = period switch
        {
            "today" => DateTime.Today,
            "week"  => DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1),
            "month" => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            _       => DateTime.Today
        };
        await RefreshChartAsync(date);
    }

    [RelayCommand]
    private void DismissAlert()
    {
        IsAlertVisible = false;
        AlertLevel = AlertLevel.None;
        AlertMessage = string.Empty;
        _flashTimer?.Stop();
    }

    // ── 内部 ────────────────────────────────────────────────────────
    private async void OnCostUpdated(object? sender, CostUpdatedEventArgs e)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var s = e.Summary;
            TodayCost    = s.Today;
            WeekCost     = s.Week;
            MonthCost    = s.Month;
            TotalCost    = s.Total;
            TodayRequests        = s.TodayRequests;
            TodaySuccessRequests = s.TodaySuccessRequests;
            TodayTokens  = s.TodayTokens;
            AvgLatencyMs = s.AvgLatencyMs;
            SuccessRate  = s.SuccessRate;
            await RefreshRecentLogsAsync();
        });
    }

    private void OnAlertTriggered(object? sender, AlertTriggeredEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AlertLevel = e.Level;
            AlertMessage = e.Message;
            IsAlertVisible = true;
            StartFlash(e.Level);
        });
    }

    private void StartFlash(AlertLevel level)
    {
        _flashTimer?.Stop();
        var interval = level == AlertLevel.Critical
            ? TimeSpan.FromMilliseconds(300)
            : TimeSpan.FromSeconds(1);
        _flashTimer = new DispatcherTimer { Interval = interval };
        _flashTimer.Tick += (_, _) => IsFlashOn = !IsFlashOn;
        _flashTimer.Start();
    }

    private async Task RefreshChartAsync(DateTime date)
    {
        var bars = await _tracker.GetHourlyCostAsync(date);
        HourlyBars.Clear();
        foreach (var b in bars)
            HourlyBars.Add(new ChartBar(b.Hour, b.Model, b.TotalCost));
    }

    private async Task RefreshRecentLogsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var logs = await db.ApiUsageLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(50)
            .ToListAsync();

        RecentLogs.Clear();
        foreach (var l in logs)
        {
            RecentLogs.Add(new LogRow(
                l.Timestamp.ToString("HH:mm:ss"),
                l.Model.Length > 28 ? l.Model[..25] + "…" : l.Model,
                l.TokensInput,
                l.TokensOutput,
                l.TokensTotal,
                l.CostCny,
                l.LatencyMs,
                l.Status));
        }
    }

    private async Task LoadAvailableModelsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var models = await db.GetUsedModelsAsync();

        AvailableModels.Clear();
        AvailableModels.Add("全部模型");
        foreach (var m in models)
            AvailableModels.Add(m);

        // 设置默认选中当前模型
        if (!string.IsNullOrEmpty(_currentModelId) && models.Contains(_currentModelId))
            SelectedModelFilter = _currentModelId;
        else
            SelectedModelFilter = "全部模型";
    }

    private async Task LoadCurrentModelAsync()
    {
        try
        {
            // 优先从ConfigService获取
            if (_configService != null)
            {
                var modelCfg = _configService.ModelCfg;
                if (modelCfg.ModelSelection.Mode == "user_specified")
                {
                    _currentModelId = modelCfg.ModelSelection.SpecifiedModel;
                }
                else
                {
                    _currentModelId = modelCfg.ClawAuto.DefaultModel;
                }
            }
            else
            {
                // 降级方案：从配置文件读取
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "models.toml");
                if (File.Exists(configPath))
                {
                    var toml = await File.ReadAllTextAsync(configPath);
                    // 简单解析，查找 specified_model 或 default_model
                    if (toml.Contains("specified_model"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(toml, @"specified_model\s*=\s*""([^""]+)""");
                        if (match.Success)
                            _currentModelId = match.Groups[1].Value;
                    }
                    else if (toml.Contains("default_model"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(toml, @"default_model\s*=\s*""([^""]+)""");
                        if (match.Success)
                            _currentModelId = match.Groups[1].Value;
                    }
                }
            }
        }
        catch { /* 忽略错误，使用默认值 */ }
    }

    private async Task RefreshChartDataAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // 确定时间范围和聚合粒度
        var (startDate, periodType, xAxisLabel) = SelectedPeriod switch
        {
            "today" => (DateTime.Today, "hour", "小时"),
            "week" => (DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1), "day", "星期"),
            "month" => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), "day", "日期"),
            "year" => (new DateTime(DateTime.Today.Year, 1, 1), "month", "月份"),
            _ => (DateTime.Today, "hour", "小时")
        };

        // 获取数据
        var modelFilter = SelectedModelFilter == "全部模型" || string.IsNullOrEmpty(SelectedModelFilter)
            ? null
            : SelectedModelFilter;

        var data = await db.GetTokenFlowAsync(periodType, startDate, modelFilter);

        // 根据Tab决定显示Token还是费用
        var isTokenFlow = SelectedTabIndex == 0;

        ChartData.Clear();
        foreach (var item in data)
        {
            var value = isTokenFlow ? item.TotalTokens : (double)item.TotalCost;
            var label = GetTimeLabel(item.TimeUnit, periodType);
            var tooltip = isTokenFlow
                ? $"{label}: {item.TotalTokens:N0} Tokens ({item.Model})"
                : $"{label}: ¥{item.TotalCost:F4} ({item.Model})";

            // 生成柱上显示的数值文本
            var valueText = isTokenFlow
                ? $"{item.TotalTokens}个"  // Token显示为"个"
                : item.TotalCost >= 1 ? $"¥{item.TotalCost:F2}" : $"¥{item.TotalCost:F4}";

            ChartData.Add(new ChartPoint
            {
                TimeUnit = item.TimeUnit,
                Label = label,
                Value = value,
                ValueText = valueText,
                Model = item.Model,
                Tooltip = tooltip
            });
        }

        // 更新图表标题和Y轴标签
        UpdateChartLabels(isTokenFlow, xAxisLabel);

        // 计算Y轴最大值和刻度
        MaxYValue = ChartData.Any() ? ChartData.Max(c => c.Value) * 1.2 : 100;
        GenerateYAxisTicks();
    }

    private void GenerateYAxisTicks()
    {
        YAxisTicks.Clear();
        if (MaxYValue <= 0) return;

        // 生成5条刻度线
        var step = MaxYValue / 5;
        for (int i = 0; i <= 5; i++)
        {
            YAxisTicks.Add(step * i);
        }
    }

    private void UpdateChartLabels(bool isTokenFlow, string xAxisLabel)
    {
        var periodName = SelectedPeriod switch
        {
            "today" => "当天",
            "week" => "本周",
            "month" => "本月",
            "year" => "今年",
            _ => "当天"
        };

        if (isTokenFlow)
        {
            ChartTitle = $"{periodName}Token使用量";
            YAxisLabel = "Token数";
        }
        else
        {
            ChartTitle = $"{periodName}费用分析";

            // 动态Y轴单位
            var maxCost = ChartData.Any() ? ChartData.Max(c => c.Value) : 0.0;
            if (maxCost <= 1.0)
                YAxisLabel = "费用（分）";
            else if (maxCost <= 10.0)
                YAxisLabel = "费用（元）";
            else
                YAxisLabel = "费用（千元）";
        }
    }

    private string GetTimeLabel(int timeUnit, string periodType)
    {
        return periodType switch
        {
            "hour" => $"{timeUnit}时",
            "day" => SelectedPeriod == "week"
                ? GetWeekDayName(timeUnit)
                : $"{timeUnit}日",
            "month" => $"{timeUnit}月",
            _ => timeUnit.ToString()
        };
    }

    private string GetWeekDayName(int day)
    {
        var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1);
        var date = weekStart.AddDays(day - weekStart.Day);
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            DayOfWeek.Sunday => "周日",
            _ => $"{day}日"
        };
    }
}
