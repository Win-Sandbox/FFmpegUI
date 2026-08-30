using FFmpegUI.Models;

namespace FFmpegUI.Models;

/// <summary>可作为「预设源」的页面视图模型接口。
/// 主窗口的「保存预设」按钮通过此接口从当前页面取出参数快照，
/// 无需为每个页面单独编写保存逻辑（遵循「不加冗余代码」原则）。</summary>
public interface IPresetSource
{
    /// <summary>预设类型（视频类走 EncodingTask，图片类走 ImageConverter）。</summary>
    PresetKind Kind { get; }

    /// <summary>页面标识（与导航 Tag 对齐，如 transcode / image）。</summary>
    string PageTag { get; }

    /// <summary>页面中文标题（预设列表展示用，如「转码」「图片转换」）。</summary>
    string PageTitle { get; }

    /// <summary>返回当前参数对象的可序列化快照（调用方负责 Clone，避免后续修改污染预设）。</summary>
    object GetOptionsSnapshot();

    /// <summary>参数摘要（如「MP4 · H.264 CRF 23」），用于预设列表快速预览。</summary>
    string GetSummary();
}
