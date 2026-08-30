using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFmpegUI.Helpers;
using FFmpegUI.Models;
using FFmpegUI.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FFmpegUI.ViewModels;

/// <summary>「预设」页面的视图模型。
/// 列出用户保存的参数预设，支持删除，以及「点预设 → 询问文件位置 → 直接处理」。
///
/// 按用户要求，预设栏是独立一栏：点某个预设后，依据其类型（视频/图片）
/// 询问输入与输出位置，然后直接执行，无需先跳转到对应功能页。</summary>
public sealed partial class PresetsPageViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Preset> _presets = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private bool _showStatus;

    /// <summary>是否有预设（控制列表显隐）。</summary>
    [ObservableProperty] private bool _hasPresets;

    /// <summary>是否无预设（控制空提示显隐）。</summary>
    [ObservableProperty] private bool _emptyHintVisible;

    /// <summary>页面首次进入时刷新预设列表。</summary>
    public void RefreshPresets()
    {
        Presets.Clear();
        foreach (var preset in PresetStore.Instance.Presets)
            Presets.Add(preset);
        SyncVisibility();
    }

    private void SyncVisibility()
    {
        HasPresets = Presets.Count > 0;
        EmptyHintVisible = Presets.Count == 0;
    }

    private void ShowStatusMessage(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        ShowStatus = true;
    }

    [RelayCommand]
    private async Task RunPresetAsync(Preset? preset)
    {
        if (preset is null) return;

        if (preset.Kind == PresetKind.Image)
            await RunImagePresetAsync(preset);
        else
            await RunVideoPresetAsync(preset);
    }

    /// <summary>视频类预设：选输入文件 → 选输出文件 → 构造任务入队。</summary>
    private async Task RunVideoPresetAsync(Preset preset)
    {
        var input = await FilePickerHelper.PickOpenFileAsync();
        if (string.IsNullOrEmpty(input))
        {
            ShowStatusMessage(StringResources.GetOr("Preset_RunCancelled", "已取消：未选择输入文件。"), InfoBarSeverity.Warning);
            return;
        }

        var options = PresetStore.DeserializeOptions<FfmpegOptions>(preset.OptionsJson);
        if (options is null)
        {
            ShowStatusMessage(StringResources.GetOr("Preset_LoadFailed", "预设参数已损坏，无法加载。"), InfoBarSeverity.Error);
            return;
        }

        // Container 是枚举，需映射回扩展名作为输出文件后缀
        var ext = CodecCatalog.Containers
            .FirstOrDefault(c => c.Format == options.Container)?.Extension ?? "mp4";
        var baseName = Path.GetFileNameWithoutExtension(input);
        var output = await FilePickerHelper.PickSaveFileAsync(
            baseName + "_" + preset.Name,
            (ext.ToUpperInvariant(), ext));
        if (string.IsNullOrEmpty(output))
        {
            ShowStatusMessage(StringResources.GetOr("Preset_RunCancelled", "已取消：未选择输出位置。"), InfoBarSeverity.Warning);
            return;
        }

        var info = await FfprobeService.ProbeAsync(input);
        var task = new EncodingTask
        {
            Input = info,
            Options = options,
            OutputPath = output,
        };
        if (info is not null) task.TotalDuration = info.Duration;
        TaskQueueService.Instance.Enqueue(task);

        ShowStatusMessage(
            StringResources.FormatOr("Preset_EnqueuedFormat", "已按预设「{0}」加入处理队列。", preset.Name),
            InfoBarSeverity.Success);
    }

    /// <summary>图片类预设：选多个输入文件 → 选输出文件夹 → 逐文件转换。</summary>
    private async Task RunImagePresetAsync(Preset preset)
    {
        var inputs = await FilePickerHelper.PickMultipleFilesAsync();
        if (inputs.Count == 0)
        {
            ShowStatusMessage(StringResources.GetOr("Preset_RunCancelled", "已取消：未选择输入文件。"), InfoBarSeverity.Warning);
            return;
        }

        var outputDir = await FilePickerHelper.PickFolderAsync();
        if (string.IsNullOrEmpty(outputDir))
        {
            ShowStatusMessage(StringResources.GetOr("Preset_RunCancelled", "已取消：未选择输出文件夹。"), InfoBarSeverity.Warning);
            return;
        }

        var options = PresetStore.DeserializeOptions<ImageConvertOptions>(preset.OptionsJson);
        if (options is null)
        {
            ShowStatusMessage(StringResources.GetOr("Preset_LoadFailed", "预设参数已损坏，无法加载。"), InfoBarSeverity.Error);
            return;
        }

        var count = inputs.Count;
        int okCount = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            var src = inputs[i];
            var outPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(src) + options.TargetFormat.Extension);
            var result = await ImageConverter.ConvertAsync(src, outPath, options);
            if (result.Succeeded) okCount++;
        }

        ShowStatusMessage(
            StringResources.FormatOr("Preset_ImageStartedFormat", "已按预设「{0}」处理完成，成功 {1}/{2} 个文件。", preset.Name, okCount, count),
            okCount == count ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    [RelayCommand]
    private void DeletePreset(Preset? preset)
    {
        if (preset is null) return;
        PresetStore.Instance.Delete(preset.Id);
        Presets.Remove(preset);
        SyncVisibility();
        ShowStatusMessage(
            StringResources.FormatOr("Preset_DeletedFormat", "已删除预设「{0}」。", preset.Name),
            InfoBarSeverity.Informational);
    }
}
