using System;
using System.Collections.Generic;
using System.Linq;

namespace FFmpegUI.Services;

/// <summary>图片质量的控制方式。
/// 不同图片编码器使用不同的质量参数，必须区别处理，
/// 否则会出现「参数被忽略」或「生成无效命令」。</summary>
public enum ImageQualityMode
{
    /// <summary>无损格式，不支持质量参数（如 PNG、BMP）。</summary>
    None,

    /// <summary>-q:v：qscale 质量（2–31，数值越小质量越高）。
    /// 用于 MJPEG/JPEG 等使用 qscale 的编码器。</summary>
    QScale,

    /// <summary>-quality：0–100，数值越大质量越高。
    /// 用于 libwebp 等编码器（与 qscale 方向相反，切勿混用）。</summary>
    Quality
}

/// <summary>图片格式定义。</summary>
public sealed record ImageFormatInfo(
    string Extension,
    string DisplayName,
    string EncoderName,
    ImageQualityMode QualityMode,
    bool SupportsAlpha,
    string Description = "",
    string ExtraArguments = "");

/// <summary>图片格式目录：定义 ffmpeg 原生支持的图片格式，并提供可用性检测。
///
/// 重要前提：ffmpeg 的图片格式支持取决于其编译配置。
/// 例如 HEIC/HEIF 需要编译时链接 libheif，多数 Windows 预编译构建并未包含，
/// 因此所有格式都必须经过实际检测才能确定是否可用，不能硬编码。</summary>
public static class ImageFormatCatalog
{
    /// <summary>ffmpeg 原生支持的图片输出格式。
    /// 已剔除需要外部库或超出位图范畴的格式（HEIC、RAW、SVG 等，见页面提示）。</summary>
    public static IReadOnlyList<ImageFormatInfo> Formats { get; } = new[]
    {
        new ImageFormatInfo("png", "PNG（无损，支持透明）", "png",
            ImageQualityMode.None, true,
            "无损压缩，支持透明通道，适合截图与需要反复编辑的图片",
            "-compression_level 6"),

        new ImageFormatInfo("jpg", "JPEG（有损，体积小）", "mjpeg",
            ImageQualityMode.QScale, false,
            "有损压缩，不支持透明，适合照片；质量 2 为视觉无损",
            "-pix_fmt yuvj420p"),

        new ImageFormatInfo("webp", "WebP（现代格式）", "libwebp",
            ImageQualityMode.Quality, true,
            "压缩率优于 JPEG，支持透明与动图",
            ""),

        new ImageFormatInfo("gif", "GIF（256 色，支持动图）", "gif",
            ImageQualityMode.None, true,
            "最多 256 色，适合简单动图与表情包",
            ""),

        new ImageFormatInfo("bmp", "BMP（无压缩）", "bmp",
            ImageQualityMode.None, false,
            "无压缩位图，体积很大，仅用于兼容旧软件",
            ""),

        new ImageFormatInfo("tiff", "TIFF（无损，印刷）", "tiff",
            ImageQualityMode.None, true,
            "无损，支持图层与透明，常用于印刷与存档",
            ""),

        new ImageFormatInfo("ico", "ICO（Windows 图标）", "bmp",
            ImageQualityMode.None, true,
            "Windows 图标；ffmpeg 限制较多（建议 ≤256×256）",
            ""),

        new ImageFormatInfo("jp2", "JPEG 2000", "jpeg2000",
            ImageQualityMode.None, true,
            "小波变换，支持无损与有损，常见于数字影院",
            ""),

        new ImageFormatInfo("tga", "TGA（Targa）", "targa",
            ImageQualityMode.None, true,
            "游戏与动画行业常用",
            ""),
    };

    /// <summary>常用图片输入的扩展名（用于文件选择器过滤）。</summary>
    public static string[] InputExtensions { get; } =
        Formats.Select(f => f.Extension)
            .Concat(new[] { "jpeg", "jfif", "heic", "heif", "avif", "apng" })
            .Distinct()
            .ToArray();

    /// <summary>默认输出格式索引（JPEG 最常用）。</summary>
    public static int DefaultFormatIndex => 1;

    /// <summary>按扩展名查找格式定义（不区分大小写）；未找到时返回 null。</summary>
    public static ImageFormatInfo? FindByExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;

        var normalized = extension.TrimStart('.').ToLowerInvariant();
        return Formats.FirstOrDefault(f => f.Extension == normalized);
    }

    /// <summary>常见的、需要外部库支持的格式提示。
    /// 用于在这些格式转换失败时给出准确的解释，而不是笼统的「转换失败」。</summary>
    public static IReadOnlyDictionary<string, string> ExternalDependencyHints { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["heic"] = "HEIC/HEIF 需要 ffmpeg 编译时启用 libheif，多数 Windows 预编译版本未包含。"
                       + "可改用 gyan.dev 的 full 构建，或先用其他工具转为 PNG/JPEG。",
            ["heif"] = "HEIF 需要 ffmpeg 编译时启用 libheif，多数 Windows 预编译版本未包含。",
            ["avif"] = "AVIF 需要 ffmpeg 编译时启用 libaom-av1 或 libsvtav1。",
            ["cr2"] = "相机 RAW（CR2/NEF/ARW 等）不在 ffmpeg 的能力范围内，需使用 LibRaw 之类的专用库。",
            ["nef"] = "相机 RAW（CR2/NEF/ARW 等）不在 ffmpeg 的能力范围内，需使用 LibRaw 之类的专用库。",
            ["arw"] = "相机 RAW（CR2/NEF/ARW 等）不在 ffmpeg 的能力范围内，需使用 LibRaw 之类的专用库。",
            ["dng"] = "相机 RAW（DNG）不在 ffmpeg 的能力范围内，需使用 LibRaw 之类的专用库。",
            ["svg"] = "SVG 是矢量图形，ffmpeg 只处理位图，不支持 SVG 转换。",
            ["pdf"] = "PDF 转换需要 ghostscript，多数 Windows 预编译版本未包含。",
        };

    /// <summary>根据文件扩展名给出外部依赖提示（无匹配时返回 null）。</summary>
    public static string? GetDependencyHint(string filePath)
    {
        var extension = System.IO.Path.GetExtension(filePath)?.TrimStart('.');
        if (string.IsNullOrEmpty(extension)) return null;

        return ExternalDependencyHints.TryGetValue(extension, out var hint) ? hint : null;
    }
}
