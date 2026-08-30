using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegUI.Helpers;
using FFmpegUI.Services;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFmpegUI.ViewModels;

/// <summary>设置页视图模型：FFmpeg 路径、输出、任务并发、外观。</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        ReloadFromSettings();
    }

    #region FFmpeg

    [ObservableProperty] private string _ffmpegPath = string.Empty;

    [ObservableProperty] private string _ffprobePath = string.Empty;

    /// <summary>ffplay.exe 路径（可选组件，缺失不影响其他功能）。</summary>
    [ObservableProperty] private string _ffplayPath = string.Empty;

    [ObservableProperty] private string _ffmpegStatusText = string.Empty;

    [ObservableProperty] private InfoBarSeverity _ffmpegStatusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty] private bool _showFfmpegStatus;

    #endregion

    #region 输出与任务

    [ObservableProperty] private string _outputDirectory = string.Empty;

    [ObservableProperty] private int _maxParallelTasks = 2;

    [ObservableProperty] private bool _overwriteOutput = true;

    [ObservableProperty] private bool _autoStartQueue = true;

    [ObservableProperty] private bool _notifyOnCompletion = true;

    [ObservableProperty] private bool _autoDetectFfmpegOnStartup = true;

    #endregion

    #region 外观

    [ObservableProperty] private int _themeIndex;

    [ObservableProperty] private int _backdropIndex;

    #endregion

    #region 静态选项

    public IReadOnlyList<KeyValuePair<string, AppTheme>> Themes { get; } = new List<KeyValuePair<string, AppTheme>>
    {
        new(StringResources.GetOr("Theme_System", "跟随系统"), AppTheme.System),
        new(StringResources.GetOr("Theme_Light", "浅色"), AppTheme.Light),
        new(StringResources.GetOr("Theme_Dark", "深色"), AppTheme.Dark)
    };

    public IReadOnlyList<KeyValuePair<string, BackdropKind>> Backdrops { get; } = new List<KeyValuePair<string, BackdropKind>>
    {
        new(StringResources.GetOr("Backdrop_Mica", "云母 (Mica)"), BackdropKind.Mica),
        new(StringResources.GetOr("Backdrop_MicaAlt", "云母 Alt (Mica Alt)"), BackdropKind.MicaAlt),
        new(StringResources.GetOr("Backdrop_Acrylic", "亚克力 (Acrylic)"), BackdropKind.Acrylic),
        new(StringResources.GetOr("Backdrop_None", "不使用系统材质"), BackdropKind.None)
    };

    /// <summary>CPU 逻辑处理器数量（用于并发数上限提示）。</summary>
    public string ProcessorInfo =>
        StringResources.Format("Settings_ProcessorInfoFormat", Environment.ProcessorCount);

    #endregion

    #region 事件

    /// <summary>主题需要重新应用时触发。</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>窗口背景材质需要重新应用时触发。</summary>
    public event EventHandler? BackdropChanged;

    /// <summary>任务并发数变化后触发（重建队列信号量）。</summary>
    public event EventHandler? ConcurrencyChanged;

    /// <summary>FFmpeg 路径相关设置变化后触发（刷新主窗口提示条）。</summary>
    public event EventHandler? FfmpegChanged;

    #endregion

    #region 命令

    public IAsyncRelayCommand BrowseFfmpegCommand => new AsyncRelayCommand(BrowseFfmpegAsync);
    public IAsyncRelayCommand BrowseFfprobeCommand => new AsyncRelayCommand(BrowseFfprobeAsync);
    public IAsyncRelayCommand BrowseFfplayCommand => new AsyncRelayCommand(BrowseFfplayAsync);
    public IAsyncRelayCommand BrowseOutputDirectoryCommand => new AsyncRelayCommand(BrowseOutputDirectoryAsync);
    public IRelayCommand AutoDetectCommand => new RelayCommand(AutoDetect);
    public IRelayCommand SaveCommand => new RelayCommand(Save);
    public IRelayCommand ResetCommand => new RelayCommand(Reset);

    /// <summary>外观设置（主题/背景）即时生效，不等待整体保存。</summary>
    public IRelayCommand ApplyAppearanceCommand => new RelayCommand(ApplyAppearance);

    #endregion

    /// <summary>从设置重新载入到界面属性。</summary>
    public void ReloadFromSettings()
    {
        FfmpegPath = SettingsService.Current.FfmpegPath;
        FfprobePath = SettingsService.Current.FfprobePath;
        FfplayPath = SettingsService.Current.FfplayPath;
        OutputDirectory = SettingsService.Current.OutputDirectory;
        MaxParallelTasks = SettingsService.Current.MaxParallelTasks;
        OverwriteOutput = SettingsService.Current.OverwriteOutput;
        AutoStartQueue = SettingsService.Current.AutoStartQueue;
        NotifyOnCompletion = SettingsService.Current.NotifyOnCompletion;
        AutoDetectFfmpegOnStartup = SettingsService.Current.AutoDetectFfmpegOnStartup;

        ThemeIndex = IndexOf(Themes.Select(t => t.Value).ToList(), SettingsService.Current.Theme);
        BackdropIndex = IndexOf(Backdrops.Select(b => b.Value).ToList(), SettingsService.Current.Backdrop);
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T value)
    {
        for (var i = 0; i < list.Count; i++)
            if (EqualityComparer<T>.Default.Equals(list[i], value)) return i;

        return 0;
    }

    #region 命令实现

    private async System.Threading.Tasks.Task BrowseFfmpegAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync("exe");
        if (string.IsNullOrEmpty(path)) return;

        FfmpegPath = path!;

        // 选择 ffmpeg 后自动补齐同目录的 ffprobe
        if (!FfmpegLocator.IsExecutable(FfprobePath))
        {
            var probe = FfmpegLocator.FindInDirectory(Path.GetDirectoryName(path!), "ffprobe.exe");
            if (probe is not null) FfprobePath = probe;
        }

        // 同目录自动补齐 ffplay（可选组件，找不到也不影响）
        if (!FfmpegLocator.IsExecutable(FfplayPath))
        {
            var play = FfmpegLocator.FindInDirectory(Path.GetDirectoryName(path!), "ffplay.exe");
            if (play is not null) FfplayPath = play;
        }
    }

    private async System.Threading.Tasks.Task BrowseFfprobeAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync("exe");
        if (!string.IsNullOrEmpty(path)) FfprobePath = path!;
    }

    private async System.Threading.Tasks.Task BrowseFfplayAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync("exe");
        if (!string.IsNullOrEmpty(path)) FfplayPath = path!;
    }

    private async System.Threading.Tasks.Task BrowseOutputDirectoryAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync();
        if (!string.IsNullOrEmpty(folder)) OutputDirectory = folder!;
    }

    private void AutoDetect()
    {
        var result = FfmpegLocator.DetectAndApply();

        ReloadFromSettings();

        ShowFfmpegStatus = true;
        FfmpegStatusSeverity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        FfmpegStatusText = result.Message;

        FfmpegChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        SettingsService.Current.FfmpegPath = FfmpegPath.Trim();
        SettingsService.Current.FfprobePath = FfprobePath.Trim();
        SettingsService.Current.FfplayPath = FfplayPath.Trim();
        SettingsService.Current.OutputDirectory = OutputDirectory.Trim();
        SettingsService.Current.MaxParallelTasks = Math.Clamp(MaxParallelTasks, 1, Environment.ProcessorCount);
        SettingsService.Current.OverwriteOutput = OverwriteOutput;
        SettingsService.Current.AutoStartQueue = AutoStartQueue;
        SettingsService.Current.NotifyOnCompletion = NotifyOnCompletion;
        SettingsService.Current.AutoDetectFfmpegOnStartup = AutoDetectFfmpegOnStartup;
        SettingsService.Current.Theme = Themes[Math.Clamp(ThemeIndex, 0, Themes.Count - 1)].Value;
        SettingsService.Current.Backdrop = Backdrops[Math.Clamp(BackdropIndex, 0, Backdrops.Count - 1)].Value;

        SettingsService.Save();

        MaxParallelTasks = SettingsService.Current.MaxParallelTasks;

        ThemeChanged?.Invoke(this, EventArgs.Empty);
        BackdropChanged?.Invoke(this, EventArgs.Empty);
        ConcurrencyChanged?.Invoke(this, EventArgs.Empty);

        ShowFfmpegStatus = true;
        FfmpegStatusSeverity = InfoBarSeverity.Success;
        FfmpegStatusText = StringResources.GetOr("Msg_SettingsSaved", "设置已保存。");

        FfmpegChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyAppearance()
    {
        // 只更新外观相关设置并立即应用，不影响未保存的路径等配置
        SettingsService.Current.Theme = Themes[Math.Clamp(ThemeIndex, 0, Themes.Count - 1)].Value;
        SettingsService.Current.Backdrop = Backdrops[Math.Clamp(BackdropIndex, 0, Backdrops.Count - 1)].Value;
        SettingsService.Save();

        ThemeChanged?.Invoke(this, EventArgs.Empty);
        BackdropChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Reset()
    {
        SettingsService.Reset();
        ReloadFromSettings();

        ThemeChanged?.Invoke(this, EventArgs.Empty);
        BackdropChanged?.Invoke(this, EventArgs.Empty);
        ConcurrencyChanged?.Invoke(this, EventArgs.Empty);

        ShowFfmpegStatus = true;
        FfmpegStatusSeverity = InfoBarSeverity.Success;
        FfmpegStatusText = StringResources.GetOr("Msg_SettingsReset", "已恢复默认设置。");
    }

    #endregion

    /// <summary>FFmpeg 是否已正确配置（用于页面顶部提示）。</summary>
    public bool IsFfmpegReady => FfmpegLocator.IsExecutable(FfmpegPath) && FfmpegLocator.IsExecutable(FfprobePath);

    partial void OnFfmpegPathChanged(string value) => OnPropertyChanged(nameof(IsFfmpegReady));

    partial void OnFfprobePathChanged(string value) => OnPropertyChanged(nameof(IsFfmpegReady));
}
