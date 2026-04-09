using System.Text;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using ClawBY19.Services.AI;
using ClawBY19.Services.Cost;
using ClawBY19.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Agent;

public enum AgentStatus { Idle, Planning, Executing, WaitingConfirmation, WaitingFeedback, Done, Failed }

public class AgentTask
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserPrompt { get; set; } = string.Empty;
    public string? ExecutionPlan { get; set; }
    public StringBuilder Log { get; } = new();
    public string? FinalResult { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public string ModelId { get; set; } = string.Empty;
    public string SelectionReason { get; set; } = string.Empty;
    public string RagNoteId { get; set; } = string.Empty;
    public int IterationCount { get; set; }
    public int TokensInput { get; set; }
    public int TokensOutput { get; set; }
    public decimal CostCny { get; set; }
}

/// <summary>核心 Agent 循环（ReAct 模式）</summary>
public class AgentService
{
    private readonly IAiClient _aiClient;
    private readonly ModelSelectionService _modelSelector;
    private readonly ModelRegistry _registry;
    private readonly ConfigService _config;
    private readonly CostTracker _costTracker;
    private readonly RagService _rag;
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly ILogger<AgentService> _logger;

    public event EventHandler<AgentStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AgentTokenEventArgs>? TokenReceived;

    public AgentService(
        IAiClient aiClient, ModelSelectionService modelSelector, ModelRegistry registry,
        ConfigService config, CostTracker costTracker, RagService rag,
        IDbContextFactory<ClawDbContext> dbFactory, ILogger<AgentService> logger)
    {
        _aiClient = aiClient;
        _modelSelector = modelSelector;
        _registry = registry;
        _config = config;
        _costTracker = costTracker;
        _rag = rag;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// 启动一次 Agent 任务。
    /// confirmCallback：返回 true=执行，false=取消；feedbackCallback：用户反馈文本。
    /// </summary>
    public async Task<AgentTask> RunAsync(
        string userPrompt,
        Func<AgentTask, Task<bool>>? confirmCallback = null,
        Func<AgentTask, Task<string?>>? feedbackCallback = null,
        CancellationToken ct = default)
    {
        var task = new AgentTask { UserPrompt = userPrompt };
        SetStatus(task, AgentStatus.Planning);

        // 1. 检索 RAG 经验
        var ragContext = await _rag.BuildContextAsync(userPrompt);
        task.RagNoteId = await _rag.CreateNoteAsync(userPrompt);

        // 2. 选择模型
        var stats = await _costTracker.GetSummaryAsync();
        var (modelId, reason, mode) = _modelSelector.SelectModel(
            userPrompt, stats.Today, _config.AlertRules.Daily.ThresholdCny);
        task.ModelId = modelId;
        task.SelectionReason = reason;

        var model = _registry.Get(modelId);
        if (model is null)
        {
            task.Status = AgentStatus.Failed;
            task.FinalResult = $"模型 {modelId} 未在注册表中找到，请检查配置。";
            return task;
        }

        var apiKey = _config.GetApiKey(model.ApiProvider);

        // 3. 生成执行计划
        var planMessages = BuildPlanMessages(userPrompt, ragContext);
        var planBuilder = new StringBuilder();
        await foreach (var token in _aiClient.StreamAsync(model.BaseUrl, model.ApiModelId, apiKey, planMessages, ct: ct))
        {
            planBuilder.Append(token);
            TokenReceived?.Invoke(this, new AgentTokenEventArgs(token, "plan"));
        }
        task.ExecutionPlan = planBuilder.ToString();
        task.Log.AppendLine($"[计划] {task.ExecutionPlan}");

        // 4. 等待用户确认
        if (_config.AppSettings.Agent.ConfirmBeforeExecution && confirmCallback is not null)
        {
            SetStatus(task, AgentStatus.WaitingConfirmation);
            var confirmed = await confirmCallback(task);
            if (!confirmed)
            {
                task.Status = AgentStatus.Done;
                task.FinalResult = "用户取消了任务。";
                await _rag.MarkFailedAsync(task.RagNoteId, "用户取消");
                return task;
            }
        }

        // 5. 执行（ReAct 迭代）
        SetStatus(task, AgentStatus.Executing);
        var maxIter = _config.AppSettings.Agent.MaxIterations;
        var messages = BuildExecutionMessages(userPrompt, task.ExecutionPlan!, ragContext);
        var resultBuilder = new StringBuilder();

        for (task.IterationCount = 0; task.IterationCount < maxIter; task.IterationCount++)
        {
            var iterResult = new StringBuilder();
            await foreach (var token in _aiClient.StreamAsync(model.BaseUrl, model.ApiModelId, apiKey, messages, ct: ct))
            {
                iterResult.Append(token);
                TokenReceived?.Invoke(this, new AgentTokenEventArgs(token, "execute"));
            }

            var response = iterResult.ToString();
            resultBuilder.AppendLine(response);
            task.Log.AppendLine($"[迭代{task.IterationCount + 1}] {response.Truncate(200)}");
            messages = [.. messages, new ApiMessage("assistant", response)];

            // 判断是否完成（简单启发式：包含终止词）
            if (IsTaskComplete(response)) break;
        }

        task.FinalResult = resultBuilder.ToString();

        // 6. 等待用户评价
        if (feedbackCallback is not null)
        {
            SetStatus(task, AgentStatus.WaitingFeedback);
            var feedback = await feedbackCallback(task);
            if (feedback is not null && feedback.Length > 0)
                await _rag.AddRevisionAsync(task.RagNoteId, feedback, null, task.FinalResult);
        }

        // 7. 记录用量 & 归档
        await RecordUsageAsync(task, model.Id, mode.ToString());
        await _rag.MarkSuccessAsync(task.RagNoteId, task.Log.ToString(), task.FinalResult, modelId);

        task.Status = AgentStatus.Done;
        SetStatus(task, AgentStatus.Done);
        return task;
    }

    // ── 内部 ────────────────────────────────────────────────────────────
    private void SetStatus(AgentTask task, AgentStatus status)
    {
        task.Status = status;
        StatusChanged?.Invoke(this, new AgentStatusChangedEventArgs(task.TaskId, status));
    }

    private static List<ApiMessage> BuildPlanMessages(string prompt, string ragContext) =>
    [
        new("system", $"""
你是 ClawBY19，一个专业的AI智能体。请根据用户指令制定清晰的执行计划。
{(ragContext.Length > 0 ? ragContext : "")}
制定计划时：
1. 列出具体执行步骤
2. 说明每步的目的
3. 指出可能的风险点
"""),
        new("user", $"请为以下任务制定执行计划：\n{prompt}")
    ];

    private static List<ApiMessage> BuildExecutionMessages(string prompt, string plan, string ragContext) =>
    [
        new("system", $"""
你是 ClawBY19，一个专业的AI智能体。请按计划执行任务并给出完整结果。
{(ragContext.Length > 0 ? ragContext : "")}
"""),
        new("user", $"任务：{prompt}\n执行计划：{plan}\n请开始执行并给出完整结果。")
    ];

    private static bool IsTaskComplete(string response) =>
        response.Contains("任务完成") || response.Contains("执行完毕") ||
        response.Contains("已完成") || response.Length > 2000;

    private async Task RecordUsageAsync(AgentTask task, string modelId, string selectionMode)
    {
        await _costTracker.RecordAsync(new ApiUsageLog
        {
            TaskId = task.TaskId,
            Model = modelId,
            SelectionMode = selectionMode,
            TokensInput = task.TokensInput,
            TokensOutput = task.TokensOutput,
            Status = "success"
        });
    }
}

public class AgentStatusChangedEventArgs(string taskId, AgentStatus status) : EventArgs
{
    public string TaskId { get; } = taskId;
    public AgentStatus Status { get; } = status;
}

public class AgentTokenEventArgs(string token, string phase) : EventArgs
{
    public string Token { get; } = token;
    public string Phase { get; } = phase;  // "plan" | "execute"
}
