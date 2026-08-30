using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FFmpegUI.ViewModels;

/// <summary>转码页视图模型：容器 / 视频编码 / 音频编码 / 字幕 / 高级选项。</summary>
public sealed partial class TranscodeViewModel : TaskPageViewModel
{
    #region 目录数据（下拉框数据源）

    public IReadOnlyList<ContainerProfile> Containers => CodecCatalog.Containers;
    public IReadOnlyList<KeyValuePair<string, string>> VideoCodecs => CodecCatalog.VideoCodecs;
    public IReadOnlyList<KeyValuePair<string, string>> AudioCodecs => CodecCatalog.AudioCodecs;
    public IReadOnlyList<KeyValuePair<string, string>> SubtitleCodecs => CodecCatalog.SubtitleCodecs;
    public IReadOnlyList<string> Presets => CodecCatalog.Presets;
    public IReadOnlyList<KeyValuePair<string, string>> Tunes => CodecCatalog.Tunes;
    public IReadOnlyList<string> Profiles => CodecCatalog.Profiles;
    public IReadOnlyList<KeyValuePair<string, string>> PixelFormats => CodecCatalog.PixelFormats;
    public IReadOnlyList<KeyValuePair<string, string>> ScaleAlgorithms => CodecCatalog.ScaleAlgorithms;
    public IReadOnlyList<KeyValuePair<string, (int Width, int Height)>> Resolutions => CodecCatalog.Resolutions;
    public IReadOnlyList<KeyValuePair<string, double>> FrameRates => CodecCatalog.FrameRates;
    public IReadOnlyList<KeyValuePair<string, int>> SampleRates => CodecCatalog.SampleRates;
    public IReadOnlyList<KeyValuePair<string, int>> ChannelOptions => CodecCatalog.ChannelOptions;
    public IReadOnlyList<KeyValuePair<string, HardwareAccel>> HardwareAccels => CodecCatalog.HardwareAccels;

    #endregion

    #region 容器与输出

    [ObservableProperty] private int _containerIndex;

    public ContainerProfile SelectedContainer =>
        Containers[Math.Clamp(ContainerIndex, 0, Containers.Count - 1)];

    public override string OutputExtension => SelectedContainer.Extension;

    protected override string OutputSuffix => StringResources.GetOr("Suffix_Transcoded", "_转码");

    partial void OnContainerIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedContainer));
        OnPropertyChanged(nameof(OutputExtension));

        // 容器不支持视频/音频时自动关闭对应开关，避免生成无效命令
        if (!SelectedContainer.SupportsVideo) KeepVideo = false;
        if (!SelectedContainer.SupportsAudio) KeepAudio = false;
        if (!SelectedContainer.SupportsSubtitle) KeepSubtitle = false;

        RefreshDefaultOutput();
    }

    #endregion

    #region 视频参数

    [ObservableProperty] private bool _keepVideo = true;

    [ObservableProperty] private int _videoCodecIndex;

    /// <summary>CRF / QP / 平均码率 / 恒定码率 / 目标体积 / 直接复制。</summary>
    [ObservableProperty] private int _videoRateControlIndex;

    [ObservableProperty] private double _crf = 23;

    [ObservableProperty] private double _qp = 26;

    [ObservableProperty] private double _videoBitrateKbps = 4000;

    [ObservableProperty] private double _maxBitrateKbps = 6000;

    [ObservableProperty] private double _bufferSizeKbps = 8000;

    [ObservableProperty] private double _targetSizeMb = 100;

    [ObservableProperty] private int _presetIndex = 5;

    [ObservableProperty] private int _tuneIndex;

    [ObservableProperty] private int _profileIndex;

    [ObservableProperty] private int _pixelFormatIndex;

    [ObservableProperty] private int _hardwareAccelIndex;

    #endregion

    #region 分辨率 / 帧率

    [ObservableProperty] private int _resolutionIndex;

    [ObservableProperty] private double _width;

    [ObservableProperty] private double _height;

    [ObservableProperty] private int _scaleAlgorithmIndex;

    [ObservableProperty] private int _frameRateIndex;

    [ObservableProperty] private double _keyframeInterval;

    [ObservableProperty] private double _threads;

    partial void OnResolutionIndexChanged(int value)
    {
        if (value <= 0)
        {
            // 「原始尺寸」：清空自定义宽高
            Width = 0;
            Height = 0;
            return;
        }

        var resolution = Resolutions[Math.Clamp(value, 0, Resolutions.Count - 1)].Value;
        Width = resolution.Width;
        Height = resolution.Height;
    }

    /// <summary>输入文件的原始分辨率文本（供界面提示）。</summary>
    public string SourceResolutionText =>
        Input is { Width: not null, Height: not null } ? $"{Input.Width}×{Input.Height}" : "—";

    #endregion

    #region 音频参数

    [ObservableProperty] private bool _keepAudio = true;

    [ObservableProperty] private int _audioCodecIndex;

    [ObservableProperty] private int _audioRateControlIndex;

    [ObservableProperty] private double _audioBitrateKbps = 192;

    [ObservableProperty] private double _audioQuality = 2;

    [ObservableProperty] private int _sampleRateIndex;

    [ObservableProperty] private int _channelIndex;

    #endregion

    #region 字幕

    [ObservableProperty] private bool _keepSubtitle = true;

    [ObservableProperty] private int _subtitleCodecIndex;

    #endregion

    #region 高级

    [ObservableProperty] private bool _fastStart = true;

    [ObservableProperty] private bool _keepMetadata = true;

    [ObservableProperty] private bool _keepChapters = true;

    [ObservableProperty] private string _customOutputArguments = string.Empty;

    #endregion

    #region 条件可见性（x:Bind 支持 bool → Visibility 自动转换）

    public bool IsCrfVisible => KeepVideo && VideoRateControlIndex == 0 && !IsVideoCopy;
    public bool IsQpVisible => KeepVideo && VideoRateControlIndex == 1 && !IsVideoCopy;
    public bool IsBitrateVisible => KeepVideo && VideoRateControlIndex == 2 && !IsVideoCopy;
    public bool IsCbrVisible => KeepVideo && VideoRateControlIndex == 3 && !IsVideoCopy;
    public bool IsTargetSizeVisible => KeepVideo && VideoRateControlIndex == 4 && !IsVideoCopy;
    public bool IsVideoCopy => KeepVideo && GetVideoCodec() == "copy";
    public bool IsVideoEncodeVisible => KeepVideo && !IsVideoCopy;
    public bool IsAudioEncodeVisible => KeepAudio && GetAudioCodec() != "copy";
    public bool IsPresetVisible => IsVideoEncodeVisible && (GetVideoCodec() is "libx264" or "libx265");

    /// <summary>码率控制方式变化后刷新相关控件的可见性。</summary>
    partial void OnVideoRateControlIndexChanged(int value) => RefreshVisibility();

    partial void OnVideoCodecIndexChanged(int value) => RefreshVisibility();

    partial void OnAudioCodecIndexChanged(int value) => RefreshVisibility();

    partial void OnKeepVideoChanged(bool value) => RefreshVisibility();

    partial void OnKeepAudioChanged(bool value) => RefreshVisibility();

    private void RefreshVisibility()
    {
        OnPropertyChanged(nameof(IsCrfVisible));
        OnPropertyChanged(nameof(IsQpVisible));
        OnPropertyChanged(nameof(IsBitrateVisible));
        OnPropertyChanged(nameof(IsCbrVisible));
        OnPropertyChanged(nameof(IsTargetSizeVisible));
        OnPropertyChanged(nameof(IsVideoCopy));
        OnPropertyChanged(nameof(IsVideoEncodeVisible));
        OnPropertyChanged(nameof(IsAudioEncodeVisible));
        OnPropertyChanged(nameof(IsPresetVisible));
    }

    #endregion

    protected override void OnInputLoaded(MediaFileInfo? info)
        => OnPropertyChanged(nameof(SourceResolutionText));

    protected override string[] GetInputExtensions() => new[]
    {
        "mp4", "mkv", "mov", "avi", "flv", "wmv", "webm", "ts", "m4v", "mpg", "mpeg", "m2ts",
        "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "wma"
    };

    private string GetVideoCodec()
        => VideoCodecs[Math.Clamp(VideoCodecIndex, 0, VideoCodecs.Count - 1)].Key;

    private string GetAudioCodec()
        => AudioCodecs[Math.Clamp(AudioCodecIndex, 0, AudioCodecs.Count - 1)].Key;

    protected override void ApplyToOptions()
    {
        var options = Options;

        options.Container = SelectedContainer.Format;
        options.OverwriteOutput = SettingsService.Current.OverwriteOutput;

        // 视频
        options.KeepVideo = KeepVideo;
        options.VideoCodec = GetVideoCodec();
        options.VideoRateControl = VideoRateControlIndex switch
        {
            1 => VideoRateControl.Qp,
            2 => VideoRateControl.AverageBitrate,
            3 => VideoRateControl.ConstantBitrate,
            4 => VideoRateControl.TargetSize,
            5 => VideoRateControl.Copy,
            _ => VideoRateControl.Crf
        };

        options.Crf = (int)Crf;
        options.Qp = (int)Qp;
        options.VideoBitrateKbps = (int)VideoBitrateKbps;
        options.MaxBitrateKbps = (int)MaxBitrateKbps;
        options.BufferSizeKbps = (int)BufferSizeKbps;
        options.TargetSizeMb = TargetSizeMb;

        options.Preset = IsPresetVisible && PresetIndex >= 0 && PresetIndex < Presets.Count
            ? Presets[PresetIndex]
            : string.Empty;

        options.Tune = TuneIndex > 0 && TuneIndex < Tunes.Count ? Tunes[TuneIndex].Key : string.Empty;
        options.Profile = ProfileIndex > 0 && ProfileIndex < Profiles.Count ? Profiles[ProfileIndex] : string.Empty;
        options.PixelFormat = PixelFormatIndex > 0 && PixelFormatIndex < PixelFormats.Count
            ? PixelFormats[PixelFormatIndex].Key
            : string.Empty;

        options.HardwareAccel = HardwareAccelIndex >= 0 && HardwareAccelIndex < HardwareAccels.Count
            ? HardwareAccels[HardwareAccelIndex].Value
            : HardwareAccel.None;

        // 分辨率与帧率
        options.Width = Width > 0 ? (int)Width : null;
        options.Height = Height > 0 ? (int)Height : null;
        options.ScaleAlgorithm = ScaleAlgorithmIndex > 0 && ScaleAlgorithmIndex < ScaleAlgorithms.Count
            ? ScaleAlgorithms[ScaleAlgorithmIndex].Key
            : string.Empty;

        options.FrameRate = FrameRateIndex > 0 && FrameRateIndex < FrameRates.Count
            ? FrameRates[FrameRateIndex].Value
            : null;

        options.KeyframeInterval = (int)KeyframeInterval;
        options.Threads = (int)Threads;

        // 音频
        options.KeepAudio = KeepAudio;
        options.AudioCodec = GetAudioCodec();
        options.AudioRateControl = AudioRateControlIndex switch
        {
            1 => AudioRateControl.Quality,
            2 => AudioRateControl.Copy,
            _ => AudioRateControl.Bitrate
        };
        options.AudioBitrateKbps = (int)AudioBitrateKbps;
        options.AudioQuality = (int)AudioQuality;
        options.SampleRate = SampleRateIndex > 0 && SampleRateIndex < SampleRates.Count
            ? SampleRates[SampleRateIndex].Value
            : 0;
        options.Channels = ChannelIndex > 0 && ChannelIndex < ChannelOptions.Count
            ? ChannelOptions[ChannelIndex].Value
            : 0;

        // 字幕
        options.KeepSubtitle = KeepSubtitle;
        options.SubtitleCodec = SubtitleCodecIndex >= 0 && SubtitleCodecIndex < SubtitleCodecs.Count
            ? SubtitleCodecs[SubtitleCodecIndex].Key
            : string.Empty;

        // 容器与高级
        options.FastStart = FastStart;
        options.KeepMetadata = KeepMetadata;
        options.KeepChapters = KeepChapters;
        options.CustomOutputArguments = CustomOutputArguments ?? string.Empty;

        // 容器不支持视频/音频/字幕时强制关闭，避免 ffmpeg 报错
        if (!SelectedContainer.SupportsVideo) options.KeepVideo = false;
        if (!SelectedContainer.SupportsAudio) options.KeepAudio = false;
        if (!SelectedContainer.SupportsSubtitle) options.KeepSubtitle = false;
    }

    protected override string? ValidateBeforeQueue()
    {
        var result = base.ValidateBeforeQueue();
        if (result is not null) return result;

        if (!KeepVideo && !KeepAudio && !KeepSubtitle)
            return StringResources.GetOr("Msg_NeedOneStream", "至少需要保留视频、音频或字幕中的一种。");

        if (!string.IsNullOrEmpty(OutputPath))
        {
            var directory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return StringResources.GetOr("Msg_OutputDirMissing", "输出目录不存在，请重新选择输出路径。");
        }

        return null;
    }
}
