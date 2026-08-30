using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.ViewModels;

/// <summary>单个待转换文件的转换状态。</summary>
public sealed class ImageItem : ObservableObject
{
    private string _status = string.Empty;
    private bool _succeeded;
    private bool _failed;

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);
    public bool IsFailed => _failed;

    public ImageItem(string path) => Path = path;

    /// <summary>状态文本（"待转换" / "完成" / 错误信息）。</summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool Succeeded
    {
        get => _succeeded;
        set => SetProperty(ref _succeeded, value);
    }

    public bool Failed
    {
        get => _failed;
        set { if (SetProperty(ref _failed, value)) OnPropertyChanged(nameof(IsFailed)); }
    }
}

/// <summary>图片转换页视图模型。
///
/// 设计要点：
/// <list type="bullet">
/// <item>目标格式经 <see cref="ImageCapabilityService"/> 过滤，
///       只呈现当前 ffmpeg 实际支持的编码器，避免转换时才失败；</item>
/// <item>批量转换按顺序执行（ffplay/ffmpeg 进程开销大，
///       且并发会显著增加 CPU 占用，顺序执行更可控且便于显示逐项状态）；</item>
/// <item>输出沿用「与源文件相同目录」或用户指定目录，文件名保持不变、仅换扩展名。</item>
/// </list></summary>
public sealed partial class ImageConvertViewModel : ObservableObject, IPresetSource
{
    /// <summary>转换参数（所有设置控件绑定到它）。</summary>
    public ImageConvertOptions Options { get; } = ImageConvertOptions.CreateDefault();

    /// <summary>待转换文件列表。</summary>
    public ObservableCollection<ImageItem> Files { get; } = new();

    #region 状态

    [ObservableProperty] private bool _isConverting;

    [ObservableProperty] private bool _isDetecting = true;

    [ObservableProperty] private double _progress;

    [ObservableProperty] private string _progressText = string.Empty;

    [ObservableProperty] private bool _showStatus;

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty] private string _outputDirectory = string.Empty;

    [ObservableProperty] private string _commandPreview = string.Empty;

    /// <summary>输出文件名自定义选项，页面“输出文件名”控件绑定于此。</summary>
    public OutputNameOptions OutputNameOptions { get; } = new();

    /// <summary>最近一次转换后的提示信息（位于“开始转换”按钮下方）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastAddedVisible))]
    private string _lastAddedMessage = string.Empty;

    /// <summary>是否有最近的提示。</summary>
    public bool LastAddedVisible => !string.IsNullOrEmpty(_lastAddedMessage);

    #endregion

    #region 下拉框数据源

    /// <summary>可用的目标格式（经能力检测过滤）。</summary>
    public ObservableCollection<ImageFormatInfo> AvailableFormats { get; } = new();

    public IReadOnlyList<KeyValuePair<string, ImageResizeMode>> ResizeModes { get; } = new[]
    {
        new KeyValuePair<string, ImageResizeMode>("保持原始尺寸", ImageResizeMode.None),
        new KeyValuePair<string, ImageResizeMode>("按宽度（高度自适应）", ImageResizeMode.ByWidth),
        new KeyValuePair<string, ImageResizeMode>("按高度（宽度自适应）", ImageResizeMode.ByHeight),
        new KeyValuePair<string, ImageResizeMode>("指定宽高", ImageResizeMode.Exact),
        new KeyValuePair<string, ImageResizeMode>("限制在矩形内（等比）", ImageResizeMode.Fit)
    };

    public IReadOnlyList<KeyValuePair<string, int>> Rotations { get; } = new[]
    {
        new KeyValuePair<string, int>("不旋转", 0),
        new KeyValuePair<string, int>("顺时针 90°", 90),
        new KeyValuePair<string, int>("180°", 180),
        new KeyValuePair<string, int>("逆时针 90°", 270)
    };

    #endregion

    #region 绑定索引

    [ObservableProperty] private int _formatIndex = -1;

    [ObservableProperty] private int _resizeModeIndex;

    [ObservableProperty] private int _rotationIndex;

    #endregion

    #region 绑定属性

    public int Quality
    {
        get => Options.Quality;
        set { if (Options.Quality == value) return; Options.Quality = value; OnPropertyChanged(); RefreshPreview(); }
    }

    public int Width
    {
        get => Options.Width;
        set { if (Options.Width == value) return; Options.Width = value; OnPropertyChanged(); RefreshPreview(); }
    }

    public int Height
    {
        get => Options.Height;
        set { if (Options.Height == value) return; Options.Height = value; OnPropertyChanged(); RefreshPreview(); }
    }

    public bool AllowUpscale
    {
        get => Options.AllowUpscale;
        set { if (Options.AllowUpscale == value) return; Options.AllowUpscale = value; OnPropertyChanged(); RefreshPreview(); }
    }

    public bool FlipHorizontal
    {
        get => Options.FlipHorizontal;
        set { if (Options.FlipHorizontal == value) return; Options.FlipHorizontal = value; OnPropertyChanged(); RefreshPreview(); }
    }

    public bool FlipVertical
    {
        get => Options.FlipVertical;
        set { if (Options.FlipVertical == value) return; Options.FlipVertical = value; OnPropertyChanged(); RefreshPreview(); }
    }

    public bool Grayscale
    {
        get => Options.Grayscale;
        set { if (Options.Grayscale == value) return; Options.Grayscale = value; OnPropertyChanged(); RefreshPreview(); }
    }

    #endregion

    /// <summary>目标格式使用 qscale（数值越小越好）时为 true，用于界面提示的方向说明。</summary>
    public bool IsQScaleMode => Options.TargetFormat.QualityMode == ImageQualityMode.QScale;

    /// <summary>是否显示质量设置（无损格式无质量参数）。</summary>
    public bool ShowQuality => Options.TargetFormat.QualityMode != ImageQualityMode.None;

    /// <summary>质量滑块的最小值。</summary>
    public int QualityMinimum => IsQScaleMode ? 2 : 0;

    /// <summary>质量滑块的最大值。</summary>
    public int QualityMaximum => IsQScaleMode ? 31 : 100;

    /// <summary>质量说明。
    /// qscale 与 quality 的数值方向相反，必须显式说明，否则用户会填反。</summary>
    public string QualityHint => Options.TargetFormat.QualityMode switch
    {
        ImageQualityMode.QScale =>
            StringResources.GetOr("Image_QualityHintQScale", "2–31，数值越小质量越高、体积越大"),
        ImageQualityMode.Quality =>
            StringResources.GetOr("Image_QualityHintQuality", "0–100，数值越大质量越高、体积越大"),
        _ => string.Empty
    };

    public bool HasFiles => Files.Count > 0;

    public bool HasFfmpeg => FfmpegLocator.IsExecutable(SettingsService.Current.FfmpegPath);

    public ImageConvertViewModel()
    {
        RefreshPreview();
    }

    /// <summary>页面加载时调用：执行能力检测并填充可用格式。</summary>
    public async Task InitializeAsync()
    {
        IsDetecting = true;

        try
        {
            await ImageCapabilityService.DetectAsync();

            AvailableFormats.Clear();
            foreach (var format in ImageCapabilityService.GetAvailableFormats())
                AvailableFormats.Add(format);

            // 默认选中 JPEG；若不可用则选第一项
            int preferredIndex = -1;
            for (int i = 0; i < AvailableFormats.Count; i++)
            {
                if (AvailableFormats[i].Extension == "jpg")
                {
                    preferredIndex = i;
                    break;
                }
            }

            FormatIndex = preferredIndex >= 0 ? preferredIndex : (AvailableFormats.Count > 0 ? 0 : -1);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ImageConvertViewModel.InitializeAsync");
            ShowError(ex.Message);
        }
        finally
        {
            IsDetecting = false;
        }
    }

    partial void OnFormatIndexChanged(int value)
    {
        if (value < 0 || value >= AvailableFormats.Count) return;

        Options.TargetFormat = AvailableFormats[value];

        // 切换格式时把质量调整到该格式的有效区间内
        Options.Quality = Options.TargetFormat.QualityMode switch
        {
            ImageQualityMode.QScale => 2,   // JPEG 默认高质量
            ImageQualityMode.Quality => 80, // WebP 默认 80
            _ => Options.Quality
        };

        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(ShowQuality));
        OnPropertyChanged(nameof(IsQScaleMode));
        OnPropertyChanged(nameof(QualityMinimum));
        OnPropertyChanged(nameof(QualityMaximum));
        OnPropertyChanged(nameof(QualityHint));
        RefreshPreview();
    }

    partial void OnResizeModeIndexChanged(int value)
    {
        if (value < 0 || value >= ResizeModes.Count) return;
        Options.ResizeMode = ResizeModes[value].Value;
        OnPropertyChanged(nameof(ShowWidthInput));
        OnPropertyChanged(nameof(ShowHeightInput));
        RefreshPreview();
    }

    partial void OnRotationIndexChanged(int value)
    {
        if (value < 0 || value >= Rotations.Count) return;
        Options.Rotate = Rotations[value].Value;
        RefreshPreview();
    }

    partial void OnOutputDirectoryChanged(string value) => RefreshPreview();

    /// <summary>当前尺寸模式是否需要输入宽度。</summary>
    public bool ShowWidthInput =>
        Options.ResizeMode is ImageResizeMode.ByWidth or ImageResizeMode.Exact or ImageResizeMode.Fit;

    /// <summary>当前尺寸模式是否需要输入高度。</summary>
    public bool ShowHeightInput =>
        Options.ResizeMode is ImageResizeMode.ByHeight or ImageResizeMode.Exact or ImageResizeMode.Fit;

    #region 命令

    // 命令必须缓存为单一实例，否则 NotifyCanExecuteChanged() 作用不到 XAML 绑定的对象
    private IAsyncRelayCommand? _addFilesCommand;
    private IAsyncRelayCommand? _pickOutputDirectoryCommand;
    private IAsyncRelayCommand? _convertCommand;
    private IRelayCommand? _clearFilesCommand;
    private IRelayCommand? _copyCommandCommand;

    public IAsyncRelayCommand AddFilesCommand =>
        _addFilesCommand ??= new AsyncRelayCommand(AddFilesAsync, () => !IsConverting);

    public IAsyncRelayCommand PickOutputDirectoryCommand =>
        _pickOutputDirectoryCommand ??= new AsyncRelayCommand(PickOutputDirectoryAsync, () => !IsConverting);

    public IAsyncRelayCommand ConvertCommand =>
        _convertCommand ??= new AsyncRelayCommand(ConvertAsync, () => HasFiles && HasFfmpeg && !IsConverting);

    public IRelayCommand ClearFilesCommand =>
        _clearFilesCommand ??= new RelayCommand(ClearFiles, () => HasFiles && !IsConverting);

    public IRelayCommand CopyCommandCommand =>
        _copyCommandCommand ??= new RelayCommand(CopyCommand);

    private async Task AddFilesAsync()
    {
        var paths = await FilePickerHelper.PickMultipleFilesAsync(ImageFormatCatalog.InputExtensions);
        if (paths.Count == 0) return;

        var added = 0;
        foreach (var path in paths)
        {
            // 去重：同一文件不重复添加
            if (Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;

            Files.Add(new ImageItem(path));
            added++;
        }

        OnPropertyChanged(nameof(HasFiles));
        RefreshCommandStates();
        RefreshPreview();

        if (added == 0 && paths.Count > 0)
            ShowInfo(StringResources.GetOr("Image_AllDuplicates", "所选文件已在列表中。"));

        // 对需要外部库的文件给出提示（如 HEIC），避免转换时才发现问题
        WarnAboutExternalDependencies(paths);
    }

    private async Task PickOutputDirectoryAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync();
        if (string.IsNullOrEmpty(folder)) return;

        OutputDirectory = folder!;
    }

    private void ClearFiles()
    {
        Files.Clear();
        Progress = 0;
        ProgressText = string.Empty;
        OnPropertyChanged(nameof(HasFiles));
        RefreshCommandStates();
        RefreshPreview();
    }

    private void CopyCommand()
    {
        if (string.IsNullOrEmpty(CommandPreview)) return;

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage
            {
                RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
            };
            package.SetText(CommandPreview);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();

            ShowInfo(StringResources.GetOr("Msg_CommandCopied", "命令已复制到剪贴板。"));
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ImageConvertViewModel.CopyCommand");
        }
    }

    private async Task ConvertAsync()
    {
        if (!HasFfmpeg)
        {
            ShowError(StringResources.GetOr("Error_NoFfmpeg", "未配置 ffmpeg.exe，请先打开设置页指定路径。"));
            return;
        }

        if (!HasFiles) return;

        IsConverting = true;
        Progress = 0;
        RefreshCommandStates();

        var succeeded = 0;
        var failed = 0;
        var useOutputDirectory = !string.IsNullOrWhiteSpace(OutputDirectory);

        foreach (var item in Files)
        {
            item.Status = StringResources.GetOr("Image_Converting", "转换中……");
            item.Succeeded = false;
            item.Failed = false;

            var targetPath = BuildTargetPath(item.Path, useOutputDirectory);
            var result = await ImageConverter.ConvertAsync(item.Path, targetPath, Options.Clone());

            if (result.Succeeded)
            {
                succeeded++;
                item.Succeeded = true;
                item.Status = StringResources.GetOr("Image_Done", "完成");
            }
            else
            {
                failed++;
                item.Failed = true;
                item.Status = result.ErrorMessage ?? StringResources.GetOr("Image_Failed", "失败");
            }

            Progress = (double)(succeeded + failed) / Files.Count * 100;
            ProgressText = string.Format(
                CultureInfo.CurrentCulture,
                StringResources.GetOr("Image_ProgressFormat", "{0} / {1}"),
                succeeded + failed, Files.Count);
        }

        IsConverting = false;
        RefreshCommandStates();

        if (failed == 0)
        {
            var message = string.Format(
                CultureInfo.CurrentCulture,
                StringResources.GetOr("Image_AllDoneFormat", "全部转换完成（{0} 个文件）。"),
                succeeded);
            ShowInfo(message);
            LastAddedMessage = message;
        }
        else
        {
            var message = string.Format(
                CultureInfo.CurrentCulture,
                StringResources.GetOr("Image_PartialFormat", "完成 {0} 个，失败 {1} 个。"),
                succeeded, failed);
            ShowError(message);
            LastAddedMessage = message;
        }
    }

    #endregion

    /// <summary>生成输出路径：按 <see cref="OutputNameOptions"/> 命名，仅换目标扩展名。</summary>
    private string BuildTargetPath(string sourcePath, bool useOutputDirectory)
    {
        var extension = Options.TargetFormat.Extension;
        var directory = useOutputDirectory
            ? OutputDirectory
            : System.IO.Path.GetDirectoryName(sourcePath) ?? string.Empty;

        return OutputNameOptions.BuildPath(sourcePath, directory, "." + extension);
    }

    /// <summary>对需要外部库的文件给出提示。</summary>
    private void WarnAboutExternalDependencies(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var hint = ImageFormatCatalog.GetDependencyHint(path);
            if (hint is null) continue;

            ShowInfo(string.Format(
                CultureInfo.CurrentCulture,
                StringResources.GetOr("Image_DependencyHintFormat",
                    "注意：{0} 可能需要额外组件——{1}"),
                System.IO.Path.GetFileName(path), hint));

            // 只提示第一个，避免刷屏
            break;
        }
    }

    private void RefreshCommandStates()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        PickOutputDirectoryCommand.NotifyCanExecuteChanged();
        ConvertCommand.NotifyCanExecuteChanged();
        ClearFilesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>刷新命令预览（用列表首个文件示例）。</summary>
    private void RefreshPreview()
    {
        try
        {
            if (Files.Count == 0)
            {
                CommandPreview = string.Empty;
                return;
            }

            var source = Files[0].Path;
            var target = BuildTargetPath(source, !string.IsNullOrWhiteSpace(OutputDirectory));
            CommandPreview = ImageConverter.BuildDisplayText(source, target, Options);
        }
        catch (Exception ex)
        {
            CommandPreview = StringResources.FormatOr("Msg_BuildCommandFailedFormat",
                $"生成命令失败：{ex.Message}", ex.Message);
        }
    }

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

    #region IPresetSource（保存预设）

    public PresetKind Kind => PresetKind.Image;

    public string PageTag => "image";

    public string PageTitle => "图片转换";

    /// <summary>返回参数的深拷贝快照，避免后续界面修改污染已保存的预设。</summary>
    public object GetOptionsSnapshot() => Options.Clone();

    /// <summary>生成参数摘要：目标格式 · 质量。</summary>
    public string GetSummary()
    {
        var parts = new List<string> { Options.TargetFormat.DisplayName };
        if (Options.TargetFormat.QualityMode != ImageQualityMode.None)
            parts.Add($"质量 {Options.Quality}");
        return string.Join(" · ", parts);
    }

    #endregion
}
