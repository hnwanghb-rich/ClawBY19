using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClawBY19.Data.Entities;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.AI;

public interface IAiClient
{
    IAsyncEnumerable<string> StreamAsync(
        string baseUrl, string apiModelId, string apiKey,
        IReadOnlyList<ApiMessage> messages,
        double temperature = 0.7,
        CancellationToken ct = default);

    Task<(string Content, int InputTokens, int OutputTokens)> CompleteAsync(
        string baseUrl, string apiModelId, string apiKey,
        IReadOnlyList<ApiMessage> messages,
        double temperature = 0.7,
        CancellationToken ct = default);

    /// <summary>图像生成，返回 Base64 图片数据；失败返回 null</summary>
    Task<string?> GenerateImageAsync(
        string baseUrl, string apiModelId, string apiKey,
        string prompt, string size = "1024x1024",
        CancellationToken ct = default);
}

/// <summary>消息内容：纯文本或多模态（文本+图片）</summary>
public record ApiMessage(string Role, object Content);

/// <summary>图片附件，Base64 编码</summary>
public record ApiImagePart(string Base64Data, string MimeType);

public class AiClient : IAiClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };
    private readonly ILogger<AiClient> _logger;
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public AiClient(ILogger<AiClient> logger) => _logger = logger;

    /// <summary>流式输出，逐 Token yield</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string baseUrl, string apiModelId, string apiKey,
        IReadOnlyList<ApiMessage> messages,
        double temperature = 0.7,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildBody(apiModelId, messages, stream: true, temperature);
        var req = BuildRequest(baseUrl, apiKey, body);

        HttpResponseMessage resp;
        string? earlyError = null;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (TaskCanceledException)
        {
            earlyError = "\n\n⚠️ 请求超时，请检查网络连接。";
            resp = null!;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI API 网络错误 {Url}", baseUrl);
            earlyError = $"\n\n❌ 网络错误：{ex.Message}";
            resp = null!;
        }

        if (earlyError is not null) { yield return earlyError; yield break; }

        if (!resp.IsSuccessStatusCode)
        {
            var errBody = string.Empty;
            try { errBody = await resp.Content.ReadAsStringAsync(ct); } catch { }
            _logger.LogError("AI API 返回错误 {Status} {Url}: {Body}", (int)resp.StatusCode, baseUrl, errBody);

            var hint = (int)resp.StatusCode switch
            {
                401 => "API Key 无效或未配置，请在设置页填写正确的 Key。",
                403 => "API Key 权限不足。",
                429 => "请求过于频繁，请稍后重试。",
                404 => "API 地址错误，请检查 Base URL 配置。",
                _   => errBody.Length > 0 ? errBody[..Math.Min(200, errBody.Length)] : $"HTTP {(int)resp.StatusCode}"
            };
            yield return $"\n\n❌ {hint}";
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..].Trim();
            if (data == "[DONE]") break;

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                delta = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta")
                    .TryGetProperty("content", out var c) ? c.GetString() : null;
            }
            catch { /* 跳过解析错误的行 */ }

            if (delta is not null) yield return delta;
        }
    }

    /// <summary>非流式完整返回，同时拿到 Token 数</summary>
    public async Task<(string Content, int InputTokens, int OutputTokens)> CompleteAsync(
        string baseUrl, string apiModelId, string apiKey,
        IReadOnlyList<ApiMessage> messages,
        double temperature = 0.7,
        CancellationToken ct = default)
    {
        var body = BuildBody(apiModelId, messages, stream: false, temperature);
        var req = BuildRequest(baseUrl, apiKey, body);

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var content = root.GetProperty("choices")[0]
                              .GetProperty("message")
                              .GetProperty("content")
                              .GetString() ?? string.Empty;
            var inputTokens = 0;
            var outputTokens = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                inputTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                outputTokens = usage.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : 0;
            }
            return (content, inputTokens, outputTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI API Complete 失败");
            return (string.Empty, 0, 0);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="Exception">API 请求失败时抛出，包含实际错误信息</exception>
    public async Task<string?> GenerateImageAsync(
        string baseUrl, string apiModelId, string apiKey,
        string prompt, string size = "1024x1024",
        CancellationToken ct = default)
    {
        var url = baseUrl.TrimEnd('/') + "/images/generations";
        // 不传 response_format，让 Volcengine 返回默认的 url 格式
        var body = JsonSerializer.Serialize(new
        {
            model = apiModelId,
            prompt,
            size
        });

        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var responseBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("图像生成 API 错误 {Status}: {Body}", (int)resp.StatusCode, responseBody);
            // 抛出异常，让调用方展示真实错误给用户
            throw new Exception($"HTTP {(int)resp.StatusCode}：{responseBody[..Math.Min(400, responseBody.Length)]}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
        {
            var first = data[0];

            // 优先取 b64_json（如果有）
            if (first.TryGetProperty("b64_json", out var b64) &&
                b64.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(b64.GetString()))
                return b64.GetString();

            // 从 url 下载
            if (first.TryGetProperty("url", out var urlProp) &&
                urlProp.ValueKind == JsonValueKind.String)
            {
                var imgUrl = urlProp.GetString()!;
                var bytes = await _http.GetByteArrayAsync(imgUrl, ct);
                return Convert.ToBase64String(bytes);
            }
        }

        throw new Exception($"图像生成响应格式不符：{responseBody[..Math.Min(300, responseBody.Length)]}");
    }

    // ── 内部 ──────────────────────────────────────────────────────────

    /// <summary>序列化请求体，自动处理纯文本和多模态两种 Content 格式</summary>
    private static string BuildBody(string model, IReadOnlyList<ApiMessage> messages, bool stream, double temperature)
    {
        var msgArray = messages.Select(m => new
        {
            role = m.Role,
            content = SerializeContent(m.Content)
        }).ToArray();

        // 手动构造 JSON 以便精确控制 content 类型（string vs array）
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteString("model", model);
        writer.WriteBoolean("stream", stream);
        writer.WriteNumber("temperature", temperature);

        writer.WritePropertyName("messages");
        writer.WriteStartArray();
        foreach (var m in messages)
        {
            writer.WriteStartObject();
            writer.WriteString("role", m.Role);

            if (m.Content is string textOnly)
            {
                writer.WriteString("content", textOnly);
            }
            else if (m.Content is List<object> parts)
            {
                writer.WritePropertyName("content");
                writer.WriteStartArray();
                foreach (var part in parts)
                {
                    if (part is string txt)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "text");
                        writer.WriteString("text", txt);
                        writer.WriteEndObject();
                    }
                    else if (part is ApiImagePart img)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "image_url");
                        writer.WritePropertyName("image_url");
                        writer.WriteStartObject();
                        writer.WriteString("url", $"data:{img.MimeType};base64,{img.Base64Data}");
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static object SerializeContent(object content) => content; // placeholder

    private static HttpRequestMessage BuildRequest(string baseUrl, string apiKey, string body)
    {
        var url = baseUrl.TrimEnd('/') + "/chat/completions";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return req;
    }
}
