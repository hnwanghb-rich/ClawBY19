using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClawBY19.Services.AI;
using ClawBY19.Services.Chat;
using ClawBY19.Services;
using ClawBY19.Services.Deploy;
using ClawBY19.Services.Git;
using ClawBY19.Services.OpenClaw;
using ClawBY19.Services.Testing;
using Microsoft.Win32;

namespace ClawBY19.ViewModels;

/// <summary>消息气泡的 UI 模型</summary>
public partial class ChatMessageItem : ObservableObject
{
    public string Role { get; init; } = "user";
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }
    private string _content = string.Empty;

    /// <summary>true = 正在等待第一个 Token（显示思考动画）</summary>
    [ObservableProperty] private bool _isThinking;

    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string? ModelUsed { get; init; }
    public decimal CostCny { get; init; }
    public bool IsUser => Role == "user";
    public string TimeStr => Timestamp.ToString("HH:mm");
    public string CostStr => CostCny > 0 ? $"¥{CostCny:F4}" : string.Empty;

    // 附件列表（用于气泡展示，ObservableCollection 支持生成结果动态追加）
    public ObservableCollection<AttachmentItem> Attachments { get; } = new();
}

/// <summary>模型切换弹出框中的单个模型选项</summary>
public partial class ModelPickerItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string CategoryLabel { get; init; } = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
}

/// <summary>凭证输入结果</summary>
public record CredentialResult(
    string TargetName,
    string TargetUrl,
    string Account,
    string Password);

public partial class ChatViewModel : ViewModelBase
{
    private readonly ChatService _chatService;
    private readonly ScheduledTaskService _scheduledTaskService;
    private readonly ModelSelectionService _modelSelector;
    private readonly ModelRegistry _registry;
    private readonly ConfigService _config;
    private readonly OperationHistoryService _opHistory;
    private readonly GitUploadService _gitUpload;
    private readonly CloudDeployService _cloudDeploy;
    private readonly TestService _testService;
    private readonly ClawBY19.Services.TaskLog.TaskLogService _taskLog;

    [ObservableProperty] private string _userInput = string.Empty;
    [ObservableProperty] private string _currentModelDisplay = string.Empty;
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private bool _isWaitingForResponse;
    [ObservableProperty] private string _currentModelEmoji = "🤖";
    [ObservableProperty] private string _selectionModeLabel = "Claw自选";

    // 模型切换弹出框
    [ObservableProperty] private bool _isModelPickerOpen;
    public ObservableCollection<ModelPickerItem> PickerModels { get; } = new();

    // 当前模型能力提示（顶部栏显示）
    [ObservableProperty] private bool _currentModelHasVision;
    [ObservableProperty] private bool _currentModelHasImageGen;
    [ObservableProperty] private bool _currentModelHasVideoUnderstanding;
    [ObservableProperty] private bool _currentModelHasVideoGen;

    // 导出弹出框
    [ObservableProperty] private bool _isExportMenuOpen;

    // 昵称
    [ObservableProperty] private string _nickName = string.Empty;
    [ObservableProperty] private bool _isNickNameDialogOpen;
    [ObservableProperty] private string _pendingNickName = string.Empty;

    // 附件
    public ObservableCollection<AttachmentItem> Attachments { get; } = new();

    // 定时任务确认
    [ObservableProperty] private bool _isTaskConfirmOpen;
    [ObservableProperty] private string _pendingTaskDescription = string.Empty;
    [ObservableProperty] private string _pendingTaskCron = string.Empty;
    [ObservableProperty] private string _pendingTaskPrompt = string.Empty;
    private ExtractedTask? _pendingTask;

    // ── GitHub 操作确认对话框 ─────────────────────────────────────────
    [ObservableProperty] private bool _isGitHubConfirmOpen;
    [ObservableProperty] private string _gitHubLastRepo = string.Empty;
    [ObservableProperty] private string _gitHubLastAccount = string.Empty;

    // ── 云端部署确认对话框 ────────────────────────────────────────────
    [ObservableProperty] private bool _isCloudConfirmOpen;
    [ObservableProperty] private string _cloudLastPlatform = string.Empty;
    [ObservableProperty] private string _cloudLastIp = string.Empty;
    [ObservableProperty] private string _cloudLastAccount = string.Empty;

    // ── Git 上传专用对话框 ────────────────────────────────────────────
    [ObservableProperty] private bool _isGitUploadOpen;
    [ObservableProperty] private string _gitLocalPath = string.Empty;
    [ObservableProperty] private string _gitRemoteUrl = string.Empty;
    [ObservableProperty] private string _gitBranch = "main";
    [ObservableProperty] private string _gitUsername = string.Empty;
    [ObservableProperty] private string _gitCommitMessage = string.Empty;
    private string _gitToken = string.Empty;
    public void SetGitToken(string token) => _gitToken = token;
    private TaskCompletionSource<bool>? _gitUploadTcs;

    // ── 测试对话框 ────────────────────────────────────────────────────
    [ObservableProperty] private bool _isTestDialogOpen;
    [ObservableProperty] private string _testType = "Web";   // "Web" | "Exe"
    [ObservableProperty] private string _testTarget = string.Empty;
    [ObservableProperty] private string _testLoginUrl = string.Empty;
    [ObservableProperty] private string _testUsername = string.Empty;
    private string _testPassword = string.Empty;
    public void SetTestPassword(string pwd) => _testPassword = pwd;
    private TaskCompletionSource<bool>? _testDialogTcs;

    // ── 凭证输入对话框 ────────────────────────────────────────────────
    [ObservableProperty] private bool _isCredentialInputOpen;
    [ObservableProperty] private string _credentialDialogTitle = "输入操作参数";
    [ObservableProperty] private string _credentialTargetNameInput = string.Empty;
    [ObservableProperty] private string _credentialUrlInput = string.Empty;
    [ObservableProperty] private string _credentialAccountInput = string.Empty;
    // PasswordBox 内容由 code-behind 注入，不用 [ObservableProperty]
    private string _credentialPasswordInput = string.Empty;
    public void SetCredentialPassword(string pwd) => _credentialPasswordInput = pwd;

    // ── 操作确认异步流程 ─────────────────────────────────────────────
    private TaskCompletionSource<bool>? _opConfirmTcs;   // true=使用上次，false=换新
    private TaskCompletionSource<CredentialResult?>? _credInputTcs; // null=取消
    private bool _isAwaitingOpConfirm;
    private string _pendingOpType = string.Empty;    // 当前待确认的操作类型
    private int _pendingOpHistoryId;                  // 上次操作记录的 ID

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    private ChatMessageItem? _streamingItem;
    private CancellationTokenSource? _cts;

    // GitHub 关键词
    private static readonly string[] GitHubKeywords =
        ["github", "git push", "git commit", "代码同步", "同步到github",
         "推送代码", "代码推送", "push代码", "上传代码到github", "代码上传github",
         "上传代码", "代码上传", "提交代码", "同步代码"];

    // 云端部署关键词
    private static readonly string[] CloudKeywords =
        ["发布到云端", "云端部署", "部署到云", "上传到云", "发布到服务器",
         "部署到服务器", "云发布", "上传服务器", "deploy", "deployment"];

    // 测试关键词
    private static readonly string[] TestKeywords =
        ["测试程序", "测试系统", "自动测试", "功能测试", "对程序进行测试",
         "测试一下", "帮我测试", "跑一下测试", "run test", "autotest"];

    // 与 SettingsViewModel 保持相同的 Provider 白名单和顺序
    private static readonly string[] VisibleProviders =
        ["Doubao", "Qwen", "DeepSeek", "Anthropic", "Google", "OpenAI"];

    public ChatViewModel(
        ChatService chatService,
        ScheduledTaskService scheduledTaskService,
        ModelSelectionService modelSelector,
        ModelRegistry registry,
        ConfigService config,
        OperationHistoryService opHistory,
        GitUploadService gitUpload,
        CloudDeployService cloudDeploy,
        TestService testService,
        ClawBY19.Services.TaskLog.TaskLogService taskLog)
    {
        _chatService = chatService;
        _scheduledTaskService = scheduledTaskService;
        _modelSelector = modelSelector;
        _registry = registry;
        _config = config;
        _opHistory = opHistory;
        _gitUpload = gitUpload;
        _cloudDeploy = cloudDeploy;
        _testService = testService;
        _taskLog = taskLog;

        _chatService.TokenReceived += OnTokenReceived;
        _chatService.TaskDetected += OnTaskDetected;
        _scheduledTaskService.TaskCompleted += OnScheduledTaskCompleted;

        BuildPickerModels();
        UpdateModelDisplay();

        NickName = _config.AppSettings.NickName;
        if (string.IsNullOrEmpty(NickName))
            IsNickNameDialogOpen = true;
        else
            ShowWelcomeMessage();
    }

    // ── 发送消息 ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var input = UserInput.Trim();
        if (string.IsNullOrEmpty(input) || IsStreaming || _isAwaitingOpConfirm) return;

        // ── 测试关键词检测（优先于其他流程）──
        var lower = input.ToLowerInvariant();
        if (TestKeywords.Any(k => lower.Contains(k)))
        {
            UserInput = string.Empty;
            Messages.Add(new ChatMessageItem { Role = "user", Content = input });
            await RunTestFlowAsync(input);
            return;
        }

        // ── 操作关键词检测与确认 ──
        _isAwaitingOpConfirm = true;
        string? apiInput;
        try
        {
            apiInput = await CheckAndConfirmOperationAsync(input);
        }
        finally
        {
            _isAwaitingOpConfirm = false;
        }

        if (apiInput is null) return;   // 用户取消 或 已由本地服务处理

        UserInput = string.Empty;
        var attachments = Attachments.ToList();
        Attachments.Clear();

        var (modelId, _, _) = _modelSelector.SelectModel(input, 0m);
        var modelRaw = _registry.Get(modelId);
        if (modelRaw is null)
        {
            Messages.Add(new ChatMessageItem
            {
                Role = "assistant",
                Content = "❌ 未找到可用模型，请在设置页配置 API Key。"
            });
            return;
        }

        // 应用 appsettings.json 中的 ApiModelIds 覆盖
        // 优先按 model.Id 查；生图/生视频模型不回退到 provider 级别（避免混用聊天 endpoint）
        var overrideId = _config.AppSettings.ApiModelIds.GetValueOrDefault(modelRaw.Id)
                      ?? (modelRaw.SupportsImageGeneration || modelRaw.SupportsVideoGeneration
                          ? null
                          : _config.AppSettings.ApiModelIds.GetValueOrDefault(modelRaw.ApiProvider));
        var model = string.IsNullOrEmpty(overrideId) ? modelRaw : new ModelInfo
        {
            Id = modelRaw.Id, FullName = modelRaw.FullName, Provider = modelRaw.Provider,
            ApiProvider = modelRaw.ApiProvider, BaseUrl = modelRaw.BaseUrl,
            ApiModelId = overrideId,
            Category = modelRaw.Category, ContextWindow = modelRaw.ContextWindow,
            Description = modelRaw.Description, IsLocal = modelRaw.IsLocal,
            IsOpenSource = modelRaw.IsOpenSource, OllamaEndpoint = modelRaw.OllamaEndpoint,
            SupportsVision = modelRaw.SupportsVision,
            SupportsImageGeneration = modelRaw.SupportsImageGeneration,
            SupportsVideoUnderstanding = modelRaw.SupportsVideoUnderstanding,
            SupportsVideoGeneration = modelRaw.SupportsVideoGeneration
        };

        var hasImages = attachments.Any(a => a.IsImage);
        var apiKey = _config.GetApiKey(model.ApiProvider);
        if (string.IsNullOrEmpty(apiKey) && !model.IsLocal)
        {
            Messages.Add(new ChatMessageItem
            {
                Role = "assistant",
                Content = $"❌ 模型 {model.FullName} 的 API Key 未配置，请前往设置页填写。"
            });
            return;
        }

        // 图像生成模型（Seedream 等）：不走普通 chat，走专用生图流程
        if (model.SupportsImageGeneration && !model.SupportsVision)
        {
            if (hasImages)
            {
                // 用户给生图模型上传了图片 → 明确提示
                Messages.Add(new ChatMessageItem
                {
                    Role = "assistant",
                    Content = $"⚠️ **{model.FullName}** 是文字生图模型，用于根据文字描述生成图片，不支持识别输入图片。\n\n请直接在输入框描述你想生成的图片内容，例如：\n「一只在草地上奔跑的橙色柴犬，动漫风格」"
                });
                return;
            }

            // 走图像生成流程
            await RunImageGenerationAsync(input, attachments, model, apiKey);
            return;
        }

        // 普通模型：有图片但不支持视觉 → 提示切换
        if (hasImages && !model.SupportsVision)
        {
            // 找出已配置真实 Key（非占位符）的视觉模型
            var availableVision = _registry.All()
                .Where(m => m.SupportsVision && !m.IsLocal)
                .Select(m => (Model: m, Key: _config.GetApiKey(m.ApiProvider)))
                .Where(x => !string.IsNullOrEmpty(x.Key) &&
                            !x.Key.StartsWith("your-", StringComparison.OrdinalIgnoreCase) &&
                            !x.Key.StartsWith("sk-your", StringComparison.OrdinalIgnoreCase) &&
                            x.Key.Length > 20)
                .Select(x => x.Model.FullName)
                .Distinct()
                .ToList();

            var hint = availableVision.Count > 0
                ? $"你已配置的视觉模型：{string.Join("、", availableVision)}\n请点击顶部模型名称切换后重新发送。"
                : "支持图片的模型有 GPT-4o、Gemini 2.5 Pro、Qwen-Max 等，请在设置页配置相应 API Key 后切换。";

            Messages.Add(new ChatMessageItem
            {
                Role = "assistant",
                Content = $"❌ 当前模型 **{model.FullName}** 不支持图片输入。\n\n{hint}"
            });
            return;
        }

        // 聊天气泡显示原始输入；API 发送丰富后的 apiInput
        var userMsg = new ChatMessageItem { Role = "user", Content = input };
        foreach (var a in attachments) userMsg.Attachments.Add(a);
        Messages.Add(userMsg);

        _streamingItem = new ChatMessageItem
        {
            Role = "assistant",
            Content = "",
            ModelUsed = model.FullName,
            IsThinking = true          // 等待第一个 Token 前显示思考动画
        };
        Messages.Add(_streamingItem);
        IsStreaming = true;
        IsWaitingForResponse = true;
        StatusMessage = $"⚡ {model.FullName} 响应中...";
        _cts = new CancellationTokenSource();

        try
        {
            await Task.Run(() => _chatService.SendAsync(apiInput, attachments, model, apiKey, _cts.Token), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (_streamingItem is not null)
                _streamingItem.Content += "\n\n⚠️ 已取消";
        }
        finally
        {
            IsStreaming = false;
            IsWaitingForResponse = false;
            StatusMessage = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelStream() => _cts?.Cancel();

    /// <summary>图像生成模型专用流程：调用 /images/generations，生成后显示图片</summary>
    private async Task RunImageGenerationAsync(
        string prompt, List<AttachmentItem> attachments, ModelInfo model, string apiKey)
    {
        // Volcengine 图像生成也需要 ep-XXXXXX，提前检测避免 400 错误
        if (model.ApiProvider == "Doubao" && !model.ApiModelId.StartsWith("ep-", StringComparison.OrdinalIgnoreCase))
        {
            Messages.Add(new ChatMessageItem { Role = "user", Content = prompt });
            Messages.Add(new ChatMessageItem
            {
                Role = "assistant",
                Content = $"⚠️ **{model.FullName}** 需要配置专属 Endpoint ID 才能生成图片。\n\n" +
                          "请前往 [火山引擎控制台](https://console.volcengine.com/ark/region:ark+cn-beijing/endpoint) → 创建推理接入点 → 选择 **Seedream** 模型 → 复制 ep-XXXXXX。\n\n" +
                          "然后在 ClawBY19 **设置页** → 选中该模型 → 填入 Endpoint ID → 保存。"
            });
            return;
        }

        // 用户消息气泡
        var userMsg = new ChatMessageItem { Role = "user", Content = prompt };
        foreach (var a in attachments) userMsg.Attachments.Add(a);
        Messages.Add(userMsg);

        // AI 响应气泡（先显示思考动画）
        var resultItem = new ChatMessageItem
        {
            Role = "assistant",
            Content = "",
            ModelUsed = model.FullName,
            IsThinking = true
        };
        Messages.Add(resultItem);
        IsStreaming = true;
        IsWaitingForResponse = true;
        StatusMessage = $"✨ {model.FullName} 生成中...";
        _cts = new CancellationTokenSource();

        try
        {
            var b64 = await _chatService.GenerateImageAsync(model, apiKey, prompt, ct: _cts.Token);
            resultItem.IsThinking = false;

            if (b64 is null)
            {
                resultItem.Content = "❌ 图像生成失败，返回数据为空。请检查 Endpoint ID 配置（ep-XXXXXX）。";
            }
            else
            {
                resultItem.Content = "✅ 图像已生成：";
                resultItem.Attachments.Add(new AttachmentItem("generated.png", "image/png", b64, true));
            }
        }
        catch (OperationCanceledException)
        {
            resultItem.IsThinking = false;
            resultItem.Content = "⚠️ 已取消";
        }
        catch (Exception ex)
        {
            resultItem.IsThinking = false;
            resultItem.Content = $"❌ 图像生成失败：{ex.Message}";
        }
        finally
        {
            IsStreaming = false;
            IsWaitingForResponse = false;
            StatusMessage = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void NewSession()
    {
        _chatService.ClearHistory();
        Messages.Clear();
        ShowWelcomeMessage();
    }

    // ── 操作确认核心逻辑 ─────────────────────────────────────────────────

    /// <summary>
    /// GitHub → GitUploadService 本地执行；
    /// 云端   → CloudDeployService AI 生成方案；
    /// 无关键词 → 返回原始输入给 AI 聊天
    /// </summary>
    private async Task<string?> CheckAndConfirmOperationAsync(string input)
    {
        var lower = input.ToLowerInvariant();

        bool isGitHub = GitHubKeywords.Any(k => lower.Contains(k));
        bool isCloud  = !isGitHub && CloudKeywords.Any(k => lower.Contains(k));

        if (!isGitHub && !isCloud) return input;

        _pendingOpType = isGitHub ? OpType.GitHub : OpType.CloudDeploy;
        var last = await _opHistory.GetLastAsync(_pendingOpType);

        // ── GitHub：执行本地 Git 上传 ──────────────────────────
        if (isGitHub)
        {
            UserInput = string.Empty;
            Messages.Add(new ChatMessageItem { Role = "user", Content = input });
            await RunGitUploadFlowAsync(input, last);
            return null;
        }

        // ── 云端部署：AI 生成部署方案 ─────────────────────────
        if (last is not null)
        {
            _pendingOpHistoryId = last.Id;
            _opConfirmTcs = new TaskCompletionSource<bool>();
            CloudLastPlatform = last.TargetName;
            CloudLastIp       = last.TargetUrl;
            CloudLastAccount  = last.Account;
            IsCloudConfirmOpen = true;

            bool useExisting = await _opConfirmTcs.Task;
            _opConfirmTcs = null;

            if (useExisting)
            {
                await _opHistory.TouchAsync(_pendingOpHistoryId);
                var pwd = _opHistory.DecryptPassword(last.PasswordEncrypted);
                var cloudParams = new CloudDeployParams(
                    last.TargetName, last.TargetUrl, last.Account, pwd,
                    string.Empty, string.Empty, input);
                UserInput = string.Empty;
                Messages.Add(new ChatMessageItem { Role = "user", Content = input });
                await RunCloudDeployFlowAsync(cloudParams);
                return null;
            }
        }

        // 弹凭证输入对话框（云端）
        CredentialDialogTitle  = "输入云端平台参数";
        CredentialTargetNameInput = last?.TargetName ?? string.Empty;
        CredentialUrlInput     = last?.TargetUrl    ?? "服务器 IP 或域名";
        CredentialAccountInput = last?.Account      ?? string.Empty;
        _credentialPasswordInput = string.Empty;

        _credInputTcs = new TaskCompletionSource<CredentialResult?>();
        IsCredentialInputOpen = true;

        var cred = await _credInputTcs.Task;
        _credInputTcs = null;

        if (cred is null) return null;

        await _opHistory.SaveAsync(_pendingOpType, cred.TargetName, cred.TargetUrl, cred.Account, cred.Password);

        var newCloudParams = new CloudDeployParams(
            cred.TargetName, cred.TargetUrl, cred.Account, cred.Password,
            string.Empty, string.Empty, input);
        UserInput = string.Empty;
        Messages.Add(new ChatMessageItem { Role = "user", Content = input });
        await RunCloudDeployFlowAsync(newCloudParams);
        return null;
    }

    // ── GitHub 确认对话框命令 ─────────────────────────────────────────

    [RelayCommand]
    private void ConfirmGitHubOp()
    {
        IsGitHubConfirmOpen = false;
        _opConfirmTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void DenyGitHubOp()
    {
        IsGitHubConfirmOpen = false;
        _opConfirmTcs?.TrySetResult(false);
    }

    [RelayCommand]
    private void CancelGitHubOp()
    {
        IsGitHubConfirmOpen = false;
        _opConfirmTcs?.TrySetResult(false);
        // 取消整个操作：将 TCS result 为 false，但凭证对话框在 CheckAndConfirmOperationAsync
        // 中仍会弹出——若要完全取消，通过 credential 对话框的 cancel 触发
        // 这里需要跳过凭证输入：重新设置 credInputTcs 并立即完成
        _credInputTcs?.TrySetResult(null);
    }

    // ── 云端部署确认对话框命令 ────────────────────────────────────────

    [RelayCommand]
    private void ConfirmCloudOp()
    {
        IsCloudConfirmOpen = false;
        _opConfirmTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void DenyCloudOp()
    {
        IsCloudConfirmOpen = false;
        _opConfirmTcs?.TrySetResult(false);
    }

    [RelayCommand]
    private void CancelCloudOp()
    {
        IsCloudConfirmOpen = false;
        _opConfirmTcs?.TrySetResult(false);
        _credInputTcs?.TrySetResult(null);
    }

    // ── 凭证输入对话框命令 ─────────────────────────────────────────────

    [RelayCommand]
    private void SubmitCredential()
    {
        var cred = new CredentialResult(
            CredentialTargetNameInput.Trim(),
            CredentialUrlInput.Trim(),
            CredentialAccountInput.Trim(),
            _credentialPasswordInput);

        if (string.IsNullOrEmpty(cred.TargetUrl) || string.IsNullOrEmpty(cred.Account)) return;

        IsCredentialInputOpen = false;
        _credentialPasswordInput = string.Empty;
        _credInputTcs?.TrySetResult(cred);
    }

    [RelayCommand]
    private void CancelCredential()
    {
        IsCredentialInputOpen = false;
        _credentialPasswordInput = string.Empty;
        _credInputTcs?.TrySetResult(null);
    }

    // ── 附件 ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "上传文件",
            Filter = "文本文件|*.txt;*.md;*.json;*.csv;*.xml;*.cs;*.py;*.js;*.ts;*.html;*.css|所有文件|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            Attachments.Add(new AttachmentItem(Path.GetFileName(path), "text/plain", Convert.ToBase64String(bytes), false));
        }
    }

    [RelayCommand]
    private async Task AddImageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "上传图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.gif",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            var ext  = Path.GetExtension(path).ToLowerInvariant();
            var mime = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", _ => "image/png" };
            var bytes = await File.ReadAllBytesAsync(path);
            Attachments.Add(new AttachmentItem(Path.GetFileName(path), mime, Convert.ToBase64String(bytes), true));
        }
    }

    [RelayCommand]
    private async Task AddVideoAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "上传视频",
            Filter = "视频文件|*.mp4;*.mov;*.avi;*.mkv;*.webm",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            Attachments.Add(new AttachmentItem(Path.GetFileName(path), "video/mp4", Convert.ToBase64String(bytes), false));
        }
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentItem item) => Attachments.Remove(item);

    /// <summary>从 code-behind 调用，处理 Ctrl+V 粘贴图片</summary>
    public void PasteImage(BitmapSource bmp)
    {
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        encoder.Save(ms);
        var b64 = Convert.ToBase64String(ms.ToArray());
        Attachments.Add(new AttachmentItem("粘贴图片.png", "image/png", b64, true));
    }

    // ── 导出聊天记录 ─────────────────────────────────────────────────

    [RelayCommand]
    private void CopyChatText()
    {
        IsExportMenuOpen = false;
        if (Messages.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var m in Messages)
        {
            var role = m.Role == "user" ? "你" : "🦞 ClawBY19";
            sb.AppendLine($"[{m.TimeStr}] {role}");
            sb.AppendLine(m.Content);
            sb.AppendLine();
        }
        Clipboard.SetText(sb.ToString());
        StatusMessage = "✅ 已复制到剪贴板";
        Task.Delay(2000).ContinueWith(_ =>
            Application.Current.Dispatcher.Invoke(() => StatusMessage = string.Empty));
    }

    [RelayCommand]
    private void ExportHtml()
    {
        IsExportMenuOpen = false;
        if (Messages.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出聊天记录",
            Filter = "HTML 文件|*.html",
            FileName = $"ClawBY19_{DateTime.Now:yyyyMMdd_HHmm}.html"
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, BuildHtml(), Encoding.UTF8);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ExportWord()
    {
        IsExportMenuOpen = false;
        if (Messages.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出为 Word",
            Filter = "Word 文档|*.rtf",
            FileName = $"ClawBY19_{DateTime.Now:yyyyMMdd_HHmm}.rtf"
        };
        if (dlg.ShowDialog() != true) return;

        var doc = new FlowDocument { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13 };
        foreach (var m in Messages)
        {
            var roleLabel = m.Role == "user" ? "你" : "🦞 ClawBY19";
            var header = new Paragraph(new Run($"[{m.TimeStr}] {roleLabel}"))
            {
                Margin = new Thickness(0, 8, 0, 2),
                Foreground = m.Role == "user"
                    ? Brushes.SteelBlue
                    : new SolidColorBrush(Color.FromRgb(200, 130, 80))
            };
            header.Inlines.First().SetValue(TextElement.FontWeightProperty, FontWeights.Bold);
            doc.Blocks.Add(header);
            doc.Blocks.Add(new Paragraph(new Run(m.Content)) { Margin = new Thickness(0, 0, 0, 4) });
        }
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        using var fs = new FileStream(dlg.FileName, FileMode.Create);
        range.Save(fs, System.Windows.DataFormats.Rtf);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ExportPdf()
    {
        IsExportMenuOpen = false;
        if (Messages.Count == 0) return;

        var doc = new FlowDocument { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12 };
        foreach (var m in Messages)
        {
            var roleLabel = m.Role == "user" ? "你" : "🦞 ClawBY19";
            var header = new Paragraph(new Run($"[{m.TimeStr}] {roleLabel}"))
            {
                Margin = new Thickness(0, 8, 0, 2),
                FontWeight = FontWeights.Bold,
                Foreground = m.Role == "user" ? Brushes.SteelBlue : Brushes.DarkOrange
            };
            doc.Blocks.Add(header);
            doc.Blocks.Add(new Paragraph(new Run(m.Content)) { Margin = new Thickness(0, 0, 0, 4) });
        }

        var pd = new System.Windows.Controls.PrintDialog();
        if (pd.ShowDialog() != true) return;
        doc.PageWidth  = pd.PrintableAreaWidth;
        doc.PageHeight = pd.PrintableAreaHeight;
        doc.ColumnWidth = pd.PrintableAreaWidth;
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        pd.PrintDocument(paginator, "ClawBY19 聊天记录");
    }

    private string BuildHtml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<title>ClawBY19 聊天记录</title><style>");
        sb.AppendLine("body{font-family:'Microsoft YaHei UI',sans-serif;background:#1a1a2e;color:#e0e0e0;max-width:820px;margin:40px auto;padding:0 20px}");
        sb.AppendLine("h1{color:#4fc3f7;font-size:18px;border-bottom:1px solid #333;padding-bottom:12px}");
        sb.AppendLine(".msg{margin:12px 0;padding:12px 16px;border-radius:10px;line-height:1.7;white-space:pre-wrap;word-break:break-word}");
        sb.AppendLine(".user{background:#1e3a5f;border-left:4px solid #4fc3f7}");
        sb.AppendLine(".assistant{background:#2a1f0e;border-left:4px solid #ffa07a}");
        sb.AppendLine(".system{background:#1a2a1a;border-left:4px solid #66bb6a;font-style:italic}");
        sb.AppendLine(".meta{font-size:11px;color:#888;margin-bottom:4px}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>🦞 ClawBY19 聊天记录 · {DateTime.Now:yyyy-MM-dd HH:mm}</h1>");
        foreach (var m in Messages)
        {
            var cls = m.Role == "user" ? "user" : m.ModelUsed == "system" ? "system" : "assistant";
            var roleLabel = m.Role == "user" ? "你" : "🦞 ClawBY19";
            var escaped = m.Content
                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;");
            sb.AppendLine($"<div class=\"msg {cls}\"><div class=\"meta\">[{m.TimeStr}] {roleLabel}</div>{escaped}</div>");
        }
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ── 模型切换 ────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleModelPicker() => IsModelPickerOpen = !IsModelPickerOpen;

    [RelayCommand]
    private void ToggleExportMenu() => IsExportMenuOpen = !IsExportMenuOpen;

    [RelayCommand]
    private void PickModel(ModelPickerItem? item)
    {
        if (item is null || !item.IsEnabled) return;
        _modelSelector.SetMode(Services.AI.SelectionMode.UserSpecified, item.Id);
        UpdateModelDisplay();
        IsModelPickerOpen = false;
    }

    // ── 昵称 ────────────────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmNickName()
    {
        var name = PendingNickName.Trim();
        if (string.IsNullOrEmpty(name)) name = "主人";
        NickName = name;
        _config.AppSettings.NickName = name;
        _config.SaveAppSettings();
        IsNickNameDialogOpen = false;
        ShowWelcomeMessage();
    }

    // ── 定时任务确认 ────────────────────────────────────────────────

    [RelayCommand]
    private async Task ConfirmScheduledTaskAsync()
    {
        if (_pendingTask is null) return;
        IsTaskConfirmOpen = false;
        await _scheduledTaskService.AddTaskAsync(_pendingTask);
        _pendingTask = null;

        Messages.Add(new ChatMessageItem
        {
            Role = "assistant",
            Content = $"✅ 定时任务已创建：**{PendingTaskDescription}**\n⏰ Cron：`{PendingTaskCron}`\n到时间我会自动执行并把结果告诉你。",
            ModelUsed = "system"
        });
    }

    [RelayCommand]
    private void DismissScheduledTask()
    {
        IsTaskConfirmOpen = false;
        _pendingTask = null;
    }

    // ── 事件处理 ────────────────────────────────────────────────────

    private void OnTokenReceived(object? sender, string token)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsWaitingForResponse = false;
            if (_streamingItem is not null)
            {
                _streamingItem.IsThinking = false;  // 收到第一个 Token，关闭思考动画
                _streamingItem.Content += token;
            }
        });
    }

    private void OnTaskDetected(object? sender, ExtractedTask task)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _pendingTask = task;
            PendingTaskDescription = task.Description;
            PendingTaskCron = task.CronExpr;
            PendingTaskPrompt = task.Prompt;
            IsTaskConfirmOpen = true;
        });
    }

    private void OnScheduledTaskCompleted(object? sender, TaskResultEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Messages.Add(new ChatMessageItem
            {
                Role = "assistant",
                Content = $"⏰ 定时任务执行结果【{e.Description}】：\n\n{e.Result}",
                ModelUsed = "system"
            });
        });
    }

    // ── 内部 ────────────────────────────────────────────────────────

    private void UpdateModelDisplay()
    {
        var mode = _modelSelector.CurrentMode;
        var (modelId, _, _) = _modelSelector.SelectModel("", 0m);
        var info = _registry.Get(modelId);
        SelectionModeLabel = mode == SelectionMode.ClawAuto ? "Claw自选" : "指定模型";
        CurrentModelDisplay = info?.FullName ?? modelId;
        CurrentModelEmoji = (info?.ApiProvider ?? "") switch
        {
            "Anthropic" => "🟠",
            "Google"    => "🔵",
            "OpenAI"    => "🟢",
            "DeepSeek"  => "🔷",
            "Doubao"    => "🟣",
            "Qwen"      => "🟡",
            _           => "🤖"
        };

        // 能力提示
        CurrentModelHasVision              = info?.SupportsVision ?? false;
        CurrentModelHasImageGen            = info?.SupportsImageGeneration ?? false;
        CurrentModelHasVideoUnderstanding  = info?.SupportsVideoUnderstanding ?? false;
        CurrentModelHasVideoGen            = info?.SupportsVideoGeneration ?? false;
    }

    private void ShowWelcomeMessage()
    {
        var (modelId, _, _) = _modelSelector.SelectModel("", 0m);
        var info = _registry.Get(modelId);
        var modelName = info?.FullName ?? modelId;
        var greeting = string.IsNullOrEmpty(NickName)
            ? $"你好！我是 openClaw 智能体 🦞\n\n当前使用 **{modelName}** 协助你完成工作。\n\n我可以帮你：代码同步到 GitHub、发布到云端、AI新闻采集、安全扫描等。\n请输入你的指令，或点击 + 上传文件↓"
            : $"你好，**{NickName}**！我是 openClaw 智能体 🦞\n\n当前使用 **{modelName}** 协助你完成工作。\n\n我可以帮你：代码同步到 GitHub、发布到云端、AI新闻采集、安全扫描等。\n请输入你的指令，或点击 + 上传文件↓";

        Messages.Add(new ChatMessageItem
        {
            Role = "assistant",
            Content = greeting,
            ModelUsed = "system"
        });
    }

    private void BuildPickerModels()
    {
        var categoryLabels = new Dictionary<Services.AI.ModelCategory, string>
        {
            [Services.AI.ModelCategory.ChineseOpenSource] = "🇨🇳 A类",
            [Services.AI.ModelCategory.ChineseCommercial] = "🇨🇳 B类",
            [Services.AI.ModelCategory.UsCommercial]      = "🇺🇸 C类",
            [Services.AI.ModelCategory.LocalOllama]       = "💻 本地",
        };

        var filtered = _registry.All()
            .Where(m => VisibleProviders.Contains(m.ApiProvider))
            .OrderBy(m => Array.IndexOf(VisibleProviders, m.ApiProvider))
            .ThenBy(m => (int)m.Category)
            .ThenBy(m => m.Id);

        foreach (var m in filtered)
        {
            var hasKey = m.IsLocal ||
                !string.IsNullOrEmpty(_config.AppSettings.ApiKeys.GetValueOrDefault(m.ApiProvider, string.Empty));

            PickerModels.Add(new ModelPickerItem
            {
                Id = m.Id,
                FullName = m.FullName,
                Provider = m.Provider,
                CategoryLabel = categoryLabels.GetValueOrDefault(m.Category, ""),
                IsEnabled = hasKey
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 功能一：Git 上传流程
    // ══════════════════════════════════════════════════════════════════

    /// <summary>弹出 Git 上传对话框，用户确认后本地执行 git 命令</summary>
    private async Task RunGitUploadFlowAsync(
        string originalInput,
        ClawBY19.Data.Entities.OperationHistory? last)
    {
        // 预填上次参数
        GitRemoteUrl  = last?.TargetUrl  ?? "https://github.com/用户名/仓库名";
        GitBranch     = "main";
        GitLocalPath  = string.Empty;
        GitCommitMessage = string.Empty;
        _gitToken     = string.Empty;

        _gitUploadTcs = new TaskCompletionSource<bool>();
        IsGitUploadOpen = true;

        var confirmed = await _gitUploadTcs.Task;
        _gitUploadTcs = null;

        if (!confirmed) return;

        // 创建任务记录
        var branch = string.IsNullOrWhiteSpace(GitBranch) ? "main" : GitBranch.Trim();
        var repoName = System.IO.Path.GetFileName(GitRemoteUrl.TrimEnd('/'));
        var taskRecordId = await _taskLog.StartTaskAsync(
            "GitUpload", $"{repoName} → {branch}",
            localPath:     GitLocalPath.Trim(),
            remoteUrl:     GitRemoteUrl.Trim(),
            branch:        branch,
            commitMessage: GitCommitMessage.Trim());

        // 执行 Git 上传
        var progressItem = new ChatMessageItem
        {
            Role = "assistant",
            Content = "⏳ 正在执行 Git 上传，请稍候...",
            ModelUsed = "system"
        };
        Messages.Add(progressItem);

        var progress = new Progress<string>(msg =>
        {
            Application.Current.Dispatcher.Invoke(() =>
                progressItem.Content = $"⏳ {msg}");
        });

        var @params = new GitUploadParams(
            GitLocalPath.Trim(),
            GitRemoteUrl.Trim(),
            branch,
            string.Empty,
            _gitToken,
            GitCommitMessage.Trim());

        GitUploadResult result;
        try
        {
            result = await Task.Run(() => _gitUpload.UploadAsync(@params, progress));
        }
        catch (Exception ex)
        {
            result = new GitUploadResult(false, $"异常：{ex.Message}", string.Empty);
        }

        // 完成任务记录
        await _taskLog.CompleteTaskAsync(taskRecordId, result.Success, result.Details);

        // 失败时异步 AI 分析（不阻塞 UI）
        if (!result.Success)
            _ = _taskLog.AnalyzeFailureAsync(taskRecordId);

        // 保存凭证
        if (!string.IsNullOrEmpty(GitRemoteUrl))
            await _opHistory.SaveAsync(OpType.GitHub,
                System.IO.Path.GetFileName(GitRemoteUrl.TrimEnd('/')),
                GitRemoteUrl.Trim(), string.Empty, _gitToken);
        _gitToken = string.Empty;

        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Remove(progressItem);
            if (result.Success)
                Messages.Add(new ChatMessageItem
                {
                    Role = "assistant",
                    Content = $"✅ {result.Message}\n\n{result.Details}",
                    ModelUsed = "GitUpload"
                });
            else
                Messages.Add(new ChatMessageItem
                {
                    Role = "assistant",
                    Content = $"❌ {result.Message}\n\n{result.Details}\n\n💡 可在「任务 → 操作记录」中查看 AI 失败分析。",
                    ModelUsed = "GitUpload"
                });
        });
    }

    // ── Git 上传对话框命令 ────────────────────────────────────────────

    [RelayCommand]
    private void SubmitGitUpload()
    {
        if (string.IsNullOrWhiteSpace(GitLocalPath) || string.IsNullOrWhiteSpace(GitRemoteUrl))
            return;
        IsGitUploadOpen = false;
        _gitUploadTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void CancelGitUpload()
    {
        IsGitUploadOpen = false;
        _gitToken = string.Empty;
        _gitUploadTcs?.TrySetResult(false);
    }

    // ══════════════════════════════════════════════════════════════════
    // 功能二：云端部署流程
    // ══════════════════════════════════════════════════════════════════

    /// <summary>调用 CloudDeployService 生成部署方案，流式输出到聊天</summary>
    private async Task RunCloudDeployFlowAsync(CloudDeployParams cloudParams)
    {
        // 创建任务记录
        var summary = string.IsNullOrEmpty(cloudParams.ServerIp)
            ? cloudParams.PlatformName
            : $"{cloudParams.PlatformName} → {cloudParams.ServerIp}";
        var taskRecordId = await _taskLog.StartTaskAsync(
            "CloudDeploy", summary,
            serverIp:   cloudParams.ServerIp,
            deployPath: cloudParams.DeployPath,
            remoteUrl:  cloudParams.GitRepoUrl);

        var deployItem = new ChatMessageItem
        {
            Role = "assistant",
            Content = "",
            ModelUsed = "CloudDeploy",
            IsThinking = true
        };
        Messages.Add(deployItem);
        IsStreaming = true;
        IsWaitingForResponse = true;
        StatusMessage = "☁️ AI 正在生成云端部署方案...";
        _cts = new CancellationTokenSource();

        _cloudDeploy.TokenReceived += OnCloudDeployToken;
        void OnCloudDeployToken(object? s, string token)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsWaitingForResponse = false;
                deployItem.IsThinking = false;
                deployItem.Content += token;
            });
        }

        bool cancelled = false;
        try
        {
            await _cloudDeploy.GenerateDeployPlanAsync(cloudParams, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            deployItem.Content += "\n\n⚠️ 已取消";
            cancelled = true;
        }
        finally
        {
            _cloudDeploy.TokenReceived -= OnCloudDeployToken;
            IsStreaming = false;
            IsWaitingForResponse = false;
            StatusMessage = string.Empty;
            _cts?.Dispose();
            _cts = null;

            // 完成任务记录（方案生成即为成功）
            if (!cancelled)
                await _taskLog.CompleteTaskAsync(
                    taskRecordId, success: true,
                    $"✅ AI方案已生成\n{deployItem.Content[..Math.Min(500, deployItem.Content.Length)]}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 功能三：自检测试流程
    // ══════════════════════════════════════════════════════════════════

    /// <summary>弹出测试对话框，用户选择类型和目标后执行测试</summary>
    private async Task RunTestFlowAsync(string originalInput)
    {
        TestType = "Web";
        TestTarget = string.Empty;
        TestLoginUrl = string.Empty;
        TestUsername = string.Empty;
        _testPassword = string.Empty;

        _testDialogTcs = new TaskCompletionSource<bool>();
        IsTestDialogOpen = true;

        var confirmed = await _testDialogTcs.Task;
        _testDialogTcs = null;

        if (!confirmed) return;

        var progressItem = new ChatMessageItem
        {
            Role = "assistant",
            Content = "⏳ 正在启动自动化测试...",
            ModelUsed = "system"
        };
        Messages.Add(progressItem);

        _testService.ProgressChanged += OnTestProgress;
        void OnTestProgress(object? s, TestProgressEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
                progressItem.Content = $"⏳ [{e.Current}/{(e.Total > 0 ? e.Total.ToString() : "?")}] {e.Message}");
        }

        _cts = new CancellationTokenSource();
        string resultText;
        try
        {
            if (TestType == "Web")
                resultText = await _testService.TestWebAppAsync(
                    TestTarget, TestLoginUrl, TestUsername, _testPassword, _cts.Token);
            else
                resultText = await _testService.TestExeAppAsync(TestTarget, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            resultText = "⚠️ 测试已取消";
        }
        catch (Exception ex)
        {
            resultText = $"❌ 测试异常：{ex.Message}";
        }
        finally
        {
            _testService.ProgressChanged -= OnTestProgress;
            _cts?.Dispose();
            _cts = null;
        }
        _testPassword = string.Empty;

        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Remove(progressItem);
            Messages.Add(new ChatMessageItem
            {
                Role = "assistant",
                Content = resultText,
                ModelUsed = "AutoTest"
            });
        });
    }

    // ── 测试对话框命令 ────────────────────────────────────────────────

    [RelayCommand]
    private void SubmitTestDialog()
    {
        if (string.IsNullOrWhiteSpace(TestTarget)) return;
        IsTestDialogOpen = false;
        _testDialogTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void CancelTestDialog()
    {
        IsTestDialogOpen = false;
        _testPassword = string.Empty;
        _testDialogTcs?.TrySetResult(false);
    }

    [RelayCommand]
    private void SelectExePath()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择要测试的 EXE 程序",
            Filter = "可执行文件|*.exe"
        };
        if (dlg.ShowDialog() == true)
            TestTarget = dlg.FileName;
    }

    [RelayCommand]
    private void SelectLocalPath()
    {
        // 用 OpenFileDialog 的目录模式（选择目录内任意文件，取其目录）
        var dlg = new OpenFileDialog
        {
            Title = "选择本地源代码目录（点击任意文件，取其所在目录）",
            Filter = "所有文件|*.*",
            CheckFileExists = false,
            FileName = "（选择此目录）"
        };
        if (dlg.ShowDialog() == true)
            GitLocalPath = System.IO.Path.GetDirectoryName(dlg.FileName) ?? GitLocalPath;
    }
}
