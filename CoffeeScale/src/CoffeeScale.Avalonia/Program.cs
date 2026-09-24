using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CoffeeScale.UI;
using CoffeeScale.ViewModels;

namespace CoffeeScale;

public static class Program
{
    // 崩溃日志与窗体同目录，便于用户回传定位
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "CoffeeScale.crash.log");
    private static int _shown; // 防止异常循环时反复弹窗

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint uType);

    [STAThread]
    public static int Main(string[] args)
    {
        // ViewModel 层把运行时异常转交到这里（框架无关，不耦合具体 UI）
        BrewViewModel.ErrorSink = ex => ReportFatal("运行时异常", ex);

        // 三大兜底：进程级未处理、UI 线程未处理、被吞掉的异步 Task 异常
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportFatal("未处理的异常 (AppDomain)", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ReportFatal("未观察的异步异常", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
        catch (Exception ex)
        {
            // 启动 / 主消息循环阶段的致命异常（如平台初始化失败）直接弹窗，避免黑窗一闪而过
            ReportFatal("启动 / 主循环异常", ex);
        }
        return 0;
    }

    /// <summary>统一致命错误出口：写日志 + 弹一次原生消息框。供 App.Dispatcher 处理器与 ViewModel.ErrorSink 复用。</summary>
    public static void ReportFatal(string context, Exception? ex)
    {
        var text = BuildReport(context, ex);
        try { File.AppendAllText(LogPath, "---- " + DateTime.Now + " ----\n" + text + "\n"); } catch { }
        if (System.Threading.Interlocked.Increment(ref _shown) <= 1)
        {
            // user32 弹窗仅 Windows 可用；Linux / macOS 只写日志（已写），再打到 stderr 便于终端排查
            if (OperatingSystem.IsWindows())
            {
                try { MessageBoxW(IntPtr.Zero, text, "CoffeeScale 遇到错误", 0x10 | 0x1000); } catch { }
            }
            else
            {
                try { Console.Error.WriteLine(text); } catch { }
            }
        }
    }

    private static string BuildReport(string context, Exception? ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CoffeeScale 发生错误：");
        sb.AppendLine("位置：" + context);
        if (ex != null)
        {
            sb.AppendLine("类型：" + ex.GetType().FullName);
            sb.AppendLine("消息：" + ex.Message);
            sb.AppendLine("堆栈：\n" + (ex.StackTrace ?? "<无>"));
            if (ex.InnerException != null)
                sb.AppendLine("内部异常：" + ex.InnerException.GetType().FullName + "：" + ex.InnerException.Message);
        }
        else sb.AppendLine("（无异常对象）");
        sb.AppendLine();
        sb.AppendLine("请把此内容（或同目录 CoffeeScale.crash.log）发回以便定位修复。");
        return sb.ToString();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        // UsePlatformDetect：按目标平台自动选后端 ——
        //   Windows → Win32（Skia 渲染）、Linux → X11、macOS → Avalonia.Native（Cocoa）
        // 这样一套桌面工程即可同时出 win-x64 / linux-x64 / osx-arm64 / osx-x64 四个产物。
        // （原先写死 UseWin32() 会导致 Linux/macOS 启动即崩：找不到 Win32 后端。）
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .UseIconFontFallbacks()   // 内嵌 emoji/符号字体回退，图标四端一致
            .LogToTrace();
}
