using FFmpegUI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FFmpegUI.Services;

/// <summary>FFmpeg 命令行生成结果：分三段拼装，保证参数顺序正确
/// （ffmpeg 官方语法：全局选项 → 输入选项/输入 → 输出选项/输出）。</summary>
public sealed class FfmpegCommand
{
    /// <summary>全局选项（可执行文件之后、输入之前）。</summary>
    public List<string> Global { get; } = new();

    /// <summary>输入相关参数（含 -i 及其后的附加输入）。</summary>
    public List<string> Input { get; } = new();

    /// <summary>输出相关参数（含输出文件路径）。</summary>
    public List<string> Output { get; } = new();

    /// <summary>展开为完整参数列表（供 ProcessStartInfo.ArgumentList 使用，无需手动加引号）。</summary>
    public List<string> ToArgumentList()
    {
        var list = new List<string>(Global.Count + Input.Count + Output.Count);
        list.AddRange(Global);
        list.AddRange(Input);
        list.AddRange(Output);
        return list;
    }

    /// <summary>生成用于在界面上展示的命令行（含引号）。</summary>
    public string ToDisplayString(string executable)
    {
        var parts = new List<string> { Quote(executable) };
        parts.AddRange(Global.Select(Quote));
        parts.AddRange(Input.Select(Quote));
        parts.AddRange(Output.Select(Quote));
        return string.Join(' ', parts);
    }

    /// <summary>按官方做法给含空格/引号的参数加引号。</summary>
    private static string Quote(string value)
        => value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
}

/// <summary>把 <see cref="FfmpegOptions"/> 转换为 ffmpeg 命令行参数。
/// 这是全应用唯一的命令生成入口，任何页面的参数变化都会经过此处，
/// 保证「界面所见」与「实际执行」严格一致。</summary>
public static class FfmpegCommandBuilder
{
    /// <summary>生成命令。duration 用于目标体积模式的码率换算（可为 Zero）。</summary>
    public static FfmpegCommand Build(FfmpegOptions options, MediaFileInfo? input, TimeSpan duration)
    {
        var command = new FfmpegCommand();

        // 全局：隐藏横幅、禁止读取标准输入（避免批处理场景卡住）
        command.Global.Add("-hide_banner");
        command.Global.Add("-nostdin");

        BuildInput(options, command);
        BuildOutput(options, command, duration);

        // -y / -n：覆盖策略必须在输出文件之前
        command.Output.Insert(0, options.OverwriteOutput ? "-y" : "-n");

        return command;
    }

    #region 输入段

    private static void BuildInput(FfmpegOptions options, FfmpegCommand command)
    {
        // 原始参数直通模式：完全使用用户提供的模板
        if (options.UseRawArgumentsOnly)
        {
            var raw = SplitArguments(options.RawArguments);
            // 模板中出现 {input}/{output} 时替换，其余原样使用
            for (var i = 0; i < raw.Count; i++)
            {
                raw[i] = raw[i]
                    .Replace("{input}", options.InputPath)
                    .Replace("{output}", options.OutputPath);
            }
            command.Input.AddRange(raw);
            return;
        }

        // 硬件解码（-hwaccel）：仅在选择硬件加速时启用
        var hwaccel = CodecCatalog.GetHardwareDecodeArgument(options.HardwareAccel);
        if (!string.IsNullOrEmpty(hwaccel))
        {
            command.Input.Add("-hwaccel");
            command.Input.Add(hwaccel);

            // 指定用哪一块显卡（多显卡机器），必须与 -hwaccel 配对使用
            if (!string.IsNullOrWhiteSpace(options.HardwareDevice))
            {
                command.Input.Add("-hwaccel_device");
                command.Input.Add(options.HardwareDevice);
            }
        }

        // 以下均为输入选项，必须排在 -i 之前

        // 按原始帧率读取（-re），模拟实时流
        if (options.RealtimeInput)
            command.Input.Add("-re");

        // 强制输入格式，自动探测失败时使用
        if (!string.IsNullOrWhiteSpace(options.InputFormat))
        {
            command.Input.Add("-f");
            command.Input.Add(options.InputFormat);
        }

        // 输入时间偏移（-itsoffset）：正值延后、负值提前，用于音画同步
        if (options.InputTimeOffset.HasValue && options.InputTimeOffset.Value != TimeSpan.Zero)
        {
            command.Input.Add("-itsoffset");
            command.Input.Add(FormatSignedTime(options.InputTimeOffset.Value));
        }

        // 输入循环次数（-stream_loop）：-1 为无限循环
        if (options.StreamLoop != 0)
        {
            command.Input.Add("-stream_loop");
            command.Input.Add(options.StreamLoop.ToString(CultureInfo.InvariantCulture));
        }

        // 输入级时长限制（-t）：先于输出级 -t 生效
        if (options.InputDuration.HasValue && options.InputDuration.Value > TimeSpan.Zero)
        {
            command.Input.Add("-t");
            command.Input.Add(FfmpegOptions.FormatTime(options.InputDuration.Value));
        }

        // 自定义输入参数（高级用法：-hwaccel_output_format、-vaapi_device 等）
        command.Input.AddRange(SplitArguments(options.CustomInputArguments));

        // -ss 放在 -i 之前：输入定位，速度快（适合长视频粗剪）
        if (options.SeekBeforeInput && options.StartTime.HasValue && options.StartTime.Value > TimeSpan.Zero)
        {
            command.Input.Add("-ss");
            command.Input.Add(FfmpegOptions.FormatTime(options.StartTime.Value));
        }

        command.Input.Add("-i");
        command.Input.Add(options.InputPath);

        // 附加输入（混流/合并场景）：索引 1, 2, 3...
        foreach (var additional in options.AdditionalInputs.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            command.Input.Add("-i");
            command.Input.Add(additional);
        }
    }

    #endregion

    #region 输出段

    private static void BuildOutput(FfmpegOptions options, FfmpegCommand command, TimeSpan duration)
    {
        if (options.UseRawArgumentsOnly)
        {
            // 直通模式：输出文件路径已包含在用户模板中，此处不再添加
            return;
        }

        // 输出定位（-ss 在 -i 之后）：精度更高
        if (!options.SeekBeforeInput && options.StartTime.HasValue && options.StartTime.Value > TimeSpan.Zero)
        {
            command.Output.Add("-ss");
            command.Output.Add(FfmpegOptions.FormatTime(options.StartTime.Value));
        }

        if (options.EndTime.HasValue && options.EndTime.Value > TimeSpan.Zero)
        {
            command.Output.Add("-to");
            command.Output.Add(FfmpegOptions.FormatTime(options.EndTime.Value));
        }
        else if (options.Duration.HasValue && options.Duration.Value > TimeSpan.Zero)
        {
            command.Output.Add("-t");
            command.Output.Add(FfmpegOptions.FormatTime(options.Duration.Value));
        }

        BuildMaps(options, command);
        BuildVideo(options, command, duration);
        BuildAudio(options, command);
        BuildSubtitle(options, command);
        BuildFilters(options, command);
        BuildContainerOptions(options, command);

        command.Output.AddRange(SplitArguments(options.CustomOutputArguments));

        command.Output.Add(options.OutputPath);
    }

    /// <summary>流映射。用户显式指定了任意流时，为所有保留的流生成显式 map；
    /// 未指定的类型用 -map 0:v? 的「可选」形式，避免流缺失时报错（ffmpeg 官方建议）。</summary>
    private static void BuildMaps(FfmpegOptions options, FfmpegCommand command)
    {
        // KeepData 必须计入：一旦为数据流生成 -map 0:d?，ffmpeg 便只输出被 map 指定的流，
        // 若此处不同步为音视频生成 map，视频/音频会被静默丢弃
        var hasExplicit = options.VideoStreamIndex.HasValue
                          || options.AudioStreamIndex.HasValue
                          || options.SubtitleStreamIndex.HasValue
                          || options.ExtraMaps.Count > 0
                          || options.KeepData;

        foreach (var map in options.ExtraMaps.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            command.Output.Add("-map");
            command.Output.Add(map);
        }

        if (!hasExplicit)
        {
            // 完全交由 ffmpeg 自动选择：单输入场景最稳妥
            if (!options.KeepVideo) command.Output.Add("-vn");
            if (!options.KeepAudio) command.Output.Add("-an");
            if (!options.KeepSubtitle) command.Output.Add("-sn");
            return;
        }

        if (options.KeepVideo)
        {
            command.Output.Add("-map");
            command.Output.Add(options.VideoStreamIndex.HasValue
                ? $"0:v:{options.VideoStreamIndex.Value}?"
                : "0:v?");
        }

        if (options.KeepAudio)
        {
            command.Output.Add("-map");
            command.Output.Add(options.AudioStreamIndex.HasValue
                ? $"0:a:{options.AudioStreamIndex.Value}?"
                : "0:a?");
        }

        if (options.KeepSubtitle)
        {
            command.Output.Add("-map");
            command.Output.Add(options.SubtitleStreamIndex.HasValue
                ? $"0:s:{options.SubtitleStreamIndex.Value}?"
                : "0:s?");
        }

        // 数据流（如 GoPro GPMD、MOV 时间码轨）。FFmpeg 默认不复制数据流，
        // 仅当用户显式要求保留时才生成映射
        if (options.KeepData)
        {
            command.Output.Add("-map");
            command.Output.Add("0:d?");
        }
    }

    private static void BuildVideo(FfmpegOptions options, FfmpegCommand command, TimeSpan duration)
    {
        if (!options.KeepVideo) return;

        var codec = ResolveVideoCodec(options);

        if (string.Equals(codec, "copy", StringComparison.OrdinalIgnoreCase))
        {
            command.Output.Add("-c:v");
            command.Output.Add("copy");
            // 复制流时不可设置编码参数，直接返回
            return;
        }

        if (!string.IsNullOrWhiteSpace(codec))
        {
            command.Output.Add("-c:v");
            command.Output.Add(codec);
        }

        // 码率控制
        switch (options.VideoRateControl)
        {
            case VideoRateControl.Crf:
                command.Output.Add("-crf");
                command.Output.Add(options.Crf.ToString(CultureInfo.InvariantCulture));
                break;

            case VideoRateControl.Qp:
                command.Output.Add("-qp");
                command.Output.Add(options.Qp.ToString(CultureInfo.InvariantCulture));
                break;

            case VideoRateControl.AverageBitrate:
                command.Output.Add("-b:v");
                command.Output.Add($"{options.VideoBitrateKbps}k");
                break;

            case VideoRateControl.ConstantBitrate:
                command.Output.Add("-b:v");
                command.Output.Add($"{options.VideoBitrateKbps}k");
                command.Output.Add("-maxrate");
                command.Output.Add($"{options.MaxBitrateKbps}k");
                command.Output.Add("-minrate");
                command.Output.Add($"{options.VideoBitrateKbps}k");
                command.Output.Add("-bufsize");
                command.Output.Add($"{options.BufferSizeKbps}k");
                break;

            case VideoRateControl.TargetSize:
                // 目标体积 → 平均码率：总比特数 = 目标大小 × 8，扣除音轨后除以时长
                var targetBitrate = CalculateTargetBitrate(options, duration);
                command.Output.Add("-b:v");
                command.Output.Add($"{targetBitrate.ToString("0", CultureInfo.InvariantCulture)}k");
                break;
        }

        if (!string.IsNullOrWhiteSpace(options.Preset))
        {
            command.Output.Add("-preset");
            command.Output.Add(options.Preset);
        }

        if (!string.IsNullOrWhiteSpace(options.Tune))
        {
            command.Output.Add("-tune");
            command.Output.Add(options.Tune);
        }

        if (!string.IsNullOrWhiteSpace(options.Profile))
        {
            command.Output.Add("-profile:v");
            command.Output.Add(options.Profile);
        }

        if (!string.IsNullOrWhiteSpace(options.PixelFormat))
        {
            command.Output.Add("-pix_fmt");
            command.Output.Add(options.PixelFormat);
        }

        if (options.FrameRate.HasValue && options.FrameRate.Value > 0)
        {
            command.Output.Add("-r");
            command.Output.Add(options.FrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        // 恒定质量因子（-q:v，等价别名 -qscale:v）：MPEG-2/4、MJPEG 等用它替代 CRF
        if (options.VideoQuality.HasValue)
        {
            command.Output.Add("-q:v");
            command.Output.Add(options.VideoQuality.Value.ToString(CultureInfo.InvariantCulture));
        }

        // NVENC 恒定质量模式（-cq）：硬件编码器的质量控制参数
        if (options.NvencCq.HasValue)
        {
            command.Output.Add("-cq");
            command.Output.Add(options.NvencCq.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(options.Level))
        {
            command.Output.Add("-level");
            command.Output.Add(options.Level);
        }

        if (options.BFrames.HasValue)
        {
            command.Output.Add("-bf");
            command.Output.Add(options.BFrames.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (options.RefFrames.HasValue)
        {
            command.Output.Add("-refs");
            command.Output.Add(options.RefFrames.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (options.UseKeyframeInterval)
        {
            command.Output.Add("-g");
            command.Output.Add(options.KeyframeInterval.ToString(CultureInfo.InvariantCulture));
        }

        if (options.Threads > 0)
        {
            command.Output.Add("-threads");
            command.Output.Add(options.Threads.ToString(CultureInfo.InvariantCulture));
        }

        // 编码器私有参数直通：按当前编码器自动选择 -x264-params / -x265-params
        if (!string.IsNullOrWhiteSpace(options.EncoderPrivateParams))
        {
            var privateOption = codec switch
            {
                "libx264" or "h264_nvenc" or "h264_qsv" or "h264_amf" => "-x264-params",
                "libx265" or "hevc_nvenc" or "hevc_qsv" or "hevc_amf" => "-x265-params",
                _ => null
            };

            // 其余编码器不使用 x264/x265 私有参数入口，避免生成无效参数
            if (privateOption is not null)
            {
                command.Output.Add(privateOption);
                command.Output.Add(options.EncoderPrivateParams);
            }
        }

        BuildColorOptions(options, command);
    }

    /// <summary>色彩空间 / HDR 三件套（-color_primaries / -color_trc / -colorspace）。
    /// 单独抽出的原因：这三个参数对视频与图片编码均适用，但不属于码率控制。</summary>
    private static void BuildColorOptions(FfmpegOptions options, FfmpegCommand command)
    {
        if (!string.IsNullOrWhiteSpace(options.ColorPrimaries))
        {
            command.Output.Add("-color_primaries");
            command.Output.Add(options.ColorPrimaries);
        }

        if (!string.IsNullOrWhiteSpace(options.ColorTransfer))
        {
            command.Output.Add("-color_trc");
            command.Output.Add(options.ColorTransfer);
        }

        if (!string.IsNullOrWhiteSpace(options.ColorSpace))
        {
            command.Output.Add("-colorspace");
            command.Output.Add(options.ColorSpace);
        }
    }

    private static void BuildAudio(FfmpegOptions options, FfmpegCommand command)
    {
        if (!options.KeepAudio) return;

        var codec = options.AudioCodec;
        if (string.IsNullOrWhiteSpace(codec) && options.Container != ContainerFormat.Custom)
            codec = CodecCatalog.GetContainer(options.Container).DefaultAudioCodec ?? string.Empty;

        if (string.IsNullOrWhiteSpace(codec)) return;

        command.Output.Add("-c:a");
        command.Output.Add(codec);

        if (string.Equals(codec, "copy", StringComparison.OrdinalIgnoreCase)) return;

        switch (options.AudioRateControl)
        {
            case AudioRateControl.Bitrate:
                command.Output.Add("-b:a");
                command.Output.Add($"{options.AudioBitrateKbps}k");
                break;

            case AudioRateControl.Quality:
                command.Output.Add("-q:a");
                command.Output.Add(options.AudioQuality.ToString(CultureInfo.InvariantCulture));
                break;
        }

        if (options.SampleRate > 0)
        {
            command.Output.Add("-ar");
            command.Output.Add(options.SampleRate.ToString(CultureInfo.InvariantCulture));
        }

        if (options.Channels > 0)
        {
            command.Output.Add("-ac");
            command.Output.Add(options.Channels.ToString(CultureInfo.InvariantCulture));
        }

        // 音量（-vol，0–256，256 为原始音量）。与 volume 滤镜不同，它不触发重编码
        if (options.AudioVolume.HasValue)
        {
            command.Output.Add("-vol");
            command.Output.Add(options.AudioVolume.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void BuildSubtitle(FfmpegOptions options, FfmpegCommand command)
    {
        if (!options.KeepSubtitle) return;

        var codec = options.SubtitleCodec;
        if (string.IsNullOrWhiteSpace(codec) && options.Container != ContainerFormat.Custom)
            codec = CodecCatalog.GetContainer(options.Container).DefaultSubtitleCodec ?? string.Empty;

        // 部分容器（如 AVI）不支持字幕，未指定编码器时不输出 -c:s
        if (string.IsNullOrWhiteSpace(codec)) return;

        command.Output.Add("-c:s");
        command.Output.Add(codec);
    }

    /// <summary>滤镜：把界面上收集到的滤镜片段拼成 -vf / -af。
    /// 分辨率缩放统一在此处生成，避免与用户的其它滤镜冲突。</summary>
    private static void BuildFilters(FfmpegOptions options, FfmpegCommand command)
    {
        var videoFilters = new List<string>(options.VideoFilters.Where(f => !string.IsNullOrWhiteSpace(f)));

        if (options.Width.HasValue && options.Height.HasValue &&
            options.Width.Value > 0 && options.Height.Value > 0)
        {
            var algorithm = string.IsNullOrWhiteSpace(options.ScaleAlgorithm)
                ? string.Empty
                : $":flags={options.ScaleAlgorithm}";
            videoFilters.Add($"scale={options.Width.Value}:{options.Height.Value}{algorithm}");
        }

        if (videoFilters.Count > 0)
        {
            command.Output.Add("-vf");
            command.Output.Add(string.Join(',', videoFilters));
        }

        var audioFilters = options.AudioFilters.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (audioFilters.Count > 0)
        {
            command.Output.Add("-af");
            command.Output.Add(string.Join(',', audioFilters));
        }
    }

    private static void BuildContainerOptions(FfmpegOptions options, FfmpegCommand command)
    {
        // MP4/MOV：把 moov 原子前移，支持边下边播
        var extension = System.IO.Path.GetExtension(options.OutputPath).TrimStart('.').ToLowerInvariant();
        if (options.FastStart && (extension is "mp4" or "mov" or "m4a"))
            command.Output.Add("-movflags");
        if (options.FastStart && (extension is "mp4" or "mov" or "m4a"))
            command.Output.Add("+faststart");

        command.Output.Add("-map_metadata");
        command.Output.Add(options.KeepMetadata ? "0" : "-1");

        command.Output.Add("-map_chapters");
        command.Output.Add(options.KeepChapters ? "0" : "-1");

        BuildGeneralOutputOptions(options, command);
        BuildMetadata(options, command);
    }

    /// <summary>通用输出选项：帧同步、复用队列、格式标志、宽高比、体积/帧数上限等。
    /// 这些参数与具体容器无关，统一放在输出段末尾、输出文件路径之前。</summary>
    private static void BuildGeneralOutputOptions(FfmpegOptions options, FfmpegCommand command)
    {
        // 强制封装格式：扩展名无法推断或输出到管道时使用
        if (!string.IsNullOrWhiteSpace(options.OutputFormat))
        {
            command.Output.Add("-f");
            command.Output.Add(options.OutputFormat);
        }

        if (!string.IsNullOrWhiteSpace(options.FrameSyncMode))
        {
            command.Output.Add("-fps_mode");
            command.Output.Add(options.FrameSyncMode);
        }

        if (options.MaxMuxingQueueSize > 0)
        {
            command.Output.Add("-max_muxing_queue_size");
            command.Output.Add(options.MaxMuxingQueueSize.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(options.FormatFlags))
        {
            command.Output.Add("-fflags");
            command.Output.Add(options.FormatFlags);
        }

        if (!string.IsNullOrWhiteSpace(options.AvoidNegativeTs))
        {
            command.Output.Add("-avoid_negative_ts");
            command.Output.Add(options.AvoidNegativeTs);
        }

        if (!string.IsNullOrWhiteSpace(options.AspectRatio))
        {
            command.Output.Add("-aspect");
            command.Output.Add(options.AspectRatio);
        }

        if (!string.IsNullOrWhiteSpace(options.SwsFlags))
        {
            command.Output.Add("-sws_flags");
            command.Output.Add(options.SwsFlags);
        }

        // 文件大小上限（-fs）：以字节为单位
        if (options.OutputSizeLimitMb > 0)
        {
            var bytes = (long)Math.Round(options.OutputSizeLimitMb * 1024 * 1024);
            command.Output.Add("-fs");
            command.Output.Add(bytes.ToString(CultureInfo.InvariantCulture));
        }

        if (options.VideoFrames > 0)
        {
            command.Output.Add("-frames:v");
            command.Output.Add(options.VideoFrames.ToString(CultureInfo.InvariantCulture));
        }

        if (options.AudioFrames > 0)
        {
            command.Output.Add("-frames:a");
            command.Output.Add(options.AudioFrames.ToString(CultureInfo.InvariantCulture));
        }

        if (options.Shortest)
            command.Output.Add("-shortest");

        if (options.CopyTimestamp)
            command.Output.Add("-copyts");

        // 注意：数据流（-map 0:d?）不在此处生成。因为 FFmpeg 一旦出现任何 -map，
        // 就只输出被 map 指定的流，在此处单独添加会导致视频/音频流被静默丢弃。
        // 故统一放在 BuildMaps 中与其他 map 一起生成。
    }

    /// <summary>元数据条目与轨道处置标记。</summary>
    private static void BuildMetadata(FfmpegOptions options, FfmpegCommand command)
    {
        foreach (var entry in options.Metadata)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value)) continue;

            command.Output.Add("-metadata");
            command.Output.Add($"{entry.Key}={entry.Value}");
        }

        // 轨道处置（-disposition:x），如 default、forced（强制显示字幕）
        if (!string.IsNullOrWhiteSpace(options.VideoDisposition))
        {
            command.Output.Add("-disposition:v");
            command.Output.Add(options.VideoDisposition);
        }

        if (!string.IsNullOrWhiteSpace(options.AudioDisposition))
        {
            command.Output.Add("-disposition:a");
            command.Output.Add(options.AudioDisposition);
        }

        if (!string.IsNullOrWhiteSpace(options.SubtitleDisposition))
        {
            command.Output.Add("-disposition:s");
            command.Output.Add(options.SubtitleDisposition);
        }
    }

    #endregion

    /// <summary>确定最终视频编码器：优先用户选择，其次容器默认，并应用硬件编码映射。</summary>
    private static string ResolveVideoCodec(FfmpegOptions options)
    {
        var codec = options.VideoCodec;

        if (string.IsNullOrWhiteSpace(codec) && options.Container != ContainerFormat.Custom)
            codec = CodecCatalog.GetContainer(options.Container).DefaultVideoCodec ?? string.Empty;

        if (string.IsNullOrWhiteSpace(codec)) return string.Empty;

        // 复制流不参与硬件编码器映射
        if (string.Equals(codec, "copy", StringComparison.OrdinalIgnoreCase)) return codec;

        return CodecCatalog.MapHardwareVideoCodec(codec, options.HardwareAccel);
    }

    /// <summary>格式化可带负号的时间偏移（-itsoffset）。
    /// 与 <see cref="FfmpegOptions.FormatTime"/> 的区别：该方法用于时长（恒为非负），
    /// 而 -itsoffset 允许负值（提前），且 FFmpeg 要求负号前不加空格。</summary>
    private static string FormatSignedTime(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var magnitude = value.Duration();
        return sign + magnitude.ToString(
            magnitude.Milliseconds == 0 ? @"hh\:mm\:ss" : @"hh\:mm\:ss\.fff",
            CultureInfo.InvariantCulture);
    }

    /// <summary>按目标体积计算视频码率（kbps）：(目标大小 × 8 × 1024 / 时长) − 音频码率。</summary>
    private static double CalculateTargetBitrate(FfmpegOptions options, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return options.VideoBitrateKbps;

        var totalBits = options.TargetSizeMb * 8 * 1024 * 1024;
        var audioBits = options.KeepAudio ? options.AudioBitrateKbps * duration.TotalSeconds : 0d;
        var videoKbps = (totalBits - audioBits * 1000d) / duration.TotalSeconds / 1000d;

        // 预留 2% 容器开销，并限制下限，避免极端情况下码率过低
        videoKbps *= 0.98;
        return videoKbps < 100 ? 100 : videoKbps;
    }

    /// <summary>把命令行字符串拆分为参数数组（支持成对引号）。</summary>
    public static List<string> SplitArguments(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) result.Add(current.ToString());

        return result;
    }
}
