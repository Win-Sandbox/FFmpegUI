using System;
using System.IO;
using System.Text.Json;

namespace FFmpegUI.Services;

/// <summary>设置的加载与保存（JSON 持久化到 %LOCALAPPDATA%\FFmpegUI\settings.json）。
/// 读写均为同步 IO，仅在启动与用户修改设置时调用，符合桌面应用常规做法。</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // 中文等非 ASCII 字符不做转义，便于用户直接查看/编辑配置文件
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>当前生效的设置实例（界面直接绑定/读写该对象）。</summary>
    public static AppSettings Current { get; private set; } = new();

    /// <summary>从磁盘加载设置；文件不存在或解析失败时使用默认值。</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(App.SettingsPath))
            {
                var json = File.ReadAllText(App.SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
                return;
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SettingsService.Load");
        }

        Current = new AppSettings();
    }

    /// <summary>保存设置到磁盘（失败时记录日志但不打断用户操作）。</summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(App.SettingsPath)!);
            var json = JsonSerializer.Serialize(Current, SerializerOptions);
            File.WriteAllText(App.SettingsPath, json);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SettingsService.Save");
        }
    }

    /// <summary>重置为默认设置。</summary>
    public static void Reset()
    {
        Current = new AppSettings();
        Save();
    }
}
