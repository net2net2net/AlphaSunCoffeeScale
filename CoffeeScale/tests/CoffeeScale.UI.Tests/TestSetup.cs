using System.Runtime.CompilerServices;
using CoffeeScale.ViewModels;

namespace CoffeeScale.UI.Tests;

/// <summary>
/// UI headless 测试程序集初始化：关闭 BrewViewModel 的「构造时自动恢复 settings.json / myplans.json」。
/// 否则前一个用例的 PersistSettings()/SaveMyPlan() 会把状态写入共享默认路径（AppContext.BaseDirectory），
/// 后续用例新建的 MainWindow 会加载到被污染的状态（如 Ratio=14.5、残留方案），导致顺序相关的偶发失败。
/// 持久化逻辑本身仍开启，由用例显式设置临时路径后单独验证。
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    public static void Init()
    {
        BrewViewModel.AutoLoadSettings = false;
        BrewViewModel.AutoLoadMyPlans = false;
    }
}
