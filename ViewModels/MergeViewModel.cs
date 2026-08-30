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
using System.Threading.Tasks;

namespace FFmpegUI.ViewModels;

/// <summary>合并与混流页视图模型：
/// 0=视频合并（concat），1=音视频混流，2=图片序列合成视频。</summary>
public sealed partial class MergeViewModel : TaskPageViewModel
{
    public IReadOnlyList<string> MergeModes => new[]
    {
        StringResources.GetOr("Merge_Mode_Video", "视频合并"),
        StringResources.GetOr("Merge_Mode_Mux", "音视频混流"),
        StringResources.GetOr("Merge_Mode_Image", "图片序列合成视频")
    };

    public IReadOnlyList<string> MergeMethods => new[]
    {
        StringResources.GetOr("Merge_Method_Copy", "无损合并（直接复制流，要求编码参数一致）"),
        StringResources.GetOr("Merge_Method_Reencode", "重新编码（兼容性最好，速度较慢）")
    };

    /// <summary>待合并的文件清单（按顺序）。</summary>
    public ObservableCollection<string> Files { get; } = new();

    [ObservableProperty] private int _modeIndex;

    [ObservableProperty] private int _mergeMethodIndex;

    [ObservableProperty] private int _selectedFileIndex = -1;

    [ObservableProperty] private string _videoFilePath = string.Empty;

    [ObservableProperty] private string _audioFilePath = string.Empty;

    [ObservableProperty] private string _imagePattern = string.Empty;

    [ObservableProperty] private double _frameRate = 25;

    [ObservableProperty] private double _crf = 20;

    [ObservableProperty] private double _audioBitrateKbps = 192;

    #region 派生属性

    public bool IsVideoMergeMode => ModeIndex == 0;
    public bool IsMuxMode => ModeIndex == 1;
    public bool IsImageSequenceMode => ModeIndex == 2;
    public bool HasFiles => Files.Count > 0;
    public bool HasSelectedFile => SelectedFileIndex >= 0 && SelectedFileIndex < Files.Count;

    public override string OutputExtension => "mp4";

    protected override string OutputSuffix => StringResources.GetOr("Suffix_Merged", "_合并");

    partial void OnModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsVideoMergeMode));
        OnPropertyChanged(nameof(IsMuxMode));
        OnPropertyChanged(nameof(IsImageSequenceMode));
        RefreshDefaultOutput();
    }

    partial void OnSelectedFileIndexChanged(int value) => OnPropertyChanged(nameof(HasSelectedFile));

    #endregion

    #region 命令

    public IAsyncRelayCommand AddFilesCommand => new AsyncRelayCommand(AddFilesAsync);
    public IRelayCommand RemoveFileCommand => new RelayCommand(RemoveSelectedFile);
    public IRelayCommand MoveUpCommand => new RelayCommand(MoveUp, () => HasSelectedFile);
    public IRelayCommand MoveDownCommand => new RelayCommand(MoveDown, () => HasSelectedFile);
    public IRelayCommand ClearFilesCommand => new RelayCommand(ClearFiles);
    public IAsyncRelayCommand PickVideoCommand => new AsyncRelayCommand(PickVideoAsync);
    public IAsyncRelayCommand PickAudioCommand => new AsyncRelayCommand(PickAudioAsync);

    #endregion

    #region 文件清单操作

    private async Task AddFilesAsync()
    {
        var paths = await FilePickerHelper.PickMultipleFilesAsync(
            "mp4", "mkv", "mov", "avi", "flv", "wmv", "webm", "ts", "m4v", "mpg");

        foreach (var path in paths)
            Files.Add(path);

        OnPropertyChanged(nameof(HasFiles));
        RefreshDefaultOutput();
        UpdateFileCommands();
    }

    private void RemoveSelectedFile()
    {
        if (SelectedFileIndex is < 0 || SelectedFileIndex >= Files.Count) return;

        Files.RemoveAt(SelectedFileIndex);
        SelectedFileIndex = Math.Min(SelectedFileIndex, Files.Count - 1);
        OnPropertyChanged(nameof(HasFiles));
        RefreshDefaultOutput();
        UpdateFileCommands();
    }

    private void MoveUp()
    {
        var index = SelectedFileIndex;
        if (index <= 0 || index >= Files.Count) return;

        (Files[index - 1], Files[index]) = (Files[index], Files[index - 1]);
        SelectedFileIndex = index - 1;
    }

    private void MoveDown()
    {
        var index = SelectedFileIndex;
        if (index < 0 || index >= Files.Count - 1) return;

        (Files[index + 1], Files[index]) = (Files[index], Files[index + 1]);
        SelectedFileIndex = index + 1;
    }

    private void ClearFiles()
    {
        Files.Clear();
        SelectedFileIndex = -1;
        OnPropertyChanged(nameof(HasFiles));
        UpdateFileCommands();
    }

    private void UpdateFileCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    #endregion

    private async Task PickVideoAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync("mp4", "mkv", "mov", "avi", "webm", "ts", "m4v");
        if (!string.IsNullOrEmpty(path)) VideoFilePath = path!;
    }

    private async Task PickAudioAsync()
    {
        var path = await FilePickerHelper.PickOpenFileAsync("mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "wma", "ac3");
        if (!string.IsNullOrEmpty(path)) AudioFilePath = path!;
    }

    protected override string BuildDefaultOutputPath()
    {
        var first = IsVideoMergeMode ? Files.FirstOrDefault()
            : IsMuxMode ? VideoFilePath
            : null;

        if (string.IsNullOrEmpty(first)) return base.BuildDefaultOutputPath();

        var configured = Services.SettingsService.Current.OutputDirectory;
        var directory = !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
            ? configured
            : Path.GetDirectoryName(first) ?? string.Empty;

        var name = Path.GetFileNameWithoutExtension(first);
        return Path.Combine(directory, $"{name}{OutputSuffix}.{OutputExtension}");
    }

    protected override void ApplyToOptions()
    {
        // 合并页的参数在「加入队列」时按模式直接构造，此处不生成预览
    }

    public override string CommandPreview
    {
        get
        {
            try
            {
                return ModeIndex switch
                {
                    0 => BuildVideoMergePreview(),
                    1 => BuildMuxPreview(),
                    _ => BuildImageSequencePreview()
                };
            }
            catch (Exception ex)
            {
                return StringResources.Format("Msg_BuildCommandFailedFormat", ex.Message);
            }
        }
    }

    private string BuildVideoMergePreview()
    {
        if (Files.Count == 0)
            return StringResources.GetOr("Msg_AddFilesFirst", "请先添加要合并的视频文件。");

        if (MergeMethodIndex == 0)
            return $"ffmpeg -hide_banner -nostdin -y -f concat -safe 0 -i <列表文件> -c copy -map 0:v? -map 0:a? \"{OutputPath}\"";

        var inputs = string.Join(' ', Files.Select(f => $"-i \"{f}\""));
        return $"ffmpeg -hide_banner -nostdin -y {inputs} -filter_complex \"…concat=n={Files.Count}:v=1:a=1[v][a]\" -map \"[v]\" -map \"[a]\" -c:v libx264 -crf {Crf} -preset veryfast -c:a aac -b:a {AudioBitrateKbps}k \"{OutputPath}\"";
    }

    private string BuildMuxPreview()
    {
        if (string.IsNullOrEmpty(VideoFilePath) || string.IsNullOrEmpty(AudioFilePath))
            return StringResources.GetOr("Msg_SelectVideoAndAudio", "请分别选择视频文件与音频文件。");

        return $"ffmpeg -hide_banner -nostdin -y -i \"{VideoFilePath}\" -i \"{AudioFilePath}\" -map 0:v? -map 1:a? -c:v copy -c:a copy \"{OutputPath}\"";
    }

    private string BuildImageSequencePreview()
    {
        if (string.IsNullOrWhiteSpace(ImagePattern))
            return StringResources.GetOr("Msg_EmptyImagePatternPlain",
                "请填写图片序列模式，例如 C:\\frames\\img%05d.png");

        return $"ffmpeg -hide_banner -nostdin -y -framerate {FrameRate.ToString("0.###", CultureInfo.InvariantCulture)} -i \"{ImagePattern}\" -c:v libx264 -crf {Crf} -pix_fmt yuv420p \"{OutputPath}\"";
    }

    /// <summary>合并页不使用「单输入文件」的选择方式。</summary>
    protected override Task PickInputAsync() => Task.CompletedTask;

    protected override async Task AddToQueueAsync()
    {
        if (!HasFfmpeg)
        {
            ShowMessage(StringResources.GetOr("Msg_NoFfmpeg",
                    "尚未配置 FFmpeg，请打开设置页指定 ffmpeg.exe 与 ffprobe.exe。"),
                InfoBarSeverity.Warning);
            return;
        }

        switch (ModeIndex)
        {
            case 0:
                await MergeVideosAsync();
                break;

            case 1:
                MuxStreams();
                break;

            default:
                BuildImageSequence();
                break;
        }
    }

    #region 视频合并

    private async Task MergeVideosAsync()
    {
        if (Files.Count < 2)
        {
            ShowMessage(StringResources.GetOr("Msg_NeedTwoFiles", "至少需要添加两个视频文件才能合并。"),
                InfoBarSeverity.Warning);
            return;
        }

        var output = EnsureOutputPath();
        if (output is null) return;

        var options = FfmpegOptions.CreateDefault();
        options.OutputPath = output;
        options.OverwriteOutput = Services.SettingsService.Current.OverwriteOutput;

        if (MergeMethodIndex == 0)
        {
            // concat demuxer：官方推荐的无损合并方式，要求各片段编码参数一致
            var listFile = WriteConcatList(Files);
            if (listFile is null)
            {
                ShowMessage(StringResources.GetOr("Msg_ConcatListFailed", "无法创建临时合并列表文件。"),
                    InfoBarSeverity.Error);
                return;
            }

            options.InputPath = listFile;
            options.CustomInputArguments = "-f concat -safe 0";
            options.VideoCodec = "copy";
            options.AudioCodec = "copy";
            options.SubtitleCodec = "copy";
            options.VideoRateControl = VideoRateControl.Copy;
            options.AudioRateControl = AudioRateControl.Copy;
            options.KeepVideo = true;
            options.KeepAudio = true;
            options.KeepSubtitle = true;
            options.FastStart = true;

            await EnqueueCoreAsync(options, null, output, listFile);
        }
        else
        {
            // concat 滤镜：重新编码合并，兼容性最好
            var inputs = string.Join(' ', Files.Select(f => $"-i \"{f}\""));
            var chains = string.Concat(Files.Select((_, i) => $"[{i}:v][{i}:a]"));
            var filter = $"{chains}concat=n={Files.Count}:v=1:a=1[v][a]";

            options.UseRawArgumentsOnly = true;
            options.RawArguments =
                $"{inputs} -filter_complex \"{filter}\" -map \"[v]\" -map \"[a]\" " +
                $"-c:v libx264 -crf {Crf} -preset veryfast -pix_fmt yuv420p " +
                $"-c:a aac -b:a {AudioBitrateKbps}k -movflags +faststart \"{output}\"";

            await EnqueueCoreAsync(options, null, output, Files.First());
        }
    }

    /// <summary>写出 concat 列表文件（路径按官方要求转义单引号）。</summary>
    private static string? WriteConcatList(IEnumerable<string> files)
    {
        try
        {
            var directory = Path.Combine(App.AppDataPath, "Temp");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"concat_{Guid.NewGuid():N}.txt");
            var lines = files.Select(f => $"file '{f.Replace("'", "'\\''")}'");
            File.WriteAllLines(path, lines);
            return path;
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "MergeViewModel.WriteConcatList");
            return null;
        }
    }

    #endregion

    #region 音视频混流

    private void MuxStreams()
    {
        if (string.IsNullOrEmpty(VideoFilePath) || !File.Exists(VideoFilePath) ||
            string.IsNullOrEmpty(AudioFilePath) || !File.Exists(AudioFilePath))
        {
            ShowMessage(StringResources.GetOr("Msg_InvalidMuxFiles", "请选择有效的视频文件与音频文件。"),
                InfoBarSeverity.Warning);
            return;
        }

        var output = EnsureOutputPath();
        if (output is null) return;

        var options = FfmpegOptions.CreateDefault();
        options.InputPath = VideoFilePath;
        options.OutputPath = output;
        options.AdditionalInputs.Add(AudioFilePath);

        // 视频取自输入 0、音频取自输入 1
        options.ExtraMaps.Add("0:v:0?");
        options.ExtraMaps.Add("1:a:0?");
        options.KeepVideo = true;
        options.KeepAudio = true;
        options.KeepSubtitle = false;

        if (MergeMethodIndex == 0)
        {
            options.VideoCodec = "copy";
            options.AudioCodec = "copy";
            options.VideoRateControl = VideoRateControl.Copy;
            options.AudioRateControl = AudioRateControl.Copy;
        }
        else
        {
            options.VideoCodec = "libx264";
            options.VideoRateControl = VideoRateControl.Crf;
            options.Crf = (int)Crf;
            options.Preset = "veryfast";
            options.AudioCodec = "aac";
            options.AudioBitrateKbps = (int)AudioBitrateKbps;
        }

        options.FastStart = true;

        var task = new EncodingTask
        {
            Input = null,
            OutputPath = output,
            Options = options,
            TotalDuration = TimeSpan.Zero
        };

        task.Arguments = FfmpegCommandBuilder.Build(options, null, TimeSpan.Zero).ToDisplayString("ffmpeg");

        Services.TaskQueueService.Instance.Enqueue(task);
        ShowMessage(StringResources.Format("Msg_AddedToQueueFormat", Path.GetFileName(output)),
            InfoBarSeverity.Success);
        RaiseTaskAdded(task);
    }

    #endregion

    #region 图片序列合成视频

    private void BuildImageSequence()
    {
        if (string.IsNullOrWhiteSpace(ImagePattern))
        {
            ShowMessage(StringResources.GetOr("Msg_EmptyImagePattern",
                    "请填写图片序列模式（如 C:\\frames\\img%05d.png）。"),
                InfoBarSeverity.Warning);
            return;
        }

        var output = EnsureOutputPath();
        if (output is null) return;

        var options = FfmpegOptions.CreateDefault();
        options.InputPath = ImagePattern;
        options.OutputPath = output;
        options.CustomInputArguments = $"-framerate {FrameRate.ToString("0.###", CultureInfo.InvariantCulture)}";
        options.VideoCodec = "libx264";
        options.VideoRateControl = VideoRateControl.Crf;
        options.Crf = (int)Crf;
        options.Preset = "medium";
        options.PixelFormat = "yuv420p";
        options.KeepVideo = true;
        options.KeepAudio = false;
        options.KeepSubtitle = false;
        options.FastStart = true;

        var task = new EncodingTask
        {
            Input = null,
            OutputPath = output,
            Options = options,
            TotalDuration = TimeSpan.Zero
        };

        task.Arguments = FfmpegCommandBuilder.Build(options, null, TimeSpan.Zero).ToDisplayString("ffmpeg");

        Services.TaskQueueService.Instance.Enqueue(task);
        ShowMessage(StringResources.Format("Msg_AddedToQueueFormat", Path.GetFileName(output)),
            InfoBarSeverity.Success);
        RaiseTaskAdded(task);
    }

    #endregion

    #region 公共

    private string? EnsureOutputPath()
    {
        var output = OutputPath;
        if (string.IsNullOrWhiteSpace(output))
        {
            output = BuildDefaultOutputPath();
            OutputPath = output;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            ShowMessage(StringResources.GetOr("Msg_NoOutputPath", "请先指定输出文件路径。"),
                InfoBarSeverity.Warning);
            return null;
        }

        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            ShowMessage(StringResources.GetOr("Msg_OutputDirMissing", "输出目录不存在，请重新选择输出路径。"),
                InfoBarSeverity.Warning);
            return null;
        }

        return output;
    }

    private async Task EnqueueCoreAsync(FfmpegOptions options, MediaFileInfo? input, string output, string probePath)
    {
        options.OutputPath = output;
        options.OverwriteOutput = Services.SettingsService.Current.OverwriteOutput;

        MediaFileInfo? info = null;
        if (HasFfmpeg && File.Exists(probePath) && !probePath.Contains('%'))
            info = await Services.FfprobeService.ProbeAsync(probePath);

        var task = new EncodingTask
        {
            Input = info,
            OutputPath = output,
            Options = options,
            TotalDuration = info?.Duration ?? TimeSpan.Zero
        };

        task.Arguments = FfmpegCommandBuilder.Build(options, info, task.TotalDuration).ToDisplayString("ffmpeg");

        Services.TaskQueueService.Instance.Enqueue(task);
        ShowMessage(StringResources.Format("Msg_AddedToQueueFormat", Path.GetFileName(output)),
            InfoBarSeverity.Success);
        RaiseTaskAdded(task);
    }

    #endregion

    /// <summary>合并页的命令预览由本类自行生成，不依赖 <see cref="Options"/>，
    /// 因此关闭基类的自动刷新以避免不必要的重新生成。</summary>
    protected override bool AutoRefreshCommandPreview => false;
}
