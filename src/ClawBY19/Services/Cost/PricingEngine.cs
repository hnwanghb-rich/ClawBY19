namespace ClawBY19.Services.Cost;

/// <summary>根据定价配置计算单次请求费用</summary>
public class PricingEngine
{
    private readonly AI.ConfigService _config;

    public PricingEngine(AI.ConfigService config) => _config = config;

    public decimal Calculate(string modelId, int inputTokens, int outputTokens, int cacheHitTokens = 0)
    {
        if (!_config.Pricing.TryGetValue(modelId, out var entry))
            return 0m;

        var cost = (inputTokens - cacheHitTokens) / 1_000_000m * entry.InputPricePerMillion
                 + outputTokens / 1_000_000m * entry.OutputPricePerMillion
                 + cacheHitTokens / 1_000_000m * entry.CacheHitPricePerMillion;
        return Math.Round(cost, 6);
    }

    public string GetProvider(string modelId) =>
        _config.Pricing.TryGetValue(modelId, out var e) ? e.Provider : "未知";
}
