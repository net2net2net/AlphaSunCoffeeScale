using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;
using CoffeeScale.UI;

namespace CoffeeScale.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    // 注册内嵌 emoji / 符号字体回退：与 Android / 桌面一致，确保界面图标
    // （🌱 ⚖ 🌡 ⚙ 💧 🫗 ⏱ ⏳ ☕ 🧮 …）在 iOS 上稳定显示。
    // iOS 系统虽自带彩色 Apple Color Emoji，但本应用图标控件显式绑定 Noto Emoji（单色），
    // 且部分 emoji 以纯文本形式出现；注册回退可保证四端渲染完全一致、无缺字。
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).UseIconFontFallbacks();
}
