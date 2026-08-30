using FFmpegUI.Helpers;
using FFmpegUI.Models;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegUI.Services;

/// <summary>可投递给 ffplay 窗口的按键。
/// 对应 ffplay 官方快捷键（见 ffplay 文档）：
/// p 暂停；←/→ 10 秒；↑/↓ 60 秒。</summary>
public enum FfplayKey
{
    /// <summary>p：暂停 / 继续。</summary>
    Pause,

    /// <summary>m：静音切换。</summary>
    Mute,

    /// <summary>←：后退 10 秒。</summary>
    SeekBackward10,

    /// <summary>→：前进 10 秒。</summary>
    SeekForward10,

    /// <summary>↓：后退 60 秒。</summary>
    SeekBackward60,

    /// <summary>↑：前进 60 秒。</summary>
    SeekForward60
}

/// <summary>ffplay 内嵌播放器宿主：把 ffplay 的窗口挂进应用窗口并控制播放。
///
/// 实现原理（WinUI 3 无内建 HwndHost，需自行封装）：
/// <list type="number">
/// <item>启动 ffplay 进程，并用 -window_title 设一个唯一标题；</item>
/// <item>轮询枚举窗口，按标题定位出 ffplay 的 SDL 窗口；</item>
/// <item>去掉标题栏与边框、清除任务栏按钮，再 SetParent 挂到主窗口，成为子窗口；</item>
/// <item>用 MoveWindow 把它摆到页面上预留的宿主区域（XAML 逻辑像素需按 DPI 换算）；</item>
/// <item>暂停与快进快退通过向该窗口投递 WM_KEYDOWN/WM_KEYUP 实现。</item>
/// </list>
///
/// 为什么不用重定向输出：ffplay 是 GUI 程序，stdout 混有 SDL 界面刷新信息，
/// 重定向后若无人持续读取，管道缓冲区写满会导致播放卡死。
///
/// 关键限制：ffplay 没有 IPC 与运行时变速能力，
/// 因此「速度」只能在启动时指定，改动后必须重新播放。</summary>
public sealed class FfplayHost
{
    private Process? _process;
    private IntPtr _hwnd;
    private IntPtr _parentHwnd;
    private readonly object _stderrLock = new();
    private string _lastStdErr = string.Empty;

    /// <summary>ffplay 启动时的 stderr 摘要，用于诊断嵌入失败或渲染错误。</summary>
    public string LastStdErr
    {
        get { lock (_stderrLock) { return _lastStdErr; } }
        private set { lock (_stderrLock) { _lastStdErr = value; } }
    }

    /// <summary>是否已成功嵌入（失败时为 false，此时画面仍是独立窗口）。</summary>
    public bool IsEmbedded { get; private set; }

    /// <summary>是否正在播放。</summary>
    public bool IsPlaying => _process is { HasExited: false };

    /// <summary>播放结束（播放完毕自动退出，或被外部关闭）时触发。</summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>启动播放并把窗口嵌入到指定区域。</summary>
    /// <param name="options">播放参数（会被写入唯一窗口标题，故传入副本）。</param>
    /// <param name="parentHwnd">宿主窗口句柄（应用主窗口）。</param>
    /// <param name="x">宿主区域左上角 X（XAML 逻辑像素，相对窗口客户区）。</param>
    /// <param name="y">宿主区域左上角 Y（XAML 逻辑像素）。</param>
    /// <param name="width">宿主区域宽度（XAML 逻辑像素）。</param>
    /// <param name="height">宿主区域高度（XAML 逻辑像素）。</param>
    /// <param name="scaleFactor">DPI 缩放系数（XAML 逻辑像素 → 物理像素）。</param>
    public async Task<bool> StartAsync(
        FfplayOptions options,
        IntPtr parentHwnd,
        double x, double y, double width, double height,
        double scaleFactor)
    {
        var ffplay = SettingsService.Current.FfplayPath;
        if (!FfmpegLocator.IsExecutable(ffplay)) return false;

        Stop();

        // 唯一标题，供定位窗口（比仅按进程 ID 枚举更可靠）
        var title = "FFmpegUI_Player_" + Guid.NewGuid().ToString("N");
        options.WindowTitle = title;

        var arguments = FfplayCommandBuilder.Build(options);

        // 尝试多策略启动：不同 SDL 环境变量组合能提升兼容性。
        var strategies = new[]
        {
            new Dictionary<string, string?>
            {
                ["SDL_VIDEO_WINDOW_PARENT"] = parentHwnd.ToInt64().ToString(),
                ["SDL_VIDEO_WINDOW_POS"] = "0,0",
                ["SDL_VIDEO_CENTERED"] = "0",
                ["SDL_RENDER_DRIVER"] = "software",
                ["SDL_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR"] = "0"
            },
            new Dictionary<string, string?>
            {
                ["SDL_VIDEO_WINDOW_PARENT"] = parentHwnd.ToInt64().ToString(),
                ["SDL_VIDEO_WINDOW_POS"] = "0,0",
                ["SDL_VIDEO_CENTERED"] = "0",
                ["SDL_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR"] = "0",
                ["SDL_RENDER_DRIVER"] = null
            },
            new Dictionary<string, string?>
            {
                ["SDL_RENDER_DRIVER"] = "software",
                ["SDL_VIDEO_X11_NET_WM_BYPASS_COMPOSITOR"] = "0"
            },
            new Dictionary<string, string?>()
        };

        Process? lastProcess = null;
        foreach (var env in strategies)
        {
            if (lastProcess is not null)
            {
                try { if (!lastProcess.HasExited) lastProcess.Kill(true); } catch { }
                lastProcess.Dispose();
                lastProcess = null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffplay,
                UseShellExecute = false,
                CreateNoWindow = true,
                // 绝不能重定向输出：ffplay 的 stdout 是 SDL 界面信息，
                // 无人持续读取时管道缓冲区写满会导致播放卡死
                RedirectStandardOutput = false,
                // 重定向 stderr 用于捕获运行时错误诊断，不重定向 stdout（SDL 输出）
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            // 清理并设置环境变量
            foreach (var kv in env)
            {
                if (kv.Value is null)
                {
                    if (startInfo.Environment.ContainsKey(kv.Key)) startInfo.Environment.Remove(kv.Key);
                }
                else
                {
                    startInfo.Environment[kv.Key] = kv.Value;
                }
            }

            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("进程创建失败。");
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "FfplayHost.StartAsync");
                continue;
            }

            lastProcess = process;
            _process = process;
            _parentHwnd = parentHwnd;

            // 异步收集 stderr 摘要
            var stderrLog = new System.Text.StringBuilder();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var readTask = process.StandardError.ReadLineAsync();
                        var line = await readTask.ConfigureAwait(true);
                        if (line is null) break;
                        lock (stderrLog)
                        {
                            stderrLog.AppendLine(line);
                            if (stderrLog.Length > 50_000) stderrLog.Remove(0, stderrLog.Length - 50_000);
                            var summary = stderrLog.Length <= 2000 ? stderrLog.ToString() : stderrLog.ToString()[^2000..];
                            LastStdErr = summary;
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.LogCrash(ex, "FfplayHost.ReadStdErr");
                }
            });

            var hwnd = await FindWindowAsync(title, process.Id).ConfigureAwait(true);
            if (hwnd == IntPtr.Zero)
            {
                await Task.Delay(500).ConfigureAwait(true);
                hwnd = await FindWindowAsync(title, process.Id).ConfigureAwait(true);
            }

            if (hwnd == IntPtr.Zero)
            {
                NativeMethods.EnumWindows((hWnd, _) =>
                {
                    try
                    {
                        if (!NativeMethods.IsWindowVisible(hWnd)) return true;
                        NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                        if (pid != process.Id) return true;

                        hwnd = hWnd;
                        return false;
                    }
                    catch { return true; }
                }, IntPtr.Zero);
            }

            if (hwnd != IntPtr.Zero)
            {
                _hwnd = hwnd;
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
                IsEmbedded = Attach(hwnd, parentHwnd);
                UpdateLayout(x, y, width, height, scaleFactor);
                try
                {
                    var px = (int)Math.Round(x * scaleFactor);
                    var py = (int)Math.Round(y * scaleFactor);
                    var pw = Math.Max(1, (int)Math.Round(width * scaleFactor));
                    var ph = Math.Max(1, (int)Math.Round(height * scaleFactor));
                    NativeMethods.MoveWindow(hwnd, px, py, pw, ph, true);
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, px, py, pw, ph,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                }
                catch { }

                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            }

            if (IsEmbedded)
            {
                _ = WatchExitAsync(process);
                return true;
            }
        }

        if (lastProcess is not null)
        {
            _process = lastProcess;
            _ = WatchExitAsync(lastProcess);
            IsEmbedded = false;
            return true;
        }

        return false;
    }

    /// <summary>宿主区域位置或尺寸变化时重新摆放内嵌画面。</summary>
    public void UpdateLayout(double x, double y, double width, double height, double scaleFactor)
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hwnd)) return;

        // SetParent 之后，坐标是相对父窗口客户区的物理像素
        var px = (int)Math.Round(x * scaleFactor);
        var py = (int)Math.Round(y * scaleFactor);
        var pw = Math.Max(1, (int)Math.Round(width * scaleFactor));
        var ph = Math.Max(1, (int)Math.Round(height * scaleFactor));

        NativeMethods.MoveWindow(_hwnd, px, py, pw, ph, false);
        // 用 SetWindowPos 强制按新尺寸重绘（SWP_SHOWWINDOW 兼带显示），
        // 修正“黑屏 / 暂停才显示且画面偏移”的问题：SDL 渲染表面必须随
        // 窗口尺寸变更被重新提交，否则画面停留在旧区域或被遮挡。
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, px, py, pw, ph,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_NOCOPYBITS | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>向 ffplay 窗口投递按键，实现暂停与快进快退。</summary>
    public void SendKey(FfplayKey key)
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.IsWindow(_hwnd)) return;

        var vk = key switch
        {
            FfplayKey.Pause => NativeMethods.VK_P,
            FfplayKey.Mute => NativeMethods.VK_M,
            FfplayKey.SeekBackward10 => NativeMethods.VK_LEFT,
            FfplayKey.SeekForward10 => NativeMethods.VK_RIGHT,
            FfplayKey.SeekBackward60 => NativeMethods.VK_DOWN,
            FfplayKey.SeekForward60 => NativeMethods.VK_UP,
            _ => (ushort)0
        };

        if (vk == 0) return;

        // SDL 通过窗口过程接收键盘消息，PostMessage 即可触发，无需抢焦点
        NativeMethods.PostMessage(_hwnd, NativeMethods.WM_KEYDOWN, (IntPtr)vk, NativeMethods.KeyDownLParam());
        NativeMethods.PostMessage(_hwnd, NativeMethods.WM_KEYUP, (IntPtr)vk, NativeMethods.KeyUpLParam());
    }

    /// <summary>停止播放并清理内嵌窗口。</summary>
    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            // 先脱离宿主，避免销毁子窗口时影响主窗口
            try { NativeMethods.SetParent(_hwnd, IntPtr.Zero); } catch { /* 窗口可能已销毁 */ }
            _hwnd = IntPtr.Zero;
        }

        IsEmbedded = false;

        if (_process is null) return;

        try
        {
            // 杀进程树：ffplay 会派生 SDL 子窗口线程，仅终止主进程可能留下残影
            if (!_process.HasExited) _process.Kill(true);
        }
        catch
        {
            // 进程可能已退出
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    /// <summary>把 ffplay 窗口改为无边框子窗口并挂到应用窗口。
    /// 正常情况下 SDL_VIDEO_WINDOW_PARENT 已让其成为子窗口，此处仅做样式清理与冗余兜底。</summary>
    private static bool Attach(IntPtr hWnd, IntPtr parentHwnd)
    {
        try
        {
            // 去掉标题栏、可调整边框、系统菜单与最小化/最大化按钮，确保为可见子窗口
            var style = (uint)(long)NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE);
            style &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME
                       | NativeMethods.WS_SYSMENU | NativeMethods.WS_MINIMIZEBOX
                       | NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_POPUP);
            style |= NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE;
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, (IntPtr)style);

            // 清除任务栏按钮，否则会多出一个独立的 ffplay 任务项
            var exStyle = (uint)(long)NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
            exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
            exStyle &= ~NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);

            // SetParent 之后通知系统样式已改变并强制重绘以避免黑屏或样式不同步的问题。
            var parentResult = NativeMethods.SetParent(hWnd, parentHwnd) != IntPtr.Zero;
            try
            {
                // 通知系统样式变化并触发重绘（不改变位置/尺寸/顺序）
                NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
            }
            catch { /* 若 SetWindowPos 失败不致命 */ }

            return parentResult;
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FfplayHost.Attach");
            return false;
        }
    }

    /// <summary>等待并定位 ffplay 窗口。
    /// 优先按标题精确匹配（唯一标题），否则退化为按进程 ID 取首个可见窗口。</summary>
    private static async Task<IntPtr> FindWindowAsync(string title, int processId)
    {
        IntPtr fallback = IntPtr.Zero;

        // SDL 创建窗口需要时间，轮询等待（最多约 5 秒）
        for (var i = 0; i < 50; i++)
        {
            var byTitle = IntPtr.Zero;
            var byPid = IntPtr.Zero;

            NativeMethods.EnumWindows((hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                if (pid != processId) return true;

                if (byPid == IntPtr.Zero) byPid = hWnd;

                // 标题精确匹配则立即停止枚举
                if (NativeMethods.GetWindowTitle(hWnd) == title)
                {
                    byTitle = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (byTitle != IntPtr.Zero) return byTitle;
            if (byPid != IntPtr.Zero) fallback = byPid;

            await Task.Delay(100).ConfigureAwait(true);
        }

        return fallback;
    }

    /// <summary>监控进程退出，用于把界面状态复位为「未播放」。</summary>
    private async Task WatchExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FfplayHost.WatchExitAsync");
        }

        // 只有仍是当前进程时才复位（避免旧的监控任务误清状态）
        if (!ReferenceEquals(_process, process)) return;

        _hwnd = IntPtr.Zero;
        IsEmbedded = false;
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }
}
