using System.Text;
using ClawBY19.Data;
using ClawBY19.Data.Entities;
using ClawBY19.Services.AI;
using ClawBY19.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClawBY19.Services.Deploy;

/// <summary>云端部署参数</summary>
public record CloudDeployParams(
    string PlatformName,    // 平台名称（如 AWS生产环境）
    string ServerIp,        // 服务器IP或域名
    string Username,        // SSH用户名
    string Password,        // SSH密码（运行时明文）
    string GitRepoUrl,      // Git仓库地址（可选）
    string DeployPath,      // 服务器上的部署路径（可选）
    string UserInstruction  // 用户的原始指令（含具体部署要求）
);

/// <summary>云端部署结果</summary>
public record CloudDeployResult(bool Success, string Message, string? Script = null);

/// <summary>
/// 功能二：云端部署服务（大模型执行）
/// 通过 AI 生成 SSH 部署脚本，检查服务器环境，
/// 返回执行步骤和一键安装脚本供用户确认后执行。
/// 成功/失败流程存入学习库。
/// </summary>
public class CloudDeployService
{
    private readonly IAiClient _aiClient;
    private readonly ConfigService _config;
    private readonly ModelRegistry _registry;
    private readonly RagService _rag;
    private readonly IDbContextFactory<ClawDbContext> _dbFactory;
    private readonly ILogger<CloudDeployService> _logger;

    public event EventHandler<string>? TokenReceived;

    public CloudDeployService(
        IAiClient aiClient,
        ConfigService config,
        ModelRegistry registry,
        RagService rag,
        IDbContextFactory<ClawDbContext> dbFactory,
        ILogger<CloudDeployService> logger)
    {
        _aiClient = aiClient;
        _config = config;
        _registry = registry;
        _rag = rag;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// 生成云端部署方案（含环境检查 + 一键安装脚本）
    /// 实际执行由用户在服务器上运行生成的脚本完成
    /// </summary>
    public async Task<string> GenerateDeployPlanAsync(
        CloudDeployParams p,
        CancellationToken ct = default)
    {
        var ragNote = await _rag.CreateNoteAsync($"云端部署：{p.PlatformName} {p.ServerIp}");

        var modelId = _config.ModelCfg.ClawAuto.DefaultModel;
        var model = _registry.Get(modelId);
        if (model is null)
            return "❌ 没有可用的 AI 模型，请在设置页配置 API Key。";

        var apiKey = _config.GetApiKey(model.ApiProvider);

        var ragContext = await _rag.BuildContextAsync($"云端部署 SSH {p.ServerIp}");

        var systemPrompt = $"""
            你是一名云端部署专家，负责帮助用户将代码从 Git 仓库部署到云服务器。

            {(ragContext.Length > 0 ? $"历史经验参考：\n{ragContext}\n" : "")}

            工作流程：
            1. 分析用户需求，列出服务器环境检查清单（检查 Git、Docker、Nginx、Node.js、JDK 等是否安装）
            2. 生成一键安装脚本（Shell 脚本，包含环境配置 + 代码拉取 + 部署命令）
            3. 给出执行说明和注意事项

            输出格式（严格按此结构）：
            ## 1. 环境检查清单
            （列出需要检查和可能需要安装的组件）

            ## 2. 一键部署脚本
            ```bash
            #!/bin/bash
            （完整可执行的 Shell 脚本）
            ```

            ## 3. 执行说明
            （如何将脚本上传并执行，以及注意事项）

            ## 4. 预计部署步骤
            （按顺序列出部署流程）

            重要：
            - 生成真实可执行的 Shell 脚本，不使用占位符
            - 密码不要写入脚本，改用提示用户输入的方式
            - 若检测到缺少环境，在脚本中自动安装（apt-get/yum/brew）
            """;

        var userMessage = $"""
            请帮我生成将代码部署到以下服务器的方案：

            服务器信息：
            - 平台名称：{p.PlatformName}
            - 服务器地址：{p.ServerIp}
            - SSH 用户名：{p.Username}
            - Git 仓库：{(string.IsNullOrEmpty(p.GitRepoUrl) ? "未指定" : p.GitRepoUrl)}
            - 部署路径：{(string.IsNullOrEmpty(p.DeployPath) ? "自动选择" : p.DeployPath)}

            用户要求：
            {p.UserInstruction}

            请生成完整的环境检查清单和一键部署脚本。
            """;

        var messages = new List<ApiMessage>
        {
            new("system", systemPrompt),
            new("user", userMessage)
        };

        var sb = new StringBuilder();
        var success = true;
        string? errorMsg = null;

        try
        {
            await foreach (var token in _aiClient.StreamAsync(
                model.BaseUrl, model.ApiModelId, apiKey, messages, ct: ct))
            {
                sb.Append(token);
                TokenReceived?.Invoke(this, token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "云端部署方案生成失败");
            success = false;
            errorMsg = ex.Message;
            sb.Append($"\n\n❌ 生成失败：{ex.Message}");
            TokenReceived?.Invoke(this, $"\n\n❌ 生成失败：{ex.Message}");
        }

        var plan = sb.ToString();

        // 存入学习库
        if (success)
            await _rag.MarkSuccessAsync(ragNote, plan, plan, modelId);
        else
            await _rag.MarkFailedAsync(ragNote, errorMsg ?? "未知错误");

        return plan;
    }

    /// <summary>记录部署学习笔记（用户执行脚本后的反馈）</summary>
    public async Task RecordDeployResultAsync(
        string ragNoteId,
        bool deploySuccess,
        string? userFeedback,
        string? result)
    {
        try
        {
            if (!string.IsNullOrEmpty(userFeedback) && !string.IsNullOrEmpty(ragNoteId))
                await _rag.AddRevisionAsync(ragNoteId, userFeedback, null, result);

            if (deploySuccess)
                await _rag.MarkSuccessAsync(ragNoteId, result ?? string.Empty, result, "user-confirmed");
            else
                await _rag.MarkFailedAsync(ragNoteId, userFeedback ?? "部署失败");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录部署学习笔记失败");
        }
    }
}
