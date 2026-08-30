using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>高级参数页：直接编写完整 FFmpeg 命令行，支持模板保存。</summary>
public sealed partial class AdvancedPage : Page
{
    public AdvancedViewModel ViewModel { get; }

    public AdvancedPage()
    {
        ViewModel = new AdvancedViewModel();
        InitializeComponent();
    }
}
