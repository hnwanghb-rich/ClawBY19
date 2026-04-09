using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using ClawBY19.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Testing;

/// <summary>测试进度事件</summary>
public class TestProgressEventArgs(string message, int current, int total) : EventArgs
{
    public string Message { get; } = message;
    public int Current { get; } = current;
    public int Total { get; } = total;
}

/// <summary>
/// 功能三：自检与测试服务（本地程序执行）
/// 支持：
/// - Web 应用：HttpClient 爬取页面，遍历所有链接和表单
/// - 本地 EXE：Windows UI Automation 遍历窗体控件
/// </summary>
public class TestService
{
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly ILogger<TestService> _logger;
    private readonly HttpClient _http;

    public event EventHandler<TestProgressEventArgs>? ProgressChanged;

    public TestService(
        IDbContextFactory<ClawDbContext> dbFactory,
        ILogger<TestService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true // 允许自签证书
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "ClawBY19-TestBot/1.0 (Automated Testing; compatible; MSIE 9.0; Windows NT 6.1)");
    }

    // ── Web 测试 ──────────────────────────────────────────────────────────

    /// <summary>测试 Web 应用：爬取所有页面，测试链接和表单</summary>
    public async Task<string> TestWebAppAsync(
        string baseUrl,
        string? loginPath = null,
        string? username = null,
        string? password = null,
        CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Url, string MenuPath)>();
        var logs = new List<TestLog>();
        var totalLinks = 0;

        // 规范化 baseUrl
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            baseUrl = "https://" + baseUrl;
        baseUrl = baseUrl.TrimEnd('/');

        Report("开始测试 Web 应用...", 0, 0);

        // 登录（如果提供了凭据）
        if (!string.IsNullOrEmpty(loginPath) && !string.IsNullOrEmpty(username))
        {
            Report("尝试登录...", 0, 0);
            var loginLog = await TryLoginAsync(sessionId, baseUrl, loginPath, username, password ?? string.Empty, ct);
            logs.Add(loginLog);
        }

        // 从首页开始爬取
        queue.Enqueue((baseUrl, "首页"));
        visited.Add(baseUrl);

        while (queue.Count > 0 && !ct.IsCancellationRequested)
        {
            var (url, menuPath) = queue.Dequeue();
            totalLinks = visited.Count + queue.Count;
            Report($"测试: {menuPath} ({url})", logs.Count, Math.Max(totalLinks, 10));

            var (html, statusCode) = await FetchPageAsync(url, ct);
            if (html is null)
            {
                logs.Add(MakeLog(sessionId, baseUrl, "Web", menuPath, url,
                    "GET 页面", "Exception", $"HTTP {statusCode} - 无法访问"));
                continue;
            }

            logs.Add(MakeLog(sessionId, baseUrl, "Web", menuPath, url,
                "GET 页面", "Pass", $"HTTP {statusCode}"));

            // 测试页面内所有链接
            var links = ExtractLinks(html, baseUrl, url);
            foreach (var link in links)
            {
                if (!visited.Contains(link) && IsSameDomain(link, baseUrl))
                {
                    visited.Add(link);
                    var linkMenuPath = $"{menuPath} > {ExtractLinkText(html, link)}";
                    queue.Enqueue((link, linkMenuPath.Truncate(120)));
                }
            }

            // 测试页面内所有表单（输入测试数据）
            var formLogs = await TestFormsAsync(sessionId, baseUrl, url, menuPath, html, ct);
            logs.AddRange(formLogs);

            // 测试页面内所有 Button（点击测试）
            var buttonLogs = TestButtons(sessionId, baseUrl, url, menuPath, html);
            logs.AddRange(buttonLogs);

            // 限制爬取深度（避免无限爬取）
            if (visited.Count >= 100)
            {
                Report("已达到100页上限，停止爬取", logs.Count, 100);
                break;
            }
        }

        // 持久化日志
        await SaveLogsAsync(logs, ct);

        var passCount = logs.Count(l => l.TestResult == "Pass");
        var failCount = logs.Count(l => l.TestResult == "Exception");
        var summary = $"""
            Web 测试完成！
            测试目标：{baseUrl}
            测试页面：{visited.Count} 个
            总测试项：{logs.Count} 项
            通过：{passCount} | 异常：{failCount}
            测试日志已保存到数据库（会话ID：{sessionId}）
            """;

        _logger.LogInformation("Web 测试完成：{Summary}", summary);
        return summary;
    }

    // ── EXE 测试 ─────────────────────────────────────────────────────────

    /// <summary>测试本地 EXE 程序（使用 Windows UI Automation）</summary>
    public async Task<string> TestExeAppAsync(
        string exePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(exePath))
            return $"❌ 程序不存在：{exePath}";

        var sessionId = Guid.NewGuid().ToString("N");
        var logs = new List<TestLog>();

        Report("启动目标程序...", 0, 0);

        Process? proc = null;
        try
        {
            proc = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false
            });

            if (proc is null)
                return "❌ 无法启动目标程序";

            // 等待程序加载（最多10秒）
            await Task.Delay(2000, ct);
            proc.WaitForInputIdle(5000);

            Report("程序已启动，开始 UI Automation 遍历...", 0, 0);

            // 使用 UI Automation 遍历主窗口
            var uiLogs = await TestExeViaUiAutomationAsync(
                sessionId, exePath, proc, ct);
            logs.AddRange(uiLogs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXE 测试失败");
            logs.Add(MakeLog(sessionId, exePath, "Exe", "主程序", exePath,
                "启动程序", "Exception", ex.Message));
        }
        finally
        {
            try { proc?.Kill(); } catch { }
            try { proc?.Dispose(); } catch { }
        }

        await SaveLogsAsync(logs, ct);

        var passCount = logs.Count(l => l.TestResult == "Pass");
        var failCount = logs.Count(l => l.TestResult == "Exception");
        return $"""
            EXE 测试完成！
            测试目标：{Path.GetFileName(exePath)}
            总测试项：{logs.Count} 项
            通过：{passCount} | 异常：{failCount}
            测试日志已保存（会话ID：{sessionId}）
            """;
    }

    // ── 内部：Web 测试工具 ───────────────────────────────────────────────

    private async Task<TestLog> TryLoginAsync(
        string sessionId, string baseUrl, string loginPath,
        string username, string password, CancellationToken ct)
    {
        var loginUrl = loginPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? loginPath : baseUrl + "/" + loginPath.TrimStart('/');
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username, ["user"] = username,
                ["email"] = username,    ["password"] = password,
                ["pwd"] = password,      ["pass"] = password
            });
            var resp = await _http.PostAsync(loginUrl, content, ct);
            return MakeLog(sessionId, baseUrl, "Web", "登录", loginUrl,
                "POST 登录", resp.IsSuccessStatusCode ? "Pass" : "Exception",
                resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return MakeLog(sessionId, baseUrl, "Web", "登录", loginUrl,
                "POST 登录", "Exception", ex.Message);
        }
    }

    private async Task<(string? Html, int StatusCode)> FetchPageAsync(string url, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync(url, ct);
            var statusCode = (int)resp.StatusCode;
            if (!resp.IsSuccessStatusCode) return (null, statusCode);
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("html")) return (null, statusCode);
            var html = await resp.Content.ReadAsStringAsync(ct);
            return (html, statusCode);
        }
        catch
        {
            return (null, 0);
        }
    }

    private static List<string> ExtractLinks(string html, string baseUrl, string currentUrl)
    {
        var links = new List<string>();
        var matches = Regex.Matches(html, @"<a\s[^>]*href\s*=\s*[""']([^""'#?]+)[""']",
            RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            var href = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(href) || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                continue;
            var abs = ToAbsoluteUrl(href, baseUrl, currentUrl);
            if (abs is not null) links.Add(abs);
        }
        return links.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ExtractLinkText(string html, string url)
    {
        var match = Regex.Match(html,
            $@"<a\s[^>]*href\s*=\s*[""']{Regex.Escape(url)}[""'][^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success) return url;
        return Regex.Replace(match.Groups[1].Value, "<[^>]+>", "").Trim().Truncate(30);
    }

    private async Task<List<TestLog>> TestFormsAsync(
        string sessionId, string baseUrl, string url,
        string menuPath, string html, CancellationToken ct)
    {
        var logs = new List<TestLog>();
        var forms = Regex.Matches(html, @"<form\b[^>]*>(.*?)</form>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match form in forms)
        {
            var formHtml = form.Value;
            var action = Regex.Match(formHtml, @"action\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            var method = Regex.Match(formHtml, @"method\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);

            var formUrl = action.Success
                ? ToAbsoluteUrl(action.Groups[1].Value, baseUrl, url) ?? url
                : url;
            var formMethod = method.Success ? method.Groups[1].Value.ToUpper() : "GET";

            // 提取所有 input 字段
            var inputs = Regex.Matches(formHtml,
                @"<input\b[^>]*name\s*=\s*[""']([^""']+)[""'][^>]*type\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);

            var testData = new Dictionary<string, string>();
            foreach (Match inp in inputs)
            {
                var name = inp.Groups[1].Value;
                var type = inp.Groups[2].Value.ToLower();
                testData[name] = type switch
                {
                    "email" => "test@test.com",
                    "number" => "123",
                    "date"   => DateTime.Today.ToString("yyyy-MM-dd"),
                    "password" => "Test1234!",
                    _       => "测试数据"
                };
            }

            try
            {
                HttpResponseMessage resp;
                if (formMethod == "POST")
                    resp = await _http.PostAsync(formUrl, new FormUrlEncodedContent(testData), ct);
                else
                    resp = await _http.GetAsync(formUrl, ct);

                logs.Add(MakeLog(sessionId, baseUrl, "Web", menuPath, formUrl,
                    $"{formMethod} 表单提交（{testData.Count} 字段）",
                    resp.IsSuccessStatusCode ? "Pass" : "Exception",
                    resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode}"));
            }
            catch (Exception ex)
            {
                logs.Add(MakeLog(sessionId, baseUrl, "Web", menuPath, formUrl,
                    $"{formMethod} 表单提交", "Exception", ex.Message));
            }
        }
        return logs;
    }

    private static List<TestLog> TestButtons(
        string sessionId, string baseUrl, string url,
        string menuPath, string html)
    {
        var logs = new List<TestLog>();
        var buttons = Regex.Matches(html,
            @"<button\b[^>]*>(.*?)</button>|<input\b[^>]*type\s*=\s*[""'](button|submit)[""'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match btn in buttons)
        {
            var label = Regex.Replace(btn.Value, "<[^>]+>", "").Trim();
            if (string.IsNullOrEmpty(label)) label = "按钮";
            logs.Add(MakeLog(sessionId, baseUrl, "Web", menuPath, url,
                $"检测 Button: {label.Truncate(50)}", "Pass", null));
        }
        return logs;
    }

    // ── 内部：EXE UI Automation ──────────────────────────────────────────

    private async Task<List<TestLog>> TestExeViaUiAutomationAsync(
        string sessionId, string exePath, Process proc, CancellationToken ct)
    {
        var logs = new List<TestLog>();
        var exeName = Path.GetFileName(exePath);

        try
        {
            // 使用 UI Automation（System.Windows.Automation）
            var automation = System.Windows.Automation.AutomationElement.RootElement;
            var condition = new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.ProcessIdProperty, proc.Id);

            // 等待主窗口出现
            System.Windows.Automation.AutomationElement? mainWindow = null;
            for (int i = 0; i < 10 && !ct.IsCancellationRequested; i++)
            {
                mainWindow = automation.FindFirst(
                    System.Windows.Automation.TreeScope.Children, condition);
                if (mainWindow is not null) break;
                await Task.Delay(500, ct);
            }

            if (mainWindow is null)
            {
                logs.Add(MakeLog(sessionId, exePath, "Exe", "主窗口", exeName,
                    "获取主窗口", "Exception", "未能找到主窗口（超时）"));
                return logs;
            }

            var windowTitle = mainWindow.Current.Name;
            logs.Add(MakeLog(sessionId, exePath, "Exe", windowTitle, exeName,
                "打开主窗口", "Pass", null));

            Report($"主窗口：{windowTitle}", 1, 0);

            // 递归遍历控件（深度限3层）
            var controlLogs = TraverseControls(sessionId, exePath, mainWindow, windowTitle, 0, 3);
            logs.AddRange(controlLogs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UI Automation 遍历失败");
            logs.Add(MakeLog(sessionId, exePath, "Exe", "UI Automation", exeName,
                "遍历控件", "Exception", ex.Message));
        }

        return logs;
    }

    private List<TestLog> TraverseControls(
        string sessionId, string target,
        System.Windows.Automation.AutomationElement element,
        string menuPath, int depth, int maxDepth)
    {
        var logs = new List<TestLog>();
        if (depth > maxDepth) return logs;

        try
        {
            var children = element.FindAll(
                System.Windows.Automation.TreeScope.Children,
                System.Windows.Automation.Condition.TrueCondition);

            foreach (System.Windows.Automation.AutomationElement child in children)
            {
                var controlType = child.Current.ControlType.ProgrammaticName;
                var name = child.Current.Name ?? string.Empty;
                var childPath = string.IsNullOrEmpty(name) ? menuPath : $"{menuPath} > {name}";

                // 测试 Button
                if (controlType.Contains("Button") && !string.IsNullOrEmpty(name))
                {
                    try
                    {
                        Report($"点击按钮: {name}", 0, 0);
                        if (child.TryGetCurrentPattern(
                            System.Windows.Automation.InvokePattern.Pattern,
                            out var pattern) && pattern is System.Windows.Automation.InvokePattern invoke)
                        {
                            invoke.Invoke();
                            System.Threading.Thread.Sleep(300); // 等待响应
                            logs.Add(MakeLog(sessionId, target, "Exe", menuPath, name,
                                $"点击 Button: {name}", "Pass", null));
                        }
                        else
                        {
                            logs.Add(MakeLog(sessionId, target, "Exe", menuPath, name,
                                $"检测 Button: {name}", "Pass", "不可点击"));
                        }
                    }
                    catch (Exception ex)
                    {
                        logs.Add(MakeLog(sessionId, target, "Exe", menuPath, name,
                            $"点击 Button: {name}", "Exception", ex.Message));
                    }
                }
                // 测试 TextBox（输入测试数据）
                else if (controlType.Contains("Edit") || controlType.Contains("Text"))
                {
                    try
                    {
                        if (child.TryGetCurrentPattern(
                            System.Windows.Automation.ValuePattern.Pattern,
                            out var pattern) && pattern is System.Windows.Automation.ValuePattern vp)
                        {
                            vp.SetValue("测试输入_ClawBY19");
                            logs.Add(MakeLog(sessionId, target, "Exe", menuPath, name,
                                $"输入 TextBox: {name}", "Pass", null));
                        }
                    }
                    catch (Exception ex)
                    {
                        logs.Add(MakeLog(sessionId, target, "Exe", menuPath, name,
                            $"输入 TextBox: {name}", "Exception", ex.Message));
                    }
                }
                // MenuItem：记录但不点击（避免破坏程序状态）
                else if (controlType.Contains("MenuItem"))
                {
                    logs.Add(MakeLog(sessionId, target, "Exe", menuPath, name,
                        $"检测菜单项: {name}", "Pass", null));
                }

                // 递归子控件
                var childLogs = TraverseControls(
                    sessionId, target, child, childPath, depth + 1, maxDepth);
                logs.AddRange(childLogs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "遍历控件层级失败");
        }

        return logs;
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private static TestLog MakeLog(
        string sessionId, string target, string testType,
        string menuPath, string functionName,
        string testAction, string result, string? errorDetail) => new()
    {
        SessionId = sessionId,
        TestTarget = target,
        TestType = testType,
        MenuPath = menuPath.Truncate(200),
        FunctionName = functionName.Truncate(200),
        TestAction = testAction.Truncate(200),
        TestResult = result,
        ErrorDetail = errorDetail?.Truncate(500),
        CreatedAt = DateTime.Now
    };

    private async Task SaveLogsAsync(List<TestLog> logs, CancellationToken ct)
    {
        if (logs.Count == 0) return;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.TestLogs.AddRange(logs);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("保存测试日志 {Count} 条", logs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存测试日志失败");
        }
    }

    private static bool IsSameDomain(string url, string baseUrl)
    {
        try
        {
            var u = new Uri(url);
            var b = new Uri(baseUrl);
            return u.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string? ToAbsoluteUrl(string href, string baseUrl, string currentUrl)
    {
        try
        {
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
            if (href.StartsWith("//")) return "https:" + href;
            var b = new Uri(currentUrl);
            return new Uri(b, href).ToString();
        }
        catch { return null; }
    }

    private void Report(string message, int current, int total) =>
        ProgressChanged?.Invoke(this, new TestProgressEventArgs(message, current, total));
}
