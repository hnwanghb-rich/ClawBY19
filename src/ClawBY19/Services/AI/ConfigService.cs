using System.IO;
using System.Text.Json;
using ClawBY19.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tomlyn;

namespace ClawBY19.Services.AI;

/// <summary>加载和保存所有 TOML / JSON 配置</summary>
public class ConfigService
{
    private readonly IConfiguration _appConfig;
    private readonly ILogger<ConfigService> _logger;
    private readonly string _configDir;

    public AppSettings AppSettings { get; }
    public ModelConfig ModelCfg { get; private set; }
    public AlertRulesConfig AlertRules { get; private set; }
    // pricing 以字典形式存储：modelId -> PricingEntry
    public Dictionary<string, PricingEntry> Pricing { get; private set; } = new();

    public ConfigService(IConfiguration appConfig, ILogger<ConfigService> logger)
    {
        _appConfig = appConfig;
        _logger = logger;

        _configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");

        AppSettings = appConfig.Get<AppSettings>() ?? new AppSettings();
        ModelCfg = LoadModelConfig();
        AlertRules = LoadAlertRules();
        Pricing = LoadPricing();
    }

    public string GetApiKey(string provider)
    {
        AppSettings.ApiKeys.TryGetValue(provider, out var key);
        return key ?? string.Empty;
    }

    public void SaveModelConfig()
    {
        try
        {
            var path = Path.Combine(_configDir, "models.toml");
            var toml = Toml.FromModel(ModelCfg);
            File.WriteAllText(path, toml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 models.toml 失败");
        }
    }

    public void SaveAppSettings()
    {
        try
        {
            var path = Path.Combine(_configDir, "appsettings.json");
            var json = JsonSerializer.Serialize(AppSettings,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 appsettings.json 失败");
        }
    }

    // ── 加载 ──────────────────────────────────────────────────────────
    private ModelConfig LoadModelConfig()
    {
        try
        {
            var path = Path.Combine(_configDir, "models.toml");
            if (!File.Exists(path)) return new ModelConfig();
            var toml = File.ReadAllText(path);
            return Toml.ToModel<ModelConfig>(toml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "models.toml 加载失败，使用默认值");
            return new ModelConfig();
        }
    }

    private AlertRulesConfig LoadAlertRules()
    {
        try
        {
            var path = Path.Combine(_configDir, "alert_rules.toml");
            if (!File.Exists(path)) return new AlertRulesConfig();
            var toml = File.ReadAllText(path);
            return Toml.ToModel<AlertRulesConfig>(toml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "alert_rules.toml 加载失败，使用默认值");
            return new AlertRulesConfig();
        }
    }

    private Dictionary<string, PricingEntry> LoadPricing()
    {
        var result = new Dictionary<string, PricingEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(_configDir, "model_pricing.toml");
            if (!File.Exists(path)) return result;

            var toml = File.ReadAllText(path);
            var model = Toml.ToModel(toml);

            foreach (var kv in model)
            {
                if (kv.Value is Tomlyn.Model.TomlTable table)
                {
                    var entry = new PricingEntry
                    {
                        Provider = table.TryGetValue("provider", out var p) ? p?.ToString() ?? "" : "",
                        InputPricePerMillion = ParseDecimal(table, "input_price_per_million"),
                        OutputPricePerMillion = ParseDecimal(table, "output_price_per_million"),
                        CacheHitPricePerMillion = ParseDecimal(table, "cache_hit_price_per_million"),
                        ExchangeRate = ParseDecimal(table, "exchange_rate", 1m)
                    };
                    result[kv.Key] = entry;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "model_pricing.toml 加载失败");
        }
        return result;
    }

    private static decimal ParseDecimal(Tomlyn.Model.TomlTable t, string key, decimal def = 0m)
    {
        if (!t.TryGetValue(key, out var v)) return def;
        return decimal.TryParse(v?.ToString(), out var d) ? d : def;
    }
}

public class PricingEntry
{
    public string Provider { get; set; } = string.Empty;
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public decimal CacheHitPricePerMillion { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
}
