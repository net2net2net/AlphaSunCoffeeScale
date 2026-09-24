using UIKit;

namespace CoffeeScale.MacCatalyst;

public class Application
{
    // Mac Catalyst 仍走 UIKit 入口（与 iOS 一致）：OutputType=Exe 时必须显式提供。
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
