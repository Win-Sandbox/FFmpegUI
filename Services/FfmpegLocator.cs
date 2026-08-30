using FFmpegUI.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFmpegUI.Services;

/// <summary>FFmpeg 可执行文件定位服务。
/// 定位顺序（符合用户预期，先用户配置后自动发现）：
/// 1) 设置中用户手动指定的路径；2) PATH 环境变量；3) 常见安装目录；4) 应用目录。
/// 找到后会同时尝试匹配同目录下的 ffprobe.exe。</summary>
public static class FfmpegLocator
{
    /// <summary>定位结果。</summary>
    public sealed record LocateResult(bool Success, string FfmpegPath, string FfprobePath, string Message)
    {
        public static LocateResult Fail(string message) => new(false, string.Empty, string.Empty, message);

        public static LocateResult FailLocalized(string resourceKey, string fallback)
            => new(false, string.Empty, string.Empty, StringResources.GetOr(resourceKey, fallback));
    }

    /// <summary>常见安装目录（按优先级排列）。</summary>
    private static readonly string[] CommonDirectories =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
        @"C:\ffmpeg\bin",
        @"C:\ffmpeg-master-latest-win64-gpl\bin",
        @"C:\Program Files\GyanFFmpeg\bin",
        @"C:\ProgramData\chocolatey\bin"
    };

    /// <summary>检查当前路径配置是否可用（ffmpeg 与 ffprobe 同时存在）。
    /// ffplay 为可选组件，不参与就绪判断——部分发行版不含 ffplay.exe。</summary>
    public static bool IsConfigured()
        => IsExecutable(SettingsService.Current.FfmpegPath) && IsExecutable(SettingsService.Current.FfprobePath);

    /// <summary>ffplay.exe 是否已配置。</summary>
    public static bool IsFfplayConfigured() => IsExecutable(SettingsService.Current.FfplayPath);

    /// <summary>判断路径是否为存在且可用的可执行文件。</summary>
    public static bool IsExecutable(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path)
           && (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>在指定目录中查找指定文件名（大小写不敏感）。</summary>
    public static string? FindInDirectory(string? directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        try
        {
            return Directory.EnumerateFiles(directory, fileName, SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>自动探测 ffmpeg.exe：PATH → 常见目录 → 应用目录。</summary>
    public static string? DetectFfmpeg()
    {
        // 1. PATH 环境变量（官方 where 命令等价实现）
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = FindInDirectory(dir.Trim('"'), "ffmpeg.exe");
            if (found != null) return found;
        }

        // 2. 常见安装目录
        foreach (var dir in CommonDirectories)
        {
            var found = FindInDirectory(dir, "ffmpeg.exe");
            if (found != null) return found;
        }

        // 3. WinGet 安装目录下递归查找（层级较深，限制搜索深度）
        var winGetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        var winGetFound = FindRecursive(winGetRoot, "ffmpeg.exe", 6);
        if (winGetFound != null) return winGetFound;

        // 4. 应用自身目录
        return FindInDirectory(App.AppBaseDirectory, "ffmpeg.exe");
    }

    /// <summary>在 ffmpeg.exe 同目录查找 ffprobe.exe。</summary>
    public static string? DetectFfprobeNear(string ffmpegPath)
    {
        var dir = Path.GetDirectoryName(ffmpegPath);
        return FindInDirectory(dir, "ffprobe.exe") ?? DetectFfmpegBySearch("ffprobe.exe");
    }

    /// <summary>独立探测 ffprobe.exe（与 ffmpeg 同目录优先）。</summary>
    public static string? DetectFfprobe()
    {
        var ffmpeg = SettingsService.Current.FfmpegPath;
        if (IsExecutable(ffmpeg))
        {
            var near = DetectFfprobeNear(ffmpeg);
            if (near != null) return near;
        }
        return DetectFfmpegBySearch("ffprobe.exe");
    }

    /// <summary>在 ffmpeg.exe 同目录查找 ffplay.exe。</summary>
    public static string? DetectFfplayNear(string ffmpegPath)
    {
        var dir = Path.GetDirectoryName(ffmpegPath);
        return FindInDirectory(dir, "ffplay.exe") ?? DetectFfmpegBySearch("ffplay.exe");
    }

    /// <summary>独立探测 ffplay.exe（与 ffmpeg 同目录优先）。</summary>
    public static string? DetectFfplay()
    {
        var ffmpeg = SettingsService.Current.FfmpegPath;
        if (IsExecutable(ffmpeg))
        {
            var near = DetectFfplayNear(ffmpeg);
            if (near != null) return near;
        }
        return DetectFfmpegBySearch("ffplay.exe");
    }

    private static string? DetectFfmpegBySearch(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = FindInDirectory(dir.Trim('"'), fileName);
            if (found != null) return found;
        }

        foreach (var dir in CommonDirectories)
        {
            var found = FindInDirectory(dir, fileName);
            if (found != null) return found;
        }

        var winGetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        return FindRecursive(winGetRoot, fileName, 6);
    }

    private static string? FindRecursive(string root, string fileName, int maxDepth)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var found = FindInDirectory(dir, fileName);
                if (found != null) return found;

                if (maxDepth > 1)
                {
                    var deeper = FindRecursive(dir, fileName, maxDepth - 1);
                    if (deeper != null) return deeper;
                }
            }
        }
        catch
        {
            // 访问被拒绝等异常忽略，继续尝试其它目录
        }

        return null;
    }

    /// <summary>执行完整探测流程并写回设置。返回定位结果供界面提示。</summary>
    public static LocateResult DetectAndApply()
    {
        var ffmpeg = DetectFfmpeg();
        if (string.IsNullOrEmpty(ffmpeg))
            return LocateResult.FailLocalized("Ffmpeg_NotFound",
                "未在 PATH 或常见安装目录中找到 ffmpeg.exe，请在设置页手动指定。");

        var ffprobe = DetectFfprobeNear(ffmpeg) ?? DetectFfprobe();
        var ffplay = DetectFfplayNear(ffmpeg) ?? DetectFfplay();

        SettingsService.Current.FfmpegPath = ffmpeg;
        if (!string.IsNullOrEmpty(ffprobe))
            SettingsService.Current.FfprobePath = ffprobe;
        if (!string.IsNullOrEmpty(ffplay))
            SettingsService.Current.FfplayPath = ffplay;
        SettingsService.Save();

        return new LocateResult(true, ffmpeg, ffprobe ?? string.Empty,
            string.IsNullOrEmpty(ffprobe)
                ? StringResources.GetOr("Ffmpeg_FoundWithoutProbe",
                    "已找到 ffmpeg.exe，但未找到 ffprobe.exe（媒体信息探测功能不可用）。")
                : StringResources.GetOr("Ffmpeg_FoundBoth", "已找到 ffmpeg.exe 与 ffprobe.exe。"));
    }

    /// <summary>校验设置中的路径，缺失时尝试自动补齐。</summary>
    public static LocateResult Validate()
    {
        if (IsConfigured())
            return new LocateResult(true, SettingsService.Current.FfmpegPath, SettingsService.Current.FfprobePath,
                StringResources.GetOr("Ffmpeg_Ready", "FFmpeg 已就绪。"));

        if (!IsExecutable(SettingsService.Current.FfmpegPath))
        {
            var ffmpeg = DetectFfmpeg();
            if (!string.IsNullOrEmpty(ffmpeg)) SettingsService.Current.FfmpegPath = ffmpeg;
        }

        if (!IsExecutable(SettingsService.Current.FfprobePath))
        {
            var ffprobe = DetectFfprobe();
            if (!string.IsNullOrEmpty(ffprobe)) SettingsService.Current.FfprobePath = ffprobe;
        }

        // ffplay 为可选组件，缺失不视为「未配置」，静默补齐即可
        if (!IsExecutable(SettingsService.Current.FfplayPath))
        {
            var ffplay = DetectFfplay();
            if (!string.IsNullOrEmpty(ffplay)) SettingsService.Current.FfplayPath = ffplay;
        }

        SettingsService.Save();

        return IsConfigured()
            ? new LocateResult(true, SettingsService.Current.FfmpegPath, SettingsService.Current.FfprobePath,
                StringResources.GetOr("Ffmpeg_Ready", "FFmpeg 已就绪。"))
            : LocateResult.FailLocalized("Ffmpeg_NotConfigured",
                "未配置 FFmpeg，请打开设置页指定 ffmpeg.exe 与 ffprobe.exe。");
    }

    /// <summary>常用目录列表，供设置页提示用户。</summary>
    public static IReadOnlyList<string> GetCommonDirectories() => CommonDirectories;
}
