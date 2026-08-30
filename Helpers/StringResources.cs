using Microsoft.Windows.ApplicationModel.Resources;

namespace FFmpegUI.Helpers;

/// <summary>本地化字符串统一访问入口（官方 MRT Core，Learn《Localize strings》）。
/// 资源文件：Strings/&lt;lang&gt;/Resources.resw；当前仅提供 zh-CN。
/// XAML 内通过 x:Uid 引用同名键（键形如 Uid.Property），代码内经本类按键取值。
/// 防御性设计（三层）：PRI 缺失 → ResourceLoader 构造失败返回 null；
/// 单键查找异常（如系统首选语言为 zh-Hans-CN 时与 zh-CN 候选不匹配，
/// GetString 抛 COMException 0x80073B17）→ 返回空串；
/// 键缺失或取值为空 → 由调用方回退到 XAML/代码中的中文中性值。
/// 任何情况下本类不抛异常，保证界面始终可用。</summary>
public static class StringResources
{
    private static readonly ResourceLoader? Loader = CreateLoader();

    private static ResourceLoader? CreateLoader()
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception ex)
        {
            // PRI 缺失或不可用时禁用本地化（界面将显示 XAML 中性回退值）
            App.LogCrash(ex, "StringResources.CreateLoader");
            return null;
        }
    }

    /// <summary>按键取本地化字符串；键缺失、查找异常或 PRI 不可用时返回空字符串。</summary>
    public static string Get(string key)
    {
        if (Loader is null) return string.Empty;

        try
        {
            return Loader.GetString(key) ?? string.Empty;
        }
        catch (Exception ex)
        {
            // 资源候选无匹配（语言不匹配等）时 GetString 会抛 COMException，
            // 降级为空串，由调用方使用中文回退值，绝不让界面崩溃
            App.LogCrash(ex, $"StringResources.Get:{key}");
            return string.Empty;
        }
    }

    /// <summary>取带值的本地化字符串；无本地化时回退到 fallback（XAML 中性值）。</summary>
    public static string GetOr(string key, string fallback)
    {
        var value = Get(key);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <summary>取格式化模板并填充参数（模板含 {0} 等占位符）。
    /// 模板缺失时返回 fallback，避免界面出现空白提示。</summary>
    public static string Format(string key, params object[] args)
    {
        return FormatOr(key, string.Empty, args);
    }

    /// <summary>取格式化模板并填充参数；模板缺失时返回 fallback。</summary>
    public static string FormatOr(string key, string fallback, params object[] args)
    {
        var template = Get(key);
        if (string.IsNullOrEmpty(template)) return fallback;
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }
}
