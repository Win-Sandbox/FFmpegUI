using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using FFmpegUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;

namespace FFmpegUI.Views;

/// <summary>任务队列页：查看进度、控制队列、查看日志。</summary>
public sealed partial class TasksPage : Page
{
    public TasksViewModel ViewModel { get; }

    /// <summary>日志刷新计时器：任务日志为 StringBuilder（不触发 INPC），
    /// 用低频率计时器轮询选中任务的日志，保证输出面板实时显示错误/警告。</summary>
    private readonly DispatcherTimer _logTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    public TasksPage()
    {
        ViewModel = new TasksViewModel();
        InitializeComponent();

        ViewModel.RequestShowLog += OnRequestShowLog;
        ViewModel.RequestOpenFolder += OnRequestOpenFolder;

        _logTimer.Tick += (_, _) => ViewModel.RefreshLog();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _logTimer.Start();

    private void OnUnloaded(object sender, RoutedEventArgs e) => _logTimer.Stop();


    /// <summary>显示任务日志（官方 ContentDialog + 可滚动内容）。</summary>
    private async void OnRequestShowLog(object? sender, EncodingTask task)
    {
        var log = TasksViewModel.GetLog(task);
        if (string.IsNullOrWhiteSpace(log))
            log = StringResources.GetOr("Tasks_NoLog", "（暂无日志输出）");

        var textBlock = new TextBlock
        {
            Text = log,
            Style = (Style)Application.Current.Resources["MonospaceTextStyle"],
            IsTextSelectionEnabled = true
        };

        var scrollViewer = new ScrollViewer
        {
            Content = textBlock,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 480,
            MinWidth = 640
        };

        var dialog = new ContentDialog
        {
            Title = StringResources.Format("Tasks_LogDialogTitleFormat", task.DisplayName),
            Content = scrollViewer,
            CloseButtonText = StringResources.GetOr("Common_Close", "关闭"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    private void OnRequestOpenFolder(object? sender, EncodingTask task)
    {
        var directory = TasksViewModel.GetOutputDirectory(task);
        if (directory is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TasksPage.OpenFolder");
        }
    }
}
