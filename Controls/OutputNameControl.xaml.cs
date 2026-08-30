using FFmpegUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FFmpegUI.Controls;

/// <summary>输出文件名自定义控件：嵌入各处理页面“输出”卡片内，
/// 绑定到 <see cref="OutputNameOptions"/>，支持原文件名 / 前缀 / 后缀 / 自定义四种模式。</summary>
public sealed partial class OutputNameControl : UserControl
{
    /// <summary>要编辑的输出命名选项（引用，页面与入队逻辑共享同一实例）。</summary>
    public static readonly DependencyProperty OutputNameOptionsProperty =
        DependencyProperty.Register(
            nameof(OutputNameOptions),
            typeof(OutputNameOptions),
            typeof(OutputNameControl),
            new PropertyMetadata(null, OnOptionsChanged));

    public OutputNameOptions? OutputNameOptions
    {
        get => (OutputNameOptions?)GetValue(OutputNameOptionsProperty);
        set => SetValue(OutputNameOptionsProperty, value);
    }

    /// <summary>x:Bind 用的内部别名（DependencyProperty 名为 OutputNameOptions，x:Bind 绑定 Options 更简洁）。</summary>
    private OutputNameOptions? Options => OutputNameOptions;

    public OutputNameControl()
    {
        InitializeComponent();
    }

    private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (OutputNameControl)d;
        control.SyncFromOptions();
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputNameOptions is null) return;
        OutputNameOptions.Mode = (OutputNameMode)ModeCombo.SelectedIndex;
        UpdateVisibility();
    }

    private void SyncFromOptions()
    {
        if (OutputNameOptions is null) return;
        ModeCombo.SelectedIndex = (int)OutputNameOptions.Mode;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var index = ModeCombo.SelectedIndex;
        PrefixBox.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SuffixBox.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        CustomBox.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
    }
}
