using ClawBY19.Data;
using ClawBY19.Services;
using ClawBY19.Services.Agent;
using ClawBY19.Services.AI;
using ClawBY19.Services.Alert;
using ClawBY19.Services.Chat;
using ClawBY19.Services.Cost;
using ClawBY19.Services.Deploy;
using ClawBY19.Services.Git;
using ClawBY19.Services.Learning;
using ClawBY19.Services.OpenClaw;
using ClawBY19.Services.Rag;
using ClawBY19.Services.Testing;
using ClawBY19.Services.TaskLog;
using ClawBY19.ViewModels;
using ClawBY19.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Windows;

namespace ClawBY19;

public partial class App : Application
{
    private IServiceProvider _services = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局未处理异常捕获
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Fatal(ex.Exception, "UI线程未处理异常");
            MessageBox.Show($"程序发生未处理错误：\n\n{ex.Exception.Message}\n\n详情见 logs 目录。",
                "ClawBY19 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            var err = ex.ExceptionObject as Exception;
            Log.Fatal(err, "AppDomain未处理异常");
        };

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        EnsureDirectories(baseDir);

        // Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(baseDir, "logs", "claw_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            // DI Container
            var sc = new ServiceCollection();
            ConfigureServices(sc, baseDir);
            _services = sc.BuildServiceProvider();

            // 应用保存的主题
            var configSvc = _services.GetRequiredService<ConfigService>();
            var themeSvc  = _services.GetRequiredService<ThemeService>();
            themeSvc.Apply(configSvc.AppSettings.Theme);

            // DB 初始化
            using (var db = _services.GetRequiredService<IDbContextFactory<ClawDbContext>>()
                                      .CreateDbContext())
            {
                db.Database.EnsureCreated();
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

                // 增量建表：EnsureCreated 只建新库，旧库需手动补缺失的表
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "OperationHistories" (
                        "Id"                 INTEGER NOT NULL CONSTRAINT "PK_OperationHistories" PRIMARY KEY AUTOINCREMENT,
                        "OperationType"      TEXT    NOT NULL DEFAULT '',
                        "TargetName"         TEXT    NOT NULL DEFAULT '',
                        "TargetUrl"          TEXT    NOT NULL DEFAULT '',
                        "Account"            TEXT    NOT NULL DEFAULT '',
                        "PasswordEncrypted"  TEXT    NOT NULL DEFAULT '',
                        "ExtraJson"          TEXT    NULL,
                        "CreatedAt"          TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        "LastUsedAt"         TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        "UseCount"           INTEGER NOT NULL DEFAULT 1,
                        "Remark"             TEXT    NULL
                    );
                    """);

                db.Database.ExecuteSqlRaw("""
                    CREATE INDEX IF NOT EXISTS "IX_OperationHistories_OperationType_TargetUrl"
                        ON "OperationHistories" ("OperationType", "TargetUrl");
                    """);

                db.Database.ExecuteSqlRaw("""
                    CREATE INDEX IF NOT EXISTS "IX_OperationHistories_LastUsedAt"
                        ON "OperationHistories" ("LastUsedAt");
                    """);

                // 测试日志表
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "TestLogs" (
                        "Id"            INTEGER NOT NULL CONSTRAINT "PK_TestLogs" PRIMARY KEY AUTOINCREMENT,
                        "SessionId"     TEXT    NOT NULL DEFAULT '',
                        "TestTarget"    TEXT    NOT NULL DEFAULT '',
                        "TestType"      TEXT    NOT NULL DEFAULT '',
                        "MenuPath"      TEXT    NOT NULL DEFAULT '',
                        "FunctionName"  TEXT    NOT NULL DEFAULT '',
                        "TestAction"    TEXT    NOT NULL DEFAULT '',
                        "TestResult"    TEXT    NOT NULL DEFAULT 'Pass',
                        "ErrorDetail"   TEXT    NULL,
                        "CreatedAt"     TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                    );
                    """);

                db.Database.ExecuteSqlRaw("""
                    CREATE INDEX IF NOT EXISTS "IX_TestLogs_SessionId"
                        ON "TestLogs" ("SessionId");
                    """);

                // 操作任务记录
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "TaskRecords" (
                        "Id"            INTEGER NOT NULL CONSTRAINT "PK_TaskRecords" PRIMARY KEY AUTOINCREMENT,
                        "TaskId"        TEXT    NOT NULL DEFAULT '',
                        "TaskType"      TEXT    NOT NULL DEFAULT '',
                        "Summary"       TEXT    NOT NULL DEFAULT '',
                        "StartedAt"     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        "FinishedAt"    TEXT    NULL,
                        "Status"        TEXT    NOT NULL DEFAULT 'running',
                        "LocalPath"     TEXT    NULL,
                        "RemoteUrl"     TEXT    NULL,
                        "Branch"        TEXT    NULL,
                        "CommitMessage" TEXT    NULL,
                        "ServerIp"      TEXT    NULL,
                        "DeployPath"    TEXT    NULL,
                        "OnlineUrl"     TEXT    NULL,
                        "StepLogsJson"  TEXT    NULL,
                        "AiAnalysis"    TEXT    NULL,
                        "ErrorFeature"  TEXT    NULL
                    );
                    """);

                db.Database.ExecuteSqlRaw("""
                    CREATE INDEX IF NOT EXISTS "IX_TaskRecords_StartedAt"
                        ON "TaskRecords" ("StartedAt");
                    """);

                // 失败路径学习库
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "FailedPaths" (
                        "Id"              INTEGER NOT NULL CONSTRAINT "PK_FailedPaths" PRIMARY KEY AUTOINCREMENT,
                        "TaskType"        TEXT    NOT NULL DEFAULT '',
                        "ErrorFeature"    TEXT    NOT NULL DEFAULT '',
                        "FailedSolution"  TEXT    NOT NULL DEFAULT '',
                        "AttemptCount"    INTEGER NOT NULL DEFAULT 1,
                        "LastAttemptAt"   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        "SuccessSolution" TEXT    NULL,
                        "IsResolved"      INTEGER NOT NULL DEFAULT 0
                    );
                    """);
            }

            // 启动主窗口
            var main = _services.GetRequiredService<MainWindow>();
            main.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "启动失败");
            MessageBox.Show($"程序启动失败：\n\n{ex.Message}\n\n{ex.InnerException?.Message}\n\n详情见 logs 目录。",
                "ClawBY19 启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void ConfigureServices(IServiceCollection sc, string baseDir)
    {
        // IConfiguration
        var config = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile(Path.Combine("config", "appsettings.json"), optional: true, reloadOnChange: true)
            .Build();
        sc.AddSingleton<IConfiguration>(config);

        // Logging
        sc.AddLogging(b => b.AddSerilog(dispose: true));

        // SQLite DbContext (Factory for thread-safe use)
        var dbPath = Path.Combine(baseDir, "data", "claw.db");
        sc.AddDbContextFactory<ClawDbContext>(o =>
            o.UseSqlite($"Data Source={dbPath}"),
            ServiceLifetime.Singleton);

        // Core Services (Singleton)
        sc.AddSingleton<ConfigService>();
        sc.AddSingleton<ThemeService>();
        sc.AddSingleton<ModelRegistry>();
        sc.AddSingleton<ModelSelectionService>();
        sc.AddSingleton<IAiClient, AiClient>();
        sc.AddSingleton<PricingEngine>();
        sc.AddSingleton<CostTracker>();
        sc.AddSingleton<AlertService>();

        // Services
        sc.AddSingleton<RagService>();
        sc.AddSingleton<AgentService>();
        sc.AddSingleton<LearningService>();
        sc.AddSingleton<ChatService>();
        sc.AddSingleton<ScheduledTaskService>();
        sc.AddSingleton<OperationHistoryService>();
        sc.AddSingleton<GitUploadService>();
        sc.AddSingleton<CloudDeployService>();
        sc.AddSingleton<TestService>();
        sc.AddSingleton<TaskLogService>();

        // ViewModels (Transient - recreated per window)
        sc.AddTransient<ChatViewModel>();
        sc.AddTransient<ConsoleViewModel>();
        sc.AddTransient<TasksViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddTransient<MainViewModel>();

        // Views
        sc.AddTransient<MainWindow>();
    }

    private static void EnsureDirectories(string baseDir)
    {
        foreach (var sub in new[] { "data", "data/workspace", "logs", "skills", "config" })
            Directory.CreateDirectory(Path.Combine(baseDir, sub));
    }
}
