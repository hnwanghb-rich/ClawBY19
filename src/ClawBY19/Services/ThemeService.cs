using System.Windows;

namespace ClawBY19.Services;

/// <summary>运行时主题切换服务</summary>
public class ThemeService
{
    private const string UriTemplate =
        "pack://application:,,,/ClawBY19;component/Themes/{0}Theme.xaml";

    public static readonly IReadOnlyList<string> AvailableThemes =
        ["Dark", "Ocean", "Matrix", "Light", "Claw"];

    public string CurrentTheme { get; private set; } = "Dark";

    public event EventHandler<string>? ThemeChanged;

    /// <summary>切换到指定主题，立即生效（DynamicResource 自动更新）</summary>
    public void Apply(string themeName)
    {
        if (!AvailableThemes.Contains(themeName)) themeName = "Dark";

        var uri = new Uri(string.Format(UriTemplate, themeName));
        var merged = Application.Current.Resources.MergedDictionaries;

        // 替换第一个合并字典（主题字典位置）
        if (merged.Count > 0 && IsThemeDict(merged[0]))
            merged.RemoveAt(0);

        merged.Insert(0, new ResourceDictionary { Source = uri });

        CurrentTheme = themeName;
        ThemeChanged?.Invoke(this, themeName);
    }

    private static bool IsThemeDict(ResourceDictionary d) =>
        d.Source?.OriginalString.Contains("Theme.xaml") == true;
}
