using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace FFmpegUI.ViewModels;

/// <summary>任务队列页视图模型：展示任务进度、控制队列运行。</summary>
public sealed partial class TasksViewModel : ObservableObject
{
    private readonly TaskQueueService _queue = TaskQueueService.Instance;

    public TasksViewModel()
    {
        _queue.TaskFinished += OnTaskFinished;
        // 队列统计变化（由 UI 线程封送）时刷新命令可用性与空状态
        _queue.QueueChanged += (_, _) => RefreshCommands();
    }

    [ObservableProperty] private EncodingTask? _selectedTask;

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private bool _showStatus;

    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    /// <summary>输出面板：当前选中任务的日志文本（由刷新计时器更新）。</summary>
    [ObservableProperty] private string _logText = string.Empty;

    /// <summary>输出面板：是否只显示错误与警告行。</summary>
    [ObservableProperty] private bool _showOnlyErrors;

    /// <summary>输出面板是否有可显示的内容。</summary>
    public bool HasLog => !string.IsNullOrEmpty(LogText);

    public ObservableCollection<EncodingTask> Tasks => _queue.Tasks;

    public int RunningCount => _queue.RunningCount;
    public int PendingCount => _queue.PendingCount;
    public int CompletedCount => _queue.CompletedCount;
    public int FailedCount => _queue.FailedCount;
    public bool IsProcessing => _queue.IsProcessing;
    public bool HasTasks => Tasks.Count > 0;
    public bool ShowEmptyState => Tasks.Count == 0;
    public bool HasSelectedTask => SelectedTask is not null;

    /// <summary>请求显示任务日志（由页面弹出内容对话框）。</summary>
    public event EventHandler<EncodingTask>? RequestShowLog;

    /// <summary>请求打开输出文件所在目录。</summary>
    public event EventHandler<EncodingTask>? RequestOpenFolder;

    #region 命令

    public IRelayCommand StartCommand => new RelayCommand(StartSelected, () => SelectedTask is { State: TaskState.Queued });

    public IRelayCommand StartAllCommand => new RelayCommand(_queue.StartAll, () => _queue.PendingCount > 0);

    public IRelayCommand CancelCommand => new RelayCommand(CancelSelected, () => SelectedTask is { IsFinished: false });

    public IRelayCommand CancelAllCommand => new RelayCommand(_queue.CancelAll, () => _queue.HasActiveTasks);

    public IRelayCommand RemoveCommand => new RelayCommand(RemoveSelected, () => SelectedTask is not null);

    public IRelayCommand ClearFinishedCommand => new RelayCommand(_queue.ClearFinished, () => Tasks.Count > 0);

    public IRelayCommand ViewLogCommand => new RelayCommand(ViewLog, () => SelectedTask is not null);

    public IRelayCommand OpenFolderCommand => new RelayCommand(OpenFolder, () => SelectedTask is not null);

    public IRelayCommand CopyLogCommand => new RelayCommand(CopyLog, () => HasLog);

    #endregion

    partial void OnSelectedTaskChanged(EncodingTask? value)
    {
        OnPropertyChanged(nameof(HasSelectedTask));
        RefreshLog();
        RefreshCommands();
    }

    partial void OnShowOnlyErrorsChanged(bool value) => RefreshLog();

    partial void OnLogTextChanged(string value) => OnPropertyChanged(nameof(HasLog));

    /// <summary>刷新输出面板日志文本（由页面计时器周期性调用，因为任务日志为 StringBuilder 不触发通知）。</summary>
    public void RefreshLog()
    {
        if (SelectedTask is null)
        {
            LogText = string.Empty;
            OnPropertyChanged(nameof(HasLog));
            return;
        }

        var raw = TaskQueueService.GetLogText(SelectedTask);
        if (!ShowOnlyErrors)
        {
            LogText = raw;
        }
        else
        {
            var lines = raw.Split('\n')
                .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("warning", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("fail", StringComparison.OrdinalIgnoreCase));
            LogText = string.Join('\n', lines);
        }

        OnPropertyChanged(nameof(HasLog));
    }

    private void StartSelected()
    {
        if (SelectedTask is null) return;
        _queue.StartTask(SelectedTask);
    }

    private void CancelSelected()
    {
        if (SelectedTask is null) return;
        _queue.Cancel(SelectedTask);
    }

    private void RemoveSelected()
    {
        if (SelectedTask is null) return;

        _queue.Remove(SelectedTask);
        SelectedTask = null;
    }

    private void ViewLog()
    {
        if (SelectedTask is null) return;
        RequestShowLog?.Invoke(this, SelectedTask);
    }

    private void OpenFolder()
    {
        if (SelectedTask is null) return;
        RequestOpenFolder?.Invoke(this, SelectedTask);
    }

    private void CopyLog()
    {
        if (!HasLog) return;

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(LogText);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ShowMessage(StringResources.GetOr("Output_Copied", "日志已复制到剪贴板。"),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TasksViewModel.CopyLog");
            ShowMessage(StringResources.GetOr("Output_CopyFailed", "复制失败。"), InfoBarSeverity.Error);
        }
    }

    private void OnTaskFinished(object? sender, EncodingTask task)
    {
        // 队列统计与任务状态都在 UI 线程更新；此处只负责刷新命令可用性与提示
        RefreshCommands();

        if (!SettingsService.Current.NotifyOnCompletion) return;

        ShowMessage($"{task.DisplayName}：{task.StatusText} — {task.DetailText}",
            TaskQueueService.GetSeverity(task.State));
    }

    /// <summary>外部（如队列统计变化）调用：刷新命令可用状态。</summary>
    public void RefreshCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        StartAllCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CancelAllCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ClearFinishedCommand.NotifyCanExecuteChanged();
        ViewLogCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        CopyLogCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        ShowStatus = true;
    }

    /// <summary>取任务日志文本。</summary>
    public static string GetLog(EncodingTask task) => TaskQueueService.GetLogText(task);

    /// <summary>取任务输出文件所在目录（用于「打开所在文件夹」）。</summary>
    public static string? GetOutputDirectory(EncodingTask task)
    {
        var directory = Path.GetDirectoryName(task.OutputPath);
        return !string.IsNullOrEmpty(directory) && Directory.Exists(directory) ? directory : null;
    }
}
