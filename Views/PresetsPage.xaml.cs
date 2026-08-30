using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>预设栏页面。
///
/// 注意：ViewModel 必须在 InitializeComponent() 之前赋值——
/// x:Bind 生成的绑定代码在其中初始化，若此时 ViewModel 为 null，绑定取不到对象。</summary>
public sealed partial class PresetsPage : Page
{
    public PresetsPageViewModel ViewModel { get; }

    public PresetsPage()
    {
        ViewModel = new PresetsPageViewModel();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 每次进入都刷新（预设可能从其他页面新增/删除）
        ViewModel.RefreshPresets();
    }
}
