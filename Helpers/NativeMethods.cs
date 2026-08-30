using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FFmpegUI.Helpers;

/// <summary>嵌入外部进程窗口（SetParent）所需的 Win32 API。
/// WinUI 3 没有 WPF 的 HwndHost 这类内建宿主控件，需自行封装。</summary>
internal static class NativeMethods
{
    #region 窗口样式常量

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const uint WS_CHILD = 0x4000_0000;
    public const uint WS_POPUP = 0x8000_0000;
    public const uint WS_VISIBLE = 0x1000_0000;
    public const uint WS_CAPTION = 0x00C0_0000;   // WS_BORDER | WS_DLGFRAME
    public const uint WS_THICKFRAME = 0x0004_0000; // 可调整大小的边框
    public const uint WS_SYSMENU = 0x0008_0000;
    public const uint WS_MINIMIZEBOX = 0x0002_0000;
    public const uint WS_MAXIMIZEBOX = 0x0001_0000;

    /// <summary>窗口在任务栏显示（嵌入后必须清除，否则会多出一个任务栏按钮）。</summary>
    public const uint WS_EX_APPWINDOW = 0x0004_0000;

    /// <summary>工具窗口（不显示任务栏按钮）。</summary>
    public const uint WS_EX_TOOLWINDOW = 0x0000_0080;

    #endregion

    #region 消息与虚拟键码

    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CLOSE = 0x0010;

    /// <summary>p 键（ffplay 的暂停/继续）。</summary>
    public const ushort VK_P = 0x50;

    /// <summary>m 键（ffplay 的静音切换）。</summary>
    public const ushort VK_M = 0x4D;

    // ffplay 的快进快退：
    //   ← / →  10 秒
    //   ↑ / ↓  60 秒
    //   PageUp / PageDown  600 秒
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_PRIOR = 0x21; // PageUp
    public const ushort VK_NEXT = 0x22;  // PageDown

    #endregion

    #region 窗口操作

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    // SetWindowPos 标志：保留当前 Z 序与激活状态，仅改变位置/尺寸
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOCOPYBITS = 0x0100;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    public static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    public static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    public static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    public static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>按进程位数分派正确的 GetWindowLongPtr。
    /// 直接 P/Invoke GetWindowLongPtr 在 32 位进程下会缺失，故需判断 IntPtr.Size。</summary>
    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLongPtr32(hWnd, nIndex);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLongPtr32(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    #endregion

    #region 窗口枚举

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    #endregion

    #region DPI

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    #endregion

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    /// <summary>读取窗口标题。用于按标题定位（比只按进程 ID 枚举更可靠）。</summary>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        var builder = new StringBuilder(512);
        return GetWindowText(hWnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    /// <summary>构造 WM_KEYDOWN 的 lParam（重复次数 1，其余位为 0）。</summary>
    public static IntPtr KeyDownLParam() => (IntPtr)0x0000_0001;

    /// <summary>构造 WM_KEYUP 的 lParam（重复次数 1 + 前状态位 + 转换状态位）。</summary>
    public static IntPtr KeyUpLParam() => unchecked((IntPtr)0xC000_0001);
}
