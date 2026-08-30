using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace FFmpegUI.ViewModels;

/// <summary>媒体信息探测页（ffprobe）视图模型。
///
/// 定位：把 ffprobe 的全部主要与高级选项暴露为可视化控件，
/// 命令生成统一交给 <see cref="FfprobeCommandBuilder"/>，
/// 执行交给 <see cref="FfprobeService.RunAsync"/>。
/// 未做成长尾选项的 UI 控件，可通过「自定义参数」直通覆盖。</summary>
public sealed partial class ProbeViewModel : ObservableObject
{
    /// <summary>底层参数对象（所有控件双向绑定到它）。</summary>
    public FfprobeOptions ProbeOptions { get; } = new();

    #region 输入

    [ObservableProperty] private string _inputPath = string.Empty;

    [ObservableProperty] private bool _isBusy;

    #endregion

    #region 状态提示（官方 InfoBar）

    [ObservableProperty] private bool _showStatus;

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    #endregion

    #region 结果

    [ObservableProperty] private string _rawOutput = string.Empty;

    [ObservableProperty] private string _commandPreview = string.Empty;

    #endregion

    #region 下拉框数据源

    /// <summary>输出格式（官方 -print_format 支持的全套格式）。</summary>
    public IReadOnlyList<KeyValuePair<string, FfprobeOutputFormat>> OutputFormats { get; } = new[]
    {
        new KeyValuePair<string, FfprobeOutputFormat>("JSON（推荐，结构化）", FfprobeOutputFormat.Json),
        new KeyValuePair<string, FfprobeOutputFormat>("XML", FfprobeOutputFormat.Xml),
        new KeyValuePair<string, FfprobeOutputFormat>("CSV（表格导入）", FfprobeOutputFormat.Csv),
        new KeyValuePair<string, FfprobeOutputFormat>("Flat（扁平键值）", FfprobeOutputFormat.Flat),
        new KeyValuePair<string, FfprobeOutputFormat>("INI", FfprobeOutputFormat.Ini),
        new KeyValuePair<string, FfprobeOutputFormat>("Compact（紧凑）", FfprobeOutputFormat.Compact),
        new KeyValuePair<string, FfprobeOutputFormat>("Default（人类可读）", FfprobeOutputFormat.Default)
    };

    /// <summary>哈希算法（-show_data_hash）。</summary>
    public IReadOnlyList<KeyValuePair<string, FfprobeHashAlgorithm>> HashAlgorithms { get; } = new[]
    {
        new KeyValuePair<string, FfprobeHashAlgorithm>("不计算", FfprobeHashAlgorithm.None),
        new KeyValuePair<string, FfprobeHashAlgorithm>("CRC32", FfprobeHashAlgorithm.CRC32),
        new KeyValuePair<string, FfprobeHashAlgorithm>("MD5", FfprobeHashAlgorithm.MD5),
        new KeyValuePair<string, FfprobeHashAlgorithm>("SHA256", FfprobeHashAlgorithm.SHA256),
        new KeyValuePair<string, FfprobeHashAlgorithm>("SHA512", FfprobeHashAlgorithm.SHA512),
        new KeyValuePair<string, FfprobeHashAlgorithm>("Adler32", FfprobeHashAlgorithm.adler32),
        new KeyValuePair<string, FfprobeHashAlgorithm>("murmur3", FfprobeHashAlgorithm.murmur3)
    };

    /// <summary>常用 -show_entries 预设。
    /// 官方推荐用 show_entries 精确控制输出字段，可大幅减小输出体积。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> EntryPresets { get; } = new[]
    {
        new KeyValuePair<string, string>("不限制（用下方 show 开关）", string.Empty),
        new KeyValuePair<string, string>("仅时长：format=duration", "format=duration"),
        new KeyValuePair<string, string>("流概览：stream=index,codec_name,codec_type", "stream=index,codec_name,codec_type"),
        new KeyValuePair<string, string>("分辨率：stream=index,width,height", "stream=index,width,height"),
        new KeyValuePair<string, string>("帧率：stream=index,avg_frame_rate,r_frame_rate", "stream=index,avg_frame_rate,r_frame_rate"),
        new KeyValuePair<string, string>("码率：stream=index,bit_rate", "stream=index,bit_rate"),
        new KeyValuePair<string, string>("容器+流摘要：format=duration,size,bit_rate:stream=index,codec_name",
            "format=duration,size,bit_rate:stream=index,codec_name"),
        new KeyValuePair<string, string>("章节：chapter=id,start,end,title", "chapter=id,start,end,title"),
        new KeyValuePair<string, string>("关键帧：frame=key_frame,pts_time", "frame=key_frame,pts_time"),
        new KeyValuePair<string, string>("数据包：packet=pts_time,dts_time,size,flags", "packet=pts_time,dts_time,size,flags")
    };

    /// <summary>常用的流选择器（-select_streams）预设。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> StreamSelectors { get; } = new[]
    {
        new KeyValuePair<string, string>("全部流", string.Empty),
        new KeyValuePair<string, string>("首个视频流：v", "v"),
        new KeyValuePair<string, string>("首个音频流：a", "a"),
        new KeyValuePair<string, string>("首个字幕流：s", "s"),
        new KeyValuePair<string, string>("第 1 条视频：v:0", "v:0"),
        new KeyValuePair<string, string>("第 2 条音频：a:1", "a:1"),
        new KeyValuePair<string, string>("第 1 条字幕：s:0", "s:0"),
        new KeyValuePair<string, string>("仅视频：V（含附件/图片）", "V"),
        new KeyValuePair<string, string>("仅数据：d", "d")
    };

    /// <summary>能力查询选项（不需要输入文件即可执行）。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> CapabilityQueries { get; } = new[]
    {
        new KeyValuePair<string, string>("编解码器 -codecs", "codecs"),
        new KeyValuePair<string, string>("封装格式 -formats", "formats"),
        new KeyValuePair<string, string>("滤镜 -filters", "filters"),
        new KeyValuePair<string, string>("像素格式 -pix_fmts", "pix_fmts"),
        new KeyValuePair<string, string>("采样格式 -sample_fmts", "sample_fmts"),
        new KeyValuePair<string, string>("声道布局 -layouts", "layouts"),
        new KeyValuePair<string, string>("硬件加速 -hwaccels", "hwaccels"),
        new KeyValuePair<string, string>("协议 -protocols", "protocols"),
        new KeyValuePair<string, string>("复用器 -muxers", "muxers"),
        new KeyValuePair<string, string>("解复用器 -demuxers", "demuxers"),
        new KeyValuePair<string, string>("设备 -devices", "devices"),
        new KeyValuePair<string, string>("比特流过滤器 -bsfs", "bsfs"),
        new KeyValuePair<string, string>("编码器 -encoders", "encoders"),
        new KeyValuePair<string, string>("解码器 -decoders", "decoders"),
        new KeyValuePair<string, string>("颜色名 -colors", "colors"),
        new KeyValuePair<string, string>("编译配置 -buildconf", "buildconf"),
        new KeyValuePair<string, string>("版本 -version", "version")
    };

    #endregion

    #region 绑定用索引（ComboBox 只能绑 SelectedIndex）

    [ObservableProperty] private int _outputFormatIndex;

    [ObservableProperty] private int _hashAlgorithmIndex;

    [ObservableProperty] private int _entryPresetIndex;

    [ObservableProperty] private int _streamSelectorIndex;

    [ObservableProperty] private int _capabilityIndex = -1;

    #endregion

    #region 直接映射到 ProbeOptions 的绑定属性

    public bool ShowFormat
    {
        get => ProbeOptions.ShowFormat;
        set { if (ProbeOptions.ShowFormat != value) { ProbeOptions.ShowFormat = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowStreams
    {
        get => ProbeOptions.ShowStreams;
        set { if (ProbeOptions.ShowStreams != value) { ProbeOptions.ShowStreams = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowPackets
    {
        get => ProbeOptions.ShowPackets;
        set { if (ProbeOptions.ShowPackets != value) { ProbeOptions.ShowPackets = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowFrames
    {
        get => ProbeOptions.ShowFrames;
        set { if (ProbeOptions.ShowFrames != value) { ProbeOptions.ShowFrames = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowPrograms
    {
        get => ProbeOptions.ShowPrograms;
        set { if (ProbeOptions.ShowPrograms != value) { ProbeOptions.ShowPrograms = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowChapters
    {
        get => ProbeOptions.ShowChapters;
        set { if (ProbeOptions.ShowChapters != value) { ProbeOptions.ShowChapters = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowData
    {
        get => ProbeOptions.ShowData;
        set { if (ProbeOptions.ShowData != value) { ProbeOptions.ShowData = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowError
    {
        get => ProbeOptions.ShowError;
        set { if (ProbeOptions.ShowError != value) { ProbeOptions.ShowError = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowLog
    {
        get => ProbeOptions.ShowLog;
        set { if (ProbeOptions.ShowLog != value) { ProbeOptions.ShowLog = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ShowPrivateData
    {
        get => ProbeOptions.ShowPrivateData;
        set { if (ProbeOptions.ShowPrivateData != value) { ProbeOptions.ShowPrivateData = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool Pretty
    {
        get => ProbeOptions.Pretty;
        set { if (ProbeOptions.Pretty != value) { ProbeOptions.Pretty = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool Unit
    {
        get => ProbeOptions.Unit;
        set { if (ProbeOptions.Unit != value) { ProbeOptions.Unit = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool Prefix
    {
        get => ProbeOptions.Prefix;
        set { if (ProbeOptions.Prefix != value) { ProbeOptions.Prefix = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool ByteBinaryPrefix
    {
        get => ProbeOptions.ByteBinaryPrefix;
        set { if (ProbeOptions.ByteBinaryPrefix != value) { ProbeOptions.ByteBinaryPrefix = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool Sexagesimal
    {
        get => ProbeOptions.Sexagesimal;
        set { if (ProbeOptions.Sexagesimal != value) { ProbeOptions.Sexagesimal = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool Bitexact
    {
        get => ProbeOptions.Bitexact;
        set { if (ProbeOptions.Bitexact != value) { ProbeOptions.Bitexact = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool CountFrames
    {
        get => ProbeOptions.CountFrames;
        set { if (ProbeOptions.CountFrames != value) { ProbeOptions.CountFrames = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public bool CountPackets
    {
        get => ProbeOptions.CountPackets;
        set { if (ProbeOptions.CountPackets != value) { ProbeOptions.CountPackets = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public string InputFormat
    {
        get => ProbeOptions.InputFormat;
        set { if (ProbeOptions.InputFormat != value) { ProbeOptions.InputFormat = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public string ShowEntriesText
    {
        get => ProbeOptions.ShowEntries;
        set { if (ProbeOptions.ShowEntries != value) { ProbeOptions.ShowEntries = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public string ReadIntervalsText
    {
        get => ProbeOptions.ReadIntervals;
        set { if (ProbeOptions.ReadIntervals != value) { ProbeOptions.ReadIntervals = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public string CustomGlobalArguments
    {
        get => ProbeOptions.CustomGlobalArguments;
        set { if (ProbeOptions.CustomGlobalArguments != value) { ProbeOptions.CustomGlobalArguments = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    public string CustomInputArguments
    {
        get => ProbeOptions.CustomInputArguments;
        set { if (ProbeOptions.CustomInputArguments != value) { ProbeOptions.CustomInputArguments = value; OnPropertyChanged(); RefreshPreview(); } }
    }

    #endregion

    public bool HasFfprobe => FfmpegLocator.IsExecutable(SettingsService.Current.FfprobePath);

    public ProbeViewModel()
    {
        RefreshPreview();
    }

    #region 索引变化回调

    partial void OnOutputFormatIndexChanged(int value)
    {
        if (value >= 0 && value < OutputFormats.Count)
            ProbeOptions.OutputFormat = OutputFormats[value].Value;
        RefreshPreview();
    }

    partial void OnHashAlgorithmIndexChanged(int value)
    {
        if (value >= 0 && value < HashAlgorithms.Count)
            ProbeOptions.ShowDataHash = HashAlgorithms[value].Value;
        RefreshPreview();
    }

    partial void OnEntryPresetIndexChanged(int value)
    {
        if (value >= 0 && value < EntryPresets.Count)
            ProbeOptions.ShowEntries = EntryPresets[value].Value;
        OnPropertyChanged(nameof(ShowEntriesText));
        RefreshPreview();
    }

    partial void OnStreamSelectorIndexChanged(int value)
    {
        if (value >= 0 && value < StreamSelectors.Count)
            ProbeOptions.SelectStreams = StreamSelectors[value].Value;
        RefreshPreview();
    }

    partial void OnCapabilityIndexChanged(int value)
        => RunCapabilityCommand.NotifyCanExecuteChanged();

    partial void OnInputPathChanged(string value)
    {
        ProbeOptions.InputPath = value;
        OnPropertyChanged(nameof(HasInput));
        RefreshPreview();
    }

    #endregion

    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);

    #region 命令

    // 命令必须缓存为单一实例：若写成 `=> new RelayCommand(...)`，
    // 每次访问属性都会生成一个新命令对象，届时调用 NotifyCanExecuteChanged()
    // 作用在新实例上，XAML 绑定的那个实例不会刷新，按钮可用状态就不会更新。

    private IAsyncRelayCommand? _pickInputCommand;
    private IAsyncRelayCommand? _runCommand;
    private IAsyncRelayCommand? _runCapabilityCommand;
    private IRelayCommand? _copyCommandCommand;
    private IRelayCommand? _copyOutputCommand;
    private IRelayCommand? _resetCommand;

    /// <summary>选择待探测的文件。</summary>
    public IAsyncRelayCommand PickInputCommand =>
        _pickInputCommand ??= new AsyncRelayCommand(PickInputAsync);

    /// <summary>执行探测。</summary>
    public IAsyncRelayCommand RunCommand =>
        _runCommand ??= new AsyncRelayCommand(RunAsync, () => !IsBusy && (HasInput || ProbeOptions.IsCapabilityQuery));

    /// <summary>执行能力查询。</summary>
    public IAsyncRelayCommand RunCapabilityCommand =>
        _runCapabilityCommand ??= new AsyncRelayCommand(RunCapabilityAsync, () => !IsBusy && CapabilityIndex >= 0);

    /// <summary>复制生成的命令到剪贴板。</summary>
    public IRelayCommand CopyCommandCommand =>
        _copyCommandCommand ??= new RelayCommand(CopyCommand);

    /// <summary>复制输出结果到剪贴板。</summary>
    public IRelayCommand CopyOutputCommand =>
        _copyOutputCommand ??= new RelayCommand(CopyOutput,
            () => !string.IsNullOrWhiteSpace(RawOutput));

    /// <summary>恢复默认设置。</summary>
    public IRelayCommand ResetCommand =>
        _resetCommand ??= new RelayCommand(Reset);

    private async Task PickInputAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync();
        if (string.IsNullOrEmpty(path)) return;

        InputPath = path!;
    }

    private async Task RunAsync()
    {
        if (!HasInput && !ProbeOptions.IsCapabilityQuery)
        {
            ShowInfo(StringResources.GetOr("Probe_NeedInput", "请先选择要探测的文件，或从上方选择一项能力查询。"));
            return;
        }

        IsBusy = true;
        RunCommand.NotifyCanExecuteChanged();

        try
        {
            var result = await FfprobeService.RunAsync(ProbeOptions.Clone());

            RawOutput = result.EffectiveOutput;

            if (result.Canceled)
            {
                ShowInfo(StringResources.GetOr("Msg_Canceled", "已取消。"));
            }
            else if (result.Succeeded)
            {
                ShowInfo(StringResources.GetOr("Probe_Succeeded", "探测完成。"));
            }
            else
            {
                ShowErrorMessage(result.ErrorMessage ??
                          StringResources.GetOr("Probe_Failed", "探测失败，请查看输出中的错误信息。"));
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ProbeViewModel.RunAsync");
            ShowErrorMessage(ex.Message);
        }
        finally
        {
            IsBusy = false;
            RunCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task RunCapabilityAsync()
    {
        if (CapabilityIndex < 0 || CapabilityIndex >= CapabilityQueries.Count) return;

        var key = CapabilityQueries[CapabilityIndex].Value;

        // 能力查询使用独立选项：不带输入文件、不带结构化输出选项
        var options = new FfprobeOptions
        {
            ShowVersion = key == "version",
            ListCodecs = key == "codecs",
            ListFormats = key == "formats",
            ListFilters = key == "filters",
            ListPixelFormats = key == "pix_fmts",
            ListSampleFormats = key == "sample_fmts",
            ListChannelLayouts = key == "layouts",
            ListHardwareAccels = key == "hwaccels",
            ListProtocols = key == "protocols",
            ListMuxers = key == "muxers",
            ListDemuxers = key == "demuxers",
            ListDevices = key == "devices",
            ListBitstreamFilters = key == "bsfs",
            ListEncoders = key == "encoders",
            ListDecoders = key == "decoders",
            ListColors = key == "colors",
            ShowBuildConfiguration = key == "buildconf"
        };

        IsBusy = true;
        RunCapabilityCommand.NotifyCanExecuteChanged();

        try
        {
            var result = await FfprobeService.RunAsync(options);
            RawOutput = result.EffectiveOutput;

            if (result.Succeeded)
                ShowInfo(StringResources.GetOr("Probe_Succeeded", "查询完成。"));
            else
                ShowErrorMessage(result.ErrorMessage ?? StringResources.GetOr("Probe_Failed", "查询失败。"));
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ProbeViewModel.RunCapabilityAsync");
            ShowErrorMessage(ex.Message);
        }
        finally
        {
            IsBusy = false;
            RunCapabilityCommand.NotifyCanExecuteChanged();
        }
    }

    private void CopyCommand()
    {
        SetClipboardText(CommandPreview);
        ShowInfo(StringResources.GetOr("Msg_CommandCopied", "命令已复制到剪贴板。"));
    }

    private void CopyOutput()
    {
        SetClipboardText(RawOutput);
        ShowInfo(StringResources.GetOr("Msg_OutputCopied", "输出已复制到剪贴板。"));
    }

    /// <summary>写入剪贴板（官方 DataPackage 方式）。</summary>
    private static void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ProbeViewModel.SetClipboardText");
        }
    }

    /// <summary>恢复默认配置（不影响已选文件）。</summary>
    private void Reset()
    {
        var path = InputPath;

        ProbeOptions.ShowFormat = true;
        ProbeOptions.ShowStreams = true;
        ProbeOptions.ShowPackets = false;
        ProbeOptions.ShowFrames = false;
        ProbeOptions.ShowPrograms = false;
        ProbeOptions.ShowChapters = false;
        ProbeOptions.ShowData = false;
        ProbeOptions.ShowError = false;
        ProbeOptions.ShowLog = false;
        ProbeOptions.ShowPrivateData = false;
        ProbeOptions.Pretty = false;
        ProbeOptions.Unit = false;
        ProbeOptions.Prefix = false;
        ProbeOptions.ByteBinaryPrefix = false;
        ProbeOptions.Sexagesimal = false;
        ProbeOptions.Bitexact = false;
        ProbeOptions.CountFrames = false;
        ProbeOptions.CountPackets = false;
        ProbeOptions.ShowEntries = string.Empty;
        ProbeOptions.SelectStreams = string.Empty;
        ProbeOptions.ReadIntervals = string.Empty;
        ProbeOptions.InputFormat = string.Empty;
        ProbeOptions.CustomGlobalArguments = string.Empty;
        ProbeOptions.CustomInputArguments = string.Empty;
        ProbeOptions.OutputFormat = FfprobeOutputFormat.Json;
        ProbeOptions.ShowDataHash = FfprobeHashAlgorithm.None;

        OutputFormatIndex = 0;
        HashAlgorithmIndex = 0;
        EntryPresetIndex = 0;
        StreamSelectorIndex = 0;
        CapabilityIndex = -1;
        InputPath = path;

        OnPropertyChanged(string.Empty);
        RefreshPreview();
        ShowInfo(StringResources.GetOr("Msg_Reset", "已恢复默认设置。"));
    }

    #endregion

    /// <summary>刷新命令预览。</summary>
    private void RefreshPreview()
    {
        try
        {
            CommandPreview = FfprobeCommandBuilder.BuildDisplayText(ProbeOptions);
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

    // 方法名不能叫 ShowError：它与 ffprobe 的 -show_error 开关属性同名，会触发 CS0102。
    // 属性名对应命令行参数语义，故改用 ShowErrorMessage。
    private void ShowErrorMessage(string message)
    {
        StatusMessage = message;
        StatusSeverity = InfoBarSeverity.Error;
        ShowStatus = true;
    }
}
