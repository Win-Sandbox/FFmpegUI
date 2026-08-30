namespace FFmpegUI.Services;

/// <summary>应用主题选项。</summary>
public enum AppTheme
{
    /// <summary>跟随系统设置（官方推荐默认）。</summary>
    System,
    Light,
    Dark
}

/// <summary>窗口背景材质（Learn《Mica》《Acrylic》）。</summary>
public enum BackdropKind
{
    /// <summary>Mica：桌面应用推荐的默认材质，Windows 10 上自动降级为纯色。</summary>
    Mica,

    /// <summary>Mica Alt：更明显的底色，适合多标签类界面。</summary>
    MicaAlt,

    /// <summary>Desktop Acrylic：更强的模糊效果。</summary>
    Acrylic,

    /// <summary>不使用系统材质（纯色背景）。</summary>
    None
}

/// <summary>应用设置（持久化到 %LOCALAPPDATA%\FFmpegUI\settings.json）。</summary>
public sealed class AppSettings
{
    /// <summary>ffmpeg.exe 路径。为空表示尚未配置。</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>ffprobe.exe 路径。为空表示尚未配置。</summary>
    public string FfprobePath { get; set; } = string.Empty;

    /// <summary>ffplay.exe 路径。为空表示尚未配置（播放功能不可用）。
    /// ffplay 属于可选组件——部分 FFmpeg 发行版（如部分 LGPL 构建）不含 ffplay.exe，
    /// 故它的缺失不应阻塞其他功能。</summary>
    public string FfplayPath { get; set; } = string.Empty;

    /// <summary>默认输出目录。为空表示「与源文件相同目录」。</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>退出前是否自动探测 FFmpeg 可执行文件。</summary>
    public bool AutoDetectFfmpegOnStartup { get; set; } = true;

    /// <summary>同时执行的任务数（官方建议默认不超过 CPU 核心数的一半）。</summary>
    public int MaxParallelTasks { get; set; } = 2;

    /// <summary>是否默认覆盖已存在的输出文件。</summary>
    public bool OverwriteOutput { get; set; } = true;

    /// <summary>界面主题。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>窗口背景材质。</summary>
    public BackdropKind Backdrop { get; set; } = BackdropKind.Mica;

    /// <summary>默认输出容器。</summary>
    public string DefaultContainer { get; set; } = "mp4";

    /// <summary>新任务加入队列后是否自动开始处理。</summary>
    public bool AutoStartQueue { get; set; } = true;

    /// <summary>任务完成后是否显示系统通知。</summary>
    public bool NotifyOnCompletion { get; set; } = true;
}
