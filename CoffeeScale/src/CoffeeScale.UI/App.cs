using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CoffeeScale.ViewModels;

namespace CoffeeScale.UI;

public class App : Application
{
    public override void Initialize()
    {
        // 使用 Fluent 主题；控件颜色在 MainWindow 中另行指定，四端一致。
        Styles.Add(new FluentTheme());
        // 关键（2026-09-13 UI 升级）：显式声明深色变体。
        // 应用本身是深色界面（Window.Background/Foreground 均为深咖啡），若不声明变体，
        // Fluent 默认按 Light 解析——导致 ① 未显式设色的子文本（如图标瓷砖内的字形）
        // 继承到 Light 的深色前景，在深色瓷砖上几乎不可见；② ComboBox/DatePicker 飞出、
        // ScrollBar、TextBox 水印等仍按浅色绘制，与整体调性割裂。声明 Dark 后四端统一。
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // UI 线程内未处理异常（按钮命令、绑定回调、InvokeAsync 内抛出的异常等）统一转交 ErrorSink
        // （不再直接依赖桌面版 Program，移动端各自设置 ErrorSink 即可；保持 App 平台无关）。
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            BrewViewModel.ErrorSink?.Invoke(e.Exception);
            e.Handled = true; // 已记录，避免进程直接消失
        };

#if !ANDROID && !IOS && !MACCATALYST
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else
#endif
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            // 移动端（Android/iOS/MacCatalyst）单视图模式：MainWindow 编译为 UserControl 铺满屏幕
            single.MainView = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
