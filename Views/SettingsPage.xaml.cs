using FFmpegUI.Helpers;
using FFmpegUI.Services;
using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using System.Reflection;

namespace FFmpegUI.Views;

/// <summary>设置页：FFmpeg 路径、输出与任务、外观、关于。</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public string AppDescription =>
        StringResources.Format("Settings_AboutDescriptionFormat", AppVersion);

    public string DataDirectoryText =>
        StringResources.Format("Settings_DataDirectoryFormat", App.AppDataPath);

    private static string AppVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel();
        InitializeComponent();

        ViewModel.ThemeChanged += OnThemeChanged;
        ViewModel.BackdropChanged += OnBackdropChanged;
        ViewModel.ConcurrencyChanged += OnConcurrencyChanged;
        ViewModel.FfmpegChanged += OnFfmpegChanged;
    }

    /// <summary>FFmpeg 路径保存或探测后刷新主窗口的就绪提示条。</summary>
    private void OnFfmpegChanged(object? sender, System.EventArgs e)
        => App.MainWindow?.RefreshFfmpegStatus();

    private void OnThemeChanged(object? sender, System.EventArgs e)
        => App.MainWindow?.ApplyTheme();

    private void OnBackdropChanged(object? sender, System.EventArgs e)
        => App.MainWindow?.ApplyBackdrop();

    private void OnConcurrencyChanged(object? sender, System.EventArgs e)
        => TaskQueueService.Instance.Configure();

    /// <summary>外观设置即时生效（不必等待点击「保存设置」）。</summary>
    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.ApplyAppearanceCommand.Execute(null);

    private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.ApplyAppearanceCommand.Execute(null);
}
