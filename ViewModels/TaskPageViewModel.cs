using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FFmpegUI.ViewModels;

/// <summary>所有「生成任务」页面的公共基类。
/// 统一负责：输入文件选择 → ffprobe 探测 → 参数汇总 → 命令预览 → 加入队列。
/// 子类只需声明自己的参数属性并实现 <see cref="ApplyToOptions"/>。
/// 实现 <see cref="IPresetSource"/> 以支持「保存预设」（无需每个页面单独编写）。</summary>
public abstract partial class TaskPageViewModel : ObservableObject, IPresetSource
{
    protected TaskPageViewModel()
    {
        Options = FfmpegOptions.CreateDefault();
    }

    /// <summary>当前页面的参数对象。</summary>
    public FfmpegOptions Options { get; }

    [ObservableProperty] private MediaFileInfo? _input;

    [ObservableProperty] private string _inputPath = string.Empty;

    [ObservableProperty] private string _outputPath = string.Empty;

    [ObservableProperty] private bool _isBusy;

    /// <summary>输出文件名自定义选项，页面“输出”卡片中的 <see cref="Controls.OutputNameControl"/> 绑定于此。</summary>
    public OutputNameOptions OutputNameOptions { get; } = new();

    /// <summary>最近一次加入队列后的提示信息（“添加到任务队列”按钮下方显示，为空则不显示）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastAddedVisible))]
    private string _lastAddedMessage = string.Empty;

    /// <summary>是否有最近的加入队列提示。</summary>
    public bool LastAddedVisible => !string.IsNullOrEmpty(_lastAddedMessage);

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private bool _showStatus;

    [ObservableProperty] private Microsoft.UI.Xaml.Controls.InfoBarSeverity _statusSeverity =
        Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    #region 派生属性

    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);

    public bool HasFfmpeg => FfmpegLocator.IsConfigured();

    /// <summary>输入文件的摘要文本（容器 · 分辨率 · 时长 · 大小）。</summary>
    public string InputSummary => Input?.SummaryText ?? string.Empty;

    /// <summary>输入文件的流清单（供下拉框选择轨道）。</summary>
    public IReadOnlyList<MediaStreamInfo> Streams =>
        Input?.Streams ?? (IReadOnlyList<MediaStreamInfo>)Array.Empty<MediaStreamInfo>();

    /// <summary>实时命令预览：任意参数变化都会重新生成。</summary>
    public virtual string CommandPreview
    {
        get
        {
            try
            {
                ApplyToOptions();
                var command = FfmpegCommandBuilder.Build(Options, Input, Input?.Duration ?? TimeSpan.Zero);
                return command.ToDisplayString("ffmpeg");
            }
            catch (Exception ex)
            {
                return StringResources.Format("Msg_BuildCommandFailedFormat", ex.Message);
            }
        }
    }

    /// <summary>输出文件的默认扩展名（子类按功能覆盖）。</summary>
    public virtual string OutputExtension => "mp4";

    /// <summary>输出文件名后缀（如 "_转码"、"_剪辑"）。</summary>
    protected virtual string OutputSuffix => string.Empty;

    #endregion

    #region 命令

    /// <summary>选择输入文件。</summary>
    public IAsyncRelayCommand PickInputCommand => new AsyncRelayCommand(PickInputAsync);

    /// <summary>选择输出文件（另存为）。</summary>
    public IAsyncRelayCommand PickOutputCommand => new AsyncRelayCommand(PickOutputAsync);

    /// <summary>把当前参数加入任务队列。</summary>
    public virtual IAsyncRelayCommand AddToQueueCommand => new AsyncRelayCommand(AddToQueueAsync);

    /// <summary>扫描指定文件夹，把其中所有视频文件按当前参数批量加入队列。</summary>
    public IAsyncRelayCommand ScanFolderCommand => new AsyncRelayCommand(ScanFolderAsync);

    /// <summary>任务成功加入队列后触发（页面据此显示提示）。</summary>
    public event EventHandler<EncodingTask>? TaskAdded;

    /// <summary>触发 <see cref="TaskAdded"/>（供子类在自定义入队流程中使用）。</summary>
    protected void RaiseTaskAdded(EncodingTask task) => TaskAdded?.Invoke(this, task);

    /// <summary>是否在任意属性变化时自动刷新命令预览。
    /// 自行生成预览的子类（如合并页）可返回 false。</summary>
    protected virtual bool AutoRefreshCommandPreview => true;

    #endregion

    #region 输入 / 输出

    protected virtual async Task PickInputAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync(GetInputExtensions());
        if (string.IsNullOrEmpty(path)) return;

        InputPath = path!;
    }

    protected virtual async Task PickOutputAsync()
    {
        var suggested = string.IsNullOrWhiteSpace(OutputPath)
            ? Path.GetFileName(BuildDefaultOutputPath())
            : Path.GetFileName(OutputPath);

        var extension = OutputExtension;
        var path = await FilePickerHelper.PickSaveFileAsync(
            suggested,
            ($"{extension.ToUpperInvariant()} 文件", extension));

        if (string.IsNullOrEmpty(path)) return;

        OutputPath = path!;
        UserSetOutputPath = true;
    }

    /// <summary>输入文件的扩展名过滤（子类按功能覆盖）。</summary>
    protected virtual string[] GetInputExtensions() => Array.Empty<string>();

    partial void OnInputPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasInput));
        _ = ProbeInputAsync(value);
    }

    partial void OnOutputPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) UserSetOutputPath = true;
    }

    /// <summary>用户是否手动指定过输出路径（手动后不再随输入文件自动改写）。</summary>
    protected bool UserSetOutputPath { get; private set; }

    /// <summary>在用户未手动指定输出路径时，重新生成默认输出路径。</summary>
    protected void RefreshDefaultOutput()
    {
        if (UserSetOutputPath && !string.IsNullOrWhiteSpace(OutputPath)) return;
        OutputPath = BuildDefaultOutputPath();
    }

    /// <summary>探测输入文件信息，并在未手动指定输出路径时生成默认输出路径。</summary>
    private async Task ProbeInputAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Input = null;
            return;
        }

        IsBusy = true;
        MediaFileInfo? info = null;
        try
        {
            if (HasFfmpeg)
                info = await FfprobeService.ProbeAsync(path);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "TaskPageViewModel.ProbeInputAsync");
        }
        finally
        {
            IsBusy = false;
        }

        Input = info;
        OnInputLoaded(info);

        // 探测失败不再静默：FFmpeg 已配置且文件存在但解析不出流时给出提示，
        // 用户可据此检查文件完整性（官方《Errors and messages》：要让用户知道发生了什么）
        if (info is null && HasFfmpeg)
        {
            ShowMessage(StringResources.Format("Msg_ProbeFailedFormat", Path.GetFileName(path)),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }

        if (!UserSetOutputPath || string.IsNullOrWhiteSpace(OutputPath))
        {
            var generated = BuildDefaultOutputPath();
            UserSetOutputPath = false;
            OutputPath = generated;
        }
    }

    /// <summary>输入文件探测完成后调用（子类用于同步分辨率、时长等默认值）。</summary>
    protected virtual void OnInputLoaded(MediaFileInfo? info) { }

    /// <summary>生成默认输出路径：默认输出目录（或源文件目录）+ 按 <see cref="OutputNameOptions"/> 命名的文件名 + 扩展名。</summary>
    protected virtual string BuildDefaultOutputPath()
    {
        if (string.IsNullOrWhiteSpace(InputPath)) return string.Empty;

        var configured = SettingsService.Current.OutputDirectory;
        var directory = !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
            ? configured
            : Path.GetDirectoryName(InputPath) ?? string.Empty;

        // 自定义文件名选项优先：原文件名/前缀/后缀/自定义模板；
        // OutputSuffix（如 _转码）作为默认后缀追加，仅在 Original 模式下生效。
        var baseName = Path.GetFileNameWithoutExtension(InputPath);
        var placeholder = OutputNameOptions.Mode == OutputNameMode.Original
            ? baseName + OutputSuffix
            : baseName;

        var fileName = OutputNameOptions.BuildFileName(
            Path.Combine(directory, placeholder + "." + OutputExtension),
            "." + OutputExtension);
        return Path.Combine(directory, fileName);
    }

    #endregion

    #region 加入队列

    protected virtual async Task AddToQueueAsync()
    {
        var validation = ValidateBeforeQueue();
        if (validation is not null)
        {
            ShowMessage(validation, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return;
        }

        var output = OutputPath;
        if (string.IsNullOrWhiteSpace(output))
        {
            output = BuildDefaultOutputPath();
            OutputPath = output;
        }

        ApplyToOptions();
        Options.InputPath = InputPath;
        Options.OutputPath = output;
        Options.OverwriteOutput = SettingsService.Current.OverwriteOutput;

        var task = new EncodingTask
        {
            Input = Input,
            OutputPath = output,
            Options = Options.Clone(),
            TotalDuration = Input?.Duration ?? TimeSpan.Zero,
            InputSizeBytes = Input?.FileSize ?? 0,
            StartedAt = null
        };

        var command = FfmpegCommandBuilder.Build(task.Options, task.Input, task.TotalDuration);
        task.Arguments = command.ToDisplayString("ffmpeg");

        TaskQueueService.Instance.Enqueue(task);

        LastAddedMessage = StringResources.Format("Msg_AddedToQueueFormat", Path.GetFileName(output));
        RaiseTaskAdded(task);

        await Task.CompletedTask;
    }

    /// <summary>扫描文件夹中支持的所有视频文件，逐个按当前参数加入队列。
    /// 输出文件默认放到用户设置的输出目录，未设置时与源文件同目录、加后缀命名。</summary>
    protected virtual async Task ScanFolderAsync()
    {
        if (!HasFfmpeg)
        {
            ShowMessage(StringResources.GetOr("Msg_NoFfmpeg", "尚未配置 FFmpeg，请打开设置页指定 ffmpeg.exe 与 ffprobe.exe。"),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return;
        }

        var folder = await FilePickerHelper.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var extensions = new HashSet<string>(GetVideoExtensions(), StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).TrimStart('.')))
            .ToList();

        if (files.Count == 0)
        {
            ShowMessage(StringResources.Format("Msg_NoVideoInFolderFormat", Path.GetDirectoryName(folder) ?? folder),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            return;
        }

        IsBusy = true;
        int added = 0;
        try
        {
            foreach (var file in files)
            {
                ApplyToOptions();
                var options = Options.Clone();
                options.InputPath = file;

                var placeholder = Path.GetFileNameWithoutExtension(file) +
                    (OutputNameOptions.Mode == OutputNameMode.Original ? OutputSuffix : string.Empty);
                var directory = !string.IsNullOrWhiteSpace(SettingsService.Current.OutputDirectory) &&
                                Directory.Exists(SettingsService.Current.OutputDirectory)
                    ? SettingsService.Current.OutputDirectory!
                    : Path.GetDirectoryName(file) ?? folder;
                var outPath = OutputNameOptions.BuildPath(
                    Path.Combine(directory, placeholder + "." + OutputExtension),
                    directory,
                    "." + OutputExtension);

                options.OutputPath = outPath;
                options.OverwriteOutput = SettingsService.Current.OverwriteOutput;

                MediaFileInfo? info = null;
                if (HasFfmpeg)
                {
                    try { info = await FfprobeService.ProbeAsync(file); }
                    catch { /* 探测失败不阻塞批量添加 */ }
                }

                var task = new EncodingTask
                {
                    Input = info,
                    OutputPath = outPath,
                    Options = options,
                    TotalDuration = info?.Duration ?? TimeSpan.Zero,
                    InputSizeBytes = info?.FileSize ?? 0,
                };

                var command = FfmpegCommandBuilder.Build(task.Options, task.Input, task.TotalDuration);
                task.Arguments = command.ToDisplayString("ffmpeg");

                TaskQueueService.Instance.Enqueue(task);
                added++;
            }
        }
        finally
        {
            IsBusy = false;
        }

        LastAddedMessage = StringResources.Format("Msg_ScanFolderAddedFormat", added, Path.GetFileName(folder));
    }

    /// <summary>扫描文件夹时识别的视频扩展名（小写，不含点）。子类可扩展。</summary>
    protected virtual string[] GetVideoExtensions() => new[]
    {
        "mp4", "mkv", "mov", "avi", "wmv", "flv", "webm", "ts", "m2ts",
        "mpg", "mpeg", "m4v", "3gp", "3g2", "ogv", "vob", "rm", "rmvb", "divx", "asf",
    };

    /// <summary>加入队列前的校验；返回错误信息或 null（通过）。</summary>
    protected virtual string? ValidateBeforeQueue()
    {
        if (!HasFfmpeg)
            return StringResources.GetOr("Msg_NoFfmpeg", "尚未配置 FFmpeg，请打开设置页指定 ffmpeg.exe 与 ffprobe.exe。");

        if (string.IsNullOrWhiteSpace(InputPath) || !File.Exists(InputPath))
            return StringResources.GetOr("Msg_NoInputFile", "请先选择一个有效的输入文件。");

        if (string.IsNullOrWhiteSpace(OutputPath))
            return StringResources.GetOr("Msg_NoOutputPath", "请指定输出文件路径。");

        return null;
    }

    /// <summary>把界面参数写入 <see cref="Options"/>（子类实现）。</summary>
    protected abstract void ApplyToOptions();

    #endregion

    #region 辅助

    /// <summary>显示页面内提示（InfoBar）。</summary>
    protected void ShowMessage(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        ShowStatus = true;
    }

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(ShowStatus));

    /// <summary>任意属性变化都会使命令预览失效。
    /// 注意：CommandPreview 自身的变化不再递归触发（下方判断已排除）。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (AutoRefreshCommandPreview && e.PropertyName != nameof(CommandPreview))
            OnPropertyChanged(nameof(CommandPreview));
    }

    /// <summary>把秒数格式化为 ffmpeg 的时间字符串。</summary>
    protected static string FormatSeconds(double seconds)
        => FfmpegOptions.FormatTime(TimeSpan.FromSeconds(seconds));

    #endregion

    #region IPresetSource（保存预设）

    /// <summary>视频类预设的固定类型。</summary>
    public PresetKind Kind => PresetKind.Video;

    /// <summary>页面标识：与导航 Tag 对齐。默认按类型名推导，子类可覆盖。</summary>
    public virtual string PageTag => MapTypeToTag(GetType().Name);

    /// <summary>页面中文标题，用于预设列表展示。</summary>
    public virtual string PageTitle => MapTypeToTitle(GetType().Name);

    /// <summary>返回参数的深拷贝快照，避免后续界面修改污染已保存的预设。</summary>
    public virtual object GetOptionsSnapshot() => Options.Clone();

    /// <summary>生成参数摘要：容器 · 视频编码 · 质量/码率。</summary>
    public virtual string GetSummary()
    {
        var o = Options;
        var parts = new System.Collections.Generic.List<string>();
        parts.Add(o.Container.ToString().ToUpperInvariant());
        if (!string.IsNullOrEmpty(o.VideoCodec)) parts.Add(o.VideoCodec);
        if (o.Crf > 0) parts.Add($"CRF {o.Crf}");
        else if (o.VideoBitrateKbps > 0) parts.Add($"{o.VideoBitrateKbps}k");
        return string.Join(" · ", parts);
    }

    private static readonly System.Collections.Generic.Dictionary<string, (string Tag, string Title)> s_typeMap = new()
    {
        ["TranscodeViewModel"] = ("transcode", "转码"),
        ["TrimViewModel"] = ("trim", "剪辑"),
        ["ExtractViewModel"] = ("extract", "提取"),
        ["MergeViewModel"] = ("merge", "合并混流"),
        ["CompressViewModel"] = ("compress", "压缩"),
        ["AdvancedViewModel"] = ("advanced", "高级参数"),
    };

    private static string MapTypeToTag(string typeName)
        => s_typeMap.TryGetValue(typeName, out var v) ? v.Tag : typeName.ToLowerInvariant();

    private static string MapTypeToTitle(string typeName)
        => s_typeMap.TryGetValue(typeName, out var v) ? v.Title : typeName;

    #endregion
}
