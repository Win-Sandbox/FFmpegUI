using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.ViewModels;

/// <summary>播放页视图模型：把 ffplay 作为内嵌播放器驱动。
///
/// 三类参数的处理方式不同，取决于 ffplay 自身的能力：
/// <list type="bullet">
/// <item><b>实时生效</b>：暂停/继续、快进快退、静音——由 <see cref="FfplayHost.SendKey"/>
/// 向 ffplay 窗口投递按键实现，无需重新播放；</item>
/// <item><b>重启生效</b>：速度、旋转、翻转、亮度/对比度/饱和度、缩放、音量等——
/// ffplay 启动后无法更改这些，改动后自动按新参数重新播放（保留起始位置）；</item>
/// <item><b>仅启动时生效</b>：起始位置、循环次数、禁用流、丢帧。</item>
/// </list>
/// 命令生成交给 <see cref="FfplayCommandBuilder"/>，进程与窗口管理交给
/// <see cref="FfplayHost"/>。</summary>
public sealed partial class PlayViewModel : ObservableObject, IDisposable
{
    /// <summary>底层参数对象（精简版）。</summary>
    public FfplayOptions PlayOptions { get; } = FfplayOptions.CreateDefault(string.Empty);

    /// <summary>内嵌播放器宿主。</summary>
    private readonly FfplayHost _host = new();

    /// <summary>宿主区域的位置与尺寸（XAML 逻辑像素），由页面在布局变化时写入。</summary>
    private double _hostX, _hostY, _hostWidth, _hostHeight;

    #region 状态

    [ObservableProperty] private string _inputPath = string.Empty;

    [ObservableProperty] private bool _isPlaying;

    /// <summary>是否已暂停（仅 IsPlaying 为真时有意义）。</summary>
    [ObservableProperty] private bool _isPaused;

    [ObservableProperty] private bool _showStatus;

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    /// <summary>起始位置的文本形式（支持 90、1:30、00:01:30）。</summary>
    [ObservableProperty] private string _seekToText = string.Empty;

    /// <summary>画面是否成功嵌入应用窗口（失败时 ffplay 会显示为独立窗口）。</summary>
    [ObservableProperty] private bool _isEmbedded;

    #endregion

    #region 下拉框数据源

    public IReadOnlyList<KeyValuePair<string, FfplayShowMode>> ShowModes { get; } = new[]
    {
        new KeyValuePair<string, FfplayShowMode>("视频画面", FfplayShowMode.Video),
        new KeyValuePair<string, FfplayShowMode>("音频波形", FfplayShowMode.Waves),
        new KeyValuePair<string, FfplayShowMode>("音频频谱", FfplayShowMode.Rdft)
    };

    public IReadOnlyList<KeyValuePair<string, int>> Rotations { get; } = new[]
    {
        new KeyValuePair<string, int>("不旋转", 0),
        new KeyValuePair<string, int>("顺时针 90°", 90),
        new KeyValuePair<string, int>("180°", 180),
        new KeyValuePair<string, int>("逆时针 90°", 270)
    };

    /// <summary>画面缩放预设（scale 滤镜）。首项为不缩放。</summary>
    public IReadOnlyList<KeyValuePair<string, (int Width, int Height)>> ScalePresets { get; } = new[]
    {
        new KeyValuePair<string, (int, int)>("原始尺寸", (0, 0)),
        new KeyValuePair<string, (int, int)>("2160p (3840×2160)", (3840, 2160)),
        new KeyValuePair<string, (int, int)>("1440p (2560×1440)", (2560, 1440)),
        new KeyValuePair<string, (int, int)>("1080p (1920×1080)", (1920, 1080)),
        new KeyValuePair<string, (int, int)>("720p (1280×720)", (1280, 720)),
        new KeyValuePair<string, (int, int)>("480p (854×480)", (854, 480)),
        new KeyValuePair<string, (int, int)>("360p (640×360)", (640, 360))
    };

    /// <summary>播放速度预设（setpts + atempo）。</summary>
    public IReadOnlyList<KeyValuePair<string, double>> SpeedPresets { get; } = new[]
    {
        new KeyValuePair<string, double>("0.5×", 0.5),
        new KeyValuePair<string, double>("0.75×", 0.75),
        new KeyValuePair<string, double>("1.0×（正常）", 1.0),
        new KeyValuePair<string, double>("1.25×", 1.25),
        new KeyValuePair<string, double>("1.5×", 1.5),
        new KeyValuePair<string, double>("2.0×", 2.0)
    };

    #endregion

    #region 绑定索引

    [ObservableProperty] private int _showModeIndex;

    [ObservableProperty] private int _rotationIndex;

    [ObservableProperty] private int _scalePresetIndex;

    [ObservableProperty] private int _speedPresetIndex = 2; // 默认 1.0×

    #endregion

    #region 直接映射到 PlayOptions 的绑定属性（改动后需重新播放）

    /// <summary>音量绑定属性。Slider.Value 是 double，而 -volume 需要整数，故在此取整。</summary>
    public double VolumeValue
    {
        get => PlayOptions.Volume;
        set
        {
            var rounded = (int)Math.Round(value);
            if (PlayOptions.Volume == rounded) return;

            PlayOptions.Volume = rounded;
            OnPropertyChanged();
            NotifyParameterChanged();
        }
    }

    public bool Muted
    {
        get => PlayOptions.Muted;
        set
        {
            if (PlayOptions.Muted == value) return;
            PlayOptions.Muted = value;
            OnPropertyChanged();

            // 静音可用 m 键实时切换，无需重新播放
            if (IsPlaying) _host.SendKey(FfplayKey.Mute);
            else NotifyParameterChanged();
        }
    }

    public int LoopCount
    {
        get => PlayOptions.LoopCount;
        set { if (PlayOptions.LoopCount == value) return; PlayOptions.LoopCount = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public double Speed
    {
        get => PlayOptions.Speed;
        set
        {
            if (Math.Abs(PlayOptions.Speed - value) < 0.001) return;
            PlayOptions.Speed = value;
            OnPropertyChanged();
            NotifyParameterChanged();
        }
    }

    public bool AutoRotate
    {
        get => PlayOptions.AutoRotate;
        set { if (PlayOptions.AutoRotate == value) return; PlayOptions.AutoRotate = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public bool FrameDrop
    {
        get => PlayOptions.FrameDrop;
        set { if (PlayOptions.FrameDrop == value) return; PlayOptions.FrameDrop = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public bool DisableAudio
    {
        get => PlayOptions.DisableAudio;
        set { if (PlayOptions.DisableAudio == value) return; PlayOptions.DisableAudio = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public bool DisableVideo
    {
        get => PlayOptions.DisableVideo;
        set { if (PlayOptions.DisableVideo == value) return; PlayOptions.DisableVideo = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public bool DisableSubtitle
    {
        get => PlayOptions.DisableSubtitle;
        set { if (PlayOptions.DisableSubtitle == value) return; PlayOptions.DisableSubtitle = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public bool FlipHorizontal
    {
        get => PlayOptions.FlipHorizontal;
        set { if (PlayOptions.FlipHorizontal == value) return; PlayOptions.FlipHorizontal = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public bool FlipVertical
    {
        get => PlayOptions.FlipVertical;
        set { if (PlayOptions.FlipVertical == value) return; PlayOptions.FlipVertical = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public double Brightness
    {
        get => PlayOptions.Brightness;
        set { if (Math.Abs(PlayOptions.Brightness - value) < 0.001) return; PlayOptions.Brightness = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public double Contrast
    {
        get => PlayOptions.Contrast;
        set { if (Math.Abs(PlayOptions.Contrast - value) < 0.001) return; PlayOptions.Contrast = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    public double Saturation
    {
        get => PlayOptions.Saturation;
        set { if (Math.Abs(PlayOptions.Saturation - value) < 0.001) return; PlayOptions.Saturation = value; OnPropertyChanged(); NotifyParameterChanged(); }
    }

    #endregion

    public bool HasFfplay => FfmpegLocator.IsExecutable(SettingsService.Current.FfplayPath);

    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);

    /// <summary>显示在画面区的文件名（未选择时为空）。</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(InputPath) ? string.Empty : System.IO.Path.GetFileName(InputPath);

    public PlayViewModel()
    {
        _host.PlaybackEnded += OnPlaybackEnded;
    }

    #region 索引变化回调

    partial void OnShowModeIndexChanged(int value)
    {
        if (value >= 0 && value < ShowModes.Count)
            PlayOptions.ShowMode = ShowModes[value].Value;
        NotifyParameterChanged();
    }

    partial void OnRotationIndexChanged(int value)
    {
        if (value >= 0 && value < Rotations.Count)
            PlayOptions.Rotate = Rotations[value].Value;
        NotifyParameterChanged();
    }

    partial void OnScalePresetIndexChanged(int value)
    {
        if (value < 0 || value >= ScalePresets.Count) return;

        var (width, height) = ScalePresets[value].Value;
        PlayOptions.ScaleWidth = width;
        PlayOptions.ScaleHeight = height;
        NotifyParameterChanged();
    }

    partial void OnSpeedPresetIndexChanged(int value)
    {
        if (value < 0 || value >= SpeedPresets.Count) return;

        PlayOptions.Speed = SpeedPresets[value].Value;
        OnPropertyChanged(nameof(Speed));
        NotifyParameterChanged();
    }

    partial void OnInputPathChanged(string value)
    {
        PlayOptions.InputPath = value;
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(DisplayName));
        PlayCommand.NotifyCanExecuteChanged();
    }

    partial void OnSeekToTextChanged(string value)
    {
        PlayOptions.SeekTo = TryParseTime(value, out var time) ? time : null;
        NotifyParameterChanged();
    }

    #endregion

    /// <summary>解析时间文本：支持秒数（"90"）、"分:秒"（"1:30"）与 "时:分:秒"。</summary>
    private static bool TryParseTime(string? text, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(':');
        try
        {
            switch (parts.Length)
            {
                case 1:
                    if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
                    {
                        result = TimeSpan.FromSeconds(seconds);
                        return true;
                    }
                    return false;

                case 2:
                    if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var minutes) &&
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var sec))
                    {
                        result = new TimeSpan(0, (int)minutes, 0) + TimeSpan.FromSeconds(sec);
                        return true;
                    }
                    return false;

                case 3:
                    if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var hours) &&
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var min) &&
                        double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
                    {
                        result = new TimeSpan((int)hours, (int)min, 0) + TimeSpan.FromSeconds(s);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    #region 命令

    // 命令必须缓存为单一实例：若写成 `=> new RelayCommand(...)`，
    // 每次访问属性都生成新对象，NotifyCanExecuteChanged() 会作用在新实例上，
    // XAML 绑定的实例不刷新——表现为「选完文件后播放按钮仍不可点」。

    private IAsyncRelayCommand? _pickInputCommand;
    private IAsyncRelayCommand? _playCommand;
    private IRelayCommand? _stopCommand;
    private IRelayCommand? _pauseCommand;
    private IRelayCommand? _seekBackwardCommand;
    private IRelayCommand? _seekForwardCommand;
    private IRelayCommand? _seekBackward60Command;
    private IRelayCommand? _seekForward60Command;
    private IRelayCommand? _resetCommand;

    /// <summary>选择待播放的文件。</summary>
    public IAsyncRelayCommand PickInputCommand =>
        _pickInputCommand ??= new AsyncRelayCommand(PickInputAsync);

    /// <summary>开始播放；正在播放时则为「用当前参数重新播放」。</summary>
    public IAsyncRelayCommand PlayCommand =>
        _playCommand ??= new AsyncRelayCommand(PlayAsync, () => HasInput && HasFfplay && !IsPlaying);

    /// <summary>停止播放。</summary>
    public IRelayCommand StopCommand =>
        _stopCommand ??= new RelayCommand(Stop, () => IsPlaying);

    /// <summary>暂停 / 继续（向 ffplay 投递 p 键）。</summary>
    public IRelayCommand PauseCommand =>
        _pauseCommand ??= new RelayCommand(TogglePause, () => IsPlaying && IsEmbedded);

    /// <summary>快退 10 秒。</summary>
    public IRelayCommand SeekBackwardCommand =>
        _seekBackwardCommand ??= new RelayCommand(
            () => Seek(FfplayKey.SeekBackward10), () => IsPlaying && IsEmbedded);

    /// <summary>快进 10 秒。</summary>
    public IRelayCommand SeekForwardCommand =>
        _seekForwardCommand ??= new RelayCommand(
            () => Seek(FfplayKey.SeekForward10), () => IsPlaying && IsEmbedded);

    /// <summary>快退 60 秒。</summary>
    public IRelayCommand SeekBackward60Command =>
        _seekBackward60Command ??= new RelayCommand(
            () => Seek(FfplayKey.SeekBackward60), () => IsPlaying && IsEmbedded);

    /// <summary>快进 60 秒。</summary>
    public IRelayCommand SeekForward60Command =>
        _seekForward60Command ??= new RelayCommand(
            () => Seek(FfplayKey.SeekForward60), () => IsPlaying && IsEmbedded);

    /// <summary>恢复默认设置。</summary>
    public IRelayCommand ResetCommand =>
        _resetCommand ??= new RelayCommand(Reset);

    private async Task PickInputAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync();
        if (string.IsNullOrEmpty(path)) return;

        InputPath = path!;
    }

    private async Task PlayAsync()
    {
        if (!HasFfplay)
        {
            ShowError(StringResources.GetOr("Error_NoFfplay",
                "未配置 ffplay.exe，播放功能不可用。请在设置页指定 ffplay.exe 路径。"));
            return;
        }

        if (!HasInput)
        {
            ShowInfo(StringResources.GetOr("Play_NeedInput", "请先选择要播放的文件。"));
            return;
        }

        var parentHwnd = App.MainWindowHandle;
        if (parentHwnd == IntPtr.Zero) return;

        var scale = NativeMethods.GetDpiForWindow(parentHwnd) / 96.0;

        // 起始位置：重新播放时从用户设定的位置继续，避免每次都从头开始
        var started = await _host.StartAsync(
            PlayOptions.Clone(),
            parentHwnd,
            _hostX, _hostY, _hostWidth, _hostHeight,
            scale);

        if (!started)
        {
            ShowError(StringResources.GetOr("Play_StartFailed", "播放启动失败，请检查 ffplay 是否可用。"));
            return;
        }

        IsPlaying = true;
        IsPaused = false;
        IsEmbedded = _host.IsEmbedded;

        RefreshPlayingCommands();
        OnPropertyChanged(nameof(PauseButtonGlyph));
        OnPropertyChanged(nameof(PauseButtonText));

        // 若嵌入失败或 stderr 有诊断信息，显示相应提示并把 stderr 摘要呈现给用户，便于诊断
        if (!IsEmbedded)
        {
            var msg = StringResources.GetOr("Play_StartedExternal",
                "已开始播放，但画面未能嵌入应用窗口，将在独立窗口中显示。");

            var err = _host.LastStdErr;
            if (!string.IsNullOrEmpty(err))
            {
                // 仅显示摘要（避免 InfoBar 过长），保留最近部分
                var excerpt = err.Length <= 800 ? err : err[^800..];
                msg += "\n" + StringResources.GetOr("Play_EmbedStderrHint", "ffplay stderr 输出：") + "\n" + excerpt;

                try
                {
                    // 同时把完整 stderr 写入临时文件，便于用户粘贴给开发者分析
                    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ffplay_stderr_{DateTimeOffset.Now:yyyyMMddHHmmss}.log");
                    System.IO.File.WriteAllText(path, err);
                    msg += "\n" + StringResources.GetOr("Play_StderrSavedHint", "已把完整 stderr 保存到：") + path;
                }
                catch { /* 写文件失败不影响展示 */ }
            }

            ShowError(msg);
        }
        else
        {
            ShowInfo(StringResources.GetOr("Play_Started", "正在播放。"));
        }
    }

    private void Stop()
    {
        _host.Stop();
        SetStoppedState();
        ShowInfo(StringResources.GetOr("Play_Stopped", "已停止播放。"));
    }

    /// <summary>暂停 / 继续。</summary>
    private void TogglePause()
    {
        _host.SendKey(FfplayKey.Pause);
        IsPaused = !IsPaused;
        OnPropertyChanged(nameof(PauseButtonGlyph));
        OnPropertyChanged(nameof(PauseButtonText));
    }

    /// <summary>快进快退。</summary>
    private void Seek(FfplayKey key) => _host.SendKey(key);

    /// <summary>暂停按钮的图标：播放中为暂停形状，暂停时为播放三角形。</summary>
    public string PauseButtonGlyph => IsPaused ? "\uE768" : "\uE769";

    /// <summary>暂停按钮的文字。</summary>
    public string PauseButtonText => IsPaused ? "继续" : "暂停";

    /// <summary>播放结束时（播完自动退出或被关闭）复位界面状态。</summary>
    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        // 从后台线程切回 UI 线程再改状态
        if (App.MainWindow?.DispatcherQueue is { } dispatcher)
            dispatcher.TryEnqueue(SetStoppedState);
        else
            SetStoppedState();
    }

    private void SetStoppedState()
    {
        IsPlaying = false;
        IsPaused = false;
        IsEmbedded = false;

        RefreshPlayingCommands();
        OnPropertyChanged(nameof(PauseButtonGlyph));
        OnPropertyChanged(nameof(PauseButtonText));
    }

    /// <summary>统一刷新「播放/停止/暂停/快进快退」这组命令的可用状态。</summary>
    private void RefreshPlayingCommands()
    {
        PlayCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        SeekBackwardCommand.NotifyCanExecuteChanged();
        SeekForwardCommand.NotifyCanExecuteChanged();
        SeekBackward60Command.NotifyCanExecuteChanged();
        SeekForward60Command.NotifyCanExecuteChanged();
    }

    /// <summary>恢复默认配置（保留已选文件与起始位置）。</summary>
    private void Reset()
    {
        var path = InputPath;
        var seek = SeekToText;

        PlayOptions.Volume = 100;
        PlayOptions.Muted = false;
        PlayOptions.LoopCount = 0;
        PlayOptions.Speed = 1.0;
        PlayOptions.AutoRotate = true;
        PlayOptions.FrameDrop = false;
        PlayOptions.DisableAudio = false;
        PlayOptions.DisableVideo = false;
        PlayOptions.DisableSubtitle = false;
        PlayOptions.Rotate = 0;
        PlayOptions.FlipHorizontal = false;
        PlayOptions.FlipVertical = false;
        PlayOptions.Brightness = 0;
        PlayOptions.Contrast = 1.0;
        PlayOptions.Saturation = 1.0;
        PlayOptions.ScaleWidth = 0;
        PlayOptions.ScaleHeight = 0;
        PlayOptions.ShowMode = FfplayShowMode.Video;

        ShowModeIndex = 0;
        RotationIndex = 0;
        ScalePresetIndex = 0;
        SpeedPresetIndex = 2;
        InputPath = path;
        SeekToText = seek;

        OnPropertyChanged(string.Empty);
        ShowInfo(StringResources.GetOr("Msg_Reset", "已恢复默认设置。"));
    }

    #endregion

    #region 宿主区域同步

    /// <summary>停止播放（供页面导航离开时调用）。
    /// 必须调用：内嵌画面是主窗口的原生子窗口，不随页面隐藏，
    /// 离开播放页若不停止，画面会残留在其他页面上方。</summary>
    public void StopPlayback()
    {
        _restartCts?.Cancel();
        _host.Stop();
        SetStoppedState();
    }

    /// <summary>由页面在宿主区域位置或尺寸变化时调用，用于重新摆放内嵌画面。
    /// 内嵌窗口是原生 HWND，不参与 XAML 布局，必须手动同步。</summary>
    public void UpdateHostBounds(double x, double y, double width, double height)
    {
        _hostX = x;
        _hostY = y;
        _hostWidth = width;
        _hostHeight = height;

        if (!IsPlaying) return;

        var scale = NativeMethods.GetDpiForWindow(App.MainWindowHandle) / 96.0;
        _host.UpdateLayout(x, y, width, height, scale);
    }

    #endregion

    #region 参数改动后自动重新播放（防抖）

    private CancellationTokenSource? _restartCts;

    /// <summary>参数变化时调用：正在播放则用新参数重新播放。
    /// 这是绕开「ffplay 启动后无法更改这些参数」限制的实现方式。
    /// 暂停与快进快退不走这里——它们由按键实时控制。</summary>
    private void NotifyParameterChanged()
    {
        if (!IsPlaying) return;

        _restartCts?.Cancel();
        _restartCts = new CancellationTokenSource();
        var token = _restartCts.Token;

        _ = RestartAfterDelayAsync(token);
    }

    private async Task RestartAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(600, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || !IsPlaying) return;

            _host.Stop();
            IsPlaying = false;
            RefreshPlayingCommands();

            await PlayAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 被后续参数变化取消，属预期行为
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "PlayViewModel.RestartAfterDelayAsync");
        }
    }

    #endregion

    private void ShowInfo(string message)
    {
        StatusMessage = message;
        StatusSeverity = InfoBarSeverity.Informational;
        ShowStatus = true;
    }

    private void ShowError(string message)
    {
        StatusMessage = message;
        StatusSeverity = InfoBarSeverity.Error;
        ShowStatus = true;
    }

    public void Dispose()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _host.PlaybackEnded -= OnPlaybackEnded;
        _host.Stop();
    }
}
