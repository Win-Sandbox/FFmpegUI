using FFmpegUI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FFmpegUI.Services;

/// <summary>ffprobe 命令构建器：把 <see cref="FfprobeOptions"/> 转换为参数列表。
/// 与 <see cref="FfmpegCommandBuilder"/> 同构，是 ffprobe 的唯一命令生成入口。
///
/// 官方 ffprobe 语法：<c>ffprobe [全局选项] [输入选项] -i 输入文件</c>
/// 注意 ffprobe 没有输出文件，所有 -show_*/-print_format 都属于「写入器选项」，
/// 官方文档中它们的位置在输入文件之前（与 ffmpeg 的输出选项在输出文件之前同理）。</summary>
public static class FfprobeCommandBuilder
{
    /// <summary>输出格式枚举到官方格式名的映射。</summary>
    private static string ToFormatName(FfprobeOutputFormat format) => format switch
    {
        FfprobeOutputFormat.Default => "default",
        FfprobeOutputFormat.Compact => "compact",
        FfprobeOutputFormat.Csv => "csv",
        FfprobeOutputFormat.Flat => "flat",
        FfprobeOutputFormat.Ini => "ini",
        FfprobeOutputFormat.Json => "json",
        FfprobeOutputFormat.Xml => "xml",
        _ => "json"
    };

    /// <summary>日志级别枚举到官方名称的映射。</summary>
    private static string ToLogLevelName(FfprobeLogLevel level) => level switch
    {
        FfprobeLogLevel.Quiet => "quiet",
        FfprobeLogLevel.Panic => "panic",
        FfprobeLogLevel.Fatal => "fatal",
        FfprobeLogLevel.Error => "error",
        FfprobeLogLevel.Warning => "warning",
        FfprobeLogLevel.Info => "info",
        FfprobeLogLevel.Verbose => "verbose",
        FfprobeLogLevel.Debug => "debug",
        FfprobeLogLevel.Trace => "trace",
        _ => "quiet"
    };

    /// <summary>哈希算法枚举到官方名称的映射。</summary>
    private static string ToHashName(FfprobeHashAlgorithm algorithm) => algorithm switch
    {
        FfprobeHashAlgorithm.MD5 => "MD5",
        FfprobeHashAlgorithm.murmur3 => "murmur3",
        FfprobeHashAlgorithm.RIPEMD128 => "RIPEMD128",
        FfprobeHashAlgorithm.RIPEMD160 => "RIPEMD160",
        FfprobeHashAlgorithm.RIPEMD256 => "RIPEMD256",
        FfprobeHashAlgorithm.RIPEMD320 => "RIPEMD320",
        FfprobeHashAlgorithm.SHA160 => "SHA160",
        FfprobeHashAlgorithm.SHA224 => "SHA224",
        FfprobeHashAlgorithm.SHA256 => "SHA256",
        FfprobeHashAlgorithm.SHA512_224 => "SHA512/224",
        FfprobeHashAlgorithm.SHA512_256 => "SHA512/256",
        FfprobeHashAlgorithm.SHA384 => "SHA384",
        FfprobeHashAlgorithm.SHA512 => "SHA512",
        FfprobeHashAlgorithm.CRC32 => "CRC32",
        FfprobeHashAlgorithm.adler32 => "adler32",
        _ => string.Empty
    };

    /// <summary>构建 ffprobe 参数列表（不含可执行文件名）。</summary>
    public static List<string> Build(FfprobeOptions options)
    {
        var arguments = new List<string>();

        // 1. 全局选项
        BuildGlobal(options, arguments);

        // 2. 能力查询类命令到此为止（不需要输入文件与结构化输出选项）
        if (options.IsCapabilityQuery)
        {
            BuildCapability(options, arguments);
            return arguments;
        }

        // 3. 写入器选项（-show_* / -print_format 等）
        BuildWriter(options, arguments);

        // 4. 输入选项与输入文件
        BuildInput(options, arguments);

        return arguments;
    }

    /// <summary>构建用于人类阅读的命令行文本（界面展示用，不用于实际执行）。</summary>
    public static string BuildDisplayText(FfprobeOptions options, string executableName = "ffprobe")
    {
        var arguments = Build(options);
        // 仅做展示用的简单转义：参数含空格或引号时用双引号包裹
        var escaped = new List<string>(arguments.Count);
        foreach (var argument in arguments)
        {
            escaped.Add(argument.Contains(' ') || argument.Contains('"')
                ? $"\"{argument.Replace("\"", "\\\"")}\""
                : argument);
        }
        return executableName + " " + string.Join(' ', escaped);
    }

    /// <summary>全局选项：横幅、日志级别、自定义全局参数。</summary>
    private static void BuildGlobal(FfprobeOptions options, List<string> arguments)
    {
        if (options.HideBanner)
            arguments.Add("-hide_banner");

        // -show_log 需要日志实际产生，故显式 quiet 会与其冲突，此时改用 info
        var logLevel = options.LogLevel;
        if (options.ShowLog && logLevel == FfprobeLogLevel.Quiet)
            logLevel = FfprobeLogLevel.Info;

        arguments.Add("-v");
        arguments.Add(ToLogLevelName(logLevel));

        arguments.AddRange(FfmpegCommandBuilder.SplitArguments(options.CustomGlobalArguments));
    }

    /// <summary>写入器选项：输出格式、数值显示、各 section 开关、过滤与统计。</summary>
    private static void BuildWriter(FfprobeOptions options, List<string> arguments)
    {
        // 输出格式
        var formatName = !string.IsNullOrWhiteSpace(options.CustomOutputFormat)
            ? options.CustomOutputFormat
            : ToFormatName(options.OutputFormat);

        if (!string.IsNullOrEmpty(formatName))
        {
            arguments.Add("-print_format");
            arguments.Add(formatName);
        }

        // 数值显示方式
        if (options.Pretty) arguments.Add("-pretty");
        if (options.Unit) arguments.Add("-unit");
        if (options.Prefix) arguments.Add("-prefix");
        if (options.ByteBinaryPrefix) arguments.Add("-byte_binary_prefix");
        if (options.Sexagesimal) arguments.Add("-sexagesimal");
        if (options.Bitexact) arguments.Add("-bitexact");
        if (options.ShowPrivateData) arguments.Add("-show_private_data");

        // 过滤与统计
        if (!string.IsNullOrWhiteSpace(options.SelectStreams))
        {
            arguments.Add("-select_streams");
            arguments.Add(options.SelectStreams);
        }

        if (!string.IsNullOrWhiteSpace(options.ReadIntervals))
        {
            arguments.Add("-read_intervals");
            arguments.Add(options.ReadIntervals);
        }

        if (options.CountFrames) arguments.Add("-count_frames");
        if (options.CountPackets) arguments.Add("-count_packets");

        // show_entries 与 show_* 互斥：官方规定 show_entries 优先，
        // 同时给出会让 ffprobe 报错，故二者选其一
        if (!string.IsNullOrWhiteSpace(options.ShowEntries))
        {
            arguments.Add("-show_entries");
            arguments.Add(options.ShowEntries);
        }
        else
        {
            if (options.ShowFormat) arguments.Add("-show_format");
            if (options.ShowStreams) arguments.Add("-show_streams");
            if (options.ShowPackets) arguments.Add("-show_packets");
            if (options.ShowFrames) arguments.Add("-show_frames");
            if (options.ShowPrograms) arguments.Add("-show_programs");
            if (options.ShowChapters) arguments.Add("-show_chapters");
            if (options.ShowError) arguments.Add("-show_error");
            if (options.ShowLog) arguments.Add("-show_log");

            if (options.ShowData) arguments.Add("-show_data");

            var hash = ToHashName(options.ShowDataHash);
            if (!string.IsNullOrEmpty(hash))
            {
                arguments.Add("-show_data_hash");
                arguments.Add(hash);
            }
        }
    }

    /// <summary>输入选项与输入文件（必须排在最后）。</summary>
    private static void BuildInput(FfprobeOptions options, List<string> arguments)
    {
        // 强制输入格式
        if (!string.IsNullOrWhiteSpace(options.InputFormat))
        {
            arguments.Add("-f");
            arguments.Add(options.InputFormat);
        }

        arguments.AddRange(FfmpegCommandBuilder.SplitArguments(options.CustomInputArguments));

        if (!string.IsNullOrWhiteSpace(options.InputPath))
        {
            arguments.Add("-i");
            arguments.Add(options.InputPath);
        }
    }

    /// <summary>能力查询类选项（-version / -codecs / -filters ...）。
    /// 这些选项不需要输入文件，也不应与结构化输出选项混用。</summary>
    private static void BuildCapability(FfprobeOptions options, List<string> arguments)
    {
        if (options.ShowVersion) arguments.Add("-version");
        if (options.ShowLicense) arguments.Add("-L");
        if (options.ShowBuildConfiguration) arguments.Add("-buildconf");
        if (options.ListFormats) arguments.Add("-formats");
        if (options.ListDemuxers) arguments.Add("-demuxers");
        if (options.ListMuxers) arguments.Add("-muxers");
        if (options.ListDevices) arguments.Add("-devices");
        if (options.ListCodecs) arguments.Add("-codecs");
        if (options.ListDecoders) arguments.Add("-decoders");
        if (options.ListEncoders) arguments.Add("-encoders");
        if (options.ListBitstreamFilters) arguments.Add("-bsfs");
        if (options.ListProtocols) arguments.Add("-protocols");
        if (options.ListFilters) arguments.Add("-filters");
        if (options.ListPixelFormats) arguments.Add("-pix_fmts");
        if (options.ListChannelLayouts) arguments.Add("-layouts");
        if (options.ListSampleFormats) arguments.Add("-sample_fmts");
        if (options.ListColors) arguments.Add("-colors");
        if (options.ListHardwareAccels) arguments.Add("-hwaccels");
        if (options.PrintSections) arguments.Add("-sections");

        // -sources / -sinks 需要后跟设备名
        if (!string.IsNullOrWhiteSpace(options.ListSources))
        {
            arguments.Add("-sources");
            arguments.Add(options.ListSources);
        }

        if (!string.IsNullOrWhiteSpace(options.ListSinks))
        {
            arguments.Add("-sinks");
            arguments.Add(options.ListSinks);
        }
    }
}
