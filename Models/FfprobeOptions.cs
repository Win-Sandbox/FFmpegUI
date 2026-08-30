using System;
using System.Collections.Generic;

namespace FFmpegUI.Models;

/// <summary>ffprobe 的输出打印格式（-print_format / -of）。
/// 对应官方支持的全部格式。</summary>
public enum FfprobeOutputFormat
{
    /// <summary>default：人类可读的默认格式。</summary>
    Default,

    /// <summary>compact：紧凑的 key|value 形式，便于脚本逐行解析。</summary>
    Compact,

    /// <summary>csv：逗号分隔，便于导入表格软件。</summary>
    Csv,

    /// <summary>flat：扁平化的 stream.0.key=value 形式。</summary>
    Flat,

    /// <summary>ini：INI 分组格式。</summary>
    Ini,

    /// <summary>json：官方推荐，结构化首选（本项目解析用）。</summary>
    Json,

    /// <summary>xml：XML 格式，兼容旧工具链。</summary>
    Xml
}

/// <summary>ffprobe 的日志级别（-v / -loglevel）。</summary>
public enum FfprobeLogLevel
{
    /// <summary>不显示任何输出（仅保留 print_format 的结构化数据）。解析 JSON 时必须用此项，
    /// 否则 banner 与日志会混入 stdout 破坏 JSON。</summary>
    Quiet,
    Panic,
    Fatal,
    Error,
    Warning,
    Info,
    Verbose,
    Debug,
    Trace
}

/// <summary>ffprobe 的数据哈希算法（-show_data_hash）。</summary>
public enum FfprobeHashAlgorithm
{
    /// <summary>不计算数据哈希。</summary>
    None,
    MD5,
    murmur3,
    RIPEMD128,
    RIPEMD160,
    RIPEMD256,
    RIPEMD320,
    SHA160,
    SHA224,
    SHA256,
    SHA512_224,
    SHA512_256,
    SHA384,
    SHA512,
    CRC32,
    adler32
}

/// <summary>ffprobe 参数模型，覆盖官方 ffprobe 的全部主要与高级选项。
/// 与 <see cref="FfmpegOptions"/> 的定位相同：作为「UI ↔ 命令行」的中间数据模型，
/// 命令生成统一由 <c>FfprobeCommandBuilder</c> 负责。
/// 未在此列出的长尾选项可通过 <see cref="CustomGlobalArguments"/> /
/// <see cref="CustomInputArguments"/> 直通。</summary>
public sealed class FfprobeOptions
{
    #region 待探测的输入

    /// <summary>待探测的文件或 URL。</summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>强制输入格式（-f），自动探测失败时使用。</summary>
    public string InputFormat { get; set; } = string.Empty;

    #endregion

    #region 输出打印格式

    /// <summary>-print_format / -of。Json 为解析首选。</summary>
    public FfprobeOutputFormat OutputFormat { get; set; } = FfprobeOutputFormat.Json;

    /// <summary>自定义输出格式名。非空时优先于 <see cref="OutputFormat"/>，
    /// 可填写官方支持的任意格式别名（如 "json"、"csv"、"xml"、"flat"、"ini"）。</summary>
    public string CustomOutputFormat { get; set; } = string.Empty;

    #endregion

    #region 数值显示方式

    /// <summary>-pretty：美化数值显示（如把字节数显示为 KB/MB）。</summary>
    public bool Pretty { get; set; }

    /// <summary>-unit：显示单位后缀。</summary>
    public bool Unit { get; set; }

    /// <summary>-prefix：对数值使用 SI 前缀（与 -byte_binary_prefix 相关）。</summary>
    public bool Prefix { get; set; }

    /// <summary>-byte_binary_prefix：字节值强制使用二进制前缀（KiB/MiB 而非 KB/MB）。</summary>
    public bool ByteBinaryPrefix { get; set; }

    /// <summary>-sexagesimal：时间值显示为 HH:MM:SS.mmm 形式。</summary>
    public bool Sexagesimal { get; set; }

    /// <summary>-bitexact：强制 bitexact 输出，去除所有依赖版本的内容（便于做回归对比）。</summary>
    public bool Bitexact { get; set; }

    #endregion

    #region 显示内容（section 开关）

    /// <summary>-show_format：容器格式信息。</summary>
    public bool ShowFormat { get; set; } = true;

    /// <summary>-show_streams：各条流的详细信息。</summary>
    public bool ShowStreams { get; set; } = true;

    /// <summary>-show_packets：逐个数据包信息（输出量很大）。</summary>
    public bool ShowPackets { get; set; }

    /// <summary>-show_frames：逐帧信息（输出量极大，慎用）。</summary>
    public bool ShowFrames { get; set; }

    /// <summary>-show_programs：节目信息（用于 MPEG-TS 等多节目流）。</summary>
    public bool ShowPrograms { get; set; }

    /// <summary>-show_chapters：章节信息。</summary>
    public bool ShowChapters { get; set; }

    /// <summary>-show_data：转储数据包负载（十六进制，输出量极大）。</summary>
    public bool ShowData { get; set; }

    /// <summary>-show_data_hash：计算数据包负载的哈希。</summary>
    public FfprobeHashAlgorithm ShowDataHash { get; set; } = FfprobeHashAlgorithm.None;

    /// <summary>-show_error：把探测错误也作为结构化数据输出。</summary>
    public bool ShowError { get; set; }

    /// <summary>-show_log：把日志作为结构化数据输出（-show_log 需配合 -print_format）。</summary>
    public bool ShowLog { get; set; }

    /// <summary>-show_private_data：显示流的私有数据（编解码器专属扩展字段）。</summary>
    public bool ShowPrivateData { get; set; }

    /// <summary>-show_entries：精确指定要输出的字段，
    /// 形如 "stream=index,codec_name:format=duration"。这是控制输出体积的官方推荐方式，
    /// 优先级高于各个 -show_* 开关（官方：show_entries 可替代全部 show_* 选项）。</summary>
    public string ShowEntries { get; set; } = string.Empty;

    #endregion

    #region 过滤与统计

    /// <summary>-select_streams：只处理指定的流，支持流说明符
    /// （如 v 首个视频、a 首个音频、v:0、a:1、s:0、p:1:i:0）。</summary>
    public string SelectStreams { get; set; } = string.Empty;

    /// <summary>-count_frames：统计每条流的帧数（需完整解码视频，较慢）。</summary>
    public bool CountFrames { get; set; }

    /// <summary>-count_packets：统计每条流的数据包数。</summary>
    public bool CountPackets { get; set; }

    /// <summary>-read_intervals：只读取指定的时间区间，
    /// 形如 "%+#10"（从起始 10 秒）、"10+20"（10 秒起 20 秒）。
    /// 与 -count_frames 配合可大幅提速。</summary>
    public string ReadIntervals { get; set; } = string.Empty;

    /// <summary>-section_entries / -sections：仅打印 section 结构信息并退出（不解析文件）。</summary>
    public bool PrintSections { get; set; }

    #endregion

    #region 日志与全局选项

    /// <summary>-v / -loglevel。默认 quiet，避免日志污染结构化输出。</summary>
    public FfprobeLogLevel LogLevel { get; set; } = FfprobeLogLevel.Quiet;

    /// <summary>-hide_banner：隐藏版权与编译信息横幅。</summary>
    public bool HideBanner { get; set; } = true;

    #endregion

    #region 能力查询（无需输入文件即可执行）

    /// <summary>-version：显示版本。</summary>
    public bool ShowVersion { get; set; }

    /// <summary>-formats：列出支持的格式。</summary>
    public bool ListFormats { get; set; }

    /// <summary>-demuxers：列出解复用器。</summary>
    public bool ListDemuxers { get; set; }

    /// <summary>-muxers：列出复用器。</summary>
    public bool ListMuxers { get; set; }

    /// <summary>-devices：列出可用设备。</summary>
    public bool ListDevices { get; set; }

    /// <summary>-codecs：列出编解码器。</summary>
    public bool ListCodecs { get; set; }

    /// <summary>-decoders：列出解码器。</summary>
    public bool ListDecoders { get; set; }

    /// <summary>-encoders：列出编码器。</summary>
    public bool ListEncoders { get; set; }

    /// <summary>-bsfs：列出比特流过滤器。</summary>
    public bool ListBitstreamFilters { get; set; }

    /// <summary>-protocols：列出协议。</summary>
    public bool ListProtocols { get; set; }

    /// <summary>-filters：列出可用滤镜。</summary>
    public bool ListFilters { get; set; }

    /// <summary>-pix_fmts：列出像素格式。</summary>
    public bool ListPixelFormats { get; set; }

    /// <summary>-layouts：列出标准声道布局。</summary>
    public bool ListChannelLayouts { get; set; }

    /// <summary>-sample_fmts：列出采样格式。</summary>
    public bool ListSampleFormats { get; set; }

    /// <summary>-colors：列出可用颜色名。</summary>
    public bool ListColors { get; set; }

    /// <summary>-hwaccels：列出可用硬件加速方式。</summary>
    public bool ListHardwareAccels { get; set; }

    /// <summary>-buildconf：显示编译配置。</summary>
    public bool ShowBuildConfiguration { get; set; }

    /// <summary>-L：显示许可证。</summary>
    public bool ShowLicense { get; set; }

    /// <summary>-sources device：列出指定输入设备的源（后跟设备名）。</summary>
    public string ListSources { get; set; } = string.Empty;

    /// <summary>-sinks device：列出指定输出设备的接收端（后跟设备名）。</summary>
    public string ListSinks { get; set; } = string.Empty;

    #endregion

    #region 直通参数

    /// <summary>附加到最前面的全局参数（在 -i 之前、所有结构化选项之前）。</summary>
    public string CustomGlobalArguments { get; set; } = string.Empty;

    /// <summary>附加到输入文件之前的参数。</summary>
    public string CustomInputArguments { get; set; } = string.Empty;

    #endregion

    /// <summary>是否为「能力查询」类命令（不需要输入文件即可执行，
    /// 如 -version、-codecs；这类命令不应附加结构化输出选项）。</summary>
    public bool IsCapabilityQuery =>
        ShowVersion || ListFormats || ListDemuxers || ListMuxers || ListDevices
        || ListCodecs || ListDecoders || ListEncoders || ListBitstreamFilters
        || ListProtocols || ListFilters || ListPixelFormats || ListChannelLayouts
        || ListSampleFormats || ListColors || ListHardwareAccels
        || ShowBuildConfiguration || ShowLicense
        || !string.IsNullOrWhiteSpace(ListSources) || !string.IsNullOrWhiteSpace(ListSinks)
        || PrintSections;

    /// <summary>拷贝一份参数，避免界面后续修改影响正在执行的命令。</summary>
    public FfprobeOptions Clone() => (FfprobeOptions)MemberwiseClone();

    /// <summary>创建一份用于常规媒体信息探测的默认配置（JSON + 容器/流）。</summary>
    public static FfprobeOptions CreateDefault(string inputPath) => new()
    {
        InputPath = inputPath,
        OutputFormat = FfprobeOutputFormat.Json,
        ShowFormat = true,
        ShowStreams = true,
        LogLevel = FfprobeLogLevel.Quiet,
        HideBanner = true
    };
}
