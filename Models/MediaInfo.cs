using FFmpegUI.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFmpegUI.Models;

/// <summary>媒体流的类型。</summary>
public enum StreamKind
{
    Video,
    Audio,
    Subtitle,
    Attachment,
    Data,
    Unknown
}

/// <summary>单条媒体流的信息（对应 ffprobe 的 streams 数组元素）。</summary>
public sealed class MediaStreamInfo
{
    /// <summary>流在文件中的序号（从 0 开始，用于 -map 0:i）。</summary>
    public int Index { get; set; }

    /// <summary>流在同类流中的序号（如第 2 条音轨为 1）。</summary>
    public int RelativeIndex { get; set; }

    public StreamKind Kind { get; set; }

    /// <summary>编码器名称，如 h264、aac。</summary>
    public string CodecName { get; set; } = string.Empty;

    public string CodecLongName { get; set; } = string.Empty;

    public string Profile { get; set; } = string.Empty;

    /// <summary>像素格式（视频）。</summary>
    public string PixelFormat { get; set; } = string.Empty;

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>平均帧率（视频），单位 fps。</summary>
    public double? FrameRate { get; set; }

    /// <summary>显示宽高比（视频）。</summary>
    public string DisplayAspectRatio { get; set; } = string.Empty;

    public int? Channels { get; set; }

    public int? SampleRate { get; set; }

    public string ChannelLayout { get; set; } = string.Empty;

    /// <summary>码率（比特/秒）。</summary>
    public long? BitRate { get; set; }

    public string Language { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    /// <summary>时长（字幕等子流可能携带）。</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>用于在界面上展示的流描述。</summary>
    public string DisplayText =>
        Kind switch
        {
            StreamKind.Video => string.Join(" · ", new[]
            {
                CodecName.ToUpperInvariant(),
                Width.HasValue && Height.HasValue ? $"{Width}×{Height}" : null,
                FrameRate.HasValue ? $"{FrameRate.Value.ToString("0.###", CultureInfo.InvariantCulture)} fps" : null,
                Language
            }.Where(s => !string.IsNullOrEmpty(s))),
            StreamKind.Audio => string.Join(" · ", new[]
            {
                CodecName.ToUpperInvariant(),
                Channels.HasValue ? $"{Channels}ch" : null,
                SampleRate.HasValue ? $"{SampleRate} Hz" : null,
                Language
            }.Where(s => !string.IsNullOrEmpty(s))),
            StreamKind.Subtitle => string.Join(" · ", new[]
            {
                CodecName.ToUpperInvariant(),
                Language,
                Title
            }.Where(s => !string.IsNullOrEmpty(s))),
            _ => CodecName.ToUpperInvariant()
        };

    /// <summary>列表显示文本（含流序号与标题）。</summary>
    public string ListText
    {
        get
        {
            var head = $"#{Index} {KindText}";
            var body = DisplayText;
            var tail = string.IsNullOrWhiteSpace(Title) ? string.Empty : $" — {Title}";
            return $"{head}：{body}{tail}";
        }
    }

    /// <summary>流类型的显示名称（经本地化，未命中资源时回退到中文）。</summary>
    public string KindText => Kind switch
    {
        StreamKind.Video => StringResources.GetOr("StreamKind_Video", "视频"),
        StreamKind.Audio => StringResources.GetOr("StreamKind_Audio", "音频"),
        StreamKind.Subtitle => StringResources.GetOr("StreamKind_Subtitle", "字幕"),
        StreamKind.Attachment => StringResources.GetOr("StreamKind_Attachment", "附件"),
        StreamKind.Data => StringResources.GetOr("StreamKind_Data", "数据"),
        _ => StringResources.GetOr("StreamKind_Unknown", "其他")
    };
}

/// <summary>媒体文件的完整信息（由 ffprobe 探测得到）。</summary>
public sealed class MediaFileInfo
{
    public string FilePath { get; set; } = string.Empty;

    public string FileName => Path.GetFileName(FilePath);

    public string ContainerFormat { get; set; } = string.Empty;

    public TimeSpan Duration { get; set; }

    public long FileSize { get; set; }

    public long? BitRate { get; set; }

    public List<MediaStreamInfo> Streams { get; set; } = new();

    public IEnumerable<MediaStreamInfo> VideoStreams => Streams.Where(s => s.Kind == StreamKind.Video);

    public IEnumerable<MediaStreamInfo> AudioStreams => Streams.Where(s => s.Kind == StreamKind.Audio);

    public IEnumerable<MediaStreamInfo> SubtitleStreams => Streams.Where(s => s.Kind == StreamKind.Subtitle);

    public int? Width => VideoStreams.FirstOrDefault()?.Width;

    public int? Height => VideoStreams.FirstOrDefault()?.Height;

    public double? FrameRate => VideoStreams.FirstOrDefault()?.FrameRate;

    public bool HasVideo => VideoStreams.Any();

    public bool HasAudio => AudioStreams.Any();

    public bool HasSubtitle => SubtitleStreams.Any();

    /// <summary>文件大小的易读格式。</summary>
    public string FileSizeText => FormatSize(FileSize);

    /// <summary>时长的易读格式（HH:MM:SS）。</summary>
    public string DurationText => Duration.ToString(@"hh\:mm\:ss");

    /// <summary>列表副标题：容器 · 分辨率 · 时长 · 大小。</summary>
    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(ContainerFormat)) parts.Add(ContainerFormat.ToUpperInvariant());
            if (Width.HasValue && Height.HasValue) parts.Add($"{Width}×{Height}");
            if (Duration > TimeSpan.Zero) parts.Add(DurationText);
            if (FileSize > 0) parts.Add(FileSizeText);
            return string.Join(StringResources.GetOr("Common_Separator", " · "), parts);
        }
    }

    /// <summary>把字节数格式化为易读文本。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes} B"
            : $"{value.ToString("0.##", CultureInfo.CurrentCulture)} {units[unit]}";
    }
}
