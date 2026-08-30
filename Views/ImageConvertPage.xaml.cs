using FFmpegUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Views;

/// <summary>图片转换页。
///
/// 注意：ViewModel 必须在 InitializeComponent() 之前赋值——
/// x:Bind 生成的绑定代码在其中初始化，若此时 ViewModel 为 null，绑定取不到对象。</summary>
public sealed partial class ImageConvertPage : Page
{
    public ImageConvertViewModel ViewModel { get; }

    public ImageConvertPage()
    {
        ViewModel = new ImageConvertViewModel();
        InitializeComponent();

        // 能力检测是一次外部进程调用，放在 Loaded 而非构造函数，
        // 避免拖慢页面导航
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 只执行一次：NavigationCacheMode=Enabled 时页面会被复用，
        // 每次导航回来都重新检测没有必要
        if (Services.ImageCapabilityService.IsDetected && ViewModel.AvailableFormats.Count > 0)
            return;

        await ViewModel.InitializeAsync();
    }
}
