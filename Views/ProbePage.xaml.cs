using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>媒体信息探测页：以可视化方式使用 ffprobe 的全部主要与高级选项。</summary>
public sealed partial class ProbePage : Page
{
    public ProbeViewModel ViewModel { get; }

    public ProbePage()
    {
        ViewModel = new ProbeViewModel();
        InitializeComponent();
    }
}
