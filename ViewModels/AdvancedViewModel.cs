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

namespace FFmpegUI.ViewModels;

/// <summary>高级（自定义参数）页视图模型：直接使用完整的 ffmpeg 参数模板，
/// 通过 {input} / {output} 占位符引用输入与输出文件。</summary>
public sealed partial class AdvancedViewModel : TaskPageViewModel
{
    /// <summary>内置模板 + 用户自定义模板。</summary>
    public ObservableCollection<CommandTemplate> Templates { get; } = new();

    [ObservableProperty] private int _templateIndex = -1;

    [ObservableProperty] private string _rawArguments = string.Empty;

    [ObservableProperty] private string _newTemplateName = string.Empty;

    /// <summary>当前选中模板的说明。</summary>
    public string TemplateDescription =>
        TemplateIndex >= 0 && TemplateIndex < Templates.Count && !string.IsNullOrWhiteSpace(Templates[TemplateIndex].Description)
            ? Templates[TemplateIndex].Description
            : StringResources.GetOr("Advanced_PlaceholderHint",
                "占位符 {input} 表示输入文件，{output} 表示输出文件。");

    protected override string OutputSuffix => StringResources.GetOr("Suffix_Output", "_输出");

    public AdvancedViewModel()
    {
        foreach (var template in TemplateService.BuiltInTemplates)
            Templates.Add(template);

        foreach (var template in TemplateService.LoadCustom())
            Templates.Add(template);

        TemplateIndex = 0;
    }

    partial void OnTemplateIndexChanged(int value)
    {
        OnPropertyChanged(nameof(TemplateDescription));

        if (value >= 0 && value < Templates.Count)
            RawArguments = Templates[value].Arguments;
    }

    #region 命令

    public IRelayCommand ApplyTemplateCommand => new RelayCommand(ApplyTemplate,
        () => TemplateIndex >= 0 && TemplateIndex < Templates.Count);

    public IRelayCommand SaveTemplateCommand => new RelayCommand(SaveTemplate,
        () => !string.IsNullOrWhiteSpace(NewTemplateName) && !string.IsNullOrWhiteSpace(RawArguments));

    public IRelayCommand DeleteTemplateCommand => new RelayCommand(DeleteTemplate, CanDeleteTemplate);

    private void ApplyTemplate()
    {
        if (TemplateIndex >= 0 && TemplateIndex < Templates.Count)
            RawArguments = Templates[TemplateIndex].Arguments;
    }

    private bool CanDeleteTemplate()
        => TemplateIndex >= 0 && TemplateIndex < Templates.Count && !Templates[TemplateIndex].IsBuiltIn;

    private void SaveTemplate()
    {
        var name = NewTemplateName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var existing = Templates.FirstOrDefault(t => t.Name == name && !t.IsBuiltIn);
        if (existing is not null)
        {
            existing.Arguments = RawArguments;
        }
        else
        {
            Templates.Add(new CommandTemplate
            {
                Name = name,
                Arguments = RawArguments,
                Description = StringResources.GetOr("Advanced_CustomTemplate", "用户自定义模板"),
                IsBuiltIn = false
            });
        }

        TemplateService.SaveCustom(Templates);
        NewTemplateName = string.Empty;
        DeleteTemplateCommand.NotifyCanExecuteChanged();

        ShowMessage(StringResources.Format("Msg_TemplateSavedFormat", name), InfoBarSeverity.Success);
    }

    private void DeleteTemplate()
    {
        if (!CanDeleteTemplate()) return;

        var template = Templates[TemplateIndex];
        Templates.Remove(template);
        TemplateIndex = Math.Min(TemplateIndex, Templates.Count - 1);

        TemplateService.SaveCustom(Templates);
        DeleteTemplateCommand.NotifyCanExecuteChanged();

        ShowMessage(StringResources.Format("Msg_TemplateDeletedFormat", template.Name), InfoBarSeverity.Success);
    }

    #endregion

    partial void OnNewTemplateNameChanged(string value) => SaveTemplateCommand.NotifyCanExecuteChanged();

    partial void OnRawArgumentsChanged(string value) => SaveTemplateCommand.NotifyCanExecuteChanged();

    protected override string[] GetInputExtensions() => new[]
    {
        "mp4", "mkv", "mov", "avi", "flv", "wmv", "webm", "ts", "m4v", "mpg",
        "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus",
        "png", "jpg", "jpeg", "bmp", "gif"
    };

    protected override void ApplyToOptions()
    {
        Options.UseRawArgumentsOnly = true;
        Options.RawArguments = RawArguments ?? string.Empty;
        Options.OverwriteOutput = SettingsService.Current.OverwriteOutput;
    }

    protected override string? ValidateBeforeQueue()
    {
        if (!HasFfmpeg)
            return StringResources.GetOr("Msg_NoFfmpeg", "尚未配置 FFmpeg，请打开设置页指定 ffmpeg.exe 与 ffprobe.exe。");

        if (string.IsNullOrWhiteSpace(RawArguments))
            return StringResources.GetOr("Msg_EmptyTemplate", "请填写参数模板。");

        if (!RawArguments.Contains("{input}") && string.IsNullOrWhiteSpace(InputPath))
            return StringResources.GetOr("Msg_NoInputPlaceholder", "参数模板中没有 {input} 占位符，请先选择输入文件。");

        if (!RawArguments.Contains("{output}") && string.IsNullOrWhiteSpace(OutputPath))
            return StringResources.GetOr("Msg_NoOutputPlaceholder", "参数模板中没有 {output} 占位符，请先指定输出文件。");

        if (!RawArguments.Contains("{input}") && !File.Exists(InputPath))
            return StringResources.GetOr("Msg_InputFileMissing", "输入文件不存在，请重新选择。");

        return null;
    }
}
