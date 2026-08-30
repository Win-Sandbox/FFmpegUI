using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.Services;

/// <summary>图片格式能力检测：查询当前 ffmpeg 实际支持哪些图片编码器。
///
/// 存在的原因：ffmpeg 的图片格式支持取决于编译配置，同一份代码在不同构建下能力不同。
/// 尤其 HEIC/HEIF 需要编译时链接 libheif，多数 Windows 预编译版本并未包含。
/// 与其在转换失败后报错，不如提前检测并只呈现可用的格式。
///
/// 结果会被缓存——ffprobe -encoders 是一次外部进程调用，不应频繁执行。</summary>
public static class ImageCapabilityService
{
    private static readonly object SyncRoot = new();

    private static HashSet<string>? _availableEncoders;

    /// <summary>是否已检测过。</summary>
    public static bool IsDetected
    {
        get { lock (SyncRoot) return _availableEncoders is not null; }
    }

    /// <summary>执行检测（若已检测则直接返回缓存结果）。</summary>
    public static async Task DetectAsync(CancellationToken cancellationToken = default)
    {
        if (IsDetected) return;

        var result = await FfprobeService.RunAsync(
            new Models.FfprobeOptions { ListEncoders = true },
            cancellationToken).ConfigureAwait(false);

        var encoders = result.Succeeded
            ? ParseEncoders(result.EffectiveOutput)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (SyncRoot)
        {
            // 只在首次写入：并发调用时后续结果忽略，避免状态反复变化
            _availableEncoders ??= encoders;
        }
    }

    /// <summary>指定编码器是否可用。
    /// 尚未检测时返回 true（乐观假设），以免在检测完成前把格式全部隐藏。</summary>
    public static bool IsEncoderAvailable(string encoderName)
    {
        if (string.IsNullOrWhiteSpace(encoderName)) return false;

        lock (SyncRoot)
        {
            if (_availableEncoders is null) return true;
            return _availableEncoders.Contains(encoderName);
        }
    }

    /// <summary>筛选出当前可用的图片格式。</summary>
    public static IReadOnlyList<ImageFormatInfo> GetAvailableFormats()
    {
        if (!IsDetected) return ImageFormatCatalog.Formats;

        return ImageFormatCatalog.Formats
            .Where(f => IsEncoderAvailable(f.EncoderName))
            .ToList();
    }

    /// <summary>清除缓存（更换 ffmpeg 路径后应调用）。</summary>
    public static void Reset()
    {
        lock (SyncRoot) _availableEncoders = null;
    }

    /// <summary>解析 ffprobe -encoders 的输出，提取编码器名称。
    /// 输出形如：
    /// <code>
    /// V....D png                  PNG (Portable Network Graphics) image
    /// V..... libwebp              libwebp WebP image
    /// </code>
    /// 其中第一个字段是 6 个能力标记，其后紧跟编码器名。</summary>
    private static HashSet<string> ParseEncoders(string output)
    {
        var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(output)) return encoders;

        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            // 能力标记形如 V....D / A..... / S..... ，后跟空格再跟编码器名
            if (trimmed.Length < 8) continue;

            var marker = trimmed[..6];
            if (marker[0] is not ('V' or 'A' or 'S' or 'D')) continue;
            if (marker[1] != '.' && marker[1] != 'F') continue;
            if (!char.IsWhiteSpace(trimmed[6])) continue;

            var name = trimmed[7..].Split(' ', '\t')[0];
            if (!string.IsNullOrWhiteSpace(name)) encoders.Add(name);
        }

        return encoders;
    }
}
