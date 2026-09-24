using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using CoffeeScale.UI;

namespace CoffeeScale.Android;

[Activity(
    Label = "@string/app_name",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    RoundIcon = "@drawable/icon",
    MainLauncher = true,
    // 旋转/折叠/字号变化不重建 Activity，由 Avalonia 响应式布局就地适配直屏↔横屏
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.FontScale | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
    // 注册内嵌 emoji / 符号字体回退：Android 平台缺少 emoji 字形回退（彩色 emoji 无法渲染），
    // 注册后界面图标（🌱 ⚖ 🌡 ⚙ 💧 🫗 ⏱ ⏳ …）可稳定显示，与桌面版保持一致。
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).UseIconFontFallbacks();
}
