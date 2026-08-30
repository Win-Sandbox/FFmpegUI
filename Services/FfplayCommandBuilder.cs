using FFmpegUI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FFmpegUI.Services;

/// <summary>ffplay 命令构建器：把 <see cref="FfplayOptions"/> 转换为参数列表。
///
/// 官方语法：<c>ffplay [选项] -i 输入</c>——ffplay 没有输出文件，所有选项都在输入之前。
///
/// 与 ffmpeg 的同名选项语义陷阱（务必注意）：
/// <list type="bullet">
/// <item><c>-y</c> 在 ffplay 是「强制窗口高度」，而 ffmpeg 里是「覆盖输出文件」；</item>
/// <item><c>-f</c> 在 ffplay 是「强制输入格式」。</item>
/// </list>
///
/// 画面调整（旋转、翻转、亮度/对比度/饱和度、缩放、变速）没有独立选项，
/// 统一拼成 -vf / -af 滤镜链，这是 ffplay 播放时实时处理的唯一方式。</summary>
public static class FfplayCommandBuilder
{
    /// <summary>构建 ffplay 参数列表（不含可执行文件名）。</summary>
    public static List<string> Build(FfplayOptions options)
    {
        var arguments = new List<string>();

        if (options.HideBanner) arguments.Add("-hide_banner");

        // 嵌入播放时用唯一标题，便于 FfplayHost 精确定位窗口
        if (!string.IsNullOrWhiteSpace(options.WindowTitle))
        {
            arguments.Add("-window_title");
            arguments.Add(options.WindowTitle);
        }

        BuildWindow(options, arguments);
        BuildPlayback(options, arguments);
        BuildStreams(options, arguments);

        if (options.FrameDrop) arguments.Add("-framedrop");

        BuildFilters(options, arguments);

        // 输入必须排在最后
        if (!string.IsNullOrWhiteSpace(options.InputPath))
        {
            arguments.Add("-i");
            arguments.Add(options.InputPath);
        }

        return arguments;
    }

    /// <summary>画面相关选项。
    /// 不生成 -fs / -noborder / -x / -y：画面嵌入应用窗口后，
    /// 尺寸与边框由宿主区域决定，这些选项反而会干扰嵌入。</summary>
    private static void BuildWindow(FfplayOptions options, List<string> arguments)
    {
        if (options.AutoRotate) arguments.Add("-autorotate");

        if (options.ShowMode != FfplayShowMode.Video)
        {
            arguments.Add("-showmode");
            arguments.Add(options.ShowMode == FfplayShowMode.Waves ? "1" : "2");
        }
    }

    /// <summary>播放控制选项。</summary>
    private static void BuildPlayback(FfplayOptions options, List<string> arguments)
    {
        if (options.SeekTo.HasValue && options.SeekTo.Value > TimeSpan.Zero)
        {
            arguments.Add("-ss");
            arguments.Add(FfmpegOptions.FormatTime(options.SeekTo.Value));
        }

        if (options.LoopCount != 0)
        {
            arguments.Add("-loop");
            arguments.Add(options.LoopCount.ToString(CultureInfo.InvariantCulture));
        }

        // ffplay 没有 -mute 选项，静音通过 -volume 0 实现
        var volume = options.Muted ? 0 : options.Volume;
        if (volume != 100)
        {
            arguments.Add("-volume");
            arguments.Add(volume.ToString(CultureInfo.InvariantCulture));
        }

        if (options.AutoExit) arguments.Add("-autoexit");
    }

    /// <summary>流选择（禁用音频/视频/字幕）。</summary>
    private static void BuildStreams(FfplayOptions options, List<string> arguments)
    {
        if (options.DisableAudio) arguments.Add("-an");
        if (options.DisableVideo) arguments.Add("-vn");
        if (options.DisableSubtitle) arguments.Add("-sn");
    }

    /// <summary>画面调整与变速：拼成 -vf / -af 滤镜链。
    /// 顺序固定为 旋转 → 翻转 → 缩放 → 色彩(eq) → 变速(setpts)，
    /// 该顺序保证几何变换先于色彩处理，避免重复采样。</summary>
    private static void BuildFilters(FfplayOptions options, List<string> arguments)
    {
        var video = new List<string>();

        // 旋转：transpose=1 顺时针 90°，transpose=2 逆时针 90°（即 270°），
        // 180° 由水平+垂直翻转组合得到
        switch (options.Rotate)
        {
            case 90:
                video.Add("transpose=1");
                break;
            case 180:
                video.Add("hflip");
                video.Add("vflip");
                break;
            case 270:
                video.Add("transpose=2");
                break;
        }

        if (options.FlipHorizontal) video.Add("hflip");
        if (options.FlipVertical) video.Add("vflip");

        if (options.ScaleWidth > 0 && options.ScaleHeight > 0)
        {
            video.Add($"scale={options.ScaleWidth}:{options.ScaleHeight}");
        }

        // eq 仅在任一值偏离默认时生成，避免无意义地触发像素处理
        if (Math.Abs(options.Brightness) > 0.001
            || Math.Abs(options.Contrast - 1.0) > 0.001
            || Math.Abs(options.Saturation - 1.0) > 0.001)
        {
            video.Add("eq=" +
                      $"brightness={options.Brightness.ToString("0.###", CultureInfo.InvariantCulture)}" +
                      $":contrast={options.Contrast.ToString("0.###", CultureInfo.InvariantCulture)}" +
                      $":saturation={options.Saturation.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        // 变速：视频用 setpts 缩放时间戳
        var speedChanged = Math.Abs(options.Speed - 1.0) > 0.001 && options.Speed > 0;
        if (speedChanged)
        {
            video.Add($"setpts={(1.0 / options.Speed).ToString("0.####", CultureInfo.InvariantCulture)}*PTS");
        }

        if (video.Count > 0)
        {
            arguments.Add("-vf");
            arguments.Add(string.Join(',', video));
        }

        // 变速：音频用 atempo 保持音调不变
        // atempo 单次支持 0.5–2.0，超出需串联；本项目把速度限制在该区间内
        if (speedChanged && !options.DisableAudio)
        {
            arguments.Add("-af");
            arguments.Add($"atempo={options.Speed.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
    }
}
