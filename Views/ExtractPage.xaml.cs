using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>提取页：提取音频/视频/字幕，或抽帧为图片序列。</summary>
public sealed partial class ExtractPage : Page
{
    public ExtractViewModel ViewModel { get; }

    public ExtractPage()
    {
        ViewModel = new ExtractViewModel();
        InitializeComponent();
    }
}
