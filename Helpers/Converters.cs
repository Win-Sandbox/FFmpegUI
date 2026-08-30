using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Globalization;

namespace FFmpegUI.Helpers;

/// <summary>布尔 → 可见性（true 显示）。
/// x:Bind 为强类型，bool 不能直接赋给 Visibility，必须经转换器。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility visibility && visibility == Visibility.Visible;
}

/// <summary>布尔 → 可见性（取反，true 隐藏）。
/// 用于「正在播放时隐藏占位提示」这类场景。</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility visibility && visibility == Visibility.Collapsed;
}

/// <summary>播放状态 → 主按钮文字。
/// 播放中为「重新播放」（用新参数重启），未播放为「播放」。</summary>
public sealed class BoolToPlayTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? "重新播放" : "播放";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>图片转换项 → 状态图标字形。
/// 用法：在 DataTemplate 中不带 Path 地 x:Bind，转换器会收到整个 ImageItem。</summary>
public sealed class ImageItemToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ViewModels.ImageItem item) return "\uE7C3"; // 文件图标

        if (item.Failed) return "\uE711";    // 取消/叉号
        if (item.Succeeded) return "\uE73E"; // 对勾

        return "\uE7C3";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 前景刷（true 为错误红，false 为次要文本色）。
/// 颜色按当前应用主题选择，避免在深色背景下看不清。</summary>
public sealed class BoolToErrorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isError = value is true;

        var isLight = Application.Current.RequestedTheme == ApplicationTheme.Light;

        // 与 Fluent 主题令牌保持一致：SystemFillColorCritical / TextFillColorSecondary
        var color = isError
            ? (isLight ? Windows.UI.Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C)
                       : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x99, 0xA4))
            : (isLight ? Windows.UI.Color.FromArgb(0x9E, 0x00, 0x00, 0x00)
                       : Windows.UI.Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF));

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>数值 → 显示文本。
///
/// 存在的必要性：x:Bind 是强类型绑定，int / double 无法直接赋给 TextBlock.Text
/// （string），编译期即报错；而经典 {Binding} 虽会自动做 ToString 转换，
/// 但项目统一使用 x:Bind，故提供此转换器。
///
/// 用法：ConverterParameter 传 .NET 标准数字格式字符串，
/// 如 "0" 取整、"0.00" 保留两位；留空则用默认 ToString()。</summary>
public sealed class NumberToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null) return string.Empty;

        var format = parameter as string;
        if (!string.IsNullOrWhiteSpace(format) && value is IFormattable formattable)
            return formattable.ToString(format, CultureInfo.CurrentCulture);

        return System.Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
