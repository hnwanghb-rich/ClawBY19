namespace ClawBY19.Config;

// ── models.toml 映射 ──
public class ModelConfig
{
    public ModelSelectionSection ModelSelection { get; set; } = new();
    public ClawAutoSection ClawAuto { get; set; } = new();
}

public class ModelSelectionSection
{
    public string Mode { get; set; } = "claw_auto";           // "user_specified" | "claw_auto"
    public string SpecifiedModel { get; set; } = "deepseek-v3";
    public bool AllowPerSessionOverride { get; set; } = true;
}

public class ClawAutoSection
{
    public string DefaultModel { get; set; } = "deepseek-v3";
    public bool ExplainSelection { get; set; } = true;
    public TaskRoutingSection TaskRouting { get; set; } = new();
    public FallbackSection Fallback { get; set; } = new();
    public BudgetSection Budget { get; set; } = new();
}

public class TaskRoutingSection
{
    public string CodeTask { get; set; } = "deepseek-v3";
    public string ReasoningTask { get; set; } = "deepseek-r1";
    public string LongContextTask { get; set; } = "minimax-text-01";
    public string ChineseTask { get; set; } = "qwen-max";
    public string QuickTask { get; set; } = "doubao-pro-32k";
    public string CreativeTask { get; set; } = "baichuan4-turbo";
    public string AnalysisTask { get; set; } = "qwen2.5-72b";
    public string AgentTask { get; set; } = "claude-sonnet-4-6";
    public string OfflineTask { get; set; } = "ollama/qwen2.5:7b";
}

public class FallbackSection
{
    public List<string> Chain { get; set; } = ["deepseek-v3", "qwen-plus", "doubao-pro-32k", "ollama/qwen2.5:7b"];
    public int FailThreshold { get; set; } = 3;
}

public class BudgetSection
{
    public bool Enabled { get; set; } = true;
    public decimal MaxCostPerRequestCny { get; set; } = 1.0m;
    public bool PreferCheapestCapable { get; set; } = false;
    public string DailyBudgetAlertModel { get; set; } = "doubao-pro-32k";
}

// ── alert_rules.toml 映射 ──
public class AlertRulesConfig
{
    public AlertPeriodConfig Daily { get; set; } = new() { ThresholdCny = 20m };
    public AlertPeriodConfig Weekly { get; set; } = new() { ThresholdCny = 100m };
    public AlertPeriodConfig Monthly { get; set; } = new() { ThresholdCny = 500m };
    public AlertPeriodConfig Cumulative { get; set; } = new() { ThresholdCny = 5000m, WarningAtPercent = 90 };
    public AlertActionConfig Action { get; set; } = new();
}

public class AlertPeriodConfig
{
    public bool Enabled { get; set; } = true;
    public decimal ThresholdCny { get; set; }
    public int WarningAtPercent { get; set; } = 80;
    public int CriticalAtPercent { get; set; } = 100;
}

public class AlertActionConfig
{
    public string OnWarning { get; set; } = "flash_yellow";
    public string OnCritical { get; set; } = "flash_red_and_pause";
    public bool AutoPauseOnCritical { get; set; } = true;
}
