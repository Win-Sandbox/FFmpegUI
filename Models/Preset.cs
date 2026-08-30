using System;
using System.Text.Json.Serialization;

namespace FFmpegUI.Models;

/// <summary>预设类型：指示预设参数对应的是视频类页面还是图片类页面，
/// 决定执行时走哪条处理链路（EncodingTask 入队 / ImageConverter 逐文件）。</summary>
public enum PresetKind
{
    Video,
    Image,
}

/// <summary>用户保存的参数预设。
/// 仅保存「参数快照」（不保存文件路径），运行时由用户选择输入/输出位置后直接处理。
/// OptionsJson 为对应页面 Options 的 JSON（视频类为 FfmpegOptions，图片类为 ImageConvertOptions）。</summary>
public sealed class Preset
{
    /// <summary>唯一标识。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>用户命名的预设名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>预设对应的页面标识（如 transcode / image），用于展示与归类。</summary>
    public string PageTag { get; set; } = string.Empty;

    /// <summary>预设类型，决定执行路径。</summary>
    public PresetKind Kind { get; set; }

    /// <summary>页面标题（中文展示用，如「转码」「图片转换」）。</summary>
    public string PageTitle { get; set; } = string.Empty;

    /// <summary>参数快照 JSON。</summary>
    public string OptionsJson { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>列表展示用的参数摘要（如「MP4 · H.264 CRF 23」），由保存时填充并随预设持久化。</summary>
    public string Summary { get; set; } = string.Empty;
}
