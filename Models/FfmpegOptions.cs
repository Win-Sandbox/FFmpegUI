using System;
using System.Collections.Generic;
using System.Globalization;

namespace FFmpegUI.Models;

/// <summary>视频码率控制方式（对应 ffmpeg 的不同参数组合）。</summary>
public enum VideoRateControl
{
    /// <summary>恒定质量因子（-crf），推荐用于大多数场景。</summary>
    Crf,

    /// <summary>恒定量化参数（-qp）。</summary>
    Qp,

    /// <summary>平均码率（-b:v），一次编码。</summary>
    AverageBitrate,

    /// <summary>恒定码率（-b:v -minrate -maxrate -bufsize），适合流媒体。</summary>
    ConstantBitrate,

    /// <summary>按目标文件体积反推平均码率（需配合目标体积设置）。</summary>
    TargetSize,

    /// <summary>直接复制流（-c copy），不重新编码。</summary>
    Copy
}

/// <summary>音频码率控制方式。</summary>
public enum AudioRateControl
{
    /// <summary>指定码率（-b:a）。</summary>
    Bitrate,

    /// <summary>质量模式（VBR，-q:a）。</summary>
    Quality,

    /// <summary>直接复制流（-c copy）。</summary>
    Copy
}

/// <summary>硬件加速方式（按 Learn/ffmpeg 官方命名）。</summary>
public enum HardwareAccel
{
    /// <summary>不使用硬件加速（纯软件编码）。</summary>
    None,

    /// <summary>NVIDIA NVENC 编码。</summary>
    Nvenc,

    /// <summary>Intel Quick Sync Video。</summary>
    Qsv,

    /// <summary>AMD AMF。</summary>
    Amf,

    /// <summary>仅使用硬件解码（DXVA2 / D3D11VA），编码仍为软件。</summary>
    HardwareDecodeOnly
}

/// <summary>输出容器格式（决定默认编码器与复用器）。</summary>
public enum ContainerFormat
{
    Mp4,
    Mkv,
    Webm,
    Mov,
    Avi,
    Flv,
    Ts,
    Gif,
    Mp3,
    M4a,
    Flac,
    Wav,
    Ogg,
    Aac,
    Opus,
    Wmv,
    /// <summary>M4V（iTunes 视频，MP4 变体）。</summary>
    M4v,
    /// <summary>MPEG-PS（VCD/DVD 节目流）。</summary>
    Mpg,
    /// <summary>M2TS（Blu-ray 传输流）。</summary>
    M2ts,
    /// <summary>3GP（早期手机视频）。</summary>
    ThreeGp,
    /// <summary>ASF / WMV（Windows Media）。</summary>
    Asf,
    /// <summary>MKA（Matroska 纯音频）。</summary>
    Mka,
    /// <summary>AIFF（Apple 无损音频容器）。</summary>
    Aiff,
    /// <summary>裸 AC-3 流。</summary>
    Ac3,
    /// <summary>裸 WMA 流。</summary>
    Wma,
    /// <summary>AMR（语音编码容器）。</summary>
    Amr,
    Custom
}

/// <summary>FFmpeg 参数集合。所有页面最终都汇总为本对象，
/// 由 <see cref="FFmpegUI.Services.FfmpegCommandBuilder"/> 生成命令行，
/// 从而保证「任意页面产生的任务」都走同一条生成路径。</summary>
public sealed class FfmpegOptions
{
    #region 输入 / 输出

    /// <summary>输入文件路径。</summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>输出文件路径（扩展名决定容器）。</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>是否覆盖已存在的输出文件（-y）。</summary>
    public bool OverwriteOutput { get; set; } = true;

    /// <summary>输出容器格式（用于默认编码器选择与扩展名建议）。</summary>
    public ContainerFormat Container { get; set; } = ContainerFormat.Mp4;

    /// <summary>自定义容器扩展名（Container 为 Custom 时使用）。</summary>
    public string CustomContainerExtension { get; set; } = string.Empty;

    #endregion

    #region 时间范围（剪辑）

    /// <summary>起始时间（-ss），null 表示不设置。</summary>
    public TimeSpan? StartTime { get; set; }

    /// <summary>结束时间（-to），null 表示不设置。</summary>
    public TimeSpan? EndTime { get; set; }

    /// <summary>截取时长（-t），与 EndTime 二选一。</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>-ss 放在 -i 之前（输入定位，速度快）还是之后（输出定位，精度高）。
    /// FFmpeg 官方建议：需要精确时用输出定位。</summary>
    public bool SeekBeforeInput { get; set; } = true;

    #endregion

    #region 输入控制（均为 -i 之前的输入选项）

    /// <summary>强制输入格式（-f，放在 -i 之前）。留空表示自动探测，
    /// 读取管道/无扩展名数据时必须显式指定。</summary>
    public string InputFormat { get; set; } = string.Empty;

    /// <summary>输入时间偏移（-itsoffset），正值延后、负值提前，用于音画同步。</summary>
    public TimeSpan? InputTimeOffset { get; set; }

    /// <summary>输入级时长限制（-t，放在 -i 之前，先于输出级 -t 生效）。</summary>
    public TimeSpan? InputDuration { get; set; }

    /// <summary>输入循环次数（-stream_loop）。0 表示不循环，-1 表示无限循环。</summary>
    public int StreamLoop { get; set; }

    /// <summary>按原始帧率读取输入（-re），模拟实时流，推流场景常用。</summary>
    public bool RealtimeInput { get; set; }

    /// <summary>硬件加速设备序号（-hwaccel_device），多显卡时指定使用哪一块。</summary>
    public string HardwareDevice { get; set; } = string.Empty;

    #endregion

    #region 流选择

    /// <summary>是否保留视频流。</summary>
    public bool KeepVideo { get; set; } = true;

    /// <summary>是否保留音频流。</summary>
    public bool KeepAudio { get; set; } = true;

    /// <summary>是否保留字幕流。</summary>
    public bool KeepSubtitle { get; set; } = true;

    /// <summary>指定视频流序号（-map 0:v:&lt;n&gt;），null 表示全部。</summary>
    public int? VideoStreamIndex { get; set; }

    /// <summary>指定音频流序号（-map 0:a:&lt;n&gt;），null 表示全部。</summary>
    public int? AudioStreamIndex { get; set; }

    /// <summary>指定字幕流序号（-map 0:s:&lt;n&gt;），null 表示全部。</summary>
    public int? SubtitleStreamIndex { get; set; }

    /// <summary>额外的 -map 参数（如多输入混流时使用）。</summary>
    public List<string> ExtraMaps { get; set; } = new();

    /// <summary>附加输入文件（合并/混流场景，索引从 1 开始）。</summary>
    public List<string> AdditionalInputs { get; set; } = new();

    #endregion

    #region 视频编码

    /// <summary>视频编码器名称，如 libx264、libx265、copy。留空表示由容器自动选择。</summary>
    public string VideoCodec { get; set; } = string.Empty;

    /// <summary>码率控制方式。</summary>
    public VideoRateControl VideoRateControl { get; set; } = VideoRateControl.Crf;

    /// <summary>CRF 值（0–51，越小质量越高）。</summary>
    public int Crf { get; set; } = 23;

    /// <summary>QP 值（0–51）。</summary>
    public int Qp { get; set; } = 26;

    /// <summary>视频码率（kbps）。</summary>
    public int VideoBitrateKbps { get; set; } = 4000;

    /// <summary>最大码率（kbps，CBR 时等于目标码率）。</summary>
    public int MaxBitrateKbps { get; set; } = 6000;

    /// <summary>缓冲区大小（kbps，CBR 用）。</summary>
    public int BufferSizeKbps { get; set; } = 8000;

    /// <summary>目标文件体积（MB），仅在 VideoRateControl 为 TargetSize 时使用。</summary>
    public double TargetSizeMb { get; set; } = 100;

    /// <summary>编码预设（preset），如 ultrafast…veryslow。留空表示使用编码器默认。</summary>
    public string Preset { get; set; } = string.Empty;

    /// <summary>调优参数（tune），如 film、animation、fastdecode。留空表示不设置。</summary>
    public string Tune { get; set; } = string.Empty;

    /// <summary>编码档次（profile），如 high、main。留空表示不设置。</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>像素格式（pix_fmt），如 yuv420p。留空表示使用编码器默认。</summary>
    public string PixelFormat { get; set; } = string.Empty;

    /// <summary>硬件加速方式。</summary>
    public HardwareAccel HardwareAccel { get; set; } = HardwareAccel.None;

    /// <summary>编码级别（-level），如 4.0、5.1。留空表示由编码器决定。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>最大 B 帧数（-bf）。null 表示使用编码器默认。</summary>
    public int? BFrames { get; set; }

    /// <summary>参考帧数（-refs）。null 表示使用编码器默认。</summary>
    public int? RefFrames { get; set; }

    /// <summary>恒定质量因子（-q:v / -qscale:v），MPEG-2/4、MJPEG 等编码器用它替代 CRF。
    /// null 表示不设置，与 CRF/QP 互斥。</summary>
    public int? VideoQuality { get; set; }

    /// <summary>NVENC 恒定质量模式（-cq），硬件编码器的质量控制参数。
    /// null 表示不设置（NVENC 未指定时使用其默认 CQ）。</summary>
    public int? NvencCq { get; set; }

    /// <summary>编码器私有参数直通（-x264-params / -x265-params）。
    /// 这是覆盖全部编码器私有参数（如 aq-mode、psy-rd、rc-lookahead 等）的通用入口，
    /// 按当前视频编码器自动选择 -x264-params 或 -x265-params。</summary>
    public string EncoderPrivateParams { get; set; } = string.Empty;

    #endregion

    #region 色彩 / HDR

    /// <summary>色彩原色（-color_primaries），如 bt709、bt2020、smpte432。</summary>
    public string ColorPrimaries { get; set; } = string.Empty;

    /// <summary>传输特性（-color_trc），如 bt709、smpte2084（PQ/HDR10）、arib-std-b67（HLG）。</summary>
    public string ColorTransfer { get; set; } = string.Empty;

    /// <summary>色彩空间矩阵（-colorspace），如 bt709、bt2020nc、bt470bg。</summary>
    public string ColorSpace { get; set; } = string.Empty;

    #endregion

    #region 分辨率 / 帧率

    /// <summary>输出宽度（配合高度使用），null 表示保持原尺寸。</summary>
    public int? Width { get; set; }

    /// <summary>输出高度。</summary>
    public int? Height { get; set; }

    /// <summary>缩放算法（scale 滤镜的 flags），如 lanczos。留空表示默认 bicubic。</summary>
    public string ScaleAlgorithm { get; set; } = string.Empty;

    /// <summary>输出帧率（fps），null 表示保持原帧率。</summary>
    public double? FrameRate { get; set; }

    /// <summary>关键帧间隔（GOP，-g）。0 表示使用编码器默认。</summary>
    public int KeyframeInterval { get; set; }

    public bool UseKeyframeInterval => KeyframeInterval > 0;

    /// <summary>编码线程数（0 表示由 ffmpeg 自动决定）。</summary>
    public int Threads { get; set; }

    #endregion

    #region 音频编码

    /// <summary>音频编码器名称，如 aac、libmp3lame、copy。留空表示由容器自动选择。</summary>
    public string AudioCodec { get; set; } = string.Empty;

    public AudioRateControl AudioRateControl { get; set; } = AudioRateControl.Bitrate;

    /// <summary>音频码率（kbps）。</summary>
    public int AudioBitrateKbps { get; set; } = 192;

    /// <summary>质量值（VBR，如 -q:a 2）。</summary>
    public int AudioQuality { get; set; } = 2;

    /// <summary>采样率（Hz），0 表示保持原采样率。</summary>
    public int SampleRate { get; set; }

    /// <summary>声道数，0 表示保持原声道数。</summary>
    public int Channels { get; set; }

    /// <summary>音频音量（-vol，0–256，256 为原始音量）。null 表示不设置，
    /// 与 volume 滤镜不同，-vol 不触发重编码。</summary>
    public int? AudioVolume { get; set; }

    #endregion

    #region 字幕

    /// <summary>字幕编码器名称（如 mov_text、srt、ass）。留空表示由容器自动选择。</summary>
    public string SubtitleCodec { get; set; } = string.Empty;

    #endregion

    #region 滤镜

    /// <summary>视频滤镜片段（会按顺序拼成 -vf "a,b,c"）。</summary>
    public List<string> VideoFilters { get; set; } = new();

    /// <summary>音频滤镜片段（会按顺序拼成 -af "a,b,c"）。</summary>
    public List<string> AudioFilters { get; set; } = new();

    #endregion

    #region 容器 / 全局选项

    /// <summary>MP4/MOV 是否把 moov 原子前移，便于网络边下边播。</summary>
    public bool FastStart { get; set; } = true;

    /// <summary>是否保留元数据（-map_metadata 0）。</summary>
    public bool KeepMetadata { get; set; } = true;

    /// <summary>是否保留章节信息（-map_chapters 0）。</summary>
    public bool KeepChapters { get; set; } = true;

    #endregion

    #region 容器 / 流控制（通用输出选项）

    /// <summary>强制封装格式（-f，输出选项）。扩展名无法推断格式或输出到管道时使用，
    /// 如 null、mp4、matroska、hls、dash、image2、segment。</summary>
    public string OutputFormat { get; set; } = string.Empty;

    /// <summary>输出文件大小上限（-fs，MB）。0 表示不限制。</summary>
    public double OutputSizeLimitMb { get; set; }

    /// <summary>限制输出视频帧数（-frames:v）。0 表示不限制。</summary>
    public int VideoFrames { get; set; }

    /// <summary>限制输出音频帧数（-frames:a）。0 表示不限制。</summary>
    public int AudioFrames { get; set; }

    /// <summary>任一输入流结束时即停止输出（-shortest）。
    /// 合并/混流、图片转视频、循环音频等场景必需，否则会一直编码到最长流结束。</summary>
    public bool Shortest { get; set; }

    /// <summary>是否保留数据流（如 GoPro GPMD、MOV 时间码轨）。
    /// 默认 false 以对齐 FFmpeg 自身行为——它默认不复制数据流；
    /// 置 true 时生成 -map 0:d?（会同时触发音视频的显式映射，见 BuildMaps）。</summary>
    public bool KeepData { get; set; }

    /// <summary>帧同步模式（-fps_mode，旧版 -vsync），取值 auto/passthrough/cfr/vfr/drop。
    /// 修复变速、抽帧后时间戳异常时常用。</summary>
    public string FrameSyncMode { get; set; } = string.Empty;

    /// <summary>最大复用队列大小（-max_muxing_queue_size）。
    /// 音频流远长于视频流时报 "Too many packets buffered for output stream" 需调大此值。</summary>
    public int MaxMuxingQueueSize { get; set; }

    /// <summary>格式标志（-fflags，输出级），如 +genpts（重建时间戳）、
    /// +igndts、discardcorrupt（丢弃损坏帧）。</summary>
    public string FormatFlags { get; set; } = string.Empty;

    /// <summary>避免负时间戳（-avoid_negative_ts），取值 auto/make_zero/make_non_negative/disabled。
    /// 拼接 MPEG-TS、生成 HLS/DASH 时常用 make_zero。</summary>
    public string AvoidNegativeTs { get; set; } = string.Empty;

    /// <summary>显示宽高比（-aspect），如 16:9、4:3、1.7777。不修改像素，仅写入容器标记。</summary>
    public string AspectRatio { get; set; } = string.Empty;

    /// <summary>全局缩放算法（-sws_flags），对未单独指定 flags 的 scale 生效，
    /// 取值如 lanczos、bicubic、bilinear、neighbor、spline、fast_bilinear。</summary>
    public string SwsFlags { get; set; } = string.Empty;

    /// <summary>输出前是否丢弃时间码轨与元数据以外的附加数据（-map_metadata / -map_chapters 已单独控制）。</summary>
    public bool CopyTimestamp { get; set; }

    #endregion

    #region 元数据 / 轨道处置

    /// <summary>附加元数据条目（-metadata key=value），如 title、author、comment。</summary>
    public List<KeyValuePair<string, string>> Metadata { get; set; } = new();

    /// <summary>视频流处置标记（-disposition:v），如 default、forced、dub、original。留空表示不设置。</summary>
    public string VideoDisposition { get; set; } = string.Empty;

    /// <summary>音频流处置标记（-disposition:a），如 default、forced、comment。留空表示不设置。</summary>
    public string AudioDisposition { get; set; } = string.Empty;

    /// <summary>字幕流处置标记（-disposition:s），如 default、forced（强制显示字幕）。留空表示不设置。</summary>
    public string SubtitleDisposition { get; set; } = string.Empty;

    #endregion

    #region 直通参数（覆盖 ffmpeg 全部能力的逃生通道）

    /// <summary>放在 -i 之前的自定义参数（如 -hwaccel cuda、-re）。</summary>
    public string CustomInputArguments { get; set; } = string.Empty;

    /// <summary>放在输出文件之前的自定义参数（如 -c copy、-f mp4）。</summary>
    public string CustomOutputArguments { get; set; } = string.Empty;

    /// <summary>直接给出完整参数模板时的开关（此时仅使用自定义参数）。</summary>
    public bool UseRawArgumentsOnly { get; set; }

    /// <summary>完整原始参数（UseRawArgumentsOnly 为 true 时直接使用）。
    /// 其中 {input} / {output} 会被替换为实际路径。</summary>
    public string RawArguments { get; set; } = string.Empty;

    #endregion

    /// <summary>创建一份默认参数（MP4 + H.264/AVC + AAC）。</summary>
    public static FfmpegOptions CreateDefault() => new()
    {
        Container = ContainerFormat.Mp4,
        VideoCodec = "libx264",
        VideoRateControl = VideoRateControl.Crf,
        Crf = 23,
        Preset = "medium",
        AudioCodec = "aac",
        AudioRateControl = AudioRateControl.Bitrate,
        AudioBitrateKbps = 192,
        FastStart = true,
        OverwriteOutput = true
    };

    /// <summary>时间跨度格式化为 ffmpeg 可接受的 hh:mm:ss[.mmm]。</summary>
    public static string FormatTime(TimeSpan value)
        => value.ToString(value.Milliseconds == 0 ? @"hh\:mm\:ss" : @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    /// <summary>拷贝一份参数（任务提交时使用，避免 UI 后续修改影响已排队任务）。
    /// 集合类型属性必须逐一复制为新集合：MemberwiseClone 是浅拷贝，
    /// 只复制引用，UI 之后对 VideoFilters 等列表的增删会同步影响已排队任务。
    /// 值类型与字符串属性由 MemberwiseClone 复制即可。</summary>
    public FfmpegOptions Clone()
    {
        var copy = (FfmpegOptions)MemberwiseClone();

        copy.VideoFilters = new List<string>(VideoFilters);
        copy.AudioFilters = new List<string>(AudioFilters);
        copy.ExtraMaps = new List<string>(ExtraMaps);
        copy.AdditionalInputs = new List<string>(AdditionalInputs);
        copy.Metadata = new List<KeyValuePair<string, string>>(Metadata);

        return copy;
    }
}
