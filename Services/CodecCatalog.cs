using FFmpegUI.Helpers;
using FFmpegUI.Models;
using System.Collections.Generic;

namespace FFmpegUI.Services;

/// <summary>容器格式的元数据（默认编码器、扩展名等）。
/// DisplayName 经本地化：优先取 resw 中 ResourceKey 对应文本，缺失时使用 FallbackName（中文）。</summary>
public sealed record ContainerProfile(
    ContainerFormat Format,
    string Extension,
    string ResourceKey,
    string FallbackName,
    string? DefaultVideoCodec,
    string? DefaultAudioCodec,
    string? DefaultSubtitleCodec,
    bool SupportsVideo,
    bool SupportsAudio,
    bool SupportsSubtitle)
{
    /// <summary>本地化后的容器名称（如「MP4」「Matroska」）。</summary>
    public string DisplayName => StringResources.GetOr(ResourceKey, FallbackName);
}

/// <summary>编码器/容器/预设目录。所有下拉框的数据源，
/// 新增格式只需在此处追加一条记录。
/// 所有面向用户的文本均通过 <see cref="StringResources"/> 本地化，
/// 未命中资源时回退到代码中的中文文本。</summary>
public static class CodecCatalog
{
    /// <summary>构造一个本地化条目：value 为实际写入命令行的值，text 为界面显示文本。</summary>
    private static KeyValuePair<string, string> Item(string value, string resourceKey, string fallback)
        => new(value, StringResources.GetOr(resourceKey, fallback));

    private static KeyValuePair<string, (int Width, int Height)> Res(string resourceKey, string fallback, int width, int height)
        => new(StringResources.GetOr(resourceKey, fallback), (width, height));

    /// <summary>支持的容器格式及其默认编码器。</summary>
    public static IReadOnlyList<ContainerProfile> Containers { get; } = new List<ContainerProfile>
    {
        new(ContainerFormat.Mp4,  "mp4",  "Codec_Container_Mp4",  "MP4",       "libx264",    "aac",        "mov_text", true,  true,  true),
        new(ContainerFormat.Mkv,  "mkv",  "Codec_Container_Mkv",  "MKV",       "libx264",    "aac",        "ass",      true,  true,  true),
        new(ContainerFormat.Mov,  "mov",  "Codec_Container_Mov",  "MOV",       "libx264",    "aac",        "mov_text", true,  true,  true),
        new(ContainerFormat.Webm, "webm", "Codec_Container_Webm", "WebM",      "libvpx-vp9", "libopus",    "webvtt",   true,  true,  true),
        new(ContainerFormat.Avi,  "avi",  "Codec_Container_Avi",  "AVI",       "mpeg4",      "mp3",        null,       true,  true,  false),
        new(ContainerFormat.Flv,  "flv",  "Codec_Container_Flv",  "FLV",       "libx264",    "aac",        null,       true,  true,  false),
        new(ContainerFormat.Ts,   "ts",   "Codec_Container_Ts",   "MPEG-TS",   "libx264",    "aac",        null,       true,  true,  false),
        new(ContainerFormat.Wmv,  "wmv",  "Codec_Container_Wmv",  "WMV",       "wmv2",       "wmav2",      null,       true,  true,  false),
        new(ContainerFormat.Gif,  "gif",  "Codec_Container_Gif",  "GIF 动图",  "gif",        null,         null,       true,  false, false),
        new(ContainerFormat.Mp3,  "mp3",  "Codec_Container_Mp3",  "MP3",       null,         "libmp3lame", null,       false, true,  false),
        new(ContainerFormat.M4a,  "m4a",  "Codec_Container_M4a",  "M4A",       null,         "aac",        null,       false, true,  false),
        new(ContainerFormat.Aac,  "aac",  "Codec_Container_Aac",  "AAC",       null,         "aac",        null,       false, true,  false),
        new(ContainerFormat.Flac, "flac", "Codec_Container_Flac", "FLAC",      null,         "flac",       null,       false, true,  false),
        new(ContainerFormat.Wav,  "wav",  "Codec_Container_Wav",  "WAV",       null,         "pcm_s16le",  null,       false, true,  false),
        new(ContainerFormat.Ogg,  "ogg",  "Codec_Container_Ogg",  "OGG",       null,         "libvorbis",  null,       false, true,  false),
        new(ContainerFormat.Opus, "opus", "Codec_Container_Opus", "OPUS",      null,         "libopus",    null,       false, true,  false),
        new(ContainerFormat.M4v,  "m4v",  "Codec_Container_M4v",  "M4V",       "libx264",    "aac",        "mov_text", true,  true,  true),
        new(ContainerFormat.Mpg,  "mpg",  "Codec_Container_Mpg",  "MPEG-PS",   "mpeg2video", "mp2",        null,       true,  true,  false),
        new(ContainerFormat.M2ts, "m2ts", "Codec_Container_M2ts", "M2TS（蓝光）", "libx264",  "ac3",        null,       true,  true,  false),
        new(ContainerFormat.ThreeGp, "3gp", "Codec_Container_3gp", "3GP（手机）", "libx264",  "aac",        null,       true,  true,  false),
        new(ContainerFormat.Asf,  "asf",  "Codec_Container_Asf",  "ASF/WMV",   "wmv2",       "wmav2",      null,       true,  true,  false),
        new(ContainerFormat.Mka,  "mka",  "Codec_Container_Mka",  "MKA",       null,         "copy",       "ass",      false, true,  true),
        new(ContainerFormat.Aiff, "aiff", "Codec_Container_Aiff", "AIFF",      null,         "pcm_s16be",  null,       false, true,  false),
        new(ContainerFormat.Ac3,  "ac3",  "Codec_Container_Ac3",  "AC-3",      null,         "ac3",        null,       false, true,  false),
        new(ContainerFormat.Wma,  "wma",  "Codec_Container_Wma",  "WMA",       null,         "wmav2",      null,       false, true,  false),
        new(ContainerFormat.Amr,  "amr",  "Codec_Container_Amr",  "AMR",       null,         "libopencore_amrnb", null, false, true, false)
    };

    /// <summary>常用视频编码器（值 → 显示名）。
    /// 同时列出软件与硬件编码器：此前仅有软件编码器，选择硬件加速后只能靠
    /// <see cref="MapHardwareVideoCodec"/> 自动映射，用户无法直接指定硬件编码器
    /// （如 AV1 硬件编码、直接选 h264_nvenc），故在此显式列出。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> VideoCodecs { get; } = new List<KeyValuePair<string, string>>
    {
        // 软件编码器
        Item("libx264", "Codec_Video_Libx264", "H.264 / AVC (x264) *"),
        Item("libx265", "Codec_Video_Libx265", "H.265 / HEVC (x265)"),
        Item("libvpx-vp9", "Codec_Video_Vp9", "VP9 (libvpx)"),
        Item("libvpx", "Codec_Video_Vp8", "VP8 (libvpx)"),
        Item("libsvtav1", "Codec_Video_SvtAv1", "AV1 (SVT-AV1)"),
        Item("libaom-av1", "Codec_Video_AomAv1", "AV1 (libaom)"),
        Item("librav1e", "Codec_Video_Rav1e", "AV1 (rav1e)"),
        Item("libxvid", "Codec_Video_Xvid", "MPEG-4 ASP (Xvid)"),
        Item("libopenh264", "Codec_Video_OpenH264", "H.264 (OpenH264)"),
        Item("mpeg4", "Codec_Video_Mpeg4", "MPEG-4 Part 2"),
        Item("mpeg2video", "Codec_Video_Mpeg2", "MPEG-2"),
        Item("mpeg1video", "Codec_Video_Mpeg1", "MPEG-1"),
        Item("wmv2", "Codec_Video_Wmv2", "Windows Media Video 9"),
        Item("libtheora", "Codec_Video_Theora", "Theora"),
        Item("prores_ks", "Codec_Video_ProRes", "Apple ProRes（剪辑中间格式）"),
        Item("dnxhd", "Codec_Video_Dnxhd", "Avid DNxHD（剪辑中间格式）"),
        Item("ffv1", "Codec_Video_Ffv1", "FFV1（无损，档案保存）"),
        Item("huffyuv", "Codec_Video_Huffyuv", "HuffYUV（无损）"),
        Item("gif", "Codec_Video_Gif", "GIF"),
        Item("png", "Codec_Video_Png", "PNG 序列"),
        Item("mjpeg", "Codec_Video_Mjpeg", "MJPEG"),
        Item("libwebp", "Codec_Video_Webp", "WebP 动图"),
        Item("rawvideo", "Codec_Video_RawVideo", "未压缩原始视频"),

        // 硬件编码器（NVIDIA NVENC）
        Item("h264_nvenc", "Codec_Video_H264Nvenc", "H.264 硬件 (NVIDIA NVENC)"),
        Item("hevc_nvenc", "Codec_Video_HevcNvenc", "H.265 硬件 (NVIDIA NVENC)"),
        Item("av1_nvenc", "Codec_Video_Av1Nvenc", "AV1 硬件 (NVIDIA NVENC)"),

        // 硬件编码器（Intel Quick Sync）
        Item("h264_qsv", "Codec_Video_H264Qsv", "H.264 硬件 (Intel QSV)"),
        Item("hevc_qsv", "Codec_Video_HevcQsv", "H.265 硬件 (Intel QSV)"),
        Item("av1_qsv", "Codec_Video_Av1Qsv", "AV1 硬件 (Intel QSV)"),
        Item("vp9_qsv", "Codec_Video_Vp9Qsv", "VP9 硬件 (Intel QSV)"),

        // 硬件编码器（AMD AMF）
        Item("h264_amf", "Codec_Video_H264Amf", "H.264 硬件 (AMD AMF)"),
        Item("hevc_amf", "Codec_Video_HevcAmf", "H.265 硬件 (AMD AMF)"),
        Item("av1_amf", "Codec_Video_Av1Amf", "AV1 硬件 (AMD AMF)"),

        Item("copy", "Codec_Copy", "直接复制（不重新编码）")
    };

    /// <summary>常用音频编码器（值 → 显示名）。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> AudioCodecs { get; } = new List<KeyValuePair<string, string>>
    {
        Item("aac", "Codec_Audio_Aac", "AAC *"),
        Item("libfdk_aac", "Codec_Audio_FdkAac", "AAC (Fraunhofer FDK，音质更佳)"),
        Item("libmp3lame", "Codec_Audio_Mp3", "MP3 (LAME)"),
        Item("libopus", "Codec_Audio_Opus", "Opus"),
        Item("libvorbis", "Codec_Audio_Vorbis", "Vorbis"),
        Item("flac", "Codec_Audio_Flac", "FLAC（无损）"),
        Item("alac", "Codec_Audio_Alac", "ALAC（无损）"),
        Item("pcm_s16le", "Codec_Audio_Pcm16", "PCM 16 位（无损）"),
        Item("pcm_s24le", "Codec_Audio_Pcm24", "PCM 24 位（无损）"),
        Item("pcm_f32le", "Codec_Audio_Pcm32f", "PCM 32 位浮点（无损）"),
        Item("wmav2", "Codec_Audio_Wma", "Windows Media Audio"),
        Item("ac3", "Codec_Audio_Ac3", "AC-3 (Dolby Digital)"),
        Item("eac3", "Codec_Audio_Eac3", "E-AC-3 (Dolby Digital Plus)"),
        Item("dts", "Codec_Audio_Dts", "DTS"),
        Item("libtwolame", "Codec_Audio_Mp2", "MP2 (TwoLAME)"),
        Item("mp2", "Codec_Audio_Mp2Native", "MP2"),
        Item("libspeex", "Codec_Audio_Speex", "Speex"),
        Item("g722", "Codec_Audio_G722", "G.722"),
        Item("copy", "Codec_Copy", "直接复制（不重新编码）")
    };

    /// <summary>常用字幕编码器（值 → 显示名）。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> SubtitleCodecs { get; } = new List<KeyValuePair<string, string>>
    {
        Item("copy", "Codec_Subtitle_Copy", "直接复制（保持原格式）"),
        Item("mov_text", "Codec_Subtitle_MovText", "MOV/MP4 文本字幕"),
        Item("srt", "Codec_Subtitle_Srt", "SubRip (SRT)"),
        Item("subrip", "Codec_Subtitle_Subrip", "SubRip (subrip)"),
        Item("ass", "Codec_Subtitle_Ass", "ASS"),
        Item("ssa", "Codec_Subtitle_Ssa", "SSA"),
        Item("webvtt", "Codec_Subtitle_Webvtt", "WebVTT"),
        Item("dvd_subtitle", "Codec_Subtitle_Dvd", "DVD 位图字幕"),
        Item("dvb_subtitle", "Codec_Subtitle_Dvb", "DVB 位图字幕"),
        Item("xsub", "Codec_Subtitle_Xsub", "XSUB 位图字幕")
    };

    /// <summary>x264/x265 的 preset 列表（英文专有名词，按官方名称展示）。</summary>
    public static IReadOnlyList<string> Presets { get; } = new[]
    {
        "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"
    };

    /// <summary>x264 的 tune 列表。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Tunes { get; } = new List<KeyValuePair<string, string>>
    {
        Item(string.Empty, "Codec_Tune_None", "无"),
        Item("film", "Codec_Tune_Film", "film（电影/实拍）"),
        Item("animation", "Codec_Tune_Animation", "animation（动画）"),
        Item("grain", "Codec_Tune_Grain", "grain（保留胶片颗粒）"),
        Item("stillimage", "Codec_Tune_StillImage", "stillimage（静态图像）"),
        Item("fastdecode", "Codec_Tune_FastDecode", "fastdecode（快速解码）"),
        Item("zerolatency", "Codec_Tune_ZeroLatency", "zerolatency（零延迟）")
    };

    /// <summary>常用 H.264 profile（英文专有名词）。</summary>
    public static IReadOnlyList<string> Profiles { get; } = new[]
    {
        string.Empty, "baseline", "main", "high", "high10", "high422", "high444"
    };

    /// <summary>常用像素格式。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> PixelFormats { get; } = new List<KeyValuePair<string, string>>
    {
        Item(string.Empty, "Codec_PixFmt_Auto", "自动（编码器默认）"),
        Item("yuv420p", "Codec_PixFmt_Yuv420p", "yuv420p（兼容性最好）"),
        Item("yuv420p10le", "Codec_PixFmt_Yuv420p10", "yuv420p10le（10 位）"),
        Item("yuv422p", "Codec_PixFmt_Yuv422p", "yuv422p"),
        Item("yuv422p10le", "Codec_PixFmt_Yuv422p10", "yuv422p10le"),
        Item("yuv444p", "Codec_PixFmt_Yuv444p", "yuv444p"),
        Item("nv12", "Codec_PixFmt_Nv12", "nv12（硬件编码常用）"),
        Item("rgb24", "Codec_PixFmt_Rgb24", "rgb24")
    };

    /// <summary>缩放算法。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ScaleAlgorithms { get; } = new List<KeyValuePair<string, string>>
    {
        Item(string.Empty, "Codec_Scale_Default", "默认（bicubic）"),
        Item("lanczos", "Codec_Scale_Lanczos", "lanczos（锐利，推荐放大）"),
        Item("bicubic", "Codec_Scale_Bicubic", "bicubic"),
        Item("bilinear", "Codec_Scale_Bilinear", "bilinear（快速）"),
        Item("spline", "Codec_Scale_Spline", "spline（平滑）"),
        Item("area", "Codec_Scale_Area", "area（推荐缩小）")
    };

    /// <summary>常用分辨率预设（宽×高）。</summary>
    public static IReadOnlyList<KeyValuePair<string, (int Width, int Height)>> Resolutions { get; } =
        new List<KeyValuePair<string, (int, int)>>
        {
            Res("Resolution_Original", "原始尺寸", 0, 0),
            Res("Resolution_4K", "3840×2160 (4K)", 3840, 2160),
            Res("Resolution_2K", "2560×1440 (2K)", 2560, 1440),
            Res("Resolution_1080p", "1920×1080 (1080p)", 1920, 1080),
            Res("Resolution_720p", "1280×720 (720p)", 1280, 720),
            Res("Resolution_480p", "854×480 (480p)", 854, 480),
            Res("Resolution_360p", "640×360 (360p)", 640, 360),
            Res("Resolution_240p", "426×240 (240p)", 426, 240)
        };

    /// <summary>常用帧率（数值为实际写入命令行的 fps）。</summary>
    public static IReadOnlyList<KeyValuePair<string, double>> FrameRates { get; } = new List<KeyValuePair<string, double>>
    {
        new(StringResources.GetOr("FrameRate_Original", "原始帧率"), 0),
        new(StringResources.GetOr("FrameRate_23976", "23.976 fps"), 24000.0 / 1001.0),
        new(StringResources.GetOr("FrameRate_24", "24 fps"), 24),
        new(StringResources.GetOr("FrameRate_25", "25 fps"), 25),
        new(StringResources.GetOr("FrameRate_2997", "29.97 fps"), 30000.0 / 1001.0),
        new(StringResources.GetOr("FrameRate_30", "30 fps"), 30),
        new(StringResources.GetOr("FrameRate_50", "50 fps"), 50),
        new(StringResources.GetOr("FrameRate_5994", "59.94 fps"), 60000.0 / 1001.0),
        new(StringResources.GetOr("FrameRate_60", "60 fps"), 60)
    };

    /// <summary>常用音频采样率（Hz）。</summary>
    public static IReadOnlyList<KeyValuePair<string, int>> SampleRates { get; } = new List<KeyValuePair<string, int>>
    {
        new(StringResources.GetOr("SampleRate_Original", "原始采样率"), 0),
        new(StringResources.GetOr("SampleRate_8000", "8000 Hz"), 8000),
        new(StringResources.GetOr("SampleRate_16000", "16000 Hz"), 16000),
        new(StringResources.GetOr("SampleRate_22050", "22050 Hz"), 22050),
        new(StringResources.GetOr("SampleRate_32000", "32000 Hz"), 32000),
        new(StringResources.GetOr("SampleRate_44100", "44100 Hz"), 44100),
        new(StringResources.GetOr("SampleRate_48000", "48000 Hz"), 48000),
        new(StringResources.GetOr("SampleRate_96000", "96000 Hz"), 96000)
    };

    /// <summary>声道数选项。</summary>
    public static IReadOnlyList<KeyValuePair<string, int>> ChannelOptions { get; } = new List<KeyValuePair<string, int>>
    {
        new(StringResources.GetOr("Channel_Original", "原始声道"), 0),
        new(StringResources.GetOr("Channel_Mono", "单声道 (1.0)"), 1),
        new(StringResources.GetOr("Channel_Stereo", "立体声 (2.0)"), 2),
        new(StringResources.GetOr("Channel_51", "5.1 环绕"), 6),
        new(StringResources.GetOr("Channel_71", "7.1 环绕"), 8)
    };

    /// <summary>硬件加速方式。</summary>
    public static IReadOnlyList<KeyValuePair<string, HardwareAccel>> HardwareAccels { get; } =
        new List<KeyValuePair<string, HardwareAccel>>
        {
            new(StringResources.GetOr("HwAccel_None", "不启用（软件编码）"), HardwareAccel.None),
            new(StringResources.GetOr("HwAccel_Nvenc", "NVIDIA NVENC"), HardwareAccel.Nvenc),
            new(StringResources.GetOr("HwAccel_Qsv", "Intel Quick Sync"), HardwareAccel.Qsv),
            new(StringResources.GetOr("HwAccel_Amf", "AMD AMF"), HardwareAccel.Amf),
            new(StringResources.GetOr("HwAccel_DecodeOnly", "仅硬件解码"), HardwareAccel.HardwareDecodeOnly)
        };

    /// <summary>常用音频码率（kbps）。</summary>
    public static IReadOnlyList<int> AudioBitrates { get; } = new[] { 64, 96, 128, 160, 192, 256, 320 };

    /// <summary>H.264 / H.265 常用编码级别（-level）。首项为空表示由编码器自动决定。</summary>
    public static IReadOnlyList<string> Levels { get; } = new[]
    {
        string.Empty, "3.0", "3.1", "4.0", "4.1", "4.2", "5.0", "5.1", "5.2", "6.0", "6.1"
    };

    /// <summary>帧同步模式（-fps_mode，旧版 -vsync 的替代选项）。
    /// 首项为空表示不设置；vfr 保留原始时间戳，cfr 恒定帧率，passthrough 完全透传。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> FrameSyncModes { get; } = new List<KeyValuePair<string, string>>
    {
        Item(string.Empty, "FrameSync_Auto", "不设置（编码器默认）"),
        Item("auto", "FrameSync_AutoValue", "auto（自动）"),
        Item("passthrough", "FrameSync_Passthrough", "passthrough（完全透传时间戳）"),
        Item("cfr", "FrameSync_Cfr", "cfr（恒定帧率）"),
        Item("vfr", "FrameSync_Vfr", "vfr（可变帧率）"),
        Item("drop", "FrameSync_Drop", "drop（丢弃帧）")
    };

    /// <summary>负时间戳处理（-avoid_negative_ts）。拼接 MPEG-TS、生成 HLS/DASH 时常用。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> AvoidNegativeTsOptions { get; } =
        new List<KeyValuePair<string, string>>
        {
            Item(string.Empty, "AvoidNegTs_Unset", "不设置（编码器默认）"),
            Item("auto", "AvoidNegTs_Auto", "auto"),
            Item("make_zero", "AvoidNegTs_MakeZero", "make_zero（起始归零）"),
            Item("make_non_negative", "AvoidNegTs_MakeNonNegative", "make_non_negative（整体平移）"),
            Item("disabled", "AvoidNegTs_Disabled", "disabled（保留负时间戳）")
        };

    /// <summary>色彩原色（-color_primaries）。空项为不设置，其后为 ITU/ISO 标准值。</summary>
    public static IReadOnlyList<string> ColorPrimaries { get; } = new[]
    {
        string.Empty, "bt709", "bt470m", "bt470bg", "smpte170m", "smpte240m",
        "film", "bt2020", "smpte428", "smpte431", "smpte432"
    };

    /// <summary>传输特性（-color_trc）。smpte2084 为 HDR10 的 PQ 曲线，
    /// arib-std-b67 为 HLG 曲线，二者是 HDR 转换的关键参数。</summary>
    public static IReadOnlyList<string> ColorTransfers { get; } = new[]
    {
        string.Empty, "bt709", "gamma22", "gamma28", "smpte170m", "smpte240m",
        "linear", "log", "log_sqrt", "iec61966_2_4", "bt1361", "iec61966_2_1",
        "bt2020_10", "bt2020_12", "smpte2084", "smpte428", "arib-std-b67"
    };

    /// <summary>色彩空间矩阵（-colorspace）。bt2020nc / bt2020c 用于 HDR 与宽色域内容。</summary>
    public static IReadOnlyList<string> ColorSpaces { get; } = new[]
    {
        string.Empty, "rgb", "bt709", "fcc", "bt470bg", "smpte170m", "smpte240m",
        "ycocg", "bt2020nc", "bt2020c", "smpte2085", "chroma_derived_nc", "chroma_derived_c", "ictcp"
    };

    /// <summary>按格式取容器配置；未匹配时返回 MP4。</summary>
    public static ContainerProfile GetContainer(ContainerFormat format)
    {
        foreach (var item in Containers)
            if (item.Format == format) return item;

        return Containers[0];
    }

    /// <summary>按扩展名取容器配置（用于「另存为」时自动匹配参数）。</summary>
    public static ContainerProfile GetContainerByExtension(string extension)
    {
        var ext = extension.TrimStart('.');
        foreach (var item in Containers)
            if (item.Extension.Equals(ext, System.StringComparison.OrdinalIgnoreCase)) return item;

        return Containers[0];
    }

    /// <summary>根据硬件加速方式把软件编码器替换为对应硬件编码器。
    /// 已显式指定硬件编码器时原样返回（避免二次映射）。
    /// 覆盖 H.264 / H.265 / AV1 / VP9 四类，此前仅映射 H.264 与 H.265，
    /// 导致选择 AV1 软件编码器时无法切换到硬件编码。</summary>
    public static string MapHardwareVideoCodec(string codec, HardwareAccel accel)
    {
        // 用户已直接选择硬件编码器（如 av1_nvenc），或选择复制流，均不再映射
        if (accel == HardwareAccel.None ||
            accel == HardwareAccel.HardwareDecodeOnly ||
            string.Equals(codec, "copy", System.StringComparison.OrdinalIgnoreCase) ||
            IsHardwareCodec(codec))
        {
            return codec;
        }

        return accel switch
        {
            HardwareAccel.Nvenc => codec switch
            {
                "libx264" or "h264" => "h264_nvenc",
                "libx265" or "hevc" => "hevc_nvenc",
                "libsvtav1" or "libaom-av1" or "librav1e" or "av1" => "av1_nvenc",
                _ => codec
            },
            HardwareAccel.Qsv => codec switch
            {
                "libx264" or "h264" => "h264_qsv",
                "libx265" or "hevc" => "hevc_qsv",
                "libsvtav1" or "libaom-av1" or "librav1e" or "av1" => "av1_qsv",
                "libvpx-vp9" or "vp9" => "vp9_qsv",
                _ => codec
            },
            HardwareAccel.Amf => codec switch
            {
                "libx264" or "h264" => "h264_amf",
                "libx265" or "hevc" => "hevc_amf",
                "libsvtav1" or "libaom-av1" or "librav1e" or "av1" => "av1_amf",
                _ => codec
            },
            _ => codec
        };
    }

    /// <summary>判断编码器名称是否为硬件编码器（后缀 _nvenc / _qsv / _amf / _vaapi 等）。</summary>
    public static bool IsHardwareCodec(string codec)
    {
        if (string.IsNullOrEmpty(codec)) return false;

        return codec.EndsWith("_nvenc", System.StringComparison.OrdinalIgnoreCase)
               || codec.EndsWith("_qsv", System.StringComparison.OrdinalIgnoreCase)
               || codec.EndsWith("_amf", System.StringComparison.OrdinalIgnoreCase)
               || codec.EndsWith("_vaapi", System.StringComparison.OrdinalIgnoreCase)
               || codec.EndsWith("_videotoolbox", System.StringComparison.OrdinalIgnoreCase)
               || codec.EndsWith("_cuda", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>硬件解码参数（仅选择硬件加速时启用）。</summary>
    public static string GetHardwareDecodeArgument(HardwareAccel accel) => accel switch
    {
        // 官方建议 Windows 上优先 d3d11va，兼容性较 dxva2 更好
        HardwareAccel.HardwareDecodeOnly => "d3d11va",
        HardwareAccel.Nvenc => "cuda",
        HardwareAccel.Qsv => "qsv",
        HardwareAccel.Amf => "d3d11va",
        _ => string.Empty
    };
}
