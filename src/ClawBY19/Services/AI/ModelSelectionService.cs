using ClawBY19.Config;
using ClawBY19.Data;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.AI;

public enum SelectionMode { UserSpecified, ClawAuto }

public class ModelSelectionService
{
    private readonly ModelRegistry _registry;
    private readonly ConfigService _config;
    private readonly ILogger<ModelSelectionService> _logger;

    // 各模型连续失败次数
    private readonly Dictionary<string, int> _failCounts = new();

    public SelectionMode CurrentMode =>
        _config.ModelCfg.ModelSelection.Mode.Equals("claw_auto", StringComparison.OrdinalIgnoreCase)
            ? SelectionMode.ClawAuto
            : SelectionMode.UserSpecified;

    public ModelSelectionService(ModelRegistry registry, ConfigService config,
        ILogger<ModelSelectionService> logger)
    {
        _registry = registry;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 为给定任务选择最优模型。
    /// 返回 (modelId, reason, selectionMode)
    /// </summary>
    public (string ModelId, string Reason, SelectionMode Mode) SelectModel(
        string userPrompt,
        decimal todaySpentCny = 0m,
        decimal dailyBudget = 20m,
        bool forceOffline = false)
    {
        if (CurrentMode == SelectionMode.UserSpecified)
        {
            var specified = _config.ModelCfg.ModelSelection.SpecifiedModel;
            return (specified, "用户指定模型", SelectionMode.UserSpecified);
        }

        return SelectAutoModel(userPrompt, todaySpentCny, dailyBudget, forceOffline);
    }

    /// <summary>切换模式（写回配置文件）</summary>
    public void SetMode(SelectionMode mode, string? specifiedModelId = null)
    {
        _config.ModelCfg.ModelSelection.Mode =
            mode == SelectionMode.ClawAuto ? "claw_auto" : "user_specified";
        if (specifiedModelId is not null)
            _config.ModelCfg.ModelSelection.SpecifiedModel = specifiedModelId;
        _config.SaveModelConfig();
    }

    /// <summary>记录模型调用失败，达到阈值后切换降级链</summary>
    public void RecordFailure(string modelId)
    {
        _failCounts[modelId] = _failCounts.GetValueOrDefault(modelId) + 1;
        _logger.LogWarning("模型 {Model} 调用失败，累计 {Count} 次",
            modelId, _failCounts[modelId]);
    }

    public void RecordSuccess(string modelId) =>
        _failCounts.Remove(modelId);

    // ── 内部：Claw自选逻辑 ──────────────────────────────────────────
    private (string ModelId, string Reason, SelectionMode Mode) SelectAutoModel(
        string prompt, decimal todaySpent, decimal dailyBudget, bool forceOffline)
    {
        var cfg = _config.ModelCfg.ClawAuto;
        var routing = cfg.TaskRouting;
        var budget = cfg.Budget;

        // 步骤1：任务类型识别
        var taskType = ClassifyTask(prompt, forceOffline);
        var candidate = taskType switch
        {
            "code"         => routing.CodeTask,
            "reasoning"    => routing.ReasoningTask,
            "long_context" => routing.LongContextTask,
            "chinese"      => routing.ChineseTask,
            "quick"        => routing.QuickTask,
            "creative"     => routing.CreativeTask,
            "analysis"     => routing.AnalysisTask,
            "agent"        => routing.AgentTask,
            "offline"      => routing.OfflineTask,
            _              => cfg.DefaultModel
        };
        var reason = $"任务类型={taskType}，路由至 {candidate}";

        // 步骤2：预算检查
        if (budget.Enabled && dailyBudget > 0 &&
            todaySpent / dailyBudget >= 0.80m)
        {
            candidate = budget.DailyBudgetAlertModel;
            reason = $"日预算已用 {todaySpent:F2}/{dailyBudget:F2}（≥80%），切换至低成本模型 {candidate}";
        }

        // 步骤3：可用性检查（降级链）
        var thres = cfg.Fallback.FailThreshold;
        if (_failCounts.GetValueOrDefault(candidate) >= thres)
        {
            foreach (var fallback in cfg.Fallback.Chain)
            {
                if (_failCounts.GetValueOrDefault(fallback) < thres)
                {
                    reason += $" → 主模型失败 {thres} 次，降级为 {fallback}";
                    candidate = fallback;
                    break;
                }
            }
        }

        _logger.LogInformation("Claw自选：{Model}（{Reason}）", candidate, reason);
        return (candidate, reason, SelectionMode.ClawAuto);
    }

    // 关键词 + 简单规则分类
    private static string ClassifyTask(string prompt, bool forceOffline)
    {
        if (forceOffline) return "offline";

        var p = prompt.ToLowerInvariant();

        if (ContainsAny(p, "代码", "函数", "bug", "debug", "编程", "程序", "脚本", "algorithm", "code", "class", "interface"))
            return "code";

        if (ContainsAny(p, "推理", "证明", "数学", "计算", "逻辑", "方程", "求解", "math", "prove", "theorem"))
            return "reasoning";

        if (prompt.Length > 3000 || ContainsAny(p, "全文", "整本", "长文档", "pdf", "分析全"))
            return "long_context";

        if (ContainsAny(p, "写作", "创作", "故事", "文案", "诗", "小说", "广告语", "营销"))
            return "creative";

        if (ContainsAny(p, "分析", "报告", "表格", "数据", "统计", "excel", "csv", "图表"))
            return "analysis";

        if (ContainsAny(p, "古文", "文言", "诗词", "成语", "文化", "历史典故"))
            return "chinese";

        if (ContainsAny(p, "执行任务", "帮我做", "自动完成", "agent", "多步骤", "工具调用"))
            return "agent";

        // 短提示 → quick
        if (prompt.Length < 50) return "quick";

        return "default";
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(text.Contains);
}
