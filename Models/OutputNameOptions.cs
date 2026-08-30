using System;
using System.IO;

namespace FFmpegUI.Models;

/// <summary>输出文件名自定义选项，应用于所有文件处理场景（视频转码/剪辑/提取/合并/压缩/图片转换等）。
///
/// 支持四种命名模式：
/// - Original：沿用源文件名（仅替换扩展名由处理类型决定）；
/// - Prefix：在原文件名前添加内容；
/// - Suffix：在原文件名后添加内容；
/// - Custom：完全自定义，模板中的 {源文件名} 占位符会被替换为不含扩展名的源文件名。</summary>
public sealed class OutputNameOptions
{
    /// <summary>命名模式。</summary>
    public OutputNameMode Mode { get; set; } = OutputNameMode.Original;

    /// <summary>前缀（模式 = Prefix 时生效）。</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>后缀（模式 = Suffix 时生效）。</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>自定义模板（模式 = Custom 时生效），支持 {源文件名} 占位符（不含扩展名）。</summary>
    public string CustomName { get; set; } = string.Empty;

    /// <summary>根据源文件路径与目标扩展名计算最终文件名（不含目录）。
    /// <paramref name="extension"/> 应含点（如 ".mp4"）；为空则沿用源文件扩展名。</summary>
    public string BuildFileName(string sourcePath, string? extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var srcExt = Path.GetExtension(sourcePath);
        var ext = string.IsNullOrWhiteSpace(extension) ? srcExt : extension!;

        var name = Mode switch
        {
            OutputNameMode.Prefix => Prefix + baseName,
            OutputNameMode.Suffix => baseName + Suffix,
            OutputNameMode.Custom => string.IsNullOrWhiteSpace(CustomName)
                ? baseName
                : CustomName.Replace("{源文件名}", baseName, StringComparison.Ordinal),
            _ => baseName,
        };

        return name + ext;
    }

    /// <summary>计算完整输出路径。<paramref name="outputDirectory"/> 为空则放在源文件同目录。</summary>
    public string BuildPath(string sourcePath, string? outputDirectory, string? extension)
    {
        var fileName = BuildFileName(sourcePath, extension);
        var dir = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(sourcePath) ?? string.Empty
            : outputDirectory!;
        return Path.Combine(dir, fileName);
    }
}

/// <summary>输出文件名命名模式。</summary>
public enum OutputNameMode
{
    /// <summary>沿用源文件名（仅扩展名由处理类型决定）。</summary>
    Original,

    /// <summary>在原文件名前添加内容。</summary>
    Prefix,

    /// <summary>在原文件名后添加内容。</summary>
    Suffix,

    /// <summary>完全自定义（支持 {源文件名} 占位符）。</summary>
    Custom,
}
