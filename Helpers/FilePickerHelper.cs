using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace FFmpegUI.Helpers;

/// <summary>文件选取器辅助类（统一入口）。
///
/// 实现基于 WinAppSDK 官方 <see cref="Microsoft.Windows.Storage.Pickers"/> 命名空间
/// （Windows 应用 SDK 1.8+ 引入，专为未打包 WinUI 3 应用设计）：
/// 旧版 <c>Windows.Storage.Pickers</c> 在未打包环境下运行时抛 COMException 0x80004005（E_FAIL），
/// 对话框无法弹出；新 API 改用 <c>WindowId</c> 构造（而非 HWND 互操作），从设计上规避该问题，
/// 且直接返回文件系统路径（<c>PickFileResult.Path</c>）。公共 API 保持不变，调用方无需任何修改。</summary>
public static class FilePickerHelper
{
    /// <summary>构造选取器所需的窗口标识（由主窗口句柄换算）。</summary>
    private static WindowId OwnerId => Win32Interop.GetWindowIdFromWindow(App.MainWindowHandle);

    /// <summary>选择单个输入文件。</summary>
    /// <param name="filters">扩展名过滤列表（不含点，如 "mp4"）。</param>
    public static async Task<string?> PickOpenFileAsync(params string[] filters)
    {
        var picker = new FileOpenPicker(OwnerId);
        ApplyOpenFilters(picker, filters);

        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    /// <summary>选择多个输入文件（合并/批量场景）。</summary>
    public static async Task<IReadOnlyList<string>> PickMultipleFilesAsync(params string[] filters)
    {
        var picker = new FileOpenPicker(OwnerId);
        ApplyOpenFilters(picker, filters);

        var results = await picker.PickMultipleFilesAsync();
        if (results is null) return Array.Empty<string>();

        var paths = new List<string>(results.Count);
        foreach (var item in results)
            if (!string.IsNullOrEmpty(item.Path)) paths.Add(item.Path);
        return paths;
    }

    /// <summary>选择输出文件（另存为）。</summary>
    /// <param name="suggestedName">建议的文件名。</param>
    /// <param name="types">文件类型列表（显示名 + 扩展名）。</param>
    public static async Task<string?> PickSaveFileAsync(string suggestedName, params (string Name, string Extension)[] types)
    {
        var picker = new FileSavePicker(OwnerId);

        if (types is { Length: > 0 })
        {
            foreach (var type in types)
                picker.FileTypeChoices.Add(type.Name, new List<string> { ToDot(type.Extension) });
        }
        else
        {
            picker.FileTypeChoices.Add("所有文件", new List<string> { "*" });
        }

        if (!string.IsNullOrWhiteSpace(suggestedName))
            picker.SuggestedFileName = suggestedName;

        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    /// <summary>选择文件夹。</summary>
    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(OwnerId);
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    /// <summary>把扩展名过滤列表应用到打开对话框（组合为一项 + 所有文件兜底）。</summary>
    private static void ApplyOpenFilters(FileOpenPicker picker, string[] filters)
    {
        if (filters is not { Length: > 0 })
        {
            picker.FileTypeFilter.Add("*");
            return;
        }

        // 合并为单一“媒体文件”项，保留用户选择的扩展名集合
        foreach (var filter in filters)
            picker.FileTypeFilter.Add(ToDot(filter));

        picker.FileTypeFilter.Add("*");
    }

    /// <summary>扩展名转点前缀："mp4" → ".mp4"；已带点则原样保留。</summary>
    private static string ToDot(string extension)
    {
        var ext = extension.Trim();
        return ext.StartsWith('.') ? ext : "." + ext;
    }
}
