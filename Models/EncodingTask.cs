using CommunityToolkit.Mvvm.ComponentModel;
using FFmpegUI.Helpers;
using Microsoft.UI.Dispatching;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FFmpegUI.Models;

/// <summary>任务状态。</summary>
public enum TaskState
{
    /// <summary>等待执行（队列中）。</summary>
    Queued,

    /// <summary>正在运行。</summary>
    Running,

    /// <summary>已完成。</summary>
    Completed,

    /// <summary>失败。</summary>
    Failed,

    /// <summary>已取消。</summary>
    Canceled
}

/// <summary>一个转码/处理任务。继承 ObservableObject 以便直接绑定到任务列表。
/// 线程模型（官方《Threading》要求 UI 对象仅限 UI 线程访问）：
/// ffmpeg 进程在后台线程解析进度，本类先把最新值暂存到普通字段，
/// 再经 DispatcherQueue 节流后切回 UI 线程才写入可通知属性并触发 INPC，
/// 保证 x:Bind 的界面更新始终发生在 UI 线程。</summary>
public sealed partial class EncodingTask : ObservableObject
{
    /// <summary>UI 线程调度器（由 TaskQueueService.Configure 注入）。</summary>
    private static DispatcherQueue? UiDispatcher;

    /// <summary>注入 UI 调度器（应用启动时在 UI 线程调用一次）。</summary>
    public static void SetUiDispatcher(DispatcherQueue dispatcher) => UiDispatcher = dispatcher;

    /// <summary>后台暂存：是否已有一次刷新在 UI 队列中排队（1=是）。</summary>
    private int _flushQueued;

    private double _pendingProgress;
    private TimeSpan _pendingProcessedTime;
    private TimeSpan _pendingRemaining;
    private double _pendingSpeed;
    private double _pendingBitrateKbps;

    private TaskState _state = TaskState.Queued;
    private double _progress;
    private TimeSpan _processedTime;
    private TimeSpan _estimatedRemaining = TimeSpan.Zero;
    private double _speed;
    private double _outputBitrateKbps;
    private string _statusText = string.Empty;
    private string _detailText = string.Empty;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;

    public EncodingTask()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    /// <summary>任务唯一标识。</summary>
    public string Id { get; }

    /// <summary>输入文件信息（探测结果，可能为 null）。</summary>
    public MediaFileInfo? Input { get; set; }

    /// <summary>输出文件路径。</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>生成该任务时使用的参数快照。</summary>
    public FfmpegOptions Options { get; set; } = FfmpegOptions.CreateDefault();

    /// <summary>最终执行的 ffmpeg 命令行（不含可执行文件）。</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>任务创建时间。</summary>
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public TaskState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(StateGlyph));
            }
        }
    }

    /// <summary>进度百分比（0–100）。</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
                OnPropertyChanged(nameof(IsIndeterminate));
        }
    }

    /// <summary>已处理的媒体时间。</summary>
    public TimeSpan ProcessedTime
    {
        get => _processedTime;
        set
        {
            if (SetProperty(ref _processedTime, value))
                OnPropertyChanged(nameof(DetailText));
        }
    }

    /// <summary>预计剩余时间。</summary>
    public TimeSpan EstimatedRemaining
    {
        get => _estimatedRemaining;
        set
        {
            if (SetProperty(ref _estimatedRemaining, value))
                OnPropertyChanged(nameof(DetailText));
        }
    }

    /// <summary>处理速度倍数（如 3.2 表示 3.2 倍速）。</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, value))
                OnPropertyChanged(nameof(DetailText));
        }
    }

    /// <summary>输出码率（kbps）。</summary>
    public double OutputBitrateKbps
    {
        get => _outputBitrateKbps;
        set
        {
            if (SetProperty(ref _outputBitrateKbps, value))
                OnPropertyChanged(nameof(DetailText));
        }
    }

    /// <summary>输入文件大小（字节），用于展示「大小变化」。</summary>
    private long _inputSizeBytes;
    public long InputSizeBytes
    {
        get => _inputSizeBytes;
        set
        {
            if (SetProperty(ref _inputSizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeChangeText));
                OnPropertyChanged(nameof(ProgressDetailText));
            }
        }
    }

    /// <summary>输出文件大小（字节），任务完成后由队列服务填入。</summary>
    private long _outputSizeBytes;
    public long OutputSizeBytes
    {
        get => _outputSizeBytes;
        set
        {
            if (SetProperty(ref _outputSizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeChangeText));
                OnPropertyChanged(nameof(ProgressDetailText));
            }
        }
    }

    /// <summary>输入/输出大小变化描述（如「125.3 MB → 48.1 MB（-62%）」）。</summary>
    public string SizeChangeText
    {
        get
        {
            if (InputSizeBytes <= 0) return string.Empty;
            if (OutputSizeBytes <= 0) return MediaFileInfo.FormatSize(InputSizeBytes);

            var pct = (double)(OutputSizeBytes - InputSizeBytes) / InputSizeBytes * 100;
            var sign = pct >= 0 ? "+" : "";
            return $"{MediaFileInfo.FormatSize(InputSizeBytes)} → {MediaFileInfo.FormatSize(OutputSizeBytes)} ({sign}{pct:0.0}%)";
        }
    }

    /// <summary>状态文本（等待中 / 进行中 / 已完成 / 失败 / 已取消）。</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>第二行详情文本（速度、剩余时间、错误信息等）。</summary>
    public string DetailText
    {
        get => _detailText;
        set => SetProperty(ref _detailText, value);
    }

    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        set => SetProperty(ref _startedAt, value);
    }

    public DateTimeOffset? FinishedAt
    {
        get => _finishedAt;
        set => SetProperty(ref _finishedAt, value);
    }

    /// <summary>运行时日志（ffmpeg 输出尾部）。</summary>
    public StringBuilder Log { get; } = new();

    /// <summary>取消源。</summary>
    public System.Threading.CancellationTokenSource? Cancellation { get; set; }

    /// <summary>输入总时长（用于计算进度；未知时为 Zero）。</summary>
    public TimeSpan TotalDuration { get; set; }

    public bool IsRunning => State == TaskState.Running;

    public bool IsFinished => State is TaskState.Completed or TaskState.Failed or TaskState.Canceled;

    public bool CanCancel => State is TaskState.Queued or TaskState.Running;

    /// <summary>进度条在总时长未知时切换为不确定模式（官方 ProgressBar 用法）。</summary>
    public bool IsIndeterminate => State == TaskState.Running && TotalDuration <= TimeSpan.Zero && Progress <= 0;

    /// <summary>列表显示名称。</summary>
    public string DisplayName =>
        Input is null ? Path.GetFileName(OutputPath) : Input.FileName;

    /// <summary>状态图标字形（Segoe Fluent Icons）。</summary>
    public string StateGlyph => State switch
    {
        TaskState.Queued => "\uE823",
        TaskState.Running => "\uE768",
        TaskState.Completed => "\uE73E",
        TaskState.Failed => "\uE783",
        TaskState.Canceled => "\uE8D8",
        _ => "\uE946"
    };

    /// <summary>进度与速度的组合描述。</summary>
    public string ProgressDetailText
    {
        get
        {
            if (State == TaskState.Failed || State == TaskState.Canceled) return DetailText;

            var parts = new System.Collections.Generic.List<string>();
            parts.Add(TotalDuration > TimeSpan.Zero
                ? $"{ProcessedTime:hh\\:mm\\:ss} / {TotalDuration:hh\\:mm\\:ss}"
                : ProcessedTime.ToString(@"hh\:mm\:ss"));

            if (Speed > 0) parts.Add($"{Speed.ToString("0.0x", CultureInfo.CurrentCulture)}");
            if (OutputBitrateKbps > 0) parts.Add($"{OutputBitrateKbps.ToString("0", CultureInfo.CurrentCulture)} kbps");
            if (State == TaskState.Running && EstimatedRemaining > TimeSpan.Zero)
                parts.Add(StringResources.Format("Task_RemainingFormat", EstimatedRemaining.ToString(@"hh\:mm\:ss")));
            if (State == TaskState.Completed && !string.IsNullOrEmpty(SizeChangeText))
                parts.Add(SizeChangeText);

            return string.Join(StringResources.GetOr("Common_Separator", " · "), parts);
        }
    }

    #region 后台进度更新（官方线程模型）

    /// <summary>由 ffmpeg 进程读取线程（后台线程）调用：暂存最新进度并请求 UI 刷新。
    /// 不直接写可通知属性——INPC 订阅者（x:Bind）要求在 UI 线程被调用。
    /// 节流策略：首次调用后置位 _flushQueued，后续调用只覆盖暂存值；
    /// UI 线程执行 Flush 时复位标志。效果是把高频输出合并到 UI 帧节奏。</summary>
    public void SetRunnerProgress(double progress, TimeSpan processed, double speed, double bitrateKbps)
    {
        if (State != TaskState.Running) return;

        // 剩余时间按速度推算（后台线程只做纯计算，不触碰 UI 对象）
        var remaining = TimeSpan.Zero;
        if (TotalDuration > TimeSpan.Zero && speed > 0)
        {
            var seconds = (TotalDuration - processed).TotalSeconds / speed;
            if (seconds > 0) remaining = TimeSpan.FromSeconds(seconds);
        }

        _pendingProgress = progress < 0 ? 0 : progress > 100 ? 100 : progress;
        _pendingProcessedTime = processed;
        _pendingSpeed = speed;
        _pendingBitrateKbps = bitrateKbps;
        _pendingRemaining = remaining;

        if (Interlocked.Exchange(ref _flushQueued, 1) == 0)
            UiDispatcher?.TryEnqueue(new DispatcherQueueHandler(FlushPendingProgress));
    }

    /// <summary>把暂存值写入可通知属性（只在 UI 线程执行）。</summary>
    private void FlushPendingProgress()
    {
        Volatile.Write(ref _flushQueued, 0);

        // 终态由 Finish 负责最终状态，避免旧进度覆盖 100%
        if (State != TaskState.Running) return;

        ProcessedTime = _pendingProcessedTime;
        Speed = _pendingSpeed;
        OutputBitrateKbps = _pendingBitrateKbps;
        EstimatedRemaining = _pendingRemaining;
        Progress = _pendingProgress;
    }

    #endregion
}
