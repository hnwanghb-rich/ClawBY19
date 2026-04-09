using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ClawBY19.Data.Entities;
using ClawBY19.Services.AI;
using ClawBY19.Services.Cost;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Chat;

/// <summary>附件项（文件或粘贴图片）</summary>
public record AttachmentItem(
    string DisplayName,
    string MimeType,
    string Base64Data,
    bool IsImage);

/// <summary>从聊天中提取到的定时任务</summary>
public record ExtractedTask(string Description, string CronExpr, string Prompt);

/// <summary>
/// 直接对话服务：维护会话历史、流式调用 IAiClient、
/// 检测定时任务关键词后向 AI 提取结构化任务。
/// </summary>
public class ChatService
{
    private readonly IAiClient _aiClient;
    private readonly CostTracker _costTracker;
    private readonly ILogger<ChatService> _logger;

    // 对话历史（内存，会话级）
    private readonly List<ApiMessage> _history = new();

    // 系统提示
    private const string SystemPrompt = """
        你是 openClaw 智能体 🦞，由 ClawBY19 驱动，具备任务执行、长期记忆和自我进化能力。

        核心能力：
        - 代码管理与 GitHub 自动同步（git add / commit / push）
        - 云端部署与发布（SSH、FTP、Docker、云平台）
        - AI 新闻采集、HTML 汇总、定时邮件发送
        - 系统与代码安全监测、漏洞扫描、告警

        行为规则：
        - 每个任务必须：制定计划 → 执行 → 验收 → 学习总结
        - 成功总结经验、失败分析根因、同类问题不再犯错
        - 所有进化写入日志，可回溯；敏感操作须用户确认后执行
        - 安全第一：不越权、不破坏、不泄露用户数据

        输出格式（每次任务响应）：
        **任务理解** → **计划** → **执行** → **结果** → **学习总结** → **建议**

        当用户消息中包含操作上下文（如 [操作上下文] 标记），请直接使用其中的仓库/平台信息执行操作，
        无需再次询问目标地址。
        """;

    // 定时任务关键词
    private static readonly string[] _scheduleKeywords =
        ["每天", "每周", "每月", "每小时", "每分钟", "定时", "定期", "cron",
         "早上", "晚上", "早8点", "9点", "凌晨", "隔天", "每隔"];

    public event EventHandler<string>? TokenReceived;
    public event EventHandler<ExtractedTask>? TaskDetected;

    public ChatService(IAiClient aiClient, CostTracker costTracker, ILogger<ChatService> logger)
    {
        _aiClient = aiClient;
        _costTracker = costTracker;
        _logger = logger;
        _history.Add(new ApiMessage("system", SystemPrompt));
    }

    /// <summary>发送消息（含附件），流式回复通过 TokenReceived 事件推送</summary>
    public async Task<string> SendAsync(
        string userText,
        IReadOnlyList<AttachmentItem> attachments,
        ModelInfo model,
        string apiKey,
        CancellationToken ct = default)
    {
        // 组装 multimodal content
        var content = BuildContent(userText, attachments);
        _history.Add(new ApiMessage("user", content));

        // 估算输入 token 数（用历史消息总字符 ÷ 4）
        var inputTokenEst = EstimateTokens(_history);

        var sb = new StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ttftMs = 0;
        var status = "success";
        string? errorMsg = null;

        try
        {
            await foreach (var token in _aiClient.StreamAsync(
                model.BaseUrl, model.ApiModelId, apiKey, _history, ct: ct))
            {
                if (ttftMs == 0) ttftMs = (int)sw.ElapsedMilliseconds;
                sb.Append(token);
                TokenReceived?.Invoke(this, token);
            }
        }
        catch (OperationCanceledException)
        {
            status = "error";
            errorMsg = "用户取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChatService.SendAsync 出错");
            status = "error";
            errorMsg = ex.Message;
            var errMsg = $"\n\n❌ 请求失败：{ex.Message}";
            sb.Append(errMsg);
            TokenReceived?.Invoke(this, errMsg);
        }

        sw.Stop();
        var reply = sb.ToString();
        if (!string.IsNullOrEmpty(reply))
            _history.Add(new ApiMessage("assistant", reply));

        // 记录 token 用量（输出 token 估算：字符 ÷ 2，中文比例高）
        var outputTokenEst = Math.Max(1, reply.Length / 2);
        _ = _costTracker.RecordAsync(new ApiUsageLog
        {
            Model        = model.ApiModelId,
            Provider     = model.ApiProvider,
            TokensInput  = inputTokenEst,
            TokensOutput = outputTokenEst,
            LatencyMs    = (int)sw.ElapsedMilliseconds,
            TtftMs       = ttftMs,
            Status       = status,
            ErrorMessage = errorMsg
        });

        // 检测定时任务（异步，不阻塞流式返回）
        _ = TryDetectScheduledTaskAsync(userText, model, apiKey);

        return reply;
    }

    /// <summary>清空历史（新会话）</summary>
    public void ClearHistory()
    {
        _history.Clear();
        _history.Add(new ApiMessage("system", SystemPrompt));
    }

    /// <summary>调用图像生成 API，返回 Base64 图片数据；失败返回 null</summary>
    public Task<string?> GenerateImageAsync(
        ModelInfo model, string apiKey, string prompt,
        string size = "1024x1024", CancellationToken ct = default)
        => _aiClient.GenerateImageAsync(model.BaseUrl, model.ApiModelId, apiKey, prompt, size, ct);

    // ── 内部 ────────────────────────────────────────────────────────

    /// <summary>估算消息列表的输入 token 数（字符数 ÷ 2，中文约2字符=1token）</summary>
    private static int EstimateTokens(IEnumerable<ApiMessage> messages)
    {
        var chars = messages.Sum(m => m.Content is string s ? s.Length : 50);
        return Math.Max(1, chars / 2);
    }

    /// <summary>构造 OpenAI multimodal content：纯文本用 string，有图片用 List</summary>
    private static object BuildContent(string text, IReadOnlyList<AttachmentItem> attachments)
    {
        if (attachments.Count == 0)
            return text;

        var parts = new List<object> { text };
        foreach (var a in attachments)
        {
            if (a.IsImage)
                parts.Add(new ApiImagePart(a.Base64Data, a.MimeType));
            else
                // 文本文件：直接追加到 text 部分
                parts[0] = (string)parts[0] + $"\n\n📎 [{a.DisplayName}]\n{DecodeText(a.Base64Data)}";
        }

        // 如果只有文本文件附件，还是返回 string
        if (parts.Count == 1 && parts[0] is string s)
            return s;

        return parts;
    }

    private static string DecodeText(string base64)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
        catch { return "[无法解码文件内容]"; }
    }

    /// <summary>检测最近对话是否提到定时安排，有则向 AI 提取结构化 JSON</summary>
    private async Task TryDetectScheduledTaskAsync(string userText, ModelInfo model, string apiKey)
    {
        // 关键词匹配
        if (!_scheduleKeywords.Any(k => userText.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            // 取最近几轮对话作为上下文
            var recentContext = string.Join("\n", _history
                .Where(m => m.Role != "system")
                .TakeLast(6)
                .Select(m => $"{m.Role}: {m.Content}"));

            var extractMessages = new List<ApiMessage>
            {
                new("system", """
                    你是一个任务解析助手。根据用户对话内容，提取定时任务安排。
                    严格只输出 JSON，格式：
                    {"description":"任务描述","cron":"cron表达式","prompt":"到时间要发给AI的指令"}
                    cron 格式：分 时 日 月 周（标准5字段），例如每天9点 = "0 9 * * *"
                    如果对话中没有明确的定时安排，输出：{"none":true}
                    """),
                new("user", $"以下是用户对话内容：\n{recentContext}\n\n请提取定时任务（只输出JSON）：")
            };

            var (json, _, _) = await _aiClient.CompleteAsync(
                model.BaseUrl, model.ApiModelId, apiKey, extractMessages, temperature: 0.1);

            // 解析 JSON
            var trimmed = json.Trim().TrimStart('`').TrimEnd('`');
            if (trimmed.StartsWith("json")) trimmed = trimmed[4..].Trim();

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.TryGetProperty("none", out _)) return;

            var desc = root.GetProperty("description").GetString() ?? string.Empty;
            var cron = root.GetProperty("cron").GetString() ?? string.Empty;
            var prompt = root.GetProperty("prompt").GetString() ?? string.Empty;

            if (!string.IsNullOrEmpty(desc) && !string.IsNullOrEmpty(cron))
                TaskDetected?.Invoke(this, new ExtractedTask(desc, cron, prompt));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定时任务提取失败（忽略）");
        }
    }
}
