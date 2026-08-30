using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.Services;

/// <summary>任务队列服务：管理任务的排队、并发执行、取消与清理。
/// 并发数来自设置（官方建议：不超过 CPU 物理核心数的一半）。</summary>
public sealed partial class TaskQueueService : ObservableObject
{
    private int _runningCount;
    private int _pendingCount;
    private int _completedCount;
    private int _failedCount;
    private bool _isProcessing;

    private TaskQueueService() { }

    /// <summary>全局单例。</summary>
    public static TaskQueueService Instance { get; } = new();

    /// <summary>所有任务（界面直接绑定）。</summary>
    public ObservableCollection<EncodingTask> Tasks { get; } = new();

    /// <summary>并发信号量（在 Configure 中按设置重建）。</summary>
    private SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>UI 线程调度器：集合变更必须回到 UI 线程执行
    /// （WinUI 的集合视图具有线程亲和性）。</summary>
    private DispatcherQueue? _dispatcher;

    public int RunningCount
    {
        get => _runningCount;
        private set => SetProperty(ref _runningCount, value);
    }

    public int PendingCount
    {
        get => _pendingCount;
        private set => SetProperty(ref _pendingCount, value);
    }

    public int CompletedCount
    {
        get => _completedCount;
        private set => SetProperty(ref _completedCount, value);
    }

    public int FailedCount
    {
        get => _failedCount;
        private set => SetProperty(ref _failedCount, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set => SetProperty(ref _isProcessing, value);
    }

    /// <summary>是否有任务正在运行或排队。</summary>
    public bool HasActiveTasks => Tasks.Any(t => !t.IsFinished);

    /// <summary>任务结束时触发（供界面显示提示）。</summary>
    public event EventHandler<EncodingTask>? TaskFinished;

    /// <summary>队列状态统计发生变化时触发。</summary>
    public event EventHandler? QueueChanged;

    /// <summary>在 UI 线程初始化调度器并按设置重建并发信号量。</summary>
    public void Configure()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        // 注入到任务对象：ffmpeg 后台线程的进度更新经此切回 UI 线程
        EncodingTask.SetUiDispatcher(_dispatcher);

        var parallelism = Math.Clamp(SettingsService.Current.MaxParallelTasks, 1, Environment.ProcessorCount);
        _semaphore?.Dispose();
        _semaphore = new SemaphoreSlim(parallelism, parallelism);
    }

    /// <summary>把任务加入队列；按设置决定是否立即开始。</summary>
    public void Enqueue(EncodingTask task)
    {
        task.State = TaskState.Queued;
        task.StatusText = GetStatusText(TaskState.Queued);
        task.DetailText = string.Empty;

        Tasks.Add(task);
        RefreshCounts();

        if (SettingsService.Current.AutoStartQueue)
            StartTask(task);
    }

    /// <summary>启动指定任务（内部等待并发位）。</summary>
    public void StartTask(EncodingTask task)
    {
        if (task.State == TaskState.Running || task.IsFinished) return;

        task.Cancellation = new CancellationTokenSource();
        _ = ExecuteAsync(task);
    }

    /// <summary>启动所有排队中的任务。</summary>
    public void StartAll()
    {
        foreach (var task in Tasks.Where(t => t.State == TaskState.Queued).ToList())
            StartTask(task);
    }

    /// <summary>取消任务（排队中或运行中）。</summary>
    public void Cancel(EncodingTask task)
    {
        if (task.IsFinished) return;

        if (task.State == TaskState.Queued)
        {
            task.State = TaskState.Canceled;
            task.StatusText = GetStatusText(TaskState.Canceled);
            task.DetailText = StringResources.GetOr("Task_Detail_CanceledBeforeStart", "任务在启动前被取消。");
            NotifyTaskFinished(task);
        }
        else
        {
            task.Cancellation?.Cancel();
        }

        RefreshCounts();
    }

    /// <summary>取消所有未完成的任务。</summary>
    public void CancelAll()
    {
        foreach (var task in Tasks.Where(t => !t.IsFinished).ToList())
            Cancel(task);
    }

    /// <summary>从列表移除任务（运行中先取消）。</summary>
    public void Remove(EncodingTask task)
    {
        Cancel(task);
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => RemoveCore(task));
            return;
        }

        RemoveCore(task);
    }

    private void RemoveCore(EncodingTask task)
    {
        Tasks.Remove(task);
        RefreshCounts();
    }

    /// <summary>清除所有已结束的任务。</summary>
    public void ClearFinished()
    {
        foreach (var task in Tasks.Where(t => t.IsFinished).ToList())
            Tasks.Remove(task);

        RefreshCounts();
    }

    /// <summary>清空全部任务（先取消未完成的）。</summary>
    public void ClearAll()
    {
        foreach (var task in Tasks.Where(t => !t.IsFinished).ToList())
            task.Cancellation?.Cancel();

        Tasks.Clear();
        RefreshCounts();
    }

    /// <summary>任务执行主体：获取并发位 → 运行 → 更新状态。</summary>
    private async Task ExecuteAsync(EncodingTask task)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            if (task.Cancellation?.IsCancellationRequested == true)
            {
                Finish(task, TaskState.Canceled, StringResources.GetOr("Task_Detail_CanceledBeforeStart", "任务在启动前被取消。"));
                return;
            }

            UpdateOnUi(() =>
            {
                task.State = TaskState.Running;
                task.StatusText = GetStatusText(TaskState.Running);
                task.StartedAt = DateTimeOffset.Now;
            });

            RefreshCounts();

            var result = await FfmpegRunner.RunAsync(task, task.Cancellation?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);

            if (result.Canceled)
            {
                Finish(task, TaskState.Canceled, StringResources.GetOr("Task_Detail_Canceled", "任务已取消。"));
                return;
            }

            if (result.Succeeded)
            {
                task.Progress = 100;
                Finish(task, TaskState.Completed,
                    StringResources.Format("Task_Detail_OutputFormat", task.OutputPath));
            }
            else
            {
                Finish(task, TaskState.Failed,
                    result.ErrorMessage ?? StringResources.GetOr("Task_Detail_Failed", "处理失败，请查看日志。"));
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TaskQueueService.ExecuteAsync");
            Finish(task, TaskState.Failed,
                StringResources.Format("Task_Detail_ExceptionFormat", ex.Message));
        }
        finally
        {
            _semaphore.Release();
            RefreshCounts();
        }
    }

    /// <summary>更新任务终态并触发通知。
    /// 状态变更与事件触发必须整体在 UI 线程执行：
    /// TaskFinished/QueueChanged 的订阅者（ViewModel、页面）会直接更新绑定属性。</summary>
    private void Finish(EncodingTask task, TaskState state, string detail)
    {
        UpdateOnUi(() =>
        {
            task.State = state;
            task.StatusText = GetStatusText(state);
            task.DetailText = detail;
            task.FinishedAt = DateTimeOffset.Now;
            task.Progress = state == TaskState.Completed ? 100 : task.Progress;
            task.ProcessedTime = state == TaskState.Completed ? task.TotalDuration : task.ProcessedTime;

            // 完成后读取输出文件大小，供「大小变化」展示使用（UI 线程，IO 操作很快）
            if (state == TaskState.Completed && !string.IsNullOrEmpty(task.OutputPath))
            {
                try
                {
                    var info = new FileInfo(task.OutputPath);
                    if (info.Exists) task.OutputSizeBytes = info.Length;
                }
                catch { /* 读取失败则保持 0，不阻塞流程 */ }
            }

            NotifyTaskFinished(task);
            RefreshCounts();
        });
    }

    private void NotifyTaskFinished(EncodingTask task)
    {
        // 本地化文本在 UI 线程取用，避免跨线程访问资源加载器
        TaskFinished?.Invoke(this, task);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>在 UI 线程执行状态更新（属性变更需封送到 UI 线程）。</summary>
    private void UpdateOnUi(Action action)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            // TryEnqueue 需要 DispatcherQueueHandler 委托（与 Action 签名相同但类型不同）
            _dispatcher.TryEnqueue(new DispatcherQueueHandler(action));
            return;
        }

        action();
    }

    private void RefreshCounts()
    {
        UpdateOnUi(() =>
        {
            RunningCount = Tasks.Count(t => t.State == TaskState.Running);
            PendingCount = Tasks.Count(t => t.State == TaskState.Queued);
            CompletedCount = Tasks.Count(t => t.State == TaskState.Completed);
            FailedCount = Tasks.Count(t => t.State is TaskState.Failed or TaskState.Canceled);
            IsProcessing = Tasks.Any(t => t.State is TaskState.Running or TaskState.Queued);
            OnPropertyChanged(nameof(HasActiveTasks));
        });

        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>任务状态的显示文本（经本地化，未命中资源时回退到中文）。</summary>
    public static string GetStatusText(TaskState state) => state switch
    {
        TaskState.Queued => StringResources.GetOr("TaskState_Queued", "等待中"),
        TaskState.Running => StringResources.GetOr("TaskState_Running", "进行中"),
        TaskState.Completed => StringResources.GetOr("TaskState_Completed", "已完成"),
        TaskState.Failed => StringResources.GetOr("TaskState_Failed", "失败"),
        TaskState.Canceled => StringResources.GetOr("TaskState_Canceled", "已取消"),
        _ => string.Empty
    };

    /// <summary>向界面推送一条提示（任务页的 InfoBar）。</summary>
    public static InfoBarSeverity GetSeverity(TaskState state) => state switch
    {
        TaskState.Completed => InfoBarSeverity.Success,
        TaskState.Failed => InfoBarSeverity.Error,
        TaskState.Canceled => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational
    };

    /// <summary>取出任务日志文本（截断到合理长度）。</summary>
    public static string GetLogText(EncodingTask task)
    {
        lock (task.Log)
        {
            var text = task.Log.ToString();
            const int maxLength = 20000;
            return text.Length <= maxLength ? text : text[^maxLength..];
        }
    }

    /// <summary>所有未完成任务的快照（用于退出前确认）。</summary>
    public IReadOnlyList<EncodingTask> GetActiveTasks() => Tasks.Where(t => !t.IsFinished).ToList();
}
