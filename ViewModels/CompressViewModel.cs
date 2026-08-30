using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FFmpegUI.ViewModels;

/// <summary>压缩页视图模型：按目标体积或质量优先压缩视频。</summary>
public sealed partial class CompressViewModel : TaskPageViewModel
{
    public IReadOnlyList<string> CompressModes => new[]
    {
        StringResources.GetOr("Compress_Mode_Size", "按目标文件大小"),
        StringResources.GetOr("Compress_Mode_Quality", "按质量（CRF）")
    };

    public IReadOnlyList<KeyValuePair<string, int>> ScalePercents => new List<KeyValuePair<string, int>>
    {
        new(StringResources.GetOr("Scale_100", "保持原始分辨率"), 100),
        new(StringResources.GetOr("Scale_75", "原始分辨率的 75%"), 75),
        new(StringResources.GetOr("Scale_50", "原始分辨率的 50%"), 50),
        new(StringResources.GetOr("Scale_25", "原始分辨率的 25%"), 25)
    };

    public IReadOnlyList<ContainerProfile> Containers => CodecCatalog.Containers;
    public IReadOnlyList<string> Presets => CodecCatalog.Presets;
    public IReadOnlyList<KeyValuePair<string, string>> ScaleAlgorithms => CodecCatalog.ScaleAlgorithms;

    [ObservableProperty] private int _modeIndex;

    [ObservableProperty] private double _targetSizeMb = 100;

    [ObservableProperty] private double _crf = 28;

    [ObservableProperty] private int _scalePercentIndex;

    [ObservableProperty] private int _containerIndex;

    [ObservableProperty] private int _presetIndex = 5;

    [ObservableProperty] private int _scaleAlgorithmIndex = 5;

    [ObservableProperty] private double _audioBitrateKbps = 128;

    [ObservableProperty] private bool _useHardwareAccel;

    #region 派生属性

    public bool IsTargetSizeMode => ModeIndex == 0;
    public bool IsQualityMode => ModeIndex == 1;

    public ContainerProfile SelectedContainer => Containers[Math.Clamp(ContainerIndex, 0, Containers.Count - 1)];

    public override string OutputExtension => SelectedContainer.Extension;

    protected override string OutputSuffix => StringResources.GetOr("Suffix_Compressed", "_压缩");

    /// <summary>目标体积模式下的预估视频码率（需先探测到时长）。</summary>
    public string EstimatedBitrateText
    {
        get
        {
            if (Input is null || Input.Duration <= TimeSpan.Zero)
                return StringResources.GetOr("Compress_EstimateHint", "选择输入文件后显示");

            var duration = Input.Duration.TotalSeconds;
            var totalBits = TargetSizeMb * 8 * 1024 * 1024;
            var audioBits = AudioBitrateKbps * 1000d * duration;
            var videoKbps = (totalBits - audioBits) / duration / 1000d * 0.98;

            return videoKbps <= 0
                ? StringResources.GetOr("Compress_TargetTooSmall", "目标体积过小，请增大目标大小")
                : StringResources.Format("Compress_EstimateFormat",
                    videoKbps.ToString("0", CultureInfo.CurrentCulture), AudioBitrateKbps);
        }
    }

    /// <summary>缩放后的目标分辨率文本。</summary>
    public string TargetResolutionText
    {
        get
        {
            if (Input is null || Input.Width is not > 0 || Input.Height is not > 0)
                return StringResources.GetOr("Common_NotAvailable", "—");

            var percent = ScalePercents[Math.Clamp(ScalePercentIndex, 0, ScalePercents.Count - 1)].Value;
            if (percent == 100)
                return StringResources.Format("Compress_ResolutionKeepFormat", Input.Width, Input.Height);

            // H.264 等编码器要求宽高为偶数
            var width = MakeEven((int)Math.Round(Input.Width.Value * percent / 100d));
            var height = MakeEven((int)Math.Round(Input.Height.Value * percent / 100d));
            return StringResources.Format("Compress_ResolutionScaleFormat", width, height, percent);
        }
    }

    partial void OnModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTargetSizeMode));
        OnPropertyChanged(nameof(IsQualityMode));
    }

    partial void OnTargetSizeMbChanged(double value) => OnPropertyChanged(nameof(EstimatedBitrateText));

    partial void OnAudioBitrateKbpsChanged(double value) => OnPropertyChanged(nameof(EstimatedBitrateText));

    partial void OnScalePercentIndexChanged(int value) => OnPropertyChanged(nameof(TargetResolutionText));

    partial void OnContainerIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedContainer));
        OnPropertyChanged(nameof(OutputExtension));
        RefreshDefaultOutput();
    }

    #endregion

    protected override void OnInputLoaded(MediaFileInfo? info)
    {
        OnPropertyChanged(nameof(EstimatedBitrateText));
        OnPropertyChanged(nameof(TargetResolutionText));
    }

    protected override string[] GetInputExtensions() => new[]
    {
        "mp4", "mkv", "mov", "avi", "flv", "wmv", "webm", "ts", "m4v", "mpg", "mpeg", "gif"
    };

    private static int MakeEven(int value) => value % 2 == 0 ? value : Math.Max(2, value - 1);

    protected override void ApplyToOptions()
    {
        var options = Options;

        options.Container = SelectedContainer.Format;
        options.KeepVideo = true;
        options.KeepAudio = true;
        options.KeepSubtitle = false;

        // 目标体积用 CRF 无法保证，统一走平均码率控制
        options.VideoCodec = "libx264";
        options.VideoRateControl = IsTargetSizeMode ? VideoRateControl.TargetSize : VideoRateControl.Crf;
        options.TargetSizeMb = TargetSizeMb;
        options.Crf = (int)Crf;
        options.Preset = Presets[Math.Clamp(PresetIndex, 0, Presets.Count - 1)];
        options.PixelFormat = "yuv420p";
        options.HardwareAccel = UseHardwareAccel ? HardwareAccel.Nvenc : HardwareAccel.None;

        // 分辨率按比例缩放
        var percent = ScalePercents[Math.Clamp(ScalePercentIndex, 0, ScalePercents.Count - 1)].Value;
        if (percent < 100 && Input is { Width: > 0, Height: > 0 })
        {
            options.Width = MakeEven((int)Math.Round(Input.Width.Value * percent / 100d));
            options.Height = MakeEven((int)Math.Round(Input.Height.Value * percent / 100d));
            options.ScaleAlgorithm = ScaleAlgorithmIndex > 0 && ScaleAlgorithmIndex < ScaleAlgorithms.Count
                ? ScaleAlgorithms[ScaleAlgorithmIndex].Key
                : string.Empty;
        }
        else
        {
            options.Width = null;
            options.Height = null;
            options.ScaleAlgorithm = string.Empty;
        }

        options.AudioCodec = SelectedContainer.DefaultAudioCodec ?? "aac";
        options.AudioRateControl = AudioRateControl.Bitrate;
        options.AudioBitrateKbps = (int)AudioBitrateKbps;
        options.FastStart = true;
        options.KeepMetadata = false;
        options.KeepChapters = false;
    }

    protected override string? ValidateBeforeQueue()
    {
        var result = base.ValidateBeforeQueue();
        if (result is not null) return result;

        if (IsTargetSizeMode && TargetSizeMb <= 0)
            return StringResources.GetOr("Msg_InvalidTargetSize", "目标文件大小必须大于 0 MB。");

        if (IsTargetSizeMode && Input is not null && Input.Duration <= TimeSpan.Zero)
            return StringResources.GetOr("Msg_NoDurationForTargetSize",
                "无法获取输入文件时长，无法按目标体积计算码率，请改用「按质量」模式。");

        if (!string.IsNullOrEmpty(OutputPath))
        {
            var directory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return StringResources.GetOr("Msg_OutputDirMissing", "输出目录不存在，请重新选择输出路径。");
        }

        return null;
    }
}
