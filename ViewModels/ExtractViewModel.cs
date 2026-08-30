using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFmpegUI.ViewModels;

/// <summary>提取页视图模型：提取音频 / 提取视频 / 提取字幕 / 抽帧为图片。</summary>
public sealed partial class ExtractViewModel : TaskPageViewModel
{
    /// <summary>0=音频，1=视频，2=字幕，3=抽帧。</summary>
    public IReadOnlyList<string> ExtractModes => new[]
    {
        StringResources.GetOr("Extract_Mode_Audio", "提取音频"),
        StringResources.GetOr("Extract_Mode_Video", "提取视频（去掉音频）"),
        StringResources.GetOr("Extract_Mode_Subtitle", "提取字幕"),
        StringResources.GetOr("Extract_Mode_Frame", "抽帧为图片")
    };

    public IReadOnlyList<KeyValuePair<string, string>> SubtitleFormats => new List<KeyValuePair<string, string>>
    {
        new(StringResources.GetOr("Subtitle_Srt", "SubRip (SRT)"), "srt"),
        new(StringResources.GetOr("Subtitle_Ass", "ASS"), "ass"),
        new(StringResources.GetOr("Subtitle_Ssa", "SSA"), "ssa"),
        new(StringResources.GetOr("Subtitle_Webvtt", "WebVTT"), "webvtt"),
        new(StringResources.GetOr("Subtitle_MovText", "MOV/MP4 文本字幕"), "mov_text")
    };

    public IReadOnlyList<KeyValuePair<string, string>> ImageFormats => new List<KeyValuePair<string, string>>
    {
        new(StringResources.GetOr("Image_Png", "PNG"), "png"),
        new(StringResources.GetOr("Image_Jpeg", "JPEG"), "jpg"),
        new(StringResources.GetOr("Image_Bmp", "BMP"), "bmp"),
        new(StringResources.GetOr("Image_Tiff", "TIFF"), "tiff")
    };

    [ObservableProperty] private int _modeIndex;

    #region 音频

    [ObservableProperty] private int _audioContainerIndex;

    [ObservableProperty] private bool _copyAudioStream = true;

    [ObservableProperty] private double _audioBitrateKbps = 192;

    #endregion

    #region 视频

    [ObservableProperty] private bool _copyVideoStream = true;

    #endregion

    #region 字幕

    [ObservableProperty] private int _subtitleStreamIndex;

    [ObservableProperty] private int _subtitleFormatIndex;

    #endregion

    #region 抽帧

    [ObservableProperty] private double _framesPerSecond = 1;

    [ObservableProperty] private int _imageFormatIndex;

    [ObservableProperty] private double _extractStartSeconds;

    [ObservableProperty] private double _extractDurationSeconds;

    [ObservableProperty] private string _frameOutputDirectory = string.Empty;

    [ObservableProperty] private string _framePrefix = "frame";

    #endregion

    #region 派生属性

    /// <summary>仅音频容器（用于「提取音频」目标格式）。</summary>
    public IReadOnlyList<ContainerProfile> AudioContainers { get; } =
        CodecCatalog.Containers.Where(c => c.SupportsAudio && !c.SupportsVideo).ToList();

    public bool IsAudioMode => ModeIndex == 0;
    public bool IsVideoMode => ModeIndex == 1;
    public bool IsSubtitleMode => ModeIndex == 2;
    public bool IsFrameMode => ModeIndex == 3;

    public bool IsAudioBitrateVisible => IsAudioMode && !CopyAudioStream;

    /// <summary>输入文件中的字幕流（供下拉选择）。</summary>
    public IReadOnlyList<MediaStreamInfo> SubtitleStreams =>
        Input?.SubtitleStreams.ToList() ?? new List<MediaStreamInfo>();

    public override string OutputExtension => ModeIndex switch
    {
        0 => AudioContainers[Math.Clamp(AudioContainerIndex, 0, AudioContainers.Count - 1)].Extension,
        1 => Input is not null && !string.IsNullOrEmpty(Input.ContainerFormat) ? Input.ContainerFormat : "mp4",
        2 => SubtitleFormats[Math.Clamp(SubtitleFormatIndex, 0, SubtitleFormats.Count - 1)].Value,
        3 => ImageFormats[Math.Clamp(ImageFormatIndex, 0, ImageFormats.Count - 1)].Value,
        _ => "mp4"
    };

    protected override string OutputSuffix => ModeIndex switch
    {
        0 => StringResources.GetOr("Suffix_Audio", "_音频"),
        1 => StringResources.GetOr("Suffix_NoAudio", "_无声"),
        2 => StringResources.GetOr("Suffix_Subtitle", "_字幕"),
        _ => string.Empty
    };

    /// <summary>抽帧模式下的输出文件模板（如 frame_%05d.png）。</summary>
    public string FrameOutputTemplate =>
        string.IsNullOrWhiteSpace(FrameOutputDirectory)
            ? string.Empty
            : Path.Combine(FrameOutputDirectory, $"{FramePrefix}_%05d.{OutputExtension}");

    partial void OnModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAudioMode));
        OnPropertyChanged(nameof(IsVideoMode));
        OnPropertyChanged(nameof(IsSubtitleMode));
        OnPropertyChanged(nameof(IsFrameMode));
        OnPropertyChanged(nameof(IsAudioBitrateVisible));
        OnPropertyChanged(nameof(OutputExtension));
        RefreshDefaultOutput();
    }

    partial void OnAudioContainerIndexChanged(int value)
    {
        OnPropertyChanged(nameof(OutputExtension));
        RefreshDefaultOutput();
    }

    partial void OnSubtitleFormatIndexChanged(int value)
    {
        OnPropertyChanged(nameof(OutputExtension));
        RefreshDefaultOutput();
    }

    partial void OnImageFormatIndexChanged(int value)
    {
        OnPropertyChanged(nameof(OutputExtension));
        RefreshDefaultOutput();
    }

    partial void OnCopyAudioStreamChanged(bool value) => OnPropertyChanged(nameof(IsAudioBitrateVisible));

    #endregion

    protected override void OnInputLoaded(MediaFileInfo? info)
    {
        OnPropertyChanged(nameof(SubtitleStreams));
        OnPropertyChanged(nameof(OutputExtension));

        if (info is null) return;

        if (string.IsNullOrWhiteSpace(FrameOutputDirectory))
            FrameOutputDirectory = Path.GetDirectoryName(info.FilePath) ?? string.Empty;

        FramePrefix = Path.GetFileNameWithoutExtension(info.FilePath);
        RefreshDefaultOutput();
    }

    protected override string[] GetInputExtensions() => new[]
    {
        "mp4", "mkv", "mov", "avi", "flv", "wmv", "webm", "ts", "m4v", "mpg", "mpeg", "m2ts"
    };

    protected override string BuildDefaultOutputPath()
    {
        if (IsFrameMode)
        {
            // 抽帧输出为图片序列，路径由「输出目录 + 前缀 + 序号模板」构成
            return string.IsNullOrWhiteSpace(FrameOutputDirectory)
                ? base.BuildDefaultOutputPath()
                : Path.Combine(FrameOutputDirectory, $"{FramePrefix}_%05d.{OutputExtension}");
        }

        return base.BuildDefaultOutputPath();
    }

    protected override void ApplyToOptions()
    {
        var options = Options;

        options.StartTime = null;
        options.EndTime = null;
        options.Duration = null;
        options.VideoFilters.Clear();
        options.AudioFilters.Clear();
        options.FastStart = false;

        switch (ModeIndex)
        {
            case 0:
                ApplyAudioMode(options);
                break;

            case 1:
                ApplyVideoMode(options);
                break;

            case 2:
                ApplySubtitleMode(options);
                break;

            default:
                ApplyFrameMode(options);
                break;
        }
    }

    private void ApplyAudioMode(FfmpegOptions options)
    {
        var container = AudioContainers[Math.Clamp(AudioContainerIndex, 0, AudioContainers.Count - 1)];

        options.Container = container.Format;
        options.KeepVideo = false;
        options.KeepAudio = true;
        options.KeepSubtitle = false;

        if (CopyAudioStream)
        {
            options.AudioCodec = "copy";
            options.AudioRateControl = AudioRateControl.Copy;
        }
        else
        {
            options.AudioCodec = container.DefaultAudioCodec ?? "aac";
            options.AudioRateControl = AudioRateControl.Bitrate;
            options.AudioBitrateKbps = (int)AudioBitrateKbps;
        }
    }

    private void ApplyVideoMode(FfmpegOptions options)
    {
        options.KeepVideo = true;
        options.KeepAudio = false;
        options.KeepSubtitle = false;
        options.VideoCodec = CopyVideoStream ? "copy" : "libx264";
        options.VideoRateControl = CopyVideoStream ? VideoRateControl.Copy : VideoRateControl.Crf;
        options.Crf = 20;
        options.Preset = "veryfast";
        options.FastStart = true;
    }

    private void ApplySubtitleMode(FfmpegOptions options)
    {
        options.KeepVideo = false;
        options.KeepAudio = false;
        options.KeepSubtitle = true;
        options.SubtitleStreamIndex = SubtitleStreamIndex;
        options.SubtitleCodec = SubtitleFormats[Math.Clamp(SubtitleFormatIndex, 0, SubtitleFormats.Count - 1)].Value;
        options.VideoStreamIndex = null;
        options.AudioStreamIndex = null;
    }

    private void ApplyFrameMode(FfmpegOptions options)
    {
        var format = ImageFormats[Math.Clamp(ImageFormatIndex, 0, ImageFormats.Count - 1)].Value;

        options.KeepVideo = true;
        options.KeepAudio = false;
        options.KeepSubtitle = false;
        options.VideoCodec = format switch
        {
            "png" => "png",
            "jpg" => "mjpeg",
            "bmp" => "bmp",
            "tiff" => "tiff",
            _ => "png"
        };

        options.VideoRateControl = VideoRateControl.Crf;

        // fps 滤镜：按每秒帧数抽帧
        var fps = FramesPerSecond > 0 ? FramesPerSecond : 1;
        options.VideoFilters.Add($"fps={fps.ToString("0.###", CultureInfo.InvariantCulture)}");

        options.StartTime = ExtractStartSeconds > 0 ? TimeSpan.FromSeconds(ExtractStartSeconds) : null;
        options.Duration = ExtractDurationSeconds > 0 ? TimeSpan.FromSeconds(ExtractDurationSeconds) : null;
        options.FastStart = false;
    }

    protected override async System.Threading.Tasks.Task PickOutputAsync()
    {
        if (IsFrameMode)
        {
            // 抽帧输出的是图片序列，按官方规范选择文件夹而非单文件
            var folder = await Helpers.FilePickerHelper.PickFolderAsync();
            if (string.IsNullOrEmpty(folder)) return;

            FrameOutputDirectory = folder!;
            OutputPath = BuildDefaultOutputPath();
            return;
        }

        await base.PickOutputAsync();
    }

    protected override string? ValidateBeforeQueue()
    {
        var result = base.ValidateBeforeQueue();
        if (result is not null) return result;

        if (IsSubtitleMode && Input is not null && !Input.HasSubtitle)
            return StringResources.GetOr("Msg_NoSubtitleStream", "该文件中没有找到字幕流。");

        if (IsFrameMode)
        {
            if (string.IsNullOrWhiteSpace(FrameOutputDirectory) || !Directory.Exists(FrameOutputDirectory))
                return StringResources.GetOr("Msg_NoFrameFolder", "请选择用于保存图片的文件夹。");

            if (FramesPerSecond <= 0)
                return StringResources.GetOr("Msg_InvalidFps", "每秒抽取帧数必须大于 0。");
        }

        return null;
    }
}
