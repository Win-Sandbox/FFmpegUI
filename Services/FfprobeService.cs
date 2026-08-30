using FFmpegUI.Helpers;
using FFmpegUI.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.Services;

/// <summary>ffprobe 执行结果。</summary>
public sealed record FfprobeRunResult(
    int ExitCode,
    bool Canceled,
    string StandardOutput,
    string StandardError,
    string? ErrorMessage)
{
    public bool Succeeded => !Canceled && ExitCode == 0 && ErrorMessage is null;

    /// <summary>实际有效输出。
    /// 版本差异处理：ffprobe 的能力查询（-version / -codecs / -filters 等）
    /// 在部分构建中写入 stderr 而非 stdout（历史行为不一致），
    /// 故优先取 stdout，为空时回退到 stderr，避免界面显示空白。</summary>
    public string EffectiveOutput =>
        !string.IsNullOrWhiteSpace(StandardOutput) ? StandardOutput : StandardError;
}

/// <summary>媒体信息探测服务（调用 ffprobe），同时支持执行任意 ffprobe 命令。
///
/// 两类用途：
/// <list type="number">
/// <item>结构化探测：<see cref="ProbeAsync"/> 用官方推荐的
/// <c>-print_format json</c> 解析为 <see cref="MediaFileInfo"/>；</item>
/// <item>通用执行：<see cref="RunAsync"/> 按 <see cref="FfprobeOptions"/> 执行任意命令，
/// 覆盖全部 ffprobe 参数（含能力查询 -version/-codecs/-filters 等），返回原始输出。</item>
/// </list>
/// 命令生成统一由 <see cref="FfprobeCommandBuilder"/> 负责。</summary>
public static class FfprobeService
{
    /// <summary>探测指定文件的媒体信息；失败时返回 null。
    /// 保持原有签名，供各页面的 ViewModel 直接调用。</summary>
    public static async Task<MediaFileInfo?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var ffprobe = SettingsService.Current.FfprobePath;
        if (!FfmpegLocator.IsExecutable(ffprobe) || !File.Exists(filePath)) return null;

        var info = new MediaFileInfo { FilePath = filePath };

        try { info.FileSize = new FileInfo(filePath).Length; } catch { /* 文件信息读取失败不影响后续 */ }

        var options = FfprobeOptions.CreateDefault(filePath);
        var result = await RunAsync(options, cancellationToken).ConfigureAwait(false);

        // ffprobe 在不同构建/场景下可能把输出写到 stdout 或 stderr，
        // 使用 EffectiveOutput 统一处理（见 FfprobeRunResult.EffectiveOutput）。
        var output = result.EffectiveOutput;
        if (!result.Succeeded || string.IsNullOrWhiteSpace(output)) return null;

        try
        {
            return Parse(output, info);
        }
        catch (JsonException ex)
        {
            // JSON 解析失败通常意味着输出被污染（如自定义参数引入了额外输出）
            App.LogCrash(ex, "FfprobeService.Parse");
            return null;
        }
    }

    /// <summary>按给定选项执行 ffprobe，返回原始输出。
    /// 这是 ffprobe 的唯一执行入口，可覆盖全部参数与能力查询。</summary>
    public static async Task<FfprobeRunResult> RunAsync(
        FfprobeOptions options,
        CancellationToken cancellationToken = default)
    {
        var ffprobe = SettingsService.Current.FfprobePath;
        if (!FfmpegLocator.IsExecutable(ffprobe))
            return new FfprobeRunResult(-1, false, string.Empty, string.Empty,
                StringResources.GetOr("Error_NoFfprobe", "未配置 ffprobe.exe，请先打开设置页指定路径。"));

        var arguments = FfprobeCommandBuilder.Build(options);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
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
            return new FfprobeRunResult(-1, false, string.Empty, string.Empty,
                StringResources.FormatOr("Error_StartFfprobeFormat",
                    $"启动 ffprobe 失败：{ex.Message}", ex.Message));
        }

        if (process is null)
            return new FfprobeRunResult(-1, false, string.Empty, string.Empty,
                StringResources.GetOr("Error_StartFfprobeFailed", "无法启动 ffprobe（进程创建失败）。"));

        // 取消时终止进程树
        await using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { /* 进程已退出 */ }
        });

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FfprobeService.WaitForExit");
        }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        process.Dispose();

        if (cancellationToken.IsCancellationRequested)
            return new FfprobeRunResult(exitCode, true, string.Empty, string.Empty, null);

        return new FfprobeRunResult(
            exitCode,
            false,
            SafeAwait(stdout),
            SafeAwait(stderr),
            exitCode == 0
                ? null
                : StringResources.FormatOr("Error_ExitCodeFormat",
                    $"ffprobe 退出码 {exitCode}", exitCode));
    }

    /// <summary>安全地获取任务结果，避免读取异常导致整个调用失败。</summary>
    private static string SafeAwait(Task<string> task)
    {
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FfprobeService.ReadOutput");
            return string.Empty;
        }
    }

    #region 能力查询便捷方法（均为 ffprobe 的官方选项包装）

    /// <summary>获取 ffprobe 版本信息（-version）。</summary>
    public static Task<FfprobeRunResult> GetVersionAsync(CancellationToken cancellationToken = default)
        => RunAsync(new FfprobeOptions { ShowVersion = true }, cancellationToken);

    /// <summary>列出支持的编解码器（-codecs）。</summary>
    public static Task<FfprobeRunResult> ListCodecsAsync(CancellationToken cancellationToken = default)
        => RunAsync(new FfprobeOptions { ListCodecs = true }, cancellationToken);

    /// <summary>列出可用滤镜（-filters）。</summary>
    public static Task<FfprobeRunResult> ListFiltersAsync(CancellationToken cancellationToken = default)
        => RunAsync(new FfprobeOptions { ListFilters = true }, cancellationToken);

    /// <summary>列出支持的封装格式（-formats）。</summary>
    public static Task<FfprobeRunResult> ListFormatsAsync(CancellationToken cancellationToken = default)
        => RunAsync(new FfprobeOptions { ListFormats = true }, cancellationToken);

    /// <summary>列出可用硬件加速方式（-hwaccels）。</summary>
    public static Task<FfprobeRunResult> ListHardwareAccelsAsync(CancellationToken cancellationToken = default)
        => RunAsync(new FfprobeOptions { ListHardwareAccels = true }, cancellationToken);

    #endregion

    #region JSON 解析（原 ProbeAsync 的解析逻辑）

    private static MediaFileInfo? Parse(string json, MediaFileInfo info)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // 容器级信息
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("format_name", out var name))
                info.ContainerFormat = name.GetString()?.Split(',')[0] ?? string.Empty;

            if (format.TryGetProperty("duration", out var duration) &&
                double.TryParse(duration.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
                info.Duration = TimeSpan.FromSeconds(seconds);

            if (format.TryGetProperty("size", out var size) &&
                long.TryParse(size.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var bytes))
                info.FileSize = bytes;

            if (format.TryGetProperty("bit_rate", out var bitrate) &&
                long.TryParse(bitrate.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var bps))
                info.BitRate = bps;
        }

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            // 同类流的相对序号（用于 -map 0:a:0 这类限定符）
            var counters = new Dictionary<StreamKind, int>();

            foreach (var stream in streams.EnumerateArray())
            {
                var item = new MediaStreamInfo
                {
                    Index = GetInt(stream, "index") ?? 0,
                    CodecName = GetString(stream, "codec_name"),
                    CodecLongName = GetString(stream, "codec_long_name"),
                    Profile = GetString(stream, "profile"),
                    PixelFormat = GetString(stream, "pix_fmt"),
                    DisplayAspectRatio = GetString(stream, "display_aspect_ratio"),
                    Width = GetInt(stream, "width"),
                    Height = GetInt(stream, "height"),
                    Channels = GetInt(stream, "channels"),
                    Kind = ParseKind(GetString(stream, "codec_type"))
                };

                if (TryGetInt(stream, "sample_rate", out var sampleRate)) item.SampleRate = sampleRate;
                if (TryGetInt(stream, "bit_rate", out var streamBitrate)) item.BitRate = streamBitrate;
                item.ChannelLayout = GetString(stream, "channel_layout");

                // 帧率：优先 avg_frame_rate（r_frame_rate 在部分容器下不准确）
                var frameRateText = GetString(stream, "avg_frame_rate");
                if (string.IsNullOrEmpty(frameRateText) || frameRateText == "0/0")
                    frameRateText = GetString(stream, "r_frame_rate");
                item.FrameRate = ParseRational(frameRateText);

                if (stream.TryGetProperty("tags", out var tags))
                {
                    item.Language = GetString(tags, "language");
                    item.Title = GetString(tags, "title");
                }

                if (stream.TryGetProperty("disposition", out var disposition))
                    item.IsDefault = GetInt(disposition, "default") == 1;

                if (stream.TryGetProperty("duration", out var durationElement))
                    item.Duration = TimeSpan.FromSeconds(durationElement.GetDouble());

                counters.TryGetValue(item.Kind, out var relative);
                item.RelativeIndex = relative;
                counters[item.Kind] = relative + 1;

                info.Streams.Add(item);
            }
        }

        // 容器时长缺失时回退到最长的视频/音频流时长
        if (info.Duration <= TimeSpan.Zero && info.Streams.Count > 0)
            info.Duration = info.Streams.Max(s => s.Duration);

        return info;
    }

    private static StreamKind ParseKind(string value) => value switch
    {
        "video" => StreamKind.Video,
        "audio" => StreamKind.Audio,
        "subtitle" => StreamKind.Subtitle,
        "attachment" => StreamKind.Attachment,
        "data" => StreamKind.Data,
        _ => StreamKind.Unknown
    };

    /// <summary>解析 "30000/1001" 形式的有理数。</summary>
    private static double? ParseRational(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('/');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var numerator)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var denominator)) return null;
        if (denominator == 0) return null;
        var result = numerator / denominator;
        return result > 0 ? result : null;
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int? GetInt(JsonElement element, string property)
        => TryGetInt(element, property, out var value) ? value : null;

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var json)) return false;

        // ffprobe 字段既可能是数字也可能是字符串
        return json.ValueKind switch
        {
            JsonValueKind.Number => json.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(json.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    #endregion
}
