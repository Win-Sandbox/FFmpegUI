using System;

namespace FFmpegUI.Models;

/// <summary>ffplay 的显示模式（-showmode）。</summary>
public enum FfplayShowMode
{
    /// <summary>显示视频画面（默认）。</summary>
    Video,

    /// <summary>显示音频波形。</summary>
    Waves,

    /// <summary>显示音频的 RDFT 频谱。</summary>
    Rdft
}

/// <summary>ffplay 参数模型（精简版：仅播放器常用选项）。
///
/// 定位：ffplay 在本项目中是「内嵌播放器」，不是通用命令行前端，
/// 完整参数覆盖由 ffmpeg 与 ffprobe 承担。
///
/// 能力边界（决定了本模型的结构）：
/// <list type="bullet">
/// <item>画面与音效调整（旋转、翻转、亮度、缩放、变速）没有独立选项，
///       统一由 <see cref="Services.FfplayCommandBuilder"/> 拼成 -vf / -af 滤镜链；</item>
/// <item>暂停、快进快退由 <see cref="Services.FfplayHost"/> 向 ffplay 窗口投递按键实现；</item>
/// <item>ffplay **没有运行时变速**能力，速度只能在启动时设定，改动后需重新播放。</item>
/// </list>
/// 已移除窗口类选项（-fs / -noborder / -x / -y）：画面嵌入应用窗口后由宿主区域决定尺寸，
/// 这些选项不再适用。</summary>
public sealed class FfplayOptions
{
    #region 输入

    /// <summary>待播放的文件、URL 或设备名。</summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>-window_title：窗口标题。
    /// 嵌入播放时设为唯一标识，供 <see cref="Services.FfplayHost"/> 精确定位 ffplay 窗口
    /// （比按进程 ID 枚举窗口更可靠，不受 ffplay 默认标题随文件名变化的影响）。</summary>
    public string WindowTitle { get; set; } = string.Empty;

    #endregion

    #region 音量

    /// <summary>-volume：启动音量（0–100）。</summary>
    public int Volume { get; set; } = 100;

    /// <summary>静音。ffplay 没有 -mute 选项，静音通过 -volume 0 实现。</summary>
    public bool Muted { get; set; }

    #endregion

    #region 播放控制

    /// <summary>-ss：起始播放位置。</summary>
    public TimeSpan? SeekTo { get; set; }

    /// <summary>-loop：循环次数。0 为不循环，-1 为无限循环。</summary>
    public int LoopCount { get; set; }

    /// <summary>播放速度倍数（0.5–2.0），由 setpts 与 atempo 滤镜实现以保持音画同步与音调。
    /// ffplay 不支持运行时变速，改动后需重新播放。</summary>
    public double Speed { get; set; } = 1.0;

    #endregion

    #region 画面

    /// <summary>-autorotate：按视频的旋转元数据自动转正画面。</summary>
    public bool AutoRotate { get; set; } = true;

    /// <summary>-showmode：视频画面 / 音频波形 / 音频频谱。</summary>
    public FfplayShowMode ShowMode { get; set; } = FfplayShowMode.Video;

    #endregion

    #region 画面调整（统一经滤镜链实现）

    /// <summary>旋转角度（0 / 90 / 180 / 270）。</summary>
    public int Rotate { get; set; }

    /// <summary>水平翻转（hflip）。</summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>垂直翻转（vflip）。</summary>
    public bool FlipVertical { get; set; }

    /// <summary>亮度（eq 的 brightness，-1.0 – 1.0，默认 0）。</summary>
    public double Brightness { get; set; }

    /// <summary>对比度（eq 的 contrast，0 – 2，默认 1）。</summary>
    public double Contrast { get; set; } = 1.0;

    /// <summary>饱和度（eq 的 saturation，0 – 3，默认 1；0 为黑白）。</summary>
    public double Saturation { get; set; } = 1.0;

    /// <summary>缩放宽度（scale 滤镜）。0 为保持原始尺寸。</summary>
    public int ScaleWidth { get; set; }

    /// <summary>缩放高度（scale 滤镜）。0 为保持原始尺寸。</summary>
    public int ScaleHeight { get; set; }

    #endregion

    #region 流选择

    /// <summary>-an：禁用音频。</summary>
    public bool DisableAudio { get; set; }

    /// <summary>-vn：禁用视频（仅播放音频）。</summary>
    public bool DisableVideo { get; set; }

    /// <summary>-sn：禁用字幕。</summary>
    public bool DisableSubtitle { get; set; }

    #endregion

    #region 性能与退出

    /// <summary>-framedrop：CPU 跟不上时丢帧，保持音画同步。</summary>
    public bool FrameDrop { get; set; }

    /// <summary>-autoexit：播放结束后自动退出（不加会停在最后一帧等待）。</summary>
    public bool AutoExit { get; set; } = true;

    /// <summary>-hide_banner：隐藏版权与编译信息横幅。</summary>
    public bool HideBanner { get; set; } = true;

    #endregion

    /// <summary>拷贝一份参数（播放期间用户改动不应影响已启动的进程）。
    /// 精简版只剩值类型与字符串，MemberwiseClone 即可。</summary>
    public FfplayOptions Clone() => (FfplayOptions)MemberwiseClone();

    /// <summary>创建一份用于常规播放的默认配置。</summary>
    public static FfplayOptions CreateDefault(string inputPath) => new()
    {
        InputPath = inputPath,
        AutoExit = true,
        AutoRotate = true,
        HideBanner = true,
        Volume = 100,
        Speed = 1.0,
        Contrast = 1.0,
        Saturation = 1.0
    };
}
