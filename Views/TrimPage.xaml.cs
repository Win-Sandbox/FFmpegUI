using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>剪辑页：截取片段、裁剪画面、旋转、变速、音量。</summary>
public sealed partial class TrimPage : Page
{
    public TrimViewModel ViewModel { get; }

    public TrimPage()
    {
        ViewModel = new TrimViewModel();
        InitializeComponent();
    }
}
