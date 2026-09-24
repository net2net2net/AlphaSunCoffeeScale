using UIKit;

namespace CoffeeScale.iOS;

public class Application
{
    // 应用程序主入口：OutputType=Exe 后必须提供，否则编译报 CS5001（无入口点）。
    // 与 Avalonia 官方 iOS 模板一致：把启动交给 AppDelegate（AvaloniaAppDelegate<App>）。
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
