using FFmpegUI.Services;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FFmpegUI;

/// <summary>应用程序入口。负责路径初始化、设置加载、全局异常记录与主题应用。</summary>
public partial class App : Application
{
    /// <summary>应用真实可执行文件所在目录。
    /// 单文件发布时 BaseDirectory 指向临时解压目录，
    /// 按 .NET 官方文档须改用 Environment.ProcessPath 定位真实 EXE。</summary>
    public static string AppBaseDirectory { get; private set; } = string.Empty;

    /// <summary>应用数据目录（%LOCALAPPDATA%\FFmpegUI）。</summary>
    public static string AppDataPath { get; private set; } = string.Empty;

    /// <summary>设置文件路径。</summary>
    public static string SettingsPath { get; private set; } = string.Empty;

    /// <summary>任务历史持久化路径。</summary>
    public static string TasksPath { get; private set; } = string.Empty;

    /// <summary>日志目录。</summary>
    public static string LogDirectory { get; private set; } = string.Empty;

    /// <summary>主窗口实例（供文件选取器等需要窗口句柄的 API 使用）。</summary>
    public static MainWindow? MainWindow { get; internal set; }

    /// <summary>主窗口句柄。
    /// 未打包应用使用 FileOpenPicker/FileSavePicker/FolderPicker 前，
    /// 必须按官方要求用窗口句柄初始化（Learn《FileOpenPicker》）。</summary>
    public static IntPtr MainWindowHandle { get; internal set; }

    public App()
    {
        // 官方推荐的全局未处理异常处理：XAML 事件循环未处理的托管异常会先到达
        // UnhandledException，记录完整堆栈到日志文件，便于定位启动崩溃根因。
        UnhandledException += (s, e) => LogCrash(e.Exception, "App.UnhandledException");
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        TaskScheduler.UnobservedTaskException += (s, e) => LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");

        InitializeComponent();
        InitializePaths();
    }

    private void InitializePaths()
    {
        var exePath = Environment.ProcessPath;
        AppBaseDirectory = !string.IsNullOrEmpty(exePath)
            ? Path.GetDirectoryName(exePath)!
            : AppContext.BaseDirectory;

        // 未打包桌面应用按官方规范将用户数据写入 %LOCALAPPDATA%（Learn《应用数据》）：
        // 卸载/重装不丢失用户设置，且不需要提升权限。
        AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFmpegUI");

        Directory.CreateDirectory(AppDataPath);

        SettingsPath = Path.Combine(AppDataPath, "settings.json");
        TasksPath = Path.Combine(AppDataPath, "tasks.json");
        LogDirectory = Path.Combine(AppDataPath, "Logs");
    }

    /// <summary>把未处理异常写入日志文件（不抛出，避免二次崩溃）。</summary>
    internal static void LogCrash(Exception? ex, string source)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, "crash.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{ex}\r\n{new string('-', 80)}\r\n";
            File.AppendAllText(path, entry);
        }
        catch
        {
            // 日志写入失败不影响应用行为
        }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // 锁定资源语言为简体中文（当前版本唯一支持的语言）。
        // 必须在创建任何窗口之前设置。原因：MRT Core 按系统首选语言列表做
        // BCP-47 匹配，若系统首选为 zh-Hans-CN（含脚本子标签），与资源候选
        // zh-CN 不匹配，ResourceLoader.GetString 会抛 COMException 0x80073B17；
        // PrimaryLanguageOverride 直接锁定上下文语言，从根上规避。
        // 注意：未打包应用必须用 Microsoft.Windows.Globalization 命名空间的实现。
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh-CN";

        // 设置必须在创建窗口（加载任何资源）之前加载，确保主题与 FFmpeg 路径就绪
        SettingsService.Load();
        TaskQueueService.Instance.Configure();

        var window = new MainWindow();
        window.Activate();
    }
}
