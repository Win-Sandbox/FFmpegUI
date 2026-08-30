using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>压缩页：按目标体积或质量压缩视频。</summary>
public sealed partial class CompressPage : Page
{
    public CompressViewModel ViewModel { get; }

    public CompressPage()
    {
        ViewModel = new CompressViewModel();
        InitializeComponent();
    }
}
