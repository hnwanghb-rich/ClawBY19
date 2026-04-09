using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClawBY19.Services.AI;
using ClawBY19.Services.Learning;
using ClawBY19.Services;

namespace ClawBY19.ViewModels;

/// <summary>单条 API Key 配置项（支持明文/密文切换）</summary>
public partial class ApiKeyEntry : ObservableObject
{
    public string Provider { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private bool _showKey;

    /// <summary>非 null 时显示 Endpoint ID 输入框</summary>
    public string? EndpointLabel { get; init; }
    [ObservableProperty] private string _endpointValue = string.Empty;

    [RelayCommand]
    private void ToggleShowKey() => ShowKey = !ShowKey;
}

/// <summary>设置页模型列表项（含可用状态）</summary>
public partial class ModelSettingItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiProvider { get; init; } = string.Empty;
    public bool IsLocal { get; init; }

    // 能力标签
    public bool SupportsVision { get; init; }
    public bool SupportsImageGeneration { get; init; }
    public bool SupportsVideoUnderstanding { get; init; }
    public bool SupportsVideoGeneration { get; init; }

    [ObservableProperty] private bool _isAvailable = true;
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _config;
    private readonly ModelRegistry _registry;
    private readonly ModelSelectionService _modelSelector;
    private readonly LearningService _learning;
    private readonly ThemeService _themeService;

    private static readonly HttpClient _testHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    // ── 选中模型右侧面板 ──
    // ListBox 双向绑定此属性，变化时自动触发 OnSelectedModelItemChanged
    [ObservableProperty] private ModelSettingItem? _selectedModelItem;
    [ObservableProperty] private string _panelModelName = string.Empty;
    [ObservableProperty] private string _panelProvider = string.Empty;
    [ObservableProperty] private string _panelBaseUrl = string.Empty;
    [ObservableProperty] private string _panelApiKeyLabel = "API Key";
    [ObservableProperty] private string _panelApiKey = string.Empty;
    [ObservableProperty] private string _panelEndpointLabel = string.Empty;
    [ObservableProperty] private string _panelEndpointValue = string.Empty;
    [ObservableProperty] private bool _panelHasEndpoint;
    [ObservableProperty] private bool _panelIsLocal;
    [ObservableProperty] private string _testResultMessage = string.Empty;
    [ObservableProperty] private bool _testResultIsSuccess;
    [ObservableProperty] private bool _isTesting;

    // 右侧面板：能力标签
    [ObservableProperty] private bool _panelSupportsVision;
    [ObservableProperty] private bool _panelSupportsImageGen;
    [ObservableProperty] private bool _panelSupportsVideoUnderstanding;
    [ObservableProperty] private bool _panelSupportsVideoGen;

    // ── 主题 ──
    [ObservableProperty] private string _selectedTheme = "Dark";
    public IReadOnlyList<string> AvailableThemes => ThemeService.AvailableThemes;

    // ── API Keys（15个Provider）──
    public ObservableCollection<ApiKeyEntry> ApiKeyEntries { get; } = new();

    // ── 设置页模型 ListBox ──
    public ObservableCollection<ModelSettingItem> ModelItems { get; } = new();

    // ── 费用预警 ──
    [ObservableProperty] private decimal _dailyBudget;
    [ObservableProperty] private decimal _monthlyBudget;
    [ObservableProperty] private bool _autoPauseOnCritical;

    // ── 升级学习 ──
    [ObservableProperty] private string _learningOutput = string.Empty;
    [ObservableProperty] private double _learningProgress;
    [ObservableProperty] private bool _isLearning;

    // 旧版 ModelGroups 保留（兼容性，如有其他地方引用）
    public ObservableCollection<ModelGroupItem> ModelGroups { get; } = new();

    public SettingsViewModel(ConfigService config, ModelRegistry registry,
        ModelSelectionService modelSelector, LearningService learning, ThemeService themeService)
    {
        _config = config;
        _registry = registry;
        _modelSelector = modelSelector;
        _learning = learning;
        _themeService = themeService;
        _learning.ProgressChanged += OnLearningProgress;

        BuildApiKeyEntries();
        LoadSettings();
        BuildModelItems();
        BuildModelGroups();
    }

    // ── 命令 ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void SaveSettings()
    {        // 回写 API Keys 和 Endpoint IDs（从 ApiKeyEntries）
        foreach (var entry in ApiKeyEntries)
        {
            _config.AppSettings.ApiKeys[entry.Provider] = entry.ApiKey;
            if (entry.EndpointLabel is not null)
                _config.AppSettings.ApiModelIds[entry.Provider] = entry.EndpointValue;
        }

        // 同步右侧面板对当前选中模型的编辑
        if (SelectedModelItem is not null)
        {
            _config.AppSettings.ApiKeys[SelectedModelItem.ApiProvider] = PanelApiKey;
            if (PanelHasEndpoint)
                // 保存到模型 ID 级别（优先），同时保留旧的 provider 级别以兼容 doubao-pro-32k
                _config.AppSettings.ApiModelIds[SelectedModelItem.Id] = PanelEndpointValue;
            // 同步回 ApiKeyEntries
            var entry = ApiKeyEntries.FirstOrDefault(e => e.Provider == SelectedModelItem.ApiProvider);
            if (entry != null)
            {
                entry.ApiKey = PanelApiKey;
                if (entry.EndpointLabel != null) entry.EndpointValue = PanelEndpointValue;
            }
        }

        // 保存主题
        _config.AppSettings.Theme = SelectedTheme;

        // 更新预警阈值
        _config.AlertRules.Daily.ThresholdCny = DailyBudget;
        _config.AlertRules.Monthly.ThresholdCny = MonthlyBudget;
        _config.AlertRules.Action.AutoPauseOnCritical = AutoPauseOnCritical;

        _config.SaveModelConfig();
        _config.SaveAppSettings();

        StatusMessage = "设置已保存 ✓";
    }

    [RelayCommand]
    private void ApplyTheme(string themeName)
    {
        SelectedTheme = themeName;
        _themeService.Apply(themeName);
    }

    /// <summary>点击 ListBox 中某个模型，右侧面板显示对应参数</summary>
    [RelayCommand]
    private void SelectModelItem(ModelSettingItem? item)
    {
        if (item is null) return;
        SelectedModelItem = item;   // 触发 OnSelectedModelItemChanged → RefreshPanel
    }

    /// <summary>CommunityToolkit.Mvvm 自动回调：SelectedModelItem 属性变化时刷新右侧面板</summary>
    partial void OnSelectedModelItemChanged(ModelSettingItem? value)
    {
        if (value is null) return;
        RefreshPanel(value);
    }

    /// <summary>右侧面板数据填充</summary>
    private void RefreshPanel(ModelSettingItem item)
    {
        PanelModelName = item.FullName;
        PanelProvider = item.Provider;
        PanelBaseUrl = item.BaseUrl;
        PanelIsLocal = item.IsLocal;
        PanelApiKeyLabel = item.IsLocal ? "Ollama 地址（无需 Key）" : $"{item.Provider} API Key";

        // 从已保存配置读取 key（明文显示）
        PanelApiKey = _config.AppSettings.ApiKeys.GetValueOrDefault(item.ApiProvider, string.Empty);

        // Endpoint：Doubao 系列全部需要配置（先按 modelId 查，再按 provider 查）
        var hasEndpoint = item.ApiProvider == "Doubao";
        PanelHasEndpoint = hasEndpoint;
        PanelEndpointLabel = hasEndpoint ? "Endpoint ID（ep-XXXXXX）" : string.Empty;
        PanelEndpointValue = hasEndpoint
            ? (_config.AppSettings.ApiModelIds.GetValueOrDefault(item.Id)
               ?? _config.AppSettings.ApiModelIds.GetValueOrDefault(item.ApiProvider, string.Empty))
            : string.Empty;

        // 能力标签
        PanelSupportsVision            = item.SupportsVision;
        PanelSupportsImageGen          = item.SupportsImageGeneration;
        PanelSupportsVideoUnderstanding = item.SupportsVideoUnderstanding;
        PanelSupportsVideoGen          = item.SupportsVideoGeneration;

        TestResultMessage = string.Empty;
    }

    [RelayCommand]
    private void SelectModel(string modelId)
    {
        var item = ModelItems.FirstOrDefault(m => m.Id == modelId);
        if (item != null) SelectModelItem(item);
    }

    /// <summary>连接测试：用 ping 消息测试选中模型是否可用</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedModelItem is null) return;
        IsTesting = true;
        TestResultMessage = "测试中...";
        TestResultIsSuccess = false;

        try
        {
            var model = _registry.Get(SelectedModelItem.Id);
            if (model is null)
            {
                TestResultMessage = "模型信息未找到";
                return;
            }

            // Ollama 本地模型：直接 ping http://localhost:11434
            if (model.IsLocal)
            {
                using var resp = await _testHttp.GetAsync("http://localhost:11434/api/tags");
                TestResultIsSuccess = resp.IsSuccessStatusCode;
                TestResultMessage = resp.IsSuccessStatusCode
                    ? "✅ 本地 Ollama 服务运行正常，模型可用"
                    : $"❌ Ollama 服务未响应：{resp.StatusCode}";
                return;
            }

            // 云端模型：发一条极短的 chat 请求
            var apiKey = PanelApiKey.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                TestResultMessage = "⚠️ 请先填写 API Key";
                return;
            }

            var apiModelId = model.IsLocal ? model.ApiModelId
                : (!string.IsNullOrEmpty(PanelEndpointValue) ? PanelEndpointValue : model.ApiModelId);

            var url = model.BaseUrl.TrimEnd('/') + "/chat/completions";
            var body = JsonSerializer.Serialize(new
            {
                model = apiModelId,
                messages = new[] { new { role = "user", content = "hi" } },
                max_tokens = 1,
                stream = false
            });

            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _testHttp.SendAsync(req);
            TestResultIsSuccess = response.IsSuccessStatusCode;
            TestResultMessage = response.IsSuccessStatusCode
                ? $"✅ {SelectedModelItem.FullName} 连接正常，当前模型可用"
                : $"❌ 连接失败（{(int)response.StatusCode}）：{await response.Content.ReadAsStringAsync()}";
        }
        catch (TaskCanceledException)
        {
            TestResultMessage = "❌ 连接超时（>10s），请检查网络或 API 地址";
        }
        catch (Exception ex)
        {
            TestResultMessage = $"❌ 异常：{ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task RunLearningUpgradeAsync()
    {
        IsLearning = true;
        LearningOutput = "正在执行升级学习...\n";
        LearningProgress = 0;
        try
        {
            var path = await _learning.RunUpgradeAsync();
            LearningOutput += $"\n✅ 升级完成！报告已保存至：\n{path}";
        }
        catch (Exception ex)
        {
            LearningOutput += $"\n❌ 升级失败：{ex.Message}";
        }
        finally
        {
            IsLearning = false;
            LearningProgress = 100;
        }
    }

    // ── 内部 ────────────────────────────────────────────────────────

    private void BuildApiKeyEntries()
    {
        var providers = new[]
        {
            ("DeepSeek",   "DeepSeek（深度求索）",    (string?)null),
            ("Qwen",       "阿里云百炼（Qwen）",       (string?)null),
            ("ZhipuAI",    "智谱AI（GLM）",             (string?)null),
            ("Moonshot",   "月之暗面（Kimi）",          (string?)null),
            ("MiniMax",    "MiniMax",                    (string?)null),
            ("iFlytek",    "科大讯飞（Spark）",         (string?)null),
            ("Baidu",      "百度文心（ERNIE）",         (string?)null),
            ("Doubao",     "字节豆包",                  (string?)"Endpoint ID（ep-XXXXXX）"),
            ("Tencent",    "腾讯混元",                  (string?)null),
            ("Baichuan",   "百川智能",                  (string?)null),
            ("Anthropic",  "Anthropic（Claude）",       (string?)null),
            ("OpenAI",     "OpenAI（GPT/o系列）",       (string?)null),
            ("Google",     "Google（Gemini）",           (string?)null),
            ("xAI",        "xAI（Grok）",               (string?)null),
            ("Mistral",    "Mistral AI",                 (string?)null),
        };

        foreach (var (prov, name, epLabel) in providers)
        {
            ApiKeyEntries.Add(new ApiKeyEntry
            {
                Provider = prov,
                DisplayName = name,
                EndpointLabel = epLabel,
            });
        }
    }

    private void LoadSettings()
    {
        SelectedTheme = _config.AppSettings.Theme;

        var keys = _config.AppSettings.ApiKeys;
        var modelIds = _config.AppSettings.ApiModelIds;

        foreach (var entry in ApiKeyEntries)
        {
            entry.ApiKey = keys.GetValueOrDefault(entry.Provider, string.Empty);
            if (entry.EndpointLabel is not null)
                entry.EndpointValue = modelIds.GetValueOrDefault(entry.Provider, string.Empty);
        }

        DailyBudget = _config.AlertRules.Daily.ThresholdCny;
        MonthlyBudget = _config.AlertRules.Monthly.ThresholdCny;
        AutoPauseOnCritical = _config.AlertRules.Action.AutoPauseOnCritical;
    }

    // 设置页和聊天选择器均显示的 Provider 白名单（按显示顺序）
    private static readonly string[] VisibleProviders =
        ["Doubao", "Qwen", "DeepSeek", "Anthropic", "Google", "OpenAI"];

    private void BuildModelItems()
    {
        var categoryLabels = new Dictionary<ModelCategory, string>
        {
            [ModelCategory.ChineseOpenSource] = "🇨🇳 A类：中国开源",
            [ModelCategory.ChineseCommercial] = "🇨🇳 B类：中国商业",
            [ModelCategory.UsCommercial]      = "🇺🇸 C类：美国主流",
            [ModelCategory.LocalOllama]       = "💻 D类：本地Ollama",
        };

        // 按 Provider 白名单顺序排列，同一 Provider 内按 Category 升序
        var filtered = _registry.All()
            .Where(m => VisibleProviders.Contains(m.ApiProvider))
            .OrderBy(m => Array.IndexOf(VisibleProviders, m.ApiProvider))
            .ThenBy(m => (int)m.Category)
            .ThenBy(m => m.Id);

        foreach (var m in filtered)
        {
            ModelItems.Add(new ModelSettingItem
            {
                Id = m.Id,
                FullName = m.FullName,
                Provider = m.Provider,
                Category = categoryLabels.GetValueOrDefault(m.Category, "其他"),
                Description = m.Description,
                BaseUrl = m.BaseUrl,
                ApiProvider = m.ApiProvider,
                IsLocal = m.IsLocal,
                SupportsVision = m.SupportsVision,
                SupportsImageGeneration = m.SupportsImageGeneration,
                SupportsVideoUnderstanding = m.SupportsVideoUnderstanding,
                SupportsVideoGeneration = m.SupportsVideoGeneration,
                IsAvailable = m.IsLocal || !string.IsNullOrEmpty(
                    _config.AppSettings.ApiKeys.GetValueOrDefault(m.ApiProvider, string.Empty))
            });
        }

        // 默认选中第一个模型，ListBox 高亮 + 右侧面板同步显示
        var current = ModelItems.FirstOrDefault();
        if (current != null) SelectedModelItem = current;   // 触发 OnSelectedModelItemChanged
    }

    private void BuildModelGroups()
    {
        var groups = _registry.All()
            .GroupBy(m => m.Category)
            .OrderBy(g => (int)g.Key);
        foreach (var g in groups)
        {
            var label = g.Key switch
            {
                ModelCategory.ChineseOpenSource => "🇨🇳 A类：中国开源模型（最优先）",
                ModelCategory.ChineseCommercial => "🇨🇳 B类：中国商业API",
                ModelCategory.UsCommercial      => "🇺🇸 C类：美国主流模型",
                ModelCategory.LocalOllama       => "💻 D类：本地Ollama（零成本）",
                _ => "其他"
            };
            ModelGroups.Add(new ModelGroupItem(label,
                g.Select(m => new ModelListItem(m.Id, m.FullName, m.Provider, m.Description)).ToList()));
        }
    }

    private void OnLearningProgress(object? sender, LearningProgressEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            LearningProgress = e.Percentage;
            LearningOutput += $"[{e.Current}/{e.Total}] 分析：{e.CurrentType}\n";
        });
    }
}

public record ModelGroupItem(string GroupName, List<ModelListItem> Models);
public record ModelListItem(string Id, string FullName, string Provider, string Description);
