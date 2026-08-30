using FFmpegUI.Helpers;
using FFmpegUI.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.Services;

/// <summary>单个文件的转换结果。</summary>
public sealed record ImageConvertResult(string SourcePath, string TargetPath, int ExitCode, string? ErrorMessage)
{
    public bool Succeeded => ExitCode == 0 && ErrorMessage is null;
}

/// <summary>图片转换服务：把图片转换请求转为 ffmpeg 命令并执行。
///
/// 与视频转换的关键差异：
/// <list type="bullet">
/// <item>图片是单帧，命令固定加 <c>-frames:v 1</c>，
///       避免动图输入（GIF/APNG/视频）被解成多帧而输出错误文件；</item>
/// <item>质量参数因编码器而异（qscale 与 quality 方向相反），由
///       <see cref="ImageQualityMode"/> 统一处理；</item>
/// <item>输出目录不存在时先创建，避免批量转换时逐个失败。</item>
/// </list></summary>
public static class ImageConverter
{
    /// <summary>构建转换参数列表。供命令生成与界面预览共用。</summary>
    public static List<string> BuildArguments(string sourcePath, string targetPath, ImageConvertOptions options)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-v", "error",
            // 覆盖已存在的输出文件（图片转换不需要 ffmpeg 的交互确认）
            "-y",
            "-i", sourcePath,
            // 只取第一帧：动图/视频输入时避免输出多帧
            "-frames:v", "1"
        };

        var filters = BuildFilterChain(options);
        if (filters.Count > 0)
        {
            arguments.Add("-vf");
            arguments.Add(string.Join(',', filters));
        }

        // 编码器必须在输入之后、输出之前指定
        arguments.Add("-c:v");
        arguments.Add(options.TargetFormat.EncoderName);

        BuildQualityArguments(options, arguments);

        if (!string.IsNullOrWhiteSpace(options.TargetFormat.ExtraArguments))
            arguments.AddRange(FfmpegCommandBuilder.SplitArguments(options.TargetFormat.ExtraArguments));

        arguments.Add(targetPath);
        return arguments;
    }

    /// <summary>构建滤镜链：旋转 → 翻转 → 缩放 → 灰度。
    /// 顺序固定，保证几何变换先于色彩处理。</summary>
    private static List<string> BuildFilterChain(ImageConvertOptions options)
    {
        var filters = new List<string>();

        switch (options.Rotate)
        {
            case 90:
                filters.Add("transpose=1");
                break;
            case 180:
                filters.Add("hflip");
                filters.Add("vflip");
                break;
            case 270:
                filters.Add("transpose=2");
                break;
        }

        if (options.FlipHorizontal) filters.Add("hflip");
        if (options.FlipVertical) filters.Add("vflip");

        var scale = BuildScaleFilter(options);
        if (!string.IsNullOrEmpty(scale)) filters.Add(scale);

        if (options.Grayscale) filters.Add("hue=s=0");

        return filters;
    }

    /// <summary>构建 scale 滤镜。
    /// 未放大限制的实现：scale 的 Min 参数（当 ShrinkOnly 时把输出限制为不超过原尺寸）。</summary>
    private static string BuildScaleFilter(ImageConvertOptions options)
    {
        if (options.ResizeMode == ImageResizeMode.None) return string.Empty;

        var scale = options.ResizeMode switch
        {
            ImageResizeMode.ByWidth when options.Width > 0 =>
                $"{options.Width}:-2",

            ImageResizeMode.ByHeight when options.Height > 0 =>
                $"-2:{options.Height}",

            ImageResizeMode.Exact when options.Width > 0 && options.Height > 0 =>
                $"{options.Width}:{options.Height}",

            // Fit：等比缩放并限制在矩形内
            ImageResizeMode.Fit when options.Width > 0 && options.Height > 0 =>
                $"'min({options.Width},iw)':'min({options.Height},ih)'"
                + ":force_original_aspect_ratio=decrease",

            _ => string.Empty
        };

        if (string.IsNullOrEmpty(scale)) return string.Empty;

        // -2 而非 -1：保证输出边长为偶数（多数编码器要求偶数尺寸，尤其 JPEG/YUV420）

        // 禁止放大：等比缩放到不超过原尺寸
        if (!options.AllowUpscale)
            scale += ":force_original_aspect_ratio=decrease";

        return "scale=" + scale;
    }

    /// <summary>质量参数：qscale 与 quality 的数值方向相反，必须分别处理。</summary>
    private static void BuildQualityArguments(ImageConvertOptions options, List<string> arguments)
    {
        switch (options.TargetFormat.QualityMode)
        {
            case ImageQualityMode.QScale:
                // 2（最高）–31（最低）
                arguments.Add("-q:v");
                arguments.Add(Clamp(options.Quality, 2, 31).ToString(CultureInfo.InvariantCulture));
                break;

            case ImageQualityMode.Quality:
                // 0（最低）–100（最高）
                arguments.Add("-quality");
                arguments.Add(Clamp(options.Quality, 0, 100).ToString(CultureInfo.InvariantCulture));
                break;

            case ImageQualityMode.None:
            default:
                // PNG 等无损格式用压缩级别（0 最快，9 最小）
                if (options.TargetFormat.Extension == "png")
                {
                    arguments.Add("-compression_level");
                    arguments.Add("9");
                }
                break;
        }
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    /// <summary>执行单个文件的转换。</summary>
    public static async Task<ImageConvertResult> ConvertAsync(
        string sourcePath,
        string targetPath,
        ImageConvertOptions options,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = SettingsService.Current.FfmpegPath;
        if (!FfmpegLocator.IsExecutable(ffmpeg))
            return new ImageConvertResult(sourcePath, targetPath, -1,
                StringResources.GetOr("Error_NoFfmpeg", "未配置 ffmpeg.exe，请先打开设置页指定路径。"));

        // 输出目录不存在时创建，避免批量转换逐个失败
        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            return new ImageConvertResult(sourcePath, targetPath, -1, ex.Message);
        }

        var arguments = BuildArguments(sourcePath, targetPath, options);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return new ImageConvertResult(sourcePath, targetPath, -1, ex.Message);
        }

        if (process is null)
            return new ImageConvertResult(sourcePath, targetPath, -1, "无法启动 ffmpeg。");

        await using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { /* 已退出 */ }
        });

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ImageConvertResult(sourcePath, targetPath, -1, "已取消。");
        }

        var exitCode = process.ExitCode;

        // 失败时读取错误输出，便于定位（成功时不必读取，省一次 IO）
        string? errorMessage = null;
        if (exitCode != 0)
        {
            try { errorMessage = await process.StandardError.ReadToEndAsync().ConfigureAwait(false); }
            catch { /* 读取失败不影响主结果 */ }

            if (string.IsNullOrWhiteSpace(errorMessage))
                errorMessage = $"转换失败（退出码 {exitCode}）";

            // 附加外部依赖提示，帮助用户理解原因
            var hint = ImageFormatCatalog.GetDependencyHint(sourcePath);
            if (hint is not null) errorMessage = hint + "\n\n" + errorMessage;
        }

        process.Dispose();
        return new ImageConvertResult(sourcePath, targetPath, exitCode, errorMessage);
    }

    /// <summary>构建用于界面展示的命令行文本。</summary>
    public static string BuildDisplayText(string sourcePath, string targetPath, ImageConvertOptions options)
    {
        var parts = BuildArguments(sourcePath, targetPath, options);
        var escaped = new List<string>(parts.Count);

        foreach (var part in parts)
            escaped.Add(part.Contains(' ') || part.Contains('"')
                ? $"\"{part.Replace("\"", "\\\"")}\""
                : part);

        return "ffmpeg " + string.Join(' ', escaped);
    }
}
