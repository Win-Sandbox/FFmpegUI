using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFmpegUI.Helpers;
using FFmpegUI.Models;

namespace FFmpegUI.Services;

/// <summary>预设存储服务。
/// 将用户保存的参数预设持久化到 %LOCALAPPDATA%\FFmpegUI\presets.json（与 settings.json 同目录）。
/// 使用 Singleton 实例（与 SettingsService 一致的模式）。</summary>
public sealed class PresetStore
{
    private static readonly Lazy<PresetStore> _instance = new(() => new PresetStore());
    public static PresetStore Instance => _instance.Value;

    private readonly string _filePath;
    private readonly object _lock = new();
    private List<Preset> _presets = new();

    private PresetStore()
    {
        var dir = App.AppDataPath;
        try { Directory.CreateDirectory(dir); } catch { /* 忽略目录创建失败，后续读写会返回空 */ }
        _filePath = Path.Combine(dir, "presets.json");
        Load();
    }

    /// <summary>所有预设（只读副本，按创建时间倒序，最新在前）。</summary>
    public IReadOnlyList<Preset> Presets
    {
        get { lock (_lock) { var copy = new List<Preset>(_presets); copy.Reverse(); return copy; } }
    }

    /// <summary>保存一个新预设（若同名已存在则覆盖）。</summary>
    public void Save(Preset preset)
    {
        if (preset == null) return;
        lock (_lock)
        {
            for (int i = 0; i < _presets.Count; i++)
            {
                if (_presets[i].Name.Equals(preset.Name, StringComparison.Ordinal))
                {
                    _presets[i] = preset;
                    Persist();
                    return;
                }
            }
            _presets.Add(preset);
            Persist();
        }
    }

    /// <summary>按名称删除预设。</summary>
    public void Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        lock (_lock)
        {
            int idx = _presets.FindIndex(p => p.Name.Equals(name, StringComparison.Ordinal));
            if (idx >= 0)
            {
                _presets.RemoveAt(idx);
                Persist();
            }
        }
    }

    /// <summary>按唯一标识删除预设。</summary>
    public void Delete(Guid id)
    {
        lock (_lock)
        {
            int idx = _presets.FindIndex(p => p.Id == id);
            if (idx >= 0)
            {
                _presets.RemoveAt(idx);
                Persist();
            }
        }
    }

    /// <summary>判断是否存在同名预设。</summary>
    public bool Exists(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        lock (_lock) { return _presets.Exists(p => p.Name.Equals(name, StringComparison.Ordinal)); }
    }

    // ---- JSON 序列化选项：注册 TimeSpan? 转换器（FfmpegOptions 含 TimeSpan? 字段，
    //      System.Text.Json 默认不支持 TimeSpan 序列化） ----
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new TimeSpanNullableConverter() },
    };

    /// <summary>序列化选项对象为 JSON（视频类/图片类通用）。</summary>
    public static string SerializeOptions(object options)
        => JsonSerializer.Serialize(options, _jsonOptions);

    /// <summary>反序列化预设参数为指定类型；失败返回 default。</summary>
    public static T? DeserializeOptions<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, _jsonOptions); }
        catch { return default; }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) { _presets = new List<Preset>(); return; }
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) { _presets = new List<Preset>(); return; }
            var list = JsonSerializer.Deserialize<List<Preset>>(json, _jsonOptions);
            _presets = list ?? new List<Preset>();
        }
        catch
        {
            _presets = new List<Preset>();
        }
    }

    private void Persist()
    {
        try { File.WriteAllText(_filePath, JsonSerializer.Serialize(_presets, _jsonOptions)); }
        catch { /* 忽略写入失败 */ }
    }

    /// <summary>支持 TimeSpan? 的 JSON 转换器（序列化为秒数，反序列化为 TimeSpan?）。
    /// 用于 FfmpegOptions 的 StartTime/EndTime 等 TimeSpan? 字段。</summary>
    private sealed class TimeSpanNullableConverter : JsonConverter<TimeSpan?>
    {
        public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out double seconds))
                return TimeSpan.FromSeconds(seconds);
            if (reader.TokenType == JsonTokenType.String && TimeSpan.TryParse(reader.GetString(), out var ts))
                return ts;
            return null;
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteNumberValue(value.Value.TotalSeconds);
        }
    }
}
