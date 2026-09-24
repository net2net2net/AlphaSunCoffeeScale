using System.Runtime.CompilerServices;
using CoffeeScale.ViewModels;

namespace CoffeeScale.ViewModels.Tests;

/// <summary>
/// 测试程序集初始化：关闭 BrewViewModel 的「构造时自动恢复 settings.json」，
/// 避免多个用例因共享默认路径（AppContext.BaseDirectory/settings.json）而串味。
/// 持久化本身仍开启（Generate 仍写出），由本程序集内显式调用 LoadSettings/SettingsStore 的用例单独验证。
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    public static void Init()
    {
        BrewViewModel.AutoLoadSettings = false;
        BrewViewModel.AutoLoadMyPlans = false; // 同样关闭「我的方案」构造期自动恢复，避免共享默认路径串味
    }
}
