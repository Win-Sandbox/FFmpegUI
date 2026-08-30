using FFmpegUI.Helpers;
using FFmpegUI.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.Services;

/// <summary>执行结果。</summary>
public sealed record FfmpegRunResult(int ExitCode, bool Canceled, string? ErrorMessage)
{
    public bool Succeeded => !Canceled && ExitCode == 0 && ErrorMessage is null;
}

/// <summary>FFmpeg 进程运行器：启动进程、解析进度、转发日志、支持取消。
/// 进度来源为官方推荐的 <c>-progress pipe:1 -nostats</c> 机器可读输出。</summary>
public static class FfmpegRunner
{
    /// <summary>日志保留的最大行数（避免长时间任务占用过多内存）。</summary>
    private const int MaxLogLines = 400;

    /// <summary>执行一个任务。调用方负责在此之前设置好 task.Options 与 task.OutputPath。</summary>
    public static async Task<FfmpegRunResult> RunAsync(EncodingTask task, CancellationToken cancellationToken)
    {
        var ffmpeg = SettingsService.Current.FfmpegPath;
        if (!FfmpegLocator.IsExecutable(ffmpeg))
            return new FfmpegRunResult(-1, false,
                StringResources.GetOr("Error_NoFfmpeg", "未配置 ffmpeg.exe，请先打开设置页指定路径。"));

        // 确保输出目录存在（ffmpeg 不会自动创建目录）
        var outputDirectory = Path.GetDirectoryName(task.OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var command = FfmpegCommandBuilder.Build(task.Options, task.Input, task.TotalDuration);

        // 进度输出必须在输出段之前插入（属于全局选项）
        command.Global.Add("-progress");
        command.Global.Add("pipe:1");
        command.Global.Add("-nostats");

        task.Arguments = string.Join(' ', command.ToArgumentList());

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in command.ToArgumentList())
            startInfo.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return new FfmpegRunResult(-1, false,
                StringResources.Format("Error_StartFfmpegFormat", ex.Message));
        }

        if (process is null)
            return new FfmpegRunResult(-1, false,
                StringResources.GetOr("Error_StartFfmpegFailed", "无法启动 ffmpeg（进程创建失败）。"));

        // 取消时终止进程树；ffmpeg 已通过 -nostdin 关闭标准输入读取，不会被提示阻塞
        await using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { /* 进程已退出 */ }
        });

        var progressReader = ReadProgressAsync(process, task);
        var logReader = ReadLogAsync(process, task);

        await Task.WhenAll(progressReader, logReader).ConfigureAwait(false);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FfmpegRunner.WaitForExit");
        }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        process.Dispose();

        if (cancellationToken.IsCancellationRequested)
            return new FfmpegRunResult(exitCode, true, null);

        return exitCode == 0
            ? new FfmpegRunResult(exitCode, false, null)
            : new FfmpegRunResult(exitCode, false,
                StringResources.Format("Error_ExitCodeFormat", exitCode));
    }

    /// <summary>-progress 输出的解析状态（每个读取循环独享，await 间保持）。</summary>
    private sealed class ProgressState
    {
        public TimeSpan ProcessedTime;
        public double Speed;
        public double BitrateKbps;
    }

    /// <summary>读取标准输出中的进度字段（key=value 形式）。</summary>
    private static async Task ReadProgressAsync(Process process, EncodingTask task)
    {
        var state = new ProgressState();
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                ApplyProgressLine(task, state, line);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FfmpegRunner.ReadProgress");
        }
    }

    /// <summary>读取标准错误中的日志（ffmpeg 的常规输出在 stderr）。</summary>
    private static async Task ReadLogAsync(Process process, EncodingTask task)
    {
        try
        {
            var lines = 0;
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                lock (task.Log)
                {
                    task.Log.AppendLine(line);
                    lines++;
                    if (lines > MaxLogLines)
                    {
                        // 超出上限时丢弃最早的日志，保留最近的输出
                        var content = task.Log.ToString();
                        var index = content.IndexOf('\n', content.Length / 2);
                        task.Log.Clear();
                        task.Log.Append(index > 0 ? content[(index + 1)..] : content);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FfmpegRunner.ReadLog");
        }
    }

    /// <summary>解析一行 -progress 输出。
    /// 只解析数值并调用 <see cref="EncodingTask.SetRunnerProgress"/> 暂存，
    /// 由任务对象经 DispatcherQueue 切回 UI 线程更新可通知属性
    /// （官方《Threading》：INPC 订阅者必须在 UI 线程被调用）。</summary>
    private static void ApplyProgressLine(EncodingTask task, ProgressState state, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var separator = line.IndexOf('=');
        if (separator <= 0) return;

        var key = line[..separator];
        var value = line[(separator + 1)..].Trim();

        switch (key)
        {
            case "out_time_us":
                if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var microseconds))
                    state.ProcessedTime = TimeSpan.FromTicks(microseconds * 10);
                break;

            case "out_time_ms":
                if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var milliseconds))
                    state.ProcessedTime = TimeSpan.FromMilliseconds(milliseconds);
                break;

            case "out_time":
                if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time))
                    state.ProcessedTime = time;
                break;

            case "speed":
                if (value.EndsWith('x')) value = value[..^1];
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var speed))
                    state.Speed = speed;
                break;

            case "bitrate":
                // 形如 "1234.5kbits/s" 或 "1234kbits/s"
                if (value.EndsWith("kbits/s", StringComparison.OrdinalIgnoreCase))
                    value = value[..^"kbits/s".Length];
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var bitrate))
                    state.BitrateKbps = bitrate;
                break;

            case "progress":
                // ffmpeg 在完成时会输出 "progress=end"，此时可把进度置为 100%
                if (string.Equals(value, "end", StringComparison.OrdinalIgnoreCase))
                {
                    var percent = 100d;
                    var processed = task.TotalDuration > TimeSpan.Zero ? task.TotalDuration : state.ProcessedTime;
                    task.SetRunnerProgress(percent, processed, state.Speed, state.BitrateKbps);
                }
                break;
        }

        // out_time 系列行最频繁且携带时长信息，在其后合并推送一次
        if (key.StartsWith("out_time", StringComparison.Ordinal))
        {
            // 进度百分比按已处理时长与总时长换算（总时长未知时由界面显示不确定态）
            var percent = task.TotalDuration > TimeSpan.Zero
                ? state.ProcessedTime.TotalSeconds / task.TotalDuration.TotalSeconds * 100d
                : 0d;

            task.SetRunnerProgress(percent, state.ProcessedTime, state.Speed, state.BitrateKbps);
        }
    }
}
