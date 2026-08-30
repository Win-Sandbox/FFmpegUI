using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>转码页：容器、视频编码、音频编码、字幕与高级参数。</summary>
public sealed partial class TranscodePage : Page
{
    public TranscodeViewModel ViewModel { get; }

    public TranscodePage()
    {
        ViewModel = new TranscodeViewModel();
        InitializeComponent();
    }
}
