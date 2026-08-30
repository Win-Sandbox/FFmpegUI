using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FFmpegUI.ViewModels;

/// <summary>剪辑页视图模型：截取片段、裁剪画面、旋转镜像、变速、音量。</summary>
public sealed partial class TrimViewModel : TaskPageViewModel
{
    public IReadOnlyList<string> TimeModes => new[]
    {
        StringResources.GetOr("Trim_TimeMode_Range", "起止时间"),
        StringResources.GetOr("Trim_TimeMode_Duration", "起始时间 + 时长")
    };

    public IReadOnlyList<string> EncodeModes => new[]
    {
        StringResources.GetOr("Trim_Encode_Copy", "复制流（无损，速度快）"),
        StringResources.GetOr("Trim_Encode_Reencode", "重新编码（可应用画面调整）")
    };

    public IReadOnlyList<KeyValuePair<string, string>> RotateOptions => new List<KeyValuePair<string, string>>
    {
        new(StringResources.GetOr("Trim_Rotate_None", "不旋转"), string.Empty),
        new(StringResources.GetOr("Trim_Rotate_Cw90", "顺时针 90°"), "1"),
        new(StringResources.GetOr("Trim_Rotate_180", "180°"), "2"),
        new(StringResources.GetOr("Trim_Rotate_Ccw90", "逆时针 90°"), "3")
    };

    public IReadOnlyList<KeyValuePair<string, string>> FlipOptions => new List<KeyValuePair<string, string>>
    {
        new(StringResources.GetOr("Trim_Flip_None", "不翻转"), string.Empty),
        new(StringResources.GetOr("Trim_Flip_Horizontal", "水平翻转（镜像）"), "hflip"),
        new(StringResources.GetOr("Trim_Flip_Vertical", "垂直翻转"), "vflip")
    };

    #region 时间范围

    [ObservableProperty] private int _timeModeIndex;

    [ObservableProperty] private double _startSeconds;

    [ObservableProperty] private double _endSeconds;

    [ObservableProperty] private double _durationSeconds;

    /// <summary>-ss 放在 -i 之前（快速）还是之后（精确）。</summary>
    [ObservableProperty] private bool _seekBeforeInput = true;

    partial void OnTimeModeIndexChanged(int value) => RefreshVisibility();

    #endregion

    #region 编码方式

    [ObservableProperty] private int _encodeModeIndex;

    [ObservableProperty] private double _crf = 18;

    [ObservableProperty] private double _audioBitrateKbps = 192;

    #endregion

    #region 画面

    [ObservableProperty] private bool _enableCrop;

    [ObservableProperty] private double _cropX;

    [ObservableProperty] private double _cropY;

    [ObservableProperty] private double _cropWidth;

    [ObservableProperty] private double _cropHeight;

    [ObservableProperty] private int _rotateIndex;

    [ObservableProperty] private int _flipIndex;

    #endregion

    #region 变速 / 音量

    [ObservableProperty] private bool _enableSpeed;

    [ObservableProperty] private double _speed = 1.0;

    [ObservableProperty] private bool _enableVolume;

    [ObservableProperty] private double _volumeDb;

    #endregion

    #region 派生属性

    protected override string OutputSuffix => StringResources.GetOr("Suffix_Trimmed", "_剪辑");

    /// <summary>是否使用了需要重新编码的画面处理。</summary>
    public bool HasVisualFilters => EnableCrop || RotateIndex > 0 || FlipIndex > 0;

    /// <summary>变速或音量调整同样需要重新编码音频。</summary>
    public bool HasAudioFilters => EnableSpeed || EnableVolume;

    public bool IsStartTimeVisible => true;
    public bool IsEndTimeVisible => TimeModeIndex == 0;
    public bool IsDurationVisible => TimeModeIndex == 1;
    public bool IsCropVisible => EnableCrop;
    public bool IsSpeedVisible => EnableSpeed;
    public bool IsVolumeVisible => EnableVolume;
    public bool IsEncodeOptionVisible => EncodeModeIndex == 1;

    /// <summary>输入时长文本（用于提示可截取范围）。</summary>
    public string DurationText => Input is not null && Input.Duration > TimeSpan.Zero
        ? Input.Duration.ToString(@"hh\:mm\:ss")
        : StringResources.GetOr("Common_Unknown", "未知");

    public string StartTimeText => TimeSpan.FromSeconds(StartSeconds).ToString(@"hh\:mm\:ss");

    public string EndTimeText => EndSeconds > 0
        ? TimeSpan.FromSeconds(EndSeconds).ToString(@"hh\:mm\:ss")
        : StringResources.GetOr("Trim_ToEnd", "结尾");

    partial void OnEnableCropChanged(bool value)
    {
        OnPropertyChanged(nameof(HasVisualFilters));
        OnPropertyChanged(nameof(IsCropVisible));
    }

    partial void OnEnableSpeedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAudioFilters));
        OnPropertyChanged(nameof(IsSpeedVisible));
    }

    partial void OnEnableVolumeChanged(bool value)
    {
        OnPropertyChanged(nameof(HasAudioFilters));
        OnPropertyChanged(nameof(IsVolumeVisible));
    }

    partial void OnRotateIndexChanged(int value) => OnPropertyChanged(nameof(HasVisualFilters));

    partial void OnFlipIndexChanged(int value) => OnPropertyChanged(nameof(HasVisualFilters));

    partial void OnEncodeModeIndexChanged(int value) => OnPropertyChanged(nameof(IsEncodeOptionVisible));

    private void RefreshVisibility()
    {
        OnPropertyChanged(nameof(IsEndTimeVisible));
        OnPropertyChanged(nameof(IsDurationVisible));
    }

    #endregion

    protected override void OnInputLoaded(MediaFileInfo? info)
    {
        OnPropertyChanged(nameof(DurationText));

        if (info is null) return;

        if (EndSeconds <= 0 && info.Duration > TimeSpan.Zero)
            EndSeconds = Math.Floor(info.Duration.TotalSeconds);

        if (CropWidth <= 0 && info.Width is > 0) CropWidth = info.Width.Value;
        if (CropHeight <= 0 && info.Height is > 0) CropHeight = info.Height.Value;
    }

    protected override string[] GetInputExtensions() => new[]
    {
        "mp4", "mkv", "mov", "avi", "flv", "wmv", "webm", "ts", "m4v", "mpg", "mpeg", "mp3", "m4a", "wav", "flac"
    };

    protected override void ApplyToOptions()
    {
        var options = Options;

        // 时间范围
        options.StartTime = StartSeconds > 0 ? TimeSpan.FromSeconds(StartSeconds) : null;
        options.EndTime = TimeModeIndex == 0 && EndSeconds > 0 ? TimeSpan.FromSeconds(EndSeconds) : null;
        options.Duration = TimeModeIndex == 1 && DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds) : null;
        options.SeekBeforeInput = SeekBeforeInput;

        // 使用滤镜时必须重新编码（复制流无法应用滤镜）
        var needEncode = EncodeModeIndex == 1 || HasVisualFilters || HasAudioFilters;

        options.VideoFilters.Clear();
        options.AudioFilters.Clear();

        if (EnableCrop && CropWidth > 0 && CropHeight > 0)
            options.VideoFilters.Add($"crop={(int)CropWidth}:{(int)CropHeight}:{(int)CropX}:{(int)CropY}");

        if (RotateIndex > 0 && RotateIndex < RotateOptions.Count)
        {
            var rotate = RotateOptions[RotateIndex].Value;
            options.VideoFilters.Add(rotate switch
            {
                "1" => "transpose=1",
                "2" => "transpose=1,transpose=1",
                "3" => "transpose=2",
                _ => string.Empty
            });
        }

        if (FlipIndex > 0 && FlipIndex < FlipOptions.Count)
            options.VideoFilters.Add(FlipOptions[FlipIndex].Value);

        if (EnableSpeed && Math.Abs(Speed - 1.0) > 0.001)
        {
            // setpts：PTS 缩放倍数 = 1 / 速度（官方滤镜用法）
            var factor = (1.0 / Speed).ToString("0.####", CultureInfo.InvariantCulture);
            options.VideoFilters.Add($"setpts={factor}*PTS");
            options.AudioFilters.Add(BuildAtempoChain(Speed));
        }

        if (EnableVolume && Math.Abs(VolumeDb) > 0.001)
            options.AudioFilters.Add($"volume={VolumeDb.ToString("0.#", CultureInfo.InvariantCulture)}dB");

        if (needEncode)
        {
            options.VideoCodec = "libx264";
            options.VideoRateControl = VideoRateControl.Crf;
            options.Crf = (int)Crf;
            options.Preset = "veryfast";
            options.AudioCodec = "aac";
            options.AudioRateControl = AudioRateControl.Bitrate;
            options.AudioBitrateKbps = (int)AudioBitrateKbps;
        }
        else
        {
            options.VideoCodec = "copy";
            options.AudioCodec = "copy";
            options.VideoRateControl = VideoRateControl.Copy;
            options.AudioRateControl = AudioRateControl.Copy;
        }

        options.KeepVideo = true;
        options.KeepAudio = true;
        options.KeepSubtitle = EncodeModeIndex == 0 && !needEncode;
        options.SubtitleCodec = "copy";
        options.FastStart = true;
    }

    /// <summary>atempo 单次只支持 0.5–2.0，超出范围时按官方做法串联多个滤镜。</summary>
    private static string BuildAtempoChain(double speed)
    {
        var chain = new List<string>();
        var remaining = speed;

        while (remaining > 2.0)
        {
            chain.Add("atempo=2.0");
            remaining /= 2.0;
        }

        while (remaining < 0.5)
        {
            chain.Add("atempo=0.5");
            remaining /= 0.5;
        }

        chain.Add($"atempo={remaining.ToString("0.###", CultureInfo.InvariantCulture)}");
        return string.Join(',', chain);
    }

    protected override string? ValidateBeforeQueue()
    {
        var result = base.ValidateBeforeQueue();
        if (result is not null) return result;

        if (TimeModeIndex == 0 && EndSeconds > 0 && EndSeconds <= StartSeconds)
            return StringResources.GetOr("Msg_InvalidEndTime", "结束时间必须晚于开始时间。");

        if (TimeModeIndex == 1 && DurationSeconds <= 0)
            return StringResources.GetOr("Msg_InvalidDuration", "请填写要截取的时长。");

        return null;
    }
}
