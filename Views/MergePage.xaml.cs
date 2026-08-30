using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>合并与混流页：视频合并、音视频混流、图片序列合成视频。</summary>
public sealed partial class MergePage : Page
{
    public MergeViewModel ViewModel { get; }

    public MergePage()
    {
        ViewModel = new MergeViewModel();
        InitializeComponent();
    }
}
