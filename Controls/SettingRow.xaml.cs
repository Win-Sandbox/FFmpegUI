using FFmpegUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FFmpegUI.Controls;

/// <summary>参数设置行控件：把「标题 + 说明 + 控件」三件套统一为一致的布局，
/// 保证所有页面的参数设置行间距与对齐方式完全统一（Fluent 规范）。
/// 本地化：调用方在本控件上设置 x:Uid，MRT 会自动把 resw 中的
/// 「Uid.Title」「Uid.Description」赋给对应属性，未命中时保留 XAML 中的中文回退值。</summary>
public sealed partial class SettingRow : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow),
            new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public static readonly DependencyProperty SettingContentProperty =
        DependencyProperty.Register(nameof(SettingContent), typeof(object), typeof(SettingRow),
            new PropertyMetadata(null, OnSettingContentChanged));

    public SettingRow()
    {
        InitializeComponent();

        // x:Uid 的属性赋值发生在 InitializeComponent() 期间，
        // 那时 DescriptionTextBlock 尚为 null，无法即时更新可见性；
        // 延迟到 Loaded（Uid 已就位）再判断一次
        Loaded += (_, _) => UpdateDescriptionVisibility();

        // 属性若在内部可视化树建立前就已赋值（如代码创建后立刻设置），
        // 变化回调因命名元素为 null 而未能写入，此处补同步一次
        SyncToVisualTree();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>右侧承载的参数控件（ComboBox、NumberBox、ToggleSwitch 等）。</summary>
    public object SettingContent
    {
        get => GetValue(SettingContentProperty);
        set => SetValue(SettingContentProperty, value);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (SettingRow)d;
        if (row.LabelTextBlock is not null)
            row.LabelTextBlock.Text = e.NewValue as string ?? string.Empty;
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (SettingRow)d;
        if (row.DescriptionTextBlock is not null)
            row.DescriptionTextBlock.Text = e.NewValue as string ?? string.Empty;

        row.UpdateDescriptionVisibility();
    }

    private static void OnSettingContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (SettingRow)d;
        if (row.ControlPresenter is not null)
            row.ControlPresenter.Content = e.NewValue;

        // ToggleSwitch 专属布局适配（官方默认样式的坑，见 WinUI generic.xaml）：
        // DefaultToggleSwitchStyle 强制 MinWidth=154（ToggleSwitchThemeMinWidth），
        // 且模板中旋钮（40px）位于第 0 列最左端，右侧约 114px 是 On/Off 文字预留区——
        // 即使 OnContent/OffContent 为空也占位。结果：控件本身已贴到行尾，
        // 但可视旋钮距行尾仍有约 114px 空白，表现为「开关偏左」。
        // 收敛 MinWidth 后控件收缩为旋钮实际宽度，旋钮真正贴到行尾
        // （右侧仅余模板内固定的 12px 列间距，与右内边距视觉一致）。
        if (e.NewValue is ToggleSwitch toggle)
            toggle.MinWidth = 0;
    }

    /// <summary>把三个依赖属性的当前值写入内部可视化树。
    /// 不用 x:Bind 而用属性变化回调的原因：调用方是通过外部 XAML 的
    /// 属性元素语法（&lt;SettingRow.SettingContent&gt;）或 x:Uid 赋值这些属性的，
    /// 赋值时机在本控件内部树建立之后，x:Bind OneWay 只在 Bindings.Initialize()
    /// 时读取一次，之后的值变化不会刷新，会导致标题与右侧控件始终为空。</summary>
    private void SyncToVisualTree()
    {
        if (LabelTextBlock is not null)
            LabelTextBlock.Text = Title ?? string.Empty;

        if (DescriptionTextBlock is not null)
            DescriptionTextBlock.Text = Description ?? string.Empty;

        if (ControlPresenter is not null)
            ControlPresenter.Content = SettingContent;

        UpdateDescriptionVisibility();
    }

    /// <summary>说明文本为空时隐藏说明行，避免产生多余空白。
    /// 直接操作命名元素而非通过计算属性 + Bindings.Update()，
    /// 以免在 XAML 解析期间（Bindings 尚未初始化）属性被赋值时抛异常。</summary>
    private void UpdateDescriptionVisibility()
    {
        if (DescriptionTextBlock is null) return;

        DescriptionTextBlock.Visibility = string.IsNullOrWhiteSpace(Description)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>供代码后台创建设置行时使用：按资源键取标题与说明。</summary>
    public void SetLocalized(string uid, string titleFallback, string descriptionFallback = "")
    {
        Title = StringResources.GetOr(uid + ".Title", titleFallback);
        Description = StringResources.GetOr(uid + ".Description", descriptionFallback);
    }

    #region 悬停反馈（CommonStates 需手动由指针事件触发）

    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        => VisualStateManager.GoToState(this, "PointerOver", true);

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        => VisualStateManager.GoToState(this, "Normal", true);

    #endregion
}
