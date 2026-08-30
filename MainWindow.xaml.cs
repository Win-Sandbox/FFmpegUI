using FFmpegUI.Models;
using FFmpegUI.Services;
using FFmpegUI.Views;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace FFmpegUI;

/// <summary>应用主窗口：承载自定义标题栏与导航框架。</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        // 官方 TitleBar 控件用法：必须通过 SetTitleBar 注册，窗格切换按钮才会生效
        SetTitleBar(AppTitleBar);

        App.MainWindow = this;
        App.MainWindowHandle = WindowNative.GetWindowHandle(this);

        InitializeWindow();
        ApplyTheme();
        ApplyBackdrop();

        // 实际主题要等元素进入可视化树后才确定，主题变化时需同步回退背景色
        RootGrid.ActualThemeChanged += (s, e) => UpdateBackdropFallback();
        UpdateBackdropFallback();

        // 启动时校验（必要时自动探测）FFmpeg，未就绪时显示提示条
        RefreshFfmpegStatus();

        // 关闭窗口时结束仍在运行的任务，避免残留 ffmpeg 进程
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Closing += OnWindowClosing;
    }

    private void InitializeWindow()
    {
        var hwnd = App.MainWindowHandle;
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Tall (48px) 标题栏，与 TitleBar 控件高度匹配
        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // 最小窗口尺寸（官方《Window customization》建议约束过小的窗口，保护布局）。
        // PreferredMinimumWidth/Height 为 WinAppSDK 1.7+ API，旧运行时静默跳过。
        try
        {
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 720;
                presenter.PreferredMinimumHeight = 480;
            }
        }
        catch
        {
            // 运行时不支持最小尺寸约束时忽略，不影响其它初始化
        }

        // 默认窗口尺寸（逻辑像素）：横向略长的长方形（约 1.44:1），比上一版更紧凑。
        // 按 DPI 换算物理像素，保证不同缩放比例下窗口逻辑尺寸一致
        const int defaultWidth = 1040;
        const int defaultHeight = 720;

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var scaleFactor = GetDpiForWindow(hwnd) / 96.0;
        var width = (int)Math.Round(defaultWidth * scaleFactor);
        var height = (int)Math.Round(defaultHeight * scaleFactor);

        // 小屏（如 1366x768 笔记本）工作区不足时等比收缩，避免窗口超出屏幕下沿
        var maxWidth = Math.Max(320, workArea.Width - 48);
        var maxHeight = Math.Max(320, workArea.Height - 48);
        if (width > maxWidth || height > maxHeight)
        {
            var ratio = Math.Min(maxWidth / (double)width, maxHeight / (double)height);
            width = (int)Math.Round(width * ratio);
            height = (int)Math.Round(height * ratio);
        }

        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        // 在工作区内居中（WorkArea 为屏幕坐标，需叠加其原点偏移）
        var centerX = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var centerY = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    #region 外观

    /// <summary>应用主题设置。按官方《Light and dark themes in WinUI 3》规范，
    /// 主题需设置在窗口根元素上以覆盖整个窗口（含标题栏与浮层）。</summary>
    public void ApplyTheme()
    {
        RootGrid.RequestedTheme = SettingsService.Current.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    /// <summary>应用窗口背景材质。Windows 10 不支持系统材质时会回退为纯色背景。
    /// 云母与亚克力分别用各自控制器的 IsSupported 检查（官方《Apply Mica/Acrylic》）。</summary>
    public void ApplyBackdrop()
    {
        var kind = SettingsService.Current.Backdrop;

        try
        {
            var supported = kind switch
            {
                BackdropKind.Acrylic => DesktopAcrylicController.IsSupported(),
                BackdropKind.None => false,
                _ => MicaController.IsSupported()
            };

            if (!supported)
            {
                SystemBackdrop = null;
                UpdateBackdropFallback();
                return;
            }

            // 使用系统材质时窗口内容必须透明，否则材质会被背景遮住
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);

            SystemBackdrop = kind switch
            {
                BackdropKind.Acrylic => new DesktopAcrylicBackdrop(),
                BackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                _ => new MicaBackdrop { Kind = MicaKind.Base }
            };

            // 材质明暗无需手动设置：Window.SystemBackdrop 的 SystemBackdropConfiguration
            // 由框架自动管理，会跟随 Window.Content（RootGrid）的 ActualTheme 变化，
            // 因此 ApplyTheme() 修改 RequestedTheme 后材质会自动同步为对应明暗。
        }
        catch (Exception ex)
        {
            // 材质创建失败时回退为纯色，不影响功能
            App.LogCrash(ex, "MainWindow.ApplyBackdrop");
            SystemBackdrop = null;
            UpdateBackdropFallback();
        }
    }

    /// <summary>无系统材质时给窗口根元素铺一层不透明背景。
    /// SystemBackdrop 为 null 时窗口客户区会直接透出系统底色（浅色为纯白、深色为纯黑），
    /// 与卡片、文字的层级配色不匹配，故按实际主题取与 Mica 底层接近的纯色。</summary>
    private void UpdateBackdropFallback()
    {
        if (SystemBackdrop is not null) return;

        RootGrid.Background = new SolidColorBrush(RootGrid.ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3)
            : Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
    }

    #endregion

    #region FFmpeg 就绪状态

    /// <summary>校验 FFmpeg 是否可用，缺失时显示提示条。
    /// 启动时调用一次，设置页保存后由页面再次调用。</summary>
    public void RefreshFfmpegStatus()
        => FfmpegStatusBar.IsOpen = !FfmpegLocator.Validate().Success;

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.SettingsItem;
        FfmpegStatusBar.IsOpen = false;
    }

    #endregion

    #region 导航

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavTranscode;
        NavigateTo("transcode");
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateTo("settings");
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    /// <summary>导航到指定页面。
    /// 使用官方 Frame.Navigate 而非直接给 Frame.Content 赋值：
    /// Navigate 走完整导航管线（页面生命周期回调 + 自带过渡动画）；
    /// 页面实例由 NavigationCacheMode=Enabled 缓存，切换时已填参数不丢失。</summary>
    private void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "transcode" => typeof(TranscodePage),
            "trim" => typeof(TrimPage),
            "extract" => typeof(ExtractPage),
            "merge" => typeof(MergePage),
            "compress" => typeof(CompressPage),
            "advanced" => typeof(AdvancedPage),
            "probe" => typeof(ProbePage),
            "image" => typeof(ImageConvertPage),
            "play" => typeof(PlayPage),
            "presets" => typeof(PresetsPage),
            "tasks" => typeof(TasksPage),
            "settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType is null) return;

        // 目标页已显示时不重复导航（点击当前项的场景）
        if (ContentFrame.Content?.GetType() == pageType) return;

        ContentFrame.Navigate(pageType);
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    #endregion

    #region 预设保存

    /// <summary>「保存预设」按钮：从当前页面取出参数快照，命名后存入预设库。
    /// 各功能页的 ViewModel 均实现 <see cref="IPresetSource"/>，
    /// 通过反射取出页面 ViewModel 属性（无需为每个页面单独接线）。</summary>
    private async void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is not Page page)
        {
            await ShowInfoDialogAsync("无法保存预设", "请先打开某个功能页面（如转码、图片转换）并设置好参数。");
            return;
        }

        var vm = page.GetType().GetProperty("ViewModel")?.GetValue(page) as IPresetSource;
        if (vm is null)
        {
            await ShowInfoDialogAsync("无法保存预设", "当前页面不支持保存预设。请在转码、剪辑、图片转换等功能页设置参数后再保存。");
            return;
        }

        var name = await PromptPresetNameAsync(vm.PageTitle);
        if (string.IsNullOrWhiteSpace(name)) return;

        var preset = new Preset
        {
            Name = name,
            Kind = vm.Kind,
            PageTag = vm.PageTag,
            PageTitle = vm.PageTitle,
            OptionsJson = PresetStore.SerializeOptions(vm.GetOptionsSnapshot()),
            Summary = vm.GetSummary(),
            CreatedAt = DateTime.Now,
        };
        PresetStore.Instance.Save(preset);

        await ShowInfoDialogAsync("已保存预设",
            $"预设「{name}」已保存，可在左侧「预设」栏点击直接处理文件。");
    }

    /// <summary>弹出一个带输入框的对话框，向用户索取预设名称。</summary>
    private async Task<string?> PromptPresetNameAsync(string pageTitle)
    {
        var textBox = new TextBox
        {
            PlaceholderText = "为这套参数起个名字",
            MinWidth = 320,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "保存预设",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"将当前「{pageTitle}」页面的参数保存为预设：",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    textBox,
                }
            },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var name = textBox.Text?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>弹出提示对话框。</summary>
    private async Task ShowInfoDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
        };
        await dialog.ShowAsync();
    }

    #endregion

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // 结束所有未完成任务（ffmpeg 进程会随取消信号被终止）
        TaskQueueService.Instance.CancelAll();
    }
}
