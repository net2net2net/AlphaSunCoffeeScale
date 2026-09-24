using Avalonia;
using Avalonia.MacCatalyst;
using Foundation;
using AppKit;
using CoffeeScale.UI;

namespace CoffeeScale.MacCatalyst;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    // 注册内嵌 emoji / 符号字体回退：与 Android / 桌面一致，确保界面图标
    // （🌱 ⚖ 🌡 ⚙ 💧 🫗 ⏱ ⏳ ☕ 🧮 …）在 MacCatalyst 上稳定显示。
    // 与 iOS 同样的考量：图标控件显式绑定 Noto Emoji（单色），注册回退保证四端渲染一致。
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).UseIconFontFallbacks();
}
