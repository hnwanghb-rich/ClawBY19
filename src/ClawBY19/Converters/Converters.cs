using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClawBY19.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool bVal = value is bool b && b;
        if (parameter is string s && s == "Inverse") bVal = !bVal;
        return bVal ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(HorizontalAlignment))]
public class IsUserToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(bool))]
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

[ValueConversion(typeof(string), typeof(Visibility))]
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value is string s && !string.IsNullOrEmpty(s);
        if (parameter is string p && p == "Inverse") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class AlertLevelToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.Brush Yellow =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7));
    private static readonly System.Windows.Media.Brush Red =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
    private static readonly System.Windows.Media.Brush Transparent =
        System.Windows.Media.Brushes.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ClawBY19.Services.Alert.AlertLevel level ? level switch
        {
            ClawBY19.Services.Alert.AlertLevel.Warning  => Yellow,
            ClawBY19.Services.Alert.AlertLevel.Critical => Red,
            _ => Transparent
        } : Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// 允许双向绑定 PasswordBox.Password。
/// 用法：helpers:PasswordBoxHelper.BoundPassword="{Binding ApiKey, Mode=TwoWay}"
/// </summary>
public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxHelper),
            new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

    private static readonly DependencyProperty UpdatingPasswordProperty =
        DependencyProperty.RegisterAttached("UpdatingPassword", typeof(bool), typeof(PasswordBoxHelper),
            new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static bool GetUpdatingPassword(DependencyObject d) => (bool)d.GetValue(UpdatingPasswordProperty);
    private static void SetUpdatingPassword(DependencyObject d, bool value) => d.SetValue(UpdatingPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= HandlePasswordChanged;
        if (!GetUpdatingPassword(box))
            box.Password = (string)e.NewValue ?? string.Empty;
        box.PasswordChanged += HandlePasswordChanged;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        SetUpdatingPassword(box, true);
        SetBoundPassword(box, box.Password);
        SetUpdatingPassword(box, false);
    }
}

/// <summary>根据消息 Role 返回对应主题颜色刷子</summary>
public class RoleToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value as string ?? "assistant";
        var key = role.ToLowerInvariant() switch
        {
            "user"   => "ChatUserBrush",
            "system" => "ChatSystemBrush",
            _        => "ChatAssistantBrush"
        };
        return Application.Current.Resources[key] as Brush ?? Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Count > 0 → Visible，否则 Collapsed</summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool 取反后转 Visibility（false → Visible）</summary>
public class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>base64 字符串 → BitmapImage（用于附件缩略图）</summary>
public class Base64ToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string b64 || string.IsNullOrEmpty(b64)) return null;
        try
        {
            var bytes = System.Convert.FromBase64String(b64);
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Tab索引转背景色（用于Tab按钮激活状态）</summary>
public class TabBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int selectedIndex || parameter is not string tabIndex)
            return Brushes.Transparent;

        return selectedIndex.ToString() == tabIndex
            ? Application.Current.Resources["BgCardBrush"] as Brush ?? Brushes.Gray
            : Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>计算图表柱高度（值/最大值 * 最大高度）</summary>
public class ChartHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double val || values[1] is not double maxVal)
            return 0.0;

        if (maxVal <= 0) return 0.0;

        const double maxHeight = 150.0;  // 最大柱高度
        return (val / maxVal) * maxHeight;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>计算Y轴刻度位置（从底部向上）</summary>
public class YAxisPositionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double tickValue || values[1] is not double maxVal)
            return 0.0;

        if (maxVal <= 0) return 0.0;

        const double chartHeight = 170.0;  // 图表总高度
        // 从底部向上计算位置（Y轴向下为正）
        return chartHeight - (tickValue / maxVal) * chartHeight;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Tab索引转前景色（选中 → AccentBlueBrush，未选中 → TextSecondaryBrush）</summary>
public class TabFgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int selectedIndex || parameter is not string tabIndex)
            return Application.Current.Resources["TextSecondaryBrush"] as Brush ?? Brushes.Gray;
        return selectedIndex.ToString() == tabIndex
            ? Application.Current.Resources["AccentBlueBrush"] as Brush ?? Brushes.OrangeRed
            : Application.Current.Resources["TextSecondaryBrush"] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>字符串等于 ConverterParameter → true（用于 RadioButton IsChecked 绑定）</summary>
public class StringEqualToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? parameter?.ToString() ?? string.Empty : Binding.DoNothing;
}

/// <summary>字符串等于 ConverterParameter → Visible，否则 Collapsed</summary>
public class StringEqualToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
