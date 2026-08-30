using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FFmpegUI.Views;

/// <summary>播放页：把 ffplay 的画面嵌入页面顶部的宿主区域。
///
/// 内嵌画面是原生 HWND 子窗口，不参与 XAML 布局，因此本页负责：
/// <list type="number">
/// <item>把宿主元素的位置换算为相对窗口客户区的坐标，交给 ViewModel 摆放画面；</item>
/// <item>在页面尺寸变化时重新同步；</item>
/// <item>导航离开时停止播放，避免画面残留到其他页面上方。</item>
/// </list></summary>
public sealed partial class PlayPage : Page
{
    public PlayViewModel ViewModel { get; }

    public PlayPage()
    {
        // ViewModel 必须在 InitializeComponent() 之前赋值：
        // x:Bind 生成的绑定代码在其中初始化，若此时 ViewModel 为 null，绑定取不到对象
        ViewModel = new PlayViewModel();
        InitializeComponent();

        Loaded += OnLoaded;
        SizeChanged += OnPageSizeChanged;
        VideoHost.SizeChanged += OnVideoHostSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => SyncHostBounds();

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => SyncHostBounds();

    private void OnVideoHostSizeChanged(object sender, SizeChangedEventArgs e) => SyncHostBounds();

    /// <summary>把宿主区域的位置与尺寸传给 ViewModel。
    /// 坐标相对窗口内容根（即窗口客户区，因为标题栏是自绘的 XAML），
    /// DPI 换算由 ViewModel 按 GetDpiForWindow 完成。</summary>
    private void SyncHostBounds()
    {
        if (App.MainWindow?.Content is not FrameworkElement root) return;

        var position = VideoHost.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));

        ViewModel.UpdateHostBounds(
            position.X,
            position.Y,
            VideoHost.ActualWidth,
            VideoHost.ActualHeight);
    }

    /// <summary>离开播放页时停止播放：内嵌窗口不随页面隐藏，不停止会残留。</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.StopPlayback();
        base.OnNavigatedFrom(e);
    }
}
