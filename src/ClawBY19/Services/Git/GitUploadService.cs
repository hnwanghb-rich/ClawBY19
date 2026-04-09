using System.Diagnostics;
using System.IO;
using System.Text;
using ClawBY19.Services.AI;
using ClawBY19.Services.Rag;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Git;

/// <summary>Git 上传参数</summary>
public record GitUploadParams(
    string LocalPath,        // 本地源代码目录
    string RemoteUrl,        // 目标仓库地址（https://github.com/user/repo）
    string Branch,           // 分支名，默认 main
    string Username,         // 账号
    string Token,            // Personal Access Token
    string CommitMessage);   // Commit 信息

/// <summary>Git 上传结果</summary>
public record GitUploadResult(bool Success, string Message, string Details);

/// <summary>
/// 功能一：本地 Git 上传服务。
/// 1. 调用大模型分析目录，动态生成 .gitignore（只上传源代码和依赖项）
/// 2. 本地执行 git 命令完成上传
/// 3. 返回结构化结果（供 UI 显示绿色/红色反馈）
/// </summary>
public class GitUploadService
{
    private readonly IAiClient _aiClient;
    private readonly ConfigService _config;
    private readonly ModelRegistry _registry;
    private readonly ILogger<GitUploadService> _logger;

    public GitUploadService(
        IAiClient aiClient,
        ConfigService config,
        ModelRegistry registry,
        ILogger<GitUploadService> logger)
    {
        _aiClient = aiClient;
        _config = config;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// 执行 Git 上传：
    /// 1. AI 分析目录 → 写入 .gitignore
    /// 2. git init / remote add / add / commit / push
    /// </summary>
    public async Task<GitUploadResult> UploadAsync(
        GitUploadParams p,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(p.LocalPath))
            return new GitUploadResult(false, $"目录不存在：{p.LocalPath}", string.Empty);

        var log = new StringBuilder();

        try
        {
            // Step 1：AI 分析目录，生成 .gitignore
            progress?.Report("AI 分析目录文件类型，生成过滤规则...");
            var gitignoreContent = await GenerateGitignoreAsync(p.LocalPath, ct);
            var gitignorePath = Path.Combine(p.LocalPath, ".gitignore");

            // 合并已有的 .gitignore（保留用户自定义规则）
            if (File.Exists(gitignorePath))
            {
                var existing = await File.ReadAllTextAsync(gitignorePath, ct);
                if (!existing.Contains("# ClawBY19 Auto-Generated"))
                    gitignoreContent = existing + "\n\n" + gitignoreContent;
                else
                    gitignoreContent = existing; // 已有自动生成的，不覆盖
            }
            await File.WriteAllTextAsync(gitignorePath, gitignoreContent, Encoding.UTF8, ct);
            log.AppendLine($"✅ .gitignore 已生成（{gitignoreContent.Split('\n').Length} 条规则）");

            // Step 2：构造带 Token 的 Remote URL
            var remoteWithAuth = BuildAuthUrl(p.RemoteUrl, p.Username, p.Token);

            // Step 3：执行 git 命令序列
            var isGitRepo = Directory.Exists(Path.Combine(p.LocalPath, ".git"));

            if (!isGitRepo)
            {
                progress?.Report("初始化 Git 仓库...");
                var initResult = await RunGitAsync(p.LocalPath, "init", ct);
                log.AppendLine($"git init: {initResult}");
                if (!initResult.Success) return new GitUploadResult(false, "git init 失败", log.ToString());
            }

            // 配置 Remote
            progress?.Report("配置远程仓库...");
            var remoteList = await RunGitAsync(p.LocalPath, "remote", ct);
            if (remoteList.Output.Contains("origin"))
            {
                await RunGitAsync(p.LocalPath, $"remote set-url origin \"{remoteWithAuth}\"", ct);
            }
            else
            {
                await RunGitAsync(p.LocalPath, $"remote add origin \"{remoteWithAuth}\"", ct);
            }
            log.AppendLine("✅ Remote 配置完成");

            // git add
            progress?.Report("暂存文件（已过滤编译/调试文件）...");
            var addResult = await RunGitAsync(p.LocalPath, "add .", ct);
            log.AppendLine($"git add: {addResult.Output}");
            if (!addResult.Success)
                return new GitUploadResult(false, "git add 失败", log.ToString());

            // 检查是否有变更
            var statusResult = await RunGitAsync(p.LocalPath, "status --short", ct);
            if (string.IsNullOrWhiteSpace(statusResult.Output))
            {
                return new GitUploadResult(true, "没有需要提交的文件（工作区是干净的）", log.ToString());
            }
            log.AppendLine($"待提交文件：\n{statusResult.Output}");

            // git commit
            progress?.Report("创建提交...");
            var commitMsg = string.IsNullOrWhiteSpace(p.CommitMessage)
                ? $"ClawBY19 Auto Commit {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                : p.CommitMessage;

            // 配置 git user（避免首次提交时报错）
            await RunGitAsync(p.LocalPath, "config user.email \"clawby19@local\"", ct);
            var gitUser = string.IsNullOrWhiteSpace(p.Username) ? "ClawBY19" : p.Username;
            await RunGitAsync(p.LocalPath, $"config user.name \"{gitUser}\"", ct);

            var commitResult = await RunGitAsync(p.LocalPath, $"commit -m \"{EscapeQuotes(commitMsg)}\"", ct);
            log.AppendLine($"git commit: {commitResult.Output}");
            if (!commitResult.Success && !commitResult.Output.Contains("nothing to commit"))
                return new GitUploadResult(false, "git commit 失败", log.ToString());

            // git push
            progress?.Report($"推送到 {p.Branch} 分支...");
            var branch = string.IsNullOrWhiteSpace(p.Branch) ? "main" : p.Branch;
            var pushResult = await RunGitAsync(p.LocalPath,
                $"push -u origin {branch} --force-with-lease", ct);
            log.AppendLine($"git push: {pushResult.Output}");

            if (!pushResult.Success)
            {
                // 如果 branch 不存在，尝试推送到 HEAD
                if (pushResult.Output.Contains("does not match any") || pushResult.Error.Contains("rejected"))
                {
                    var pushResult2 = await RunGitAsync(p.LocalPath,
                        $"push -u origin HEAD:{branch}", ct);
                    log.AppendLine($"git push (retry): {pushResult2.Output}");
                    if (!pushResult2.Success)
                        return new GitUploadResult(false, $"推送失败：{pushResult2.Error}", log.ToString());
                }
                else
                {
                    return new GitUploadResult(false, $"推送失败：{pushResult.Error}", log.ToString());
                }
            }

            var successMsg = $"全部成功！代码已推送到 {p.RemoteUrl} ({branch} 分支)";
            log.AppendLine($"✅ {successMsg}");
            _logger.LogInformation("Git 上传成功：{Url}", p.RemoteUrl);
            return new GitUploadResult(true, successMsg, log.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Git 上传失败");
            return new GitUploadResult(false, $"上传异常：{ex.Message}", log.ToString());
        }
    }

    /// <summary>调用大模型分析目录，动态生成 .gitignore 内容</summary>
    public async Task<string> GenerateGitignoreAsync(string localPath, CancellationToken ct = default)
    {
        // 扫描目录顶层内容（最多200个条目）
        var entries = new StringBuilder();
        try
        {
            var dirs = Directory.GetDirectories(localPath)
                .Select(d => $"[DIR]  {Path.GetFileName(d)}")
                .Take(50);
            var files = Directory.GetFiles(localPath, "*", SearchOption.TopDirectoryOnly)
                .Select(f => $"[FILE] {Path.GetFileName(f)}")
                .Take(50);
            var subFiles = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetExtension(f))
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct()
                .Take(30);

            entries.AppendLine("顶层目录和文件：");
            foreach (var d in dirs) entries.AppendLine(d);
            foreach (var f in files) entries.AppendLine(f);
            entries.AppendLine($"\n所有文件扩展名（去重）：{string.Join(", ", subFiles)}");
        }
        catch
        {
            entries.AppendLine("（目录扫描失败，使用默认规则）");
        }

        var modelId = _config.ModelCfg.ClawAuto.DefaultModel;
        var model = _registry.Get(modelId);
        if (model is null)
            return DefaultGitignore();

        var apiKey = _config.GetApiKey(model.ApiProvider);
        if (string.IsNullOrEmpty(apiKey))
            return DefaultGitignore();

        var messages = new List<ApiMessage>
        {
            new("system", """
                你是一个代码仓库管理专家。根据用户提供的项目目录内容，生成合适的 .gitignore 文件。
                只输出 .gitignore 文件内容，不要有其他说明文字。
                规则：只上传源代码文件和项目配置文件（如 .csproj、package.json、requirements.txt 等）。
                排除：编译产物（bin/、obj/、dist/、build/、target/）、调试文件（*.pdb、*.log）、IDE配置（.vs/、.idea/、.vscode/内部设置）、环境变量文件（.env）等。
                """),
            new("user", $"请根据以下项目目录内容，生成 .gitignore 文件：\n\n{entries}\n\n只输出 .gitignore 内容，第一行写 # ClawBY19 Auto-Generated")
        };

        try
        {
            var sb = new StringBuilder();
            await foreach (var token in _aiClient.StreamAsync(
                model.BaseUrl, model.ApiModelId, apiKey, messages, ct: ct))
            {
                sb.Append(token);
            }
            var result = sb.ToString().Trim();
            return result.Length > 20 ? result : DefaultGitignore();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 生成 .gitignore 失败，使用默认规则");
            return DefaultGitignore();
        }
    }

    // ── 内部工具 ─────────────────────────────────────────────────────────

    private record GitRunResult(bool Success, string Output, string Error);

    private async Task<GitRunResult> RunGitAsync(string workDir, string arguments, CancellationToken ct)
    {
        _logger.LogDebug("git {Args} in {Dir}", arguments, workDir);

        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var outTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var stdout = await outTask;
            var stderr = await errTask;
            var success = proc.ExitCode == 0;

            _logger.LogDebug("git exit={Code} out={Out} err={Err}",
                proc.ExitCode, stdout.Truncate(200), stderr.Truncate(200));

            return new GitRunResult(success, stdout.Trim(), stderr.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "运行 git {Args} 失败", arguments);
            return new GitRunResult(false, string.Empty, ex.Message);
        }
    }

    /// <summary>将 Token 嵌入 HTTPS URL，用于免密认证</summary>
    private static string BuildAuthUrl(string remoteUrl, string username, string token)
    {
        if (string.IsNullOrEmpty(token)) return remoteUrl;

        if (remoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var host = remoteUrl[8..]; // 去掉 https://
            // 避免重复嵌入
            if (host.Contains('@')) return remoteUrl;
            var user = string.IsNullOrWhiteSpace(username) ? "oauth2" : username;
            return $"https://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(token)}@{host}";
        }
        return remoteUrl; // SSH 格式不处理
    }

    private static string EscapeQuotes(string s) => s.Replace("\"", "\\\"");

    private static string DefaultGitignore() => """
        # ClawBY19 Auto-Generated

        # .NET / C#
        bin/
        obj/
        *.user
        *.suo
        .vs/
        *.pdb
        *.nupkg

        # Python
        __pycache__/
        *.pyc
        *.pyo
        .venv/
        venv/
        *.egg-info/
        dist/

        # Node.js
        node_modules/
        dist/
        build/
        .env
        .env.local
        *.log

        # Java / Maven / Gradle
        target/
        *.class
        .gradle/
        build/

        # IDE
        .idea/
        .vscode/settings.json
        .vscode/launch.json

        # OS
        .DS_Store
        Thumbs.db
        desktop.ini

        # Logs
        logs/
        *.log
        """;
}
