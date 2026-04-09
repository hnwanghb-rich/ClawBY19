using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using ClawBY19.Services.TaskLog;
using Microsoft.EntityFrameworkCore;

namespace ClawBY19.ViewModels;

// ── 显示模型 ─────────────────────────────────────────────────────────────────

public class StepLogDisplay
{
    public string Step    { get; set; } = string.Empty;
    public string Output  { get; set; } = string.Empty;
    public bool   Success { get; set; }
    public string Icon    => Success ? "✅" : "❌";
    public string Text    => string.IsNullOrEmpty(Output) ? Step : $"{Step}: {Output}";
}

public class TaskRecordItem
{
    public int    DbId        { get; set; }
    public string TaskId      { get; set; } = string.Empty;
    public string TaskType    { get; set; } = string.Empty;
    public string TypeIcon    { get; set; } = string.Empty;
    public string TypeLabel   { get; set; } = string.Empty;
    public string Summary     { get; set; } = string.Empty;
    public string StartedAt   { get; set; } = string.Empty;
    public string Duration    { get; set; } = string.Empty;
    public string Status      { get; set; } = string.Empty;
    public string StatusIcon  { get; set; } = string.Empty;
    public bool   IsSuccess   { get; set; }
    public bool   IsFailed    { get; set; }
    public bool   IsRunning   { get; set; }

    // Git 专用
    public string? LocalPath     { get; set; }
    public string? RemoteUrl     { get; set; }
    public string? Branch        { get; set; }
    public string? CommitMessage { get; set; }

    // 部署专用
    public string? ServerIp  { get; set; }
    public string? DeployPath { get; set; }
    public string? OnlineUrl  { get; set; }

    // 详情
    public string?              AiAnalysis { get; set; }
    public List<StepLogDisplay> Steps      { get; set; } = new();
}

// ── 正在执行的任务行（保留） ──────────────────────────────────────────────────

public class RunningTaskItem
{
    public string TaskContent  { get; set; } = string.Empty;
    public string ExecutionRule { get; set; } = string.Empty;
    public int    ExecutedCount { get; set; }
}

// ── 学习日志行（保留） ────────────────────────────────────────────────────────

public class TaskLogItem
{
    public string TaskContent    { get; set; } = string.Empty;
    public string ExecutionRule  { get; set; } = string.Empty;
    public string ExecutionTime  { get; set; } = string.Empty;
    public string ExecutionResult { get; set; } = string.Empty;
    public bool   IsSuccess      { get; set; }
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public partial class TasksViewModel : ViewModelBase
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly TaskLogService _taskLog;

    // 操作记录列表
    public ObservableCollection<TaskRecordItem> OperationTasks { get; } = new();

    // 已有列表（保留）
    public ObservableCollection<RunningTaskItem> RunningTasks { get; } = new();
    public ObservableCollection<TaskLogItem>     TaskLogs     { get; } = new();

    [ObservableProperty] private int    _selectedTabIndex;
    [ObservableProperty] private string _typeFilter   = "全部";
    [ObservableProperty] private string _statusFilter = "全部";

    // 详情弹窗
    [ObservableProperty] private bool            _isDetailOpen;
    [ObservableProperty] private TaskRecordItem? _selectedTask;
    [ObservableProperty] private string          _detailAiAnalysis = string.Empty;
    [ObservableProperty] private bool            _isAnalyzing;

    partial void OnTypeFilterChanged(string value)   => _ = LoadOperationsAsync();
    partial void OnStatusFilterChanged(string value) => _ = LoadOperationsAsync();

    public TasksViewModel(IDbContextFactory<ClawDbContext> dbFactory, TaskLogService taskLog)
    {
        _dbFactory = dbFactory;
        _taskLog   = taskLog;
        _ = LoadAsync();
    }

    // ── 命令 ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private void SetTypeFilter(string filter) => TypeFilter = filter;

    [RelayCommand]
    private void SetStatusFilter(string filter) => StatusFilter = filter;

    [RelayCommand]
    private void OpenDetail(TaskRecordItem? item)
    {
        if (item is null) return;
        SelectedTask      = item;
        DetailAiAnalysis  = item.AiAnalysis ?? string.Empty;
        IsDetailOpen      = true;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailOpen = false;
        SelectedTask = null;
    }

    [RelayCommand]
    private async Task AnalyzeFailure()
    {
        if (SelectedTask is null || !SelectedTask.IsFailed) return;
        IsAnalyzing      = true;
        DetailAiAnalysis = "🤖 AI 正在分析失败原因，请稍候...";
        try
        {
            var analysis = await _taskLog.AnalyzeFailureAsync(SelectedTask.DbId);
            DetailAiAnalysis      = analysis;
            SelectedTask.AiAnalysis = analysis;
        }
        finally { IsAnalyzing = false; }
    }

    [RelayCommand]
    private async Task MarkFailedPath()
    {
        if (SelectedTask is null || !SelectedTask.IsFailed) return;
        var solution = string.IsNullOrEmpty(SelectedTask.AiAnalysis)
            ? "（AI 建议方案）"
            : SelectedTask.AiAnalysis[..Math.Min(200, SelectedTask.AiAnalysis.Length)];
        await _taskLog.RecordFailedPathAsync(SelectedTask.DbId, solution);
        DetailAiAnalysis += "\n\n✅ 已将本方案标记为失败，下次 AI 分析将自动排除。";
    }

    // ── 数据加载 ──────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        await LoadOperationsAsync();
        await LoadLegacyAsync();
    }

    private async Task LoadOperationsAsync()
    {
        var records = await _taskLog.GetTasksAsync(
            typeFilter:   TypeFilter   == "全部" ? null : TypeFilter,
            statusFilter: StatusFilter == "全部" ? null : StatusFilter);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OperationTasks.Clear();
            foreach (var r in records)
                OperationTasks.Add(MapToItem(r));
        });
    }

    private async Task LoadLegacyAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var running = await db.LearningNotes
                .Where(n => n.Status == "running")
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            RunningTasks.Clear();
            foreach (var n in running)
                RunningTasks.Add(new RunningTaskItem
                {
                    TaskContent   = n.UserPrompt,
                    ExecutionRule = n.TaskType ?? "通用",
                    ExecutedCount = n.RevisionCount + 1
                });

            var logs = await db.LearningNotes
                .Where(n => n.Status == "success" || n.Status == "failed")
                .OrderByDescending(n => n.CreatedAt)
                .Take(200)
                .ToListAsync();

            TaskLogs.Clear();
            foreach (var n in logs)
                TaskLogs.Add(new TaskLogItem
                {
                    TaskContent    = n.UserPrompt,
                    ExecutionRule  = n.TaskType ?? "通用",
                    ExecutionTime  = n.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    ExecutionResult = n.Status == "success"
                        ? (n.SummarySolution ?? n.FinalResult ?? "执行成功")
                        : (n.ExecutionLog ?? "执行失败"),
                    IsSuccess = n.Status == "success"
                });
        });
    }

    // ── 映射 ──────────────────────────────────────────────────────────────

    private static TaskRecordItem MapToItem(TaskRecord r)
    {
        var duration = r.FinishedAt.HasValue
            ? FormatDuration(r.FinishedAt.Value - r.StartedAt)
            : "进行中";

        var steps = new List<StepLogDisplay>();
        if (!string.IsNullOrEmpty(r.StepLogsJson))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<StepLogEntry>>(r.StepLogsJson);
                if (entries is not null)
                    steps = entries.Select(e => new StepLogDisplay
                    {
                        Step    = e.Step,
                        Output  = e.Output,
                        Success = e.Success
                    }).ToList();
            }
            catch { /* ignore malformed JSON */ }
        }

        return new TaskRecordItem
        {
            DbId         = r.Id,
            TaskId       = r.TaskId,
            TaskType     = r.TaskType,
            TypeIcon     = r.TaskType == "GitUpload" ? "📤" : "☁️",
            TypeLabel    = r.TaskType == "GitUpload" ? "Git 上传" : "云端部署",
            Summary      = r.Summary,
            StartedAt    = r.StartedAt.ToString("MM-dd HH:mm"),
            Duration     = duration,
            Status       = r.Status,
            StatusIcon   = r.Status switch { "success" => "✅", "failed" => "❌", _ => "⏳" },
            IsSuccess    = r.Status == "success",
            IsFailed     = r.Status == "failed",
            IsRunning    = r.Status == "running",
            LocalPath    = r.LocalPath,
            RemoteUrl    = r.RemoteUrl,
            Branch       = r.Branch,
            CommitMessage = r.CommitMessage,
            ServerIp     = r.ServerIp,
            DeployPath   = r.DeployPath,
            OnlineUrl    = r.OnlineUrl,
            AiAnalysis   = r.AiAnalysis,
            Steps        = steps
        };
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{ts.TotalSeconds:F0}s";
        if (ts.TotalMinutes < 60) return $"{ts.TotalMinutes:F0}m{ts.Seconds}s";
        return $"{ts.TotalHours:F0}h{ts.Minutes}m";
    }
}
