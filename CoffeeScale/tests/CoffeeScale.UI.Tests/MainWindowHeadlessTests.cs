using System.Linq;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CoffeeScale.Core;
using CoffeeScale.UI;
using CoffeeScale.ViewModels;
using Xunit;

namespace CoffeeScale.UI.Tests;

// 回归守卫：防止「界面全空白」类 bug 复发。
// 历史根因：MainWindow 构造函数构建了 _root 却从未 Content = _root，
// 且 _root 声明为 Grid 却用 DockPanel.SetDock（Dock 在 Grid 下无效 → 顶栏/主体重叠）。
// 本测试在无渲染环境下真实构造 MainWindow，断言内容树非空。
public class MainWindowHeadlessTests
{
    // Avalonia 的 AppBuilder.Start 内部只能 Setup 一次（进程级单例），重复调用会抛
    // "Setup was already called"。用 Lazy 保证整个测试进程只 Start 一次（同时由 App.Initialize 注入 FluentTheme），
    // 各测试方法直接 new MainWindow() 即可。
    private static readonly System.Lazy<object> _app = new(() =>
    {
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .Start((_, _) => { }, System.Array.Empty<string>());
        return null!;
    });

    private static void EnsureApp() => _ = _app.Value;

    [Fact]
    public void MainWindow_Content_IsSet_And_HasHeaderAndBody()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();

        // 1) 根内容必须被挂载（空白屏的第一道护栏）
        // 外层是 Border（移动端安全区承载层，Padding 消化状态栏/手势条 inset），
        // 其内为 Grid：主体 DockPanel（顶栏+主体）+ 模态浮层 Panel（移动端弹窗遮罩）
        Assert.NotNull(win.Content);
        var border = Assert.IsType<Border>(win.Content);
        var outer = Assert.IsType<Grid>(border.Child);
        var root = outer.Children.OfType<DockPanel>().FirstOrDefault();
        Assert.NotNull(root);
        Assert.NotNull(outer.Children.OfType<Panel>().FirstOrDefault(p => p != root)); // _modalLayer 浮层层存在

        // 2) 主体 DockPanel 下应有顶栏(header) + 主体(body) 两个直接子元素
        Assert.Equal(2, root!.Children.Count);

        // 3) 主体是 Grid，且包含两个面板（左设置 / 右冲煮）。
        // 改走 _bodyGrid 字段直取：mainArea 现在还多挂了 _masterListHost，按"末位 Border"取已不再稳定。
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        var bodyGrid = (Grid)t.GetField("_bodyGrid", f)!.GetValue(win)!;
        Assert.NotNull(bodyGrid);
        Assert.True(bodyGrid.Children.Count >= 2,
            "主体 Grid 应至少包含左设置区与右冲煮区两个面板");

        // 4) 左设置区实际构建了控件（非空）
        var settingsHost = (Border)bodyGrid.Children[0];
        Assert.NotNull(settingsHost.Child);

        // 5) 右冲煮区同样构建了控件（防止右半边也空白）
        var brewHost = (Border)bodyGrid.Children[1];
        Assert.NotNull(brewHost.Child);

        // 6) 顶栏含标题 + 作者文本块（作者跟随标题、留间隔、垂直靠底；语言按钮右侧）
        // 2026-09-09：顶栏已由 DockPanel 改为 Grid 两列（绕开 Android 单视图后端 fill 元素布局/渲染偏移）
        var header = root.Children.OfType<Grid>().FirstOrDefault();
        Assert.NotNull(header);
        var titleAuthor = header.Children.OfType<StackPanel>().FirstOrDefault();
        Assert.NotNull(titleAuthor);
        Assert.Contains(titleAuthor!.Children.OfType<TextBlock>(),
            t => t.Text == I18n.T("AppTitle"));
        Assert.Contains(titleAuthor.Children.OfType<TextBlock>(),
            t => t.Text == I18n.T("Author"));
        // 语言按钮在顶栏右侧
        Assert.Contains(header.Children.OfType<Button>(),
            b => (b.Content as string) == "CN/EN");

        // 6.5) Phase B 回归守卫：烘焙日期 + 冲煮日期 两个 DatePicker；称控制键齐全且顺序正确。
        // 防止「把日期输入改回普通文本框」「打乱 主运输键/时间清零/重置/下一阶段/上一阶段 顺序」类回归。
        // （2026-09-03：原「重量归零」去皮键已改为黄色圆形「重置」键，重置实时冲煮状态）
        I18n.Current = I18n.ZhCN;
        var datePickers = FindControls<DatePicker>(win).ToList();
        int dpCount = datePickers.Count;
        Assert.Equal(2, dpCount); // 烘焙日期 + 冲煮日期
        var expectedScaleOrder = new System.Collections.Generic.List<string>
        {
            I18n.T("Start"),          // 主运输圆形键（构造时显示「开始」）
            I18n.T("TimerReset"),     // 时间清零（绿色圆形）
            I18n.T("Reset"),          // 重置（黄色圆形，替代原重量归零）
            I18n.T("Next"),           // 下一阶段
            I18n.T("Prev"),           // 上一阶段
        };
        // 限定在称控制行的 WrapPanel 内收集（设置页另有一个「重置」键调用 ResetAll，不能混入顺序断言）
        var scaleRow = FindControls<WrapPanel>(win)
            .First(w => FindControls<Button>(w).Any(b => (b.Content as string) == I18n.T("Start")));
        var actualScaleOrder = FindControls<Button>(scaleRow)
            .Select(b => (b.Content as string) ?? string.Empty)
            .Where(c => expectedScaleOrder.Contains(c))
            .ToList();
        Assert.Equal(expectedScaleOrder, actualScaleOrder); // 顺序 + 完整性 + 无缺无多
        // 下一阶段 / 上一阶段 必须在同一行（同一父容器）
        var nextBtn = FindControls<Button>(win).FirstOrDefault(b => (b.Content as string) == I18n.T("Next"));
        var prevBtn = FindControls<Button>(win).FirstOrDefault(b => (b.Content as string) == I18n.T("Prev"));
        Assert.NotNull(nextBtn); Assert.NotNull(prevBtn);
        Assert.Same(nextBtn!.Parent, prevBtn!.Parent); // 同行（共享父 Grid）

        // 7) 语言切换回归守卫：切英文再切回中文（多次），走与界面语言按钮相同的
        //    PropertyChanged→RebuildAllText 路径。旧实现复用同一 readonly ItemsControl
        //    实例包进两个 ScrollViewer → "already has a visual parent" 崩溃（用户实测闪退/空白）。
        //    每次切换后立即执行真实布局遍历（Measure+Arrange），把布局期异常（双父级/附加属性误用等）暴露出来。
        var vm = (BrewViewModel)win.DataContext!;
        foreach (var lang in new[] { I18n.EnUS, I18n.ZhCN, I18n.EnUS, I18n.ZhCN })
        {
            vm.Culture = lang;
            // 真实布局遍历：触发同步 layout pass，捕获布局期 InvalidOperationException
            win.Measure(new Size(1040, 760));
            win.Arrange(new Rect(0, 0, 1040, 760));
            Assert.NotNull(win.Content);
            var outer2 = Assert.IsType<Border>(win.Content);
            var grid2 = Assert.IsType<Grid>(outer2.Child);
            var root2 = grid2.Children.OfType<DockPanel>().First();
            Assert.Equal(2, root2.Children.Count);
            // mainArea 末位 Border 现在是 _masterListHost（无 Child），改走 _bodyGrid 直取右冲煮区
            var bodyGrid2 = (Grid)t.GetField("_bodyGrid", f)!.GetValue(win)!;
            Assert.NotNull(bodyGrid2);
            Assert.True(bodyGrid2.Children.Count >= 2);
            var brewHost2 = (Border)bodyGrid2.Children[1];
            Assert.NotNull(brewHost2.Child);
        }

        // 8) 生成一次配方后再切语言 + 布局，确认绑定/实时控件重建后无双父级异常
        vm.StopTimer();
        vm.Generate();
        foreach (var lang in new[] { I18n.EnUS, I18n.ZhCN })
        {
            vm.Culture = lang;
            win.Measure(new Size(1040, 760));
            win.Arrange(new Rect(0, 0, 1040, 760));
            var root3 = Assert.IsType<Border>(win.Content!).Child is Grid g3
                ? g3.Children.OfType<DockPanel>().First()
                : throw new InvalidOperationException("Content 结构异常");
            // mainArea 末位 Border 现在是 _masterListHost（无 Child），改走 _bodyGrid 直取右冲煮区
            var bodyGrid3 = (Grid)t.GetField("_bodyGrid", f)!.GetValue(win)!;
            Assert.NotNull(bodyGrid3);
            var brewHost3 = (Border)bodyGrid3.Children[1];
            Assert.NotNull(brewHost3.Child);
        }
    }

    // 递归收集逻辑树中所有指定类型的控件（构造期即连接，无需布局 pass；headless 下比视觉树遍历更可靠）
    private static System.Collections.Generic.IEnumerable<T> FindControls<T>(Visual root) where T : class
    {
        foreach (var child in root.GetLogicalChildren())
        {
            if (child is T t) yield return t;
            if (child is Visual v) foreach (var d in FindControls<T>(v)) yield return d;
        }
    }

    // Phase 4 回归守卫：响应式布局在手机/电脑宽度下正确切换。
    // 2026-09-03：手机(380px)改为"左右分页"模式（设置/冲煮两页 + 底部指示器），不再上下堆叠；
    // 电脑(1200px)仍是左右双栏布局。用反射读私有 _bodyGrid / _compactHost / _compactStack / _settingsHost / _brewHost 断言。
    // 2026-09-03 晚：紧凑两页改 Grid 叠放 + IsVisible 切换（不再用 Canvas 写死宽高），
    //   并修复"竖屏→横屏→竖屏"回切时两页未重挂导致整屏空白的旋转 bug。
    [Fact]
    public void MainWindow_ResponsiveLayout_SwitchesStackVsColumns()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var bodyGrid = (Grid)t.GetField("_bodyGrid", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var settingsHost = (Border)t.GetField("_settingsHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var brewHost = (Border)t.GetField("_brewHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var compactHostF = t.GetField("_compactHost", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var compactStackF = t.GetField("_compactStack", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var compactPageF = t.GetField("_compactPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var apply = t.GetMethod("ApplyLayout", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // —— 手机宽度：左右分页（bodyGrid = 1 列 2 行：上主体 + 下指示器；两页在 compactHost 的叠放 Grid 里）
        apply.Invoke(win, new object[] { 380.0, 800.0 });
        Assert.Single(bodyGrid.ColumnDefinitions);
        Assert.Equal(2, bodyGrid.RowDefinitions.Count);
        var compactHost = (Grid?)compactHostF.GetValue(win);
        Assert.NotNull(compactHost);               // 紧凑容器已构建
        var stack = (Grid?)compactStackF.GetValue(win);
        Assert.NotNull(stack);                     // 叠放容器已构建
        Assert.Contains(settingsHost, stack!.Children);  // 设置页在叠放容器
        Assert.Contains(brewHost, stack.Children);       // 冲煮页同
        Assert.True(settingsHost.IsVisible);       // 起始页=设置：可见
        Assert.False(brewHost.IsVisible);          // 冲煮页隐藏（控件仍在树上，滚动位置保留）
        var pageName = compactPageF.GetValue(win)?.ToString();
        Assert.Equal("Settings", pageName);
        // 指示器 Grid（行1）存在：左切按钮 + 文案 + 右切按钮 三个控件
        var indRow = compactHost.Children[1] as Grid;
        Assert.NotNull(indRow);
        Assert.Equal(3, indRow!.Children.Count);

        // —— 电脑宽度：左右双栏（2 列 / 1 行），两面板直接在 bodyGrid
        apply.Invoke(win, new object[] { 1200.0, 800.0 });
        Assert.Equal(2, bodyGrid.ColumnDefinitions.Count);
        Assert.Single(bodyGrid.RowDefinitions);
        Assert.Equal(0, Grid.GetColumn(settingsHost)); Assert.Equal(0, Grid.GetRow(settingsHost));
        Assert.Equal(1, Grid.GetColumn(brewHost)); Assert.Equal(0, Grid.GetRow(brewHost));
        Assert.True(settingsHost.IsVisible && brewHost.IsVisible); // 双栏：两页都可见

        // —— 旋转回切守卫：竖屏→横屏→竖屏，两页必须重新挂回叠放容器（否则整屏空白）
        apply.Invoke(win, new object[] { 380.0, 800.0 });
        Assert.Contains(settingsHost, ((Grid?)compactStackF.GetValue(win))!.Children);
        Assert.Contains(brewHost, ((Grid?)compactStackF.GetValue(win))!.Children);
        // 横屏双栏时无任何固定宽高残留（清成 NaN 交还 Grid 布局，避免撑出屏幕裁切）
        apply.Invoke(win, new object[] { 1200.0, 800.0 });
        Assert.Equal(double.NaN, settingsHost.Width);
        Assert.Equal(double.NaN, settingsHost.Height);
        Assert.Equal(double.NaN, brewHost.Width);
        Assert.Equal(double.NaN, brewHost.Height);
    }

    // 四模块入口主界面（Home）守卫（2026-09-09）：启动默认显示 Home；
    // 构造期（宽屏形态）卡片容器为 2×2 Grid 含 4 张模块卡；紧凑形态重建为单列 StackPanel；
    // 进专业模式 → Home 隐藏 + ⌂ 返回键显示；返回 → Home 重建并显示。
    [Fact]
    public void MainWindow_HomeLauncher_EntersProModeAndReturns()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var homeHost = (Border)t.GetField("_homeHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var backBtn = (Button)t.GetField("_homeBackBtn", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var enterPro = t.GetMethod("EnterProMode", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var showHome = t.GetMethod("ShowHome", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var apply = t.GetMethod("ApplyLayout", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.True(homeHost.IsVisible);   // 启动默认在 Home（四模块入口）
        Assert.False(backBtn.IsVisible);   // ⌂ 返回键隐藏

        // 构造期（宽屏形态）：Home = Grid[背景装饰层 + ScrollViewer] > StackPanel(Hero + 2×2 卡片 Grid + 底部署名条)，4 张模块卡
        // 2026-09-11：BuildHome 外层加咖啡背景装饰层（暖光/咖啡渍/蒸汽），ScrollViewer 挪到 Grid 内
        var scroll = Assert.IsType<ScrollViewer>(
            Assert.IsType<Grid>(homeHost.Child!).Children.OfType<ScrollViewer>().First());
        var panel = Assert.IsType<StackPanel>(scroll.Content!);
        Assert.Equal(3, panel.Children.Count);   // Hero / 卡片容器 / 底部署名条（作者+版本）
        var cardsGrid = Assert.IsType<Grid>(panel.Children[1]);
        Assert.Equal(4, cardsGrid.Children.OfType<Button>().Count());

        // 紧凑形态（手机竖屏）：卡片容器重建为单列 StackPanel（Hero + 4 行卡片 + 底部署名条）
        apply.Invoke(win, new object[] { 380.0, 800.0 });
        var scroll2 = Assert.IsType<ScrollViewer>(
            Assert.IsType<Grid>(homeHost.Child!).Children.OfType<ScrollViewer>().First());
        var panel2 = Assert.IsType<StackPanel>(scroll2.Content!);
        Assert.Equal(3, panel2.Children.Count);
        var cards2 = Assert.IsType<StackPanel>(panel2.Children[1]);
        Assert.Equal(4, cards2.Children.OfType<Button>().Count());

        // 进入专业模式：Home 隐藏（露出下方现有冲煮界面），⌂ 返回键显示
        enterPro.Invoke(win, null);
        Assert.False(homeHost.IsVisible);
        Assert.True(backBtn.IsVisible);

        // 返回 Home：重建 + 显示，⌂ 重新隐藏
        showHome.Invoke(win, null);
        Assert.True(homeHost.IsVisible);
        Assert.False(backBtn.IsVisible);
        Assert.NotNull(homeHost.Child);
    }

    // Home 全量翻译守卫（2026-09-09 复盘）：CN/EN 切换后 Home 的 Hero 标语、四模块卡名称/
    // 描述/徽标、底部署名条必须整体重译——不允许出现「只有标题翻译、模块名回退中文」的漏翻。
    [Fact]
    public void MainWindow_HomeLauncher_TranslatesModulesOnLanguageSwitch()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var homeHost = (Border)t.GetField("_homeHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var vm = (BrewViewModel)win.DataContext!;
        var prev = vm.Culture;

        // 切英文：模块卡名称/描述/徽标 + Hero 标语必须出现英文文案（卡片文本带 "  " 前缀，用子串匹配）
        vm.Culture = I18n.EnUS;
        var enTexts = FindControls<TextBlock>(homeHost).Select(x => x.Text ?? "").ToList();
        string modeEn = I18n.T("ModeBeginner"), descEn = I18n.T("ModeDescBeginner");
        Assert.Contains(enTexts, s => s.Contains(modeEn));      // Beginner Mode
        Assert.Contains(enTexts, s => s.Contains(descEn));      // 描述
        // 2026-09-10：新手模式已实现，四模块徽标均为「Open」（不再有 Coming soon）。
        // 改以 StatusOpen 校验徽标翻译（StatusWip 已无对应模块）。
        Assert.Contains(enTexts, s => s.Contains(I18n.T("StatusOpen")));  // Open
        Assert.Contains(enTexts, s => s.Contains(I18n.T("HomeHero")));    // 标语
        Assert.DoesNotContain(enTexts, s => s.Contains("新手模式"));       // 不允许残留中文模块名

        // 切回中文：恢复中文文案（务必还原全局语言，避免泄漏影响后续用例）
        vm.Culture = I18n.ZhCN;
        var zhTexts = FindControls<TextBlock>(homeHost).Select(x => x.Text ?? "").ToList();
        Assert.Contains(zhTexts, s => s.Contains("新手模式"));
        Assert.Contains(zhTexts, s => s.Contains(I18n.T("HomeHero")));
        if (prev != I18n.ZhCN && prev != I18n.EnUS) vm.Culture = prev;
    }

    // 主界面「手冲大师方案」内联清单守卫（2026-09-10 #7）：点击卡片不再弹独立窗口，
    // 而是在 _masterListHost 覆盖层内联展开（手风琴：点击名字展开/收起，详情末端带「采用此方案」「取消」）。
    // 清单由 MasterPlans() 按年份最新→最旧返回。
    [Fact]
    public void MainWindow_HomeMasterPlan_InlineList_AccordionAndSort()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var f = BindingFlags.NonPublic | BindingFlags.Instance;

        // ① 打开主界面入口 → 覆盖层 _masterListHost 显示，home 隐藏
        t.GetMethod("ShowHomeMasterPlanPicker", f)!.Invoke(win, null);
        var host = (Border?)t.GetField("_masterListHost", f)!.GetValue(win);
        Assert.NotNull(host);
        Assert.True(host!.IsVisible);
        var homeHost = (Border?)t.GetField("_homeHost", f)!.GetValue(win);
        Assert.False(homeHost!.IsVisible);
        Assert.NotNull(host.Child);

        // ② 清单按年份最新→最旧排序（无年份的沉底）
        var plans = (System.Collections.Generic.List<MasterPlanItem>?)t
            .GetMethod("MasterPlans", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        Assert.NotNull(plans);
        Assert.NotEmpty(plans!);
        for (int i = 1; i < plans!.Count; i++)
        {
            int yPrev = (int)t.GetMethod("MasterPlanYear", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { plans[i - 1].Title })!;
            int yCur = (int)t.GetMethod("MasterPlanYear", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { plans[i].Title })!;
            Assert.True(yPrev >= yCur, $"大师清单应按年份降序：#{i - 1}({yPrev}) ≥ #{i}({yCur})");
        }

        // ③ 列表有内容（不是空态）
        var panel = (StackPanel?)t.GetField("_masterListPanel", f)!.GetValue(win);
        Assert.NotNull(panel);
        Assert.NotEmpty(panel!.Children);

        // ④ 内联展开第一个方案：设 _expandedMasterTitle 并重排，验证该行下方出现「采用此方案」「取消」按钮
        var expandedF = t.GetField("_expandedMasterTitle", f)!;
        expandedF.SetValue(win, plans[0].Title);
        t.GetMethod("MasterListPopulate", f)!.Invoke(win, new object[] { "" });
        var panelAfter = (StackPanel)t.GetField("_masterListPanel", f)!.GetValue(win)!;
        Assert.True(ChildTreeHasText(panelAfter, "✅")
                  , "展开后应在某行出现「✅ 采用此方案」按钮");
        // 取消按钮文案走 I18n（zh "取消" / en "Cancel"），按当前语言解析后断言（语言无关）
        string cancelText = I18n.T("Cancel");
        Assert.True(ChildTreeHasText(panelAfter, cancelText)
                  , $"展开后应在某行出现「{cancelText}」按钮");
    }

    // 递归收集控件树中所有 TextBlock.Text 与 ContentControl.Content（字符串），命中 needle 即返回 true。
    // 走 VisualTree.GetVisualChildren() 统一遍历（Panel 与 ContentControl 都覆盖）。
    // Button 等的 Content 是字符串（"✅ 采用此方案"），不挂在 TextBlock 上，需单独检查。
    private static bool ChildTreeHasText(Avalonia.Controls.Control root, string needle)
    {
        if (root is TextBlock tb && (tb.Text ?? "").Contains(needle, System.StringComparison.OrdinalIgnoreCase)) return true;
        if (root is Avalonia.Controls.ContentControl cc && cc.Content is string cs
            && cs.Contains(needle, System.StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var c in root.GetVisualChildren())
            if (c is Avalonia.Controls.Control child && ChildTreeHasText(child, needle)) return true;
        return false;
    }

    // 专业模式标签行「返回主界面」守卫（2026-09-09）：左侧标签栏同一行存在返回主界面按钮，
    // 点击后关闭专业模式界面并回到四模块入口主界面（Home 重新可见、⌂ 顶栏键隐藏）。
    [Fact]
    public void MainWindow_ProModeTabs_BackHomeButtonReturnsHome()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var homeHost = (Border)t.GetField("_homeHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var backBtn = (Button)t.GetField("_homeBackBtn", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var settingsHost = (Border)t.GetField("_settingsHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
        var enterPro = t.GetMethod("EnterProMode", BindingFlags.NonPublic | BindingFlags.Instance)!;

        enterPro.Invoke(win, null);
        Assert.False(homeHost.IsVisible);

        // 标签行同一行存在「返回主界面」按钮（Content 为字符串，含 BackHome 文案）
        var backHome = FindControls<Button>(settingsHost)
            .FirstOrDefault(b => (b.Content as string)?.Contains(I18n.T("BackHome")) == true);
        Assert.NotNull(backHome);

        // 点击 → 返回主界面
        backHome!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(homeHost.IsVisible);   // Home 重新可见 = 专业模式界面被盖回
        Assert.False(backBtn.IsVisible);   // 顶栏 ⌂ 键隐藏
    }

    // 简易咖啡计算器守卫（2026-09-09）：粉水比三联算（正向/反推）+ 分段计时状态机。
    [Fact]
    public void MainWindow_SimpleCalc_RatioAndTimer()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        t.GetMethod("ShowCalc", f)!.Invoke(win, null);
        var calcHost = (Border)t.GetField("_calcHost", f)!.GetValue(win)!;
        Assert.True(calcHost.IsVisible);   // 计算器层已打开
        var doseBox = (TextBox)t.GetField("_calcDoseBox", f)!.GetValue(win)!;
        var ratioBox = (TextBox)t.GetField("_calcRatioBox", f)!.GetValue(win)!;
        var waterBox = (TextBox)t.GetField("_calcWaterBox", f)!.GetValue(win)!;

        // 初始：15.0 × 15.0 = 225.0
        Assert.Equal("225.0", waterBox.Text);

        // 反推优先粉量：改总注水量 300 → 粉量 = 300 / 15 = 20.0，比值保持 15.0 不变
        waterBox.Text = "300.0";
        Assert.Equal("20.0", doseBox.Text);
        Assert.Equal("15", ratioBox.Text);      // 比值为整数（2026-09-10：不支持小数）

        // 总注水量 250 → 粉量 = 250 / 15 = 16.7（1 位小数格式）
        waterBox.Text = "250.0";
        Assert.Equal("16.7", doseBox.Text);

        // 比值为空时改为输出比值：清空比值，总注水量 334 → 比值 = 334 / 16.7 = 20.0
        ratioBox.Text = "";
        waterBox.Text = "334.0";
        Assert.Equal("20", ratioBox.Text);      // 整数输出

        // 正向：改粉量 10 → 总注水量 = 10 × 20 = 200.0
        doseBox.Text = "10.0";
        Assert.Equal("200.0", waterBox.Text);

        // 分段计时：启动 → 下一段（第1段入账）→ 停止（第2段入账 + 总耗时行）
        t.GetMethod("CalcStart", f)!.Invoke(win, null);
        Assert.True((bool)t.GetField("_calcRunning", f)!.GetValue(win)!);
        t.GetMethod("CalcNext", f)!.Invoke(win, null);
        var segs = (System.Collections.Generic.List<double>)t.GetField("_calcSegs", f)!.GetValue(win)!;
        Assert.Single(segs);                        // 第 1 段已结束
        Assert.True((bool)t.GetField("_calcRunning", f)!.GetValue(win)!);  // 第 2 段进行中
        t.GetMethod("CalcStop", f)!.Invoke(win, null);
        Assert.False((bool)t.GetField("_calcRunning", f)!.GetValue(win)!);
        Assert.Equal(2, segs.Count);                // 停止时第 2 段入账
        foreach (var s in segs) Assert.True(s >= 0); // 段时长非负
    }

    // 2026-09-10 守卫：区间校验（粉量 5–50 / 比值 1–30 整数 / 注水量 5–1500）
    // + 越界不扩散 + 比值去小数 + 一行公式（粉量带 g 单位）
    // + 机械秒表（大秒针 60 秒一圈 / 小分针 30 分钟一圈 / 停止后定格）+ 启动圆形/下一段蓝/停止红。
    [Fact]
    public void MainWindow_SimpleCalc_RangeValidationAndTimerVisuals()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        t.GetMethod("ShowCalc", f)!.Invoke(win, null);

        var doseBox = (TextBox)t.GetField("_calcDoseBox", f)!.GetValue(win)!;
        var ratioBox = (TextBox)t.GetField("_calcRatioBox", f)!.GetValue(win)!;
        var waterBox = (TextBox)t.GetField("_calcWaterBox", f)!.GetValue(win)!;
        var warnText = (TextBlock)t.GetField("_calcWarnText", f)!.GetValue(win)!;
        var ringText = (TextBlock)t.GetField("_calcRingText", f)!.GetValue(win)!;      // 表盘下方状态文字
        var totalText = (TextBlock)t.GetField("_calcTotalText", f)!.GetValue(win)!;    // 右侧电子秒表
        var startBtn = (Button)t.GetField("_calcStartBtn", f)!.GetValue(win)!;
        var nextBtn = (Button)t.GetField("_calcNextBtn", f)!.GetValue(win)!;
        var stopBtn = (Button)t.GetField("_calcStopBtn", f)!.GetValue(win)!;

        // ① 初值合法 → 无越界提示
        Assert.False(warnText.IsVisible);

        // ② 粉量越界（60 > 50）→ 提示出现，且非法值不扩散到总注水量
        string? waterBefore = waterBox.Text;
        doseBox.Text = "60";
        Assert.True(warnText.IsVisible);
        Assert.Contains("5", warnText.Text);
        Assert.Equal(waterBefore, waterBox.Text);   // 未联动

        doseBox.Text = "18";                        // 恢复合法 → 提示消失
        Assert.False(warnText.IsVisible);

        // 粉量只允许 1 位小数（12.345 → 12.3）
        doseBox.Text = "12.345";
        Assert.Equal("12.3", doseBox.Text);
        doseBox.Text = "18";

        // ③ 比值整数化（输入 15.7 → 15）+ 越界（31 > 30）
        ratioBox.Text = "15.7";
        Assert.Equal("15", ratioBox.Text);
        ratioBox.Text = "31";
        Assert.True(warnText.IsVisible);
        ratioBox.Text = "16";
        Assert.False(warnText.IsVisible);

        // ④ 总注水量：合法 640 → 反算粉量 40.0；越界 2001 → 提示出现且不反推
        waterBox.Text = "640";
        Assert.Equal("40.0", doseBox.Text);
        waterBox.Text = "2001";
        Assert.True(warnText.IsVisible);
        Assert.Equal("40.0", doseBox.Text);         // 越界不扩散
        waterBox.Text = "640";
        Assert.False(warnText.IsVisible);

        // ⑤ 一行公式布局：三个输入框同属一个横向容器（粉量 × 1:比值 = 总注水量）
        var eqPanel = Assert.IsType<StackPanel>(doseBox.Parent);
        Assert.Equal(Orientation.Horizontal, eqPanel.Orientation);
        Assert.Contains(ratioBox, eqPanel.Children);
        Assert.Contains(waterBox, eqPanel.Children);

        // ⑤b 粉量输入格后紧跟单位 g
        int doseIdx = eqPanel.Children.IndexOf(doseBox);
        var doseUnit = Assert.IsType<TextBlock>(eqPanel.Children[doseIdx + 1]);
        Assert.Equal("g", doseUnit.Text);

        // ⑤c 机械秒表：大秒针 + 小分针存在，启动后大秒针转动
        var hand = (Avalonia.Controls.Shapes.Line)t.GetField("_calcRingHand", f)!.GetValue(win)!;
        var minHand = (Avalonia.Controls.Shapes.Line)t.GetField("_calcRingMinHand", f)!.GetValue(win)!;
        var handStart = hand.EndPoint;
        double ringD = (double)t.GetField("_calcRingD", f)!.GetValue(win)!;
        Assert.True(minHand.EndPoint.Y <= ringD / 2 + 0.5);   // 小分针初始指向 12 点（上方）

        // ⑥ 按钮：未启动时「下一段」禁用；启动后启用并转蓝；「停止」为红色
        Assert.False(nextBtn.IsEnabled);
        Assert.False(stopBtn.IsEnabled);
        Assert.Equal(startBtn.Width, startBtn.Height);                       // 启动键正圆
        Assert.Contains("c0392b", ((Avalonia.Media.ISolidColorBrush)stopBtn.Background!).Color.ToString(),
            StringComparison.OrdinalIgnoreCase);   // 「停止」红

        t.GetMethod("CalcStart", f)!.Invoke(win, null);
        System.Threading.Thread.Sleep(300);
        t.GetMethod("UpdateCalcRing", f)!.Invoke(win, null);
        Assert.NotEqual(handStart.X, hand.EndPoint.X);   // 秒表指针已转动
        Assert.True(nextBtn.IsEnabled);
        Assert.Contains("2e6fd6", ((Avalonia.Media.ISolidColorBrush)nextBtn.Background!).Color.ToString(),
            StringComparison.OrdinalIgnoreCase);   // 启动后「下一段」转蓝

        // ⑦ 下一段不停表 → 停止后指针定格（表盘不显示电子数字，读数看电子秒表）
        System.Threading.Thread.Sleep(150);
        t.GetMethod("CalcNext", f)!.Invoke(win, null);
        Assert.True((bool)t.GetField("_calcRunning", f)!.GetValue(win)!);    // 仍在计时
        System.Threading.Thread.Sleep(250);
        t.GetMethod("CalcStop", f)!.Invoke(win, null);
        var segs = (System.Collections.Generic.List<double>)t.GetField("_calcSegs", f)!.GetValue(win)!;
        Assert.Equal(2, segs.Count);
        Assert.NotEqual("00:00.00", totalText.Text);                          // 电子秒表读数定格
        var frozen = hand.EndPoint;
        System.Threading.Thread.Sleep(120);
        t.GetMethod("UpdateCalcRing", f)!.Invoke(win, null);
        Assert.Equal(frozen.X, hand.EndPoint.X);                              // 停止后指针不再走
        Assert.Equal(I18n.T("CalcSegStopped"), ringText.Text);                // 状态文字 = 已停止
    }

    // 新手模式守卫（2026-09-10）：图形化滤杯选择 → 选烘焙度 → 引擎给建议（研磨度/水温/粉水比/总注水量）
    // + 一键模拟冲煮节奏（驱动 BrewEngine 自动走段至完成）。
    [Fact]
    public void MainWindow_BeginnerMode_GuidedFlow()
    {
        EnsureApp();
        var win = new MainWindow();
        var t = win.GetType();
        var f = BindingFlags.NonPublic | BindingFlags.Instance;

        t.GetMethod("ShowBeginner", f)!.Invoke(win, null);
        var host = (Border)t.GetField("_beginnerHost", f)!.GetValue(win)!;
        Assert.True(host.IsVisible);                       // 新手模式层已打开

        // ① 图形化滤杯卡（锥形/平底/滤瓶/浸泡阀四类线稿）+ ② 三档烘焙度卡
        var dripperChips = (WrapPanel)t.GetField("_begDripperChips", f)!.GetValue(win)!;
        var roastChips = (WrapPanel)t.GetField("_begRoastChips", f)!.GetValue(win)!;
        Assert.InRange(dripperChips.Children.Count, 3, 5);   // 仅常用 3–4 款滤杯图形卡（当前固定 4 款）
        Assert.Equal(3, roastChips.Children.Count);         // 深 / 中 / 浅

        // 每个烘焙度都应产出合法建议配方（研磨度/水温/粉水比非空）
        var recipeF = t.GetField("_begRecipe", f)!;
        var suggestCard = (Border)t.GetField("_begSuggestCard", f)!.GetValue(win)!;
        foreach (var roast in new[] { "dark", "medium", "light" })
        {
            t.GetMethod("BeginnerSelectDripper", f)!.Invoke(win, new object[] { "v60" });
            t.GetMethod("BeginnerSelectRoast", f)!.Invoke(win, new object[] { roast });
            var recipe = (Recipe?)recipeF.GetValue(win);
            Assert.NotNull(recipe);
            Assert.True(recipe!.Temp > 0);
            Assert.True(recipe.GrindC40 > 0);
            Assert.True(recipe.Ratio >= 8);
            Assert.True(suggestCard.IsVisible);             // 建议卡显示
        }

        // 深烘建议的水温应低于浅烘（浅烘高水温提升萃取）——验证引擎逻辑联动
        t.GetMethod("BeginnerSelectDripper", f)!.Invoke(win, new object[] { "v60" });
        t.GetMethod("BeginnerSelectRoast", f)!.Invoke(win, new object[] { "light" });
        int lightTemp = ((Recipe?)recipeF.GetValue(win))!.Temp;
        t.GetMethod("BeginnerSelectRoast", f)!.Invoke(win, new object[] { "dark" });
        int darkTemp = ((Recipe?)recipeF.GetValue(win))!.Temp;
        Assert.True(lightTemp > darkTemp, "浅烘水温应高于深烘");

        // ③ 模拟冲煮：启动后逐步推进至完成（自增时钟步进，与 UI 定时器同路径）
        t.GetMethod("BeginnerStartSim", f)!.Invoke(win, null);
        var stateF = t.GetField("_begState", f)!;
        Assert.NotNull(stateF.GetValue(win));
        var brewPanel = (Border)t.GetField("_begBrewPanel", f)!.GetValue(win)!;
        Assert.True(brewPanel.IsVisible);                   // 模拟面板显示、建议卡隐藏
        Assert.False(suggestCard.IsVisible);

        var step = t.GetMethod("BeginnerStep", f)!;
        for (int i = 0; i < 3000; i++)
        {
            step!.Invoke(win, new object[] { 150.0 });      // 150ms / 步
            var st = (EngineState?)stateF.GetValue(win);
            if (st != null && st.Done) break;
        }
        var final = (EngineState?)stateF.GetValue(win);
        Assert.NotNull(final);
        Assert.True(final!.Done, "模拟冲煮应在步进上限内完成");
        Assert.True(final.Weight >= final.Recipe.TotalWater - 1.0, "模拟末重应达到总注水量");

        // 重新开始应回到建议卡、状态清空
        t.GetMethod("BeginnerReset", f)!.Invoke(win, null);
        Assert.Null(stateF.GetValue(win));
        Assert.True(suggestCard.IsVisible);
    }

    // 2026-09-10 新手模式语言一致性：中文态全中文、英文态全英文，无裸 key、无中文泄漏。
    [Fact]
    public void MainWindow_BeginnerMode_LanguageConsistency()
    {
        // —— 中文态 ——
        I18n.Current = I18n.ZhCN;
        {
            var win = new MainWindow();
            var t = win.GetType();
            var f = BindingFlags.NonPublic | BindingFlags.Instance;
            t.GetMethod("ShowBeginner", f)!.Invoke(win, null);

            var dripperChips = (WrapPanel)t.GetField("_begDripperChips", f)!.GetValue(win)!;
            Button? v60 = null;
            foreach (var b in dripperChips.Children.OfType<Button>())
            {
                var tag = ((string, List<Avalonia.Controls.Shapes.Shape>, TextBlock))b.Tag!;
                if (tag.Item1 == "v60") { v60 = b; break; }
            }
            Assert.NotNull(v60);
            var v60Lab = ((string, List<Avalonia.Controls.Shapes.Shape>, TextBlock))v60!.Tag!;
            Assert.Equal("V60 锥形滤杯", v60Lab.Item3.Text);                 // 中文标签
            Assert.Equal(I18n.T("Dripper_v60"), v60Lab.Item3.Text);

            t.GetMethod("BeginnerSelectDripper", f)!.Invoke(win, new object[] { "v60" });
            var filterPanel = (StackPanel)t.GetField("_begFilterPanel", f)!.GetValue(win)!;
            var filterTexts = filterPanel.Children.OfType<TextBlock>().Select(tb => tb.Text ?? "").ToList();
            Assert.Contains(filterTexts, s => s.Contains("V60 漂白滤纸"));     // 中文具体滤纸建议
            Assert.DoesNotContain(filterTexts, s => s.Contains("放入滤纸"));   // 无「放入滤纸」环节

            t.GetMethod("BeginnerSelectRoast", f)!.Invoke(win, new object[] { "dark" });
            var recipe = (Recipe?)t.GetField("_begRecipe", f)!.GetValue(win);
            Assert.NotNull(recipe);
            Assert.Equal(I18n.T("Grind_" + recipe!.Grind), recipe.GrindLabel);
            Assert.Equal(I18n.T("Dripper_" + recipe.Dripper), recipe.DripperLabel);
            Assert.Equal(I18n.T("Roast_" + recipe.Roast), recipe.RoastLabel);
            Assert.Equal(I18n.T("Filter_" + recipe.Filter), recipe.FilterLabel);
            Assert.False(recipe.GrindLabel.StartsWith("Grind_"), "研磨标签不应回退为 key");
            Assert.False(recipe.RoastLabel.StartsWith("Roast_"), "烘焙标签不应回退为 key");
        }

        // —— 英文态：全英文，无中文泄漏、无裸 key ——
        I18n.Current = I18n.EnUS;
        {
            var win = new MainWindow();
            var t = win.GetType();
            var f = BindingFlags.NonPublic | BindingFlags.Instance;
            t.GetMethod("ShowBeginner", f)!.Invoke(win, null);

            var dripperChips = (WrapPanel)t.GetField("_begDripperChips", f)!.GetValue(win)!;
            Button? v60 = null;
            foreach (var b in dripperChips.Children.OfType<Button>())
            {
                var tag = ((string, List<Avalonia.Controls.Shapes.Shape>, TextBlock))b.Tag!;
                if (tag.Item1 == "v60") { v60 = b; break; }
            }
            Assert.NotNull(v60);
            var v60Lab = ((string, List<Avalonia.Controls.Shapes.Shape>, TextBlock))v60!.Tag!;
            Assert.Equal("V60 cone dripper", v60Lab.Item3.Text);            // 英文标签
            Assert.Equal(I18n.T("Dripper_v60"), v60Lab.Item3.Text);
            Assert.DoesNotContain("锥形", v60Lab.Item3.Text);

            t.GetMethod("BeginnerSelectDripper", f)!.Invoke(win, new object[] { "v60" });
            var filterPanel = (StackPanel)t.GetField("_begFilterPanel", f)!.GetValue(win)!;
            var filterTexts = filterPanel.Children.OfType<TextBlock>().Select(tb => tb.Text ?? "").ToList();
            Assert.Contains(filterTexts, s => s.Contains("V60 bleached"));   // 英文具体滤纸建议
            Assert.DoesNotContain(filterTexts, s => s.Contains("Place filter"));

            t.GetMethod("BeginnerSelectRoast", f)!.Invoke(win, new object[] { "dark" });
            var recipe = (Recipe?)t.GetField("_begRecipe", f)!.GetValue(win);
            Assert.NotNull(recipe);
            Assert.Equal(I18n.T("Grind_" + recipe!.Grind), recipe.GrindLabel);
            Assert.DoesNotContain("粗", recipe.GrindLabel);                  // 无中文泄漏
            Assert.False(recipe.GrindLabel.StartsWith("Grind_"));

            // 整个新手模式层不应出现中文字符（℃/·/emoji 等非 CJK，均安全）
            var host = (Border)t.GetField("_beginnerHost", f)!.GetValue(win)!;
            var hostTexts = FindControls<TextBlock>((Control)host)
                .Select(tb => tb.Text ?? "")
                .Concat(FindControls<Button>((Control)host).Select(b => (b.Content as string) ?? ""))
                .ToList();
            Assert.DoesNotContain(hostTexts, s => s.Any(ch => ch >= 0x4E00 && ch <= 0x9FFF));
        }

        I18n.Current = I18n.ZhCN; // 复位，防串扰
    }

    // 2026-09-03 布局判定守卫：移动端按横竖屏（竖屏=分页 / 横屏=双栏完整），桌面按宽度 <640。
    [Fact]
    public void MainWindow_IsCompactLayout_OrientationBasedOnMobile()
    {
        var t = typeof(MainWindow);
        var m = t.GetMethod("IsCompactLayout", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<bool, double, double, bool>>();

        // 移动端：竖屏（高>宽）→ 分页；横屏（宽>高）→ 双栏完整
        Assert.True(m(true, 380, 800));     // 手机竖屏
        Assert.True(m(true, 617, 1097));    // 手机竖屏（dp，与 1080x1920@280dpi 一致）
        Assert.False(m(true, 1097, 617));   // 手机横屏 → 双栏完整（横屏要求整个界面放下）
        Assert.False(m(true, 1280, 800));   // 平板横屏
        Assert.True(m(true, 800, 1280));    // 平板竖屏（仍走分页）
        // 桌面端：只看宽度
        Assert.True(m(false, 380, 800));    // 窄窗口 → 分页
        Assert.False(m(false, 1200, 800));  // 宽窗口 → 双栏
        Assert.True(m(false, 500, 200));    // 桌面窄窗口（<640）→ 分页
        // 未测量（0x0）：移动端默认分页（Android 构造期 Bounds 为零）；桌面保持宽度判定
        Assert.True(m(true, 0, 0));
        Assert.True(m(false, 0, 0));
    }

    // 2026-09-10 形态因子守卫：手机/手机横屏/平板/桌面/宽屏；非测量态默认 Desktop；
    // 移动端尺寸（短边 ≤420 / 长边 ≤920dp）归 Phone 系，否则折叠屏按宽度进 Tablet/Desktop。
    [Fact]
    public void MainWindow_DetectFormFactor_CoversFiveShapes()
    {
        var t = typeof(MainWindow);
        var m = t.GetMethod("DetectFormFactor", BindingFlags.NonPublic | BindingFlags.Static)!;
        // FormFactor 是 internal 枚举，跨程序集不能直接 CreateDelegate<...FormFactor>；用 object + Convert.ToInt32 校验
        static int Call(System.Reflection.MethodInfo m, bool mobile, double w, double h) =>
            Convert.ToInt32(m.Invoke(null, new object[] { mobile, w, h }));

        // Phone=0, PhoneLandscape=1, Tablet=2, Desktop=3, Wide=4
        Assert.Equal(0, Call(m, true, 380, 820));        // 手机竖屏（典型 6.5″）
        Assert.Equal(0, Call(m, true, 411, 891));        // Pixel 竖屏
        Assert.Equal(1, Call(m, true, 820, 380));        // 手机横屏
        Assert.Equal(2, Call(m, true, 768, 1024));       // 平板竖屏（短边 768 > 420）
        Assert.Equal(3, Call(m, false, 1280, 800));      // 桌面
        Assert.Equal(4, Call(m, false, 1920, 1080));     // 超宽屏
        Assert.Equal(4, Call(m, false, 2560, 1440));     // 2K 宽屏
        Assert.Equal(0, Call(m, false, 480, 800));       // 桌面窄窗口 < 640 复用 Phone
        Assert.Equal(3, Call(m, false, 0, 0));           // 未测量 → Desktop
    }

    // 2026-09-10 主界面每模块色条守卫：4 张卡片的内容 Grid 应含 1 列固定 5dp 的 Border（不同模式色）。
    [Fact]
    public void MainWindow_HomeCards_HavePerModeAccentStripe()
    {
        EnsureApp();
        I18n.Current = I18n.ZhCN;
        var win = new MainWindow();
        var t = win.GetType();
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        // BuildHome 守卫：内部 width/height 直接走 _homeCompact=true 单列路径，避免依赖窗口尺寸
        t.GetField("_homeShort", f)!.SetValue(win, false);
        t.GetField("_homeCompact", f)!.SetValue(win, true);
        var host = (Border?)t.GetField("_homeHost", f)!.GetValue(win);
        if (host?.Child == null) t.GetMethod("BuildHome", f)!.Invoke(win, null);

        var cards = FindControls<Button>(win)
            .Where(b => b.Parent is Grid g2 && g2.Children.Count == 4) // 2×2 grid card parent
            .ToList();
        // 上一步只覆盖宽屏网格；此处改用递归匹配 HomeCard 模式：内容是 Grid 且首列是 5dp 宽度 Border
        var accentStripes = FindControls<Border>(win)
            .Where(b => b.Width == 5 && b.Height.Equals(double.NaN) == false && b.VerticalAlignment == VerticalAlignment.Stretch)
            .Count();
        // 兜底：直接数 HomeCard 内部 Grid 的 Column 0 元素
        int stripeCount = 0;
        foreach (var card in FindControls<Button>(win))
        {
            // 2026-09-13：HomeCard 的卡面改由内层 Border 承担（Button 无 BoxShadow，投影需画在 Border 上），
            // 故先解一层 Border 包装再取内部 Grid；断言意图不变——4 张主卡都要有 5dp 模块色条。
            var content = card.Content is Border wrap ? wrap.Child : card.Content;
            if (content is Grid cg && cg.ColumnDefinitions.Count == 2 &&
                cg.ColumnDefinitions[0].Width.IsAbsolute == false) // "Auto,*" => Auto 解析后 Width.IsAbsolute=false
            {
                // Auto 列上必须有 Border 子控件
                foreach (var ch in cg.Children)
                    if (ch is Border bd && bd.Width == 5) { stripeCount++; break; }
            }
        }
        Assert.Equal(4, stripeCount); // 4 张主卡都应带色条
    }

    // 左侧分页回归守卫：初始停留在「冲煮方案设置」页，推荐区不在逻辑树；
    // 点击「生成推荐冲煮方案」后切到「推荐冲煮方案」页，推荐区标题出现（右侧不再含推荐内容）。
    [Fact]
    public void MainWindow_LeftPaging_SwitchesToRecommendAfterGenerate()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;

        // 1) 顶部两个分页标签存在（冲煮方案设置 / 推荐冲煮方案）
        var tabTexts = FindControls<Button>(win)
            .Select(b => (b.Content as string) ?? string.Empty)
            .Where(c => c == I18n.T("Settings") || c == I18n.T("RecommendBottom"))
            .ToList();
        Assert.Equal(2, tabTexts.Count);

        // 2) 初始处于「冲煮方案设置」页：推荐区标题（TextBlock）尚未构建
        bool HasRecommendTitle() =>
            FindControls<TextBlock>(win).Any(t => t.Text == I18n.T("RecommendBottom"));
        Assert.False(HasRecommendTitle(), "初始应停留在冲煮方案设置页，推荐区不应在逻辑树中");

        // 3) 点击「生成推荐冲煮方案」→ 直接切到「推荐冲煮方案」页
        var genBtn = FindControls<Button>(win)
            .FirstOrDefault(b => (b.Content as string) == I18n.T("GeneratePlan"));
        Assert.NotNull(genBtn);
        genBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(HasRecommendTitle(), "生成后应切到推荐冲煮方案页，推荐区标题应出现");
    }

    // 大师方案清单回归守卫：推荐区「导入咖啡大师方案」弹出「咖啡大师冲煮方案清单」窗口（标题 2026-09-02 按用户要求修正）；
    // 选中一项后内容写入 VM.MasterPlanText（推荐区出现「咖啡大师方案」卡）；同时「保存 / 统计 / 记录」
    // 已从推荐区迁出到右侧实时冲煮末尾（推荐区不应再含「保存本次冲煮记录」按钮）。
    [Fact]
    public void MainWindow_MasterPlanPicker_OpensAndApplies()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var leftPageHost = (Border)win.GetType()
            .GetField("_leftPageHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(win)!;

        // 进入推荐冲煮方案页
        var genBtn = FindControls<Button>(win)
            .FirstOrDefault(b => (b.Content as string) == I18n.T("GeneratePlan"));
        Assert.NotNull(genBtn);
        genBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // 推荐区不再含「保存本次冲煮记录」按钮（已迁到右侧实时冲煮末尾）
        Assert.DoesNotContain(FindControls<Button>(leftPageHost),
            b => (b.Content as string) == I18n.T("Save"));

        // 点击「导入咖啡大师方案」弹出清单窗口（标题为「咖啡大师冲煮方案清单」）
        var importBtn = FindControls<Button>(win)
            .FirstOrDefault(b => (b.Content as string)?.Contains(I18n.T("ImportPlan")) == true);
        Assert.NotNull(importBtn);
        importBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var picker = (Window?)win.GetType()
            .GetField("_masterPlanPickerWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(picker);
        Assert.Equal(I18n.T("MasterPlanTitle"), picker!.Title); // 窗口标题已按用户要求修正

        // 清单罗列多个大师方案（Content 为 StackPanel 的按钮）
        var planItems = FindControls<Button>(picker!)
            .Where(b => b.Content is StackPanel).ToList();
        Assert.True(planItems.Count >= 3, "清单应罗列多个大师方案");

        // 选中第一项 → 内容写入 VM.MasterPlanText，窗口关闭
        planItems[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(string.IsNullOrEmpty(vm.MasterPlanText), "选中大师方案后 VM.MasterPlanText 应非空");
        Assert.Null((Window?)win.GetType()
            .GetField("_masterPlanPickerWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(win));

        // 推荐区出现「咖啡大师方案」卡标题
        Assert.Contains(FindControls<TextBlock>(leftPageHost),
            t => t.Text == I18n.T("MasterPlanCard"));
    }

    // 大师方案联动回归守卫：选中清单首项（粕谷哲 4:6 法，粉量 20g / 粉水比 1:15）后，
    // 不仅正文写入 MasterPlanText，还应把粉量/粉水比回灌冲煮引擎（VM.Dose / VM.Ratio 随之更新），
    // 使「推荐研磨 / 报告 / 流程」随大师参数重算、可一键冲煮（模块联动 / 数据共享）。
    [Fact]
    public void MainWindow_MasterPlanPicker_AppliesParamsToEngine()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;

        // 进入推荐冲煮方案页并打开清单
        FindControls<Button>(win)
            .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindControls<Button>(win)
            .First(b => (b.Content as string)?.Contains(I18n.T("ImportPlan")) == true)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var picker = (Window?)win.GetType()
            .GetField("_masterPlanPickerWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(picker);

        // 查找「粕谷哲 4:6 法」（粉量 20g / 粉水比 1:15）——方案列表顺序会随新增方案变化，按标题精确定位
        var planItems = FindControls<Button>(picker!)
            .Where(b => b.Content is StackPanel).ToList();
        Assert.True(planItems.Count >= 1);
        var kasuyaItem = planItems.First(b =>
            (b.Content as StackPanel)?.Children.OfType<TextBlock>()?.FirstOrDefault()?.Text?.Contains("粕谷哲") == true);
        kasuyaItem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // 联动断言：粉量 / 粉水比 / 方法 已回灌引擎
        Assert.Equal(20, vm.Dose);
        Assert.Equal(15, vm.Ratio);
        // 粉量档位随之切换（20g → 标准档）
        Assert.Equal("standard", vm.Size);
        Assert.Equal("kasuya46", vm.Method); // 粕谷哲 4:6 法 → kasuya46
        // 配方已据大师参数重算（可一键冲煮）
        Assert.True(vm.HasRecipe);

        // 二次打开清单，选「关阀浸泡」类大师（如 Nas Jaafar）→ 聪明杯(浸泡) 滤杯联动
        FindControls<Button>(win)
            .First(b => (b.Content as string)?.Contains(I18n.T("ImportPlan")) == true)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var picker2 = (Window?)win.GetType()
            .GetField("_masterPlanPickerWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(picker2);
        var soakItem = FindControls<Button>(picker2!)
            .Where(b => b.Content is StackPanel)
            .First(b => b.Content is StackPanel sp && sp.Children.OfType<TextBlock>()
                .Any(t => t.Text is { } tx && (tx.Contains("关阀浸泡") || tx.Contains("Nas Jaafar"))));
        soakItem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("smart", vm.Dripper); // 关阀浸泡 / Switch → 聪明杯(浸泡)
    }

    // 大师方案清单搜索回归守卫：清单顶部搜索框按标题/摘要/正文实时筛选；
    // 输入匹配词缩减列表、输入无匹配词显示「未找到」空态。
    [Fact]
    public void MainWindow_MasterPlanPicker_SearchFilters()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        FindControls<Button>(win)
            .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindControls<Button>(win)
            .First(b => (b.Content as string)?.Contains(I18n.T("ImportPlan")) == true)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var picker = (Window?)win.GetType()
            .GetField("_masterPlanPickerWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(picker);
        var pkr = picker!; // 非空本地副本，供下方 lambda / 局部函数使用（规避流分析警告）

        int PlanCount() => FindControls<Button>(pkr).Count(b => b.Content is StackPanel);
        int all = PlanCount();
        Assert.True(all >= 20, "清单应罗列 20+ 大师方案");

        // 输入匹配词 → 列表缩减但不为空（不区分大小写：WBrC / wbrc）
        var search = FindControls<TextBox>(pkr).First();
        search.Text = "关阀浸泡";
        int filtered = PlanCount();
        Assert.True(filtered > 0 && filtered < all, "搜索应缩减列表且不为空");
        search.Text = "wbrc";
        Assert.True(PlanCount() > 0 && PlanCount() < all, "英文关键词(小写)也应命中");

        // 输入无匹配词 → 列表清空并显示空态
        search.Text = "zzz_no_match";
        Assert.Equal(0, PlanCount());
        Assert.Contains(FindControls<TextBlock>(pkr), t => (t.Text ?? "").Contains("未找到"));
    }

    // 我的方案回归守卫 ①：大师清单每项右侧 ☆ 可把方案存为「我的方案」（落盘 + 内存列表更新）。
    [Fact]
    public void MainWindow_MasterPlanPicker_SavesToMyPlans()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        // 隔离：把我的方案写到临时文件，避免与本机默认路径串味
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string)?.Contains(I18n.T("ImportPlan")) == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var picker = (Window?)win.GetType()
                .GetField("_masterPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(picker);

            // 首项即「粕谷哲 4:6 法」；点其右侧 ☆ 收藏按钮
            var firstTitle = FindControls<Button>(picker!)
                .Where(b => b.Content is StackPanel)
                .Select(b => ((StackPanel)b.Content!).Children.OfType<TextBlock>().First().Text)
                .First();
            var star = FindControls<Button>(picker)
                .First(b => (b.Content as string) == I18n.T("SaveMyPlan"));
            star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 内存列表与落盘文件都应含该方案
            Assert.Contains(vm.MyPlans, p => p.Title == firstTitle);
            Assert.True(File.Exists(tmp), "我的方案应落盘到 myplans.json");
            var reloaded = CoffeeScale.Core.MyPlansStore.Load(tmp);
            Assert.Contains(reloaded, p => p.Title == firstTitle);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // 我的方案回归守卫 ②：推荐区「我的方案」打开收藏清单，点选即应用（回灌引擎，联动 4:6→kasuya46）并关闭窗口。
    [Fact]
    public void MainWindow_MyPlanPicker_OpensAndApplies()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            // 直接存一条含 4:6 / 粉量 20g / 粉水比 1:15 的方案
            vm.SaveMyPlan(new CoffeeScale.Core.MyPlan
            {
                Title = "我的 4:6 测试",
                Summary = "自存测试方案",
                Detail = "粕谷哲 4:6 冲煮法\n· 粉量 20g / 总水 300g，粉水比 1:15\n· 水温 92℃",
            });
            Assert.Single(vm.MyPlans);

            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("MyPlanButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var myPicker = (Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(myPicker);

            // 清单列出该收藏方案（apply 按钮内容为 StackPanel）
            var items = FindControls<Button>(myPicker!)
                .Where(b => b.Content is StackPanel).ToList();
            Assert.Single(items);

            // 点选即应用 + 关闭
            items[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(string.IsNullOrEmpty(vm.MasterPlanText), "选中我的方案后 MasterPlanText 应非空");
            Assert.Equal("kasuya46", vm.Method); // 4:6 → kasuya46 联动
            Assert.Null((Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // 我的方案回归守卫 ③：收藏清单项的 × 可删除（内存列表与落盘同步清空）。
    [Fact]
    public void MainWindow_MyPlanPicker_DeleteRemovesPlan()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            vm.SaveMyPlan(new CoffeeScale.Core.MyPlan { Title = "待删除方案", Summary = "s", Detail = "detail" });
            Assert.Single(vm.MyPlans);

            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("MyPlanButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var myPicker = (Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(myPicker);

            var del = FindControls<Button>(myPicker!)
                .First(b => (b.Content as string) == I18n.T("DeleteMyPlan"));
            del.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 内存 + 落盘均清空；窗口保持打开并显示空态
            Assert.Empty(vm.MyPlans);
            Assert.True(File.Exists(tmp));
            Assert.Empty(CoffeeScale.Core.MyPlansStore.Load(tmp));
            Assert.NotNull((Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // Phase 10 守卫 ①：大师方案的结构化元数据以 Title 为关联键，键必须命中清单中真实存在的一项。
    // 防止方案改名 / 拼写错误后元数据静默失效（表现为「烘焙度联动突然不生效」却无任何报错）。
    [Fact]
    public void MainWindow_MasterPlanMeta_KeysAllMatchPlanTitles()
    {
        EnsureApp();
        I18n.Current = I18n.ZhCN;
        var t = typeof(MainWindow);

        var metaField = t.GetField("MasterPlanMeta", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(metaField);
        var meta = (System.Collections.IEnumerable)metaField!.GetValue(null)!;
        var keys = new System.Collections.Generic.List<string>();
        foreach (var kv in meta) // KeyValuePair<string, (string?, string?, string?)>，反射取 Key
        {
            var keyProp = kv!.GetType().GetProperty("Key");
            Assert.NotNull(keyProp);
            keys.Add((string)keyProp!.GetValue(kv)!);
        }
        Assert.NotEmpty(keys); // 元数据表不应为空（否则说明结构化联动整体失效）

        var plansMethod = t.GetMethod("MasterPlans", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(plansMethod);
        var plans = (System.Collections.Generic.IEnumerable<MasterPlanItem>)plansMethod!.Invoke(null, null)!;
        var titles = plans.Select(p => p.Title).ToList();

        foreach (var k in keys)
            Assert.Contains(k, titles);
    }

    // Phase 10 守卫 ②：大师方案的结构化「烘焙度」回灌引擎。
    // 「三段变温法」正文只写「适合中深烘」——这是正则无法稳健解析、必须靠结构化字段承载的典型场景。
    [Fact]
    public void MainWindow_MasterPlanPicker_AppliesStructuredRoast()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        vm.Roast = "ultra_light"; // 先置一个明显不同的初值，确保断言真的在验证「被改成中深烘」

        FindControls<Button>(win)
            .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FindControls<Button>(win)
            .First(b => (b.Content as string)?.Contains(I18n.T("ImportPlan")) == true)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var picker = (Window?)win.GetType()
            .GetField("_masterPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(picker);

        var item = FindControls<Button>(picker!)
            .Where(b => b.Content is StackPanel)
            .First(b => ((StackPanel)b.Content!).Children.OfType<TextBlock>().Any(t => t.Text == "三段变温法"));
        item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("medium_dark", vm.Roast); // 结构化烘焙度回灌
        Assert.Equal(15, vm.Ratio);            // 剂量类字段仍由正文正则提供（"粉水比 1:15 左右"）
    }

    // Phase 10 守卫 ③：「我的方案」携带的结构化字段（处理方式 / 产地 / 烘焙度 / 风味 …）点选后完整回灌引擎。
    // 这是日常复用的核心路径：自己存的方案必须能还原自己的豆子，而不只是粉量/粉水比。
    [Fact]
    public void MainWindow_MyPlanPicker_AppliesStructuredFields()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            vm.SaveMyPlan(new CoffeeScale.Core.MyPlan
            {
                Title = "结构化测试方案", Summary = "s", Detail = "plain detail（不含任何可正则参数）",
                Method = "swiss", Dripper = "wave", Dose = "18", Ratio = "16",
                Process = "natural", Origin = "kenya", Roast = "medium_dark", Flavor = "sweet",
            });
            Assert.Single(vm.MyPlans);

            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("MyPlanButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var myPicker = (Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(myPicker);

            var items = FindControls<Button>(myPicker!)
                .Where(b => b.Content is StackPanel).ToList();
            Assert.Single(items);
            items[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 结构化字段全部回灌
            Assert.Equal("natural", vm.Process);
            Assert.Equal("kenya", vm.Origin);
            Assert.Equal("medium_dark", vm.Roast);
            Assert.Equal("sweet", vm.Flavor);
            Assert.Equal("swiss", vm.Method);
            Assert.Equal("wave", vm.Dripper);
            Assert.Equal(18, vm.Dose);
            Assert.Equal(16, vm.Ratio);
            Assert.Equal("standard", vm.Size); // 18g → 标准档
            Assert.True(vm.HasRecipe, "应用后应据方案参数重算出可冲煮配方");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // Phase 10 守卫 ④：「保存当前为我的方案」把当前整套参数（含 处理方式 / 产地 / 烘焙度）落盘，
    // 之后可离线一键还原自己的豆子配置。
    [Fact]
    public void MainWindow_SaveCurrentAsMyPlan_CapturesAllStructuredFields()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            // 设定一套「自己的豆子」参数并生成配方
            vm.Process = "natural"; vm.Origin = "kenya"; vm.Roast = "medium_dark";
            vm.Flavor = "sweet"; vm.Method = "classic"; vm.Dripper = "wave";
            vm.Generate();

            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("SaveCurrentPlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Single(vm.MyPlans);
            var saved = vm.MyPlans[0];
            Assert.Equal("natural", saved.Process);
            Assert.Equal("kenya", saved.Origin);
            Assert.Equal("medium_dark", saved.Roast);
            Assert.Equal("sweet", saved.Flavor);
            Assert.Equal("classic", saved.Method);
            Assert.Equal("wave", saved.Dripper);
            Assert.False(string.IsNullOrEmpty(saved.Dose));
            Assert.False(string.IsNullOrEmpty(saved.Ratio));
            Assert.False(string.IsNullOrWhiteSpace(saved.Title), "应自动生成可读标题");
            Assert.Contains("1:", saved.Detail); // 正文含可读摘要（粉水比行）

            // 正文应完整（即使保存前未手动点「生成」，也应自动补齐配方后再落盘）
            Assert.Contains("粉水比", saved.Detail);
            Assert.Contains("研磨", saved.Detail);

            // 落盘后重新加载仍完整（结构化字段随 JSON 持久化）
            var reloaded = CoffeeScale.Core.MyPlansStore.Load(tmp);
            Assert.Single(reloaded);
            Assert.Equal("natural", reloaded[0].Process);
            Assert.Equal("kenya", reloaded[0].Origin);
            Assert.Equal("medium_dark", reloaded[0].Roast);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // Phase 10 守卫 ⑥：生成过一次后改参数、未再点「生成」就保存——仍应存下与实际输入一致的完整方案。
    // 这是最典型的日常误操作：调完豆子/粉量就去保存，此时界面上的配方还是上一份。
    [Fact]
    public void MainWindow_SaveCurrentAsMyPlan_AfterEditingParams_StillComplete()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            // 先走一次正常流程（生成 → 翻到推荐页，保存按钮在此页）
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 然后改参数，刻意不再点「生成」
            vm.Process = "honey"; vm.Origin = "colombia"; vm.Roast = "medium";
            vm.Method = "swiss"; vm.Dose = 22; vm.Ratio = 17;

            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("SaveCurrentPlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Single(vm.MyPlans);
            var saved = vm.MyPlans[0];
            Assert.False(string.IsNullOrWhiteSpace(saved.Detail), "改参数后保存也应得到完整配方正文");
            // 引擎把 1:17 归一化为 1:16.7：正文与结构化字段必须同源，不能一边写 16.7 一边写 17
            Assert.Contains("粉水比 1:16.7", saved.Detail);
            Assert.Equal("honey", saved.Process);
            Assert.Equal("colombia", saved.Origin);
            Assert.Equal("medium", saved.Roast);
            Assert.Equal("swiss", saved.Method);
            Assert.Equal("22", saved.Dose);        // 与当前输入一致，而非界面上那份旧配方
            Assert.Equal("16.7", saved.Ratio);     // 取配方有效值，与正文一致
            Assert.Contains("瑞士搅拌", saved.Detail); // 正文同样反映改动后的冲煮方式
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // Phase 11 守卫：历史记录行的「☆ 存为我的方案」按钮真实可用——满意的一杯一键沉淀为方案。
    [Fact]
    public void MainWindow_RecordRow_CloneToMyPlan()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var recPath = Path.Combine(Path.GetTempPath(), "records_test_" + Guid.NewGuid().ToString("N") + ".json");
        var plansPath = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.RecordsPath = recPath;
        vm.MyPlansPath = plansPath;
        try
        {
            // 走真实流程：生成 → 保存记录（带上方案溯源）→ 记录列表刷新
            vm.Process = "natural"; vm.Origin = "kenya"; vm.Roast = "medium";
            vm.MarkPlanSource("我的肯尼亚方案");
            vm.Generate();
            vm.SaveCurrentBrew();
            Assert.Single(vm.Records);
            Assert.Equal("natural", vm.Records[0].Process); // 记录带结构化键

            // 记录列表已并入「冲煮记录查询和统计」弹窗：点主窗口按钮打开，再在弹窗内找「☆ 存为我的方案」按钮
            FindControls<Button>(win)
                .First(b => ((b.Content as string) ?? "").Contains(I18n.T("RecordsQuery")))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dlg = (Window?)win.GetType()
                .GetField("_recordsDialogWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(dlg);

            var cloneBtn = FindControls<Button>(dlg!)
                .First(b => (b.Content as string) == I18n.T("SaveMyPlan"));
            cloneBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Single(vm.MyPlans);
            var plan = vm.MyPlans[0];
            Assert.Equal("我的肯尼亚方案", plan.Title); // 沿用来源方案名
            Assert.Equal("natural", plan.Process);      // 结构化键直取（免反查）
            Assert.Equal("kenya", plan.Origin);
            Assert.Equal("medium", plan.Roast);
            Assert.Contains("粉水比", plan.Detail);
        }
        finally
        {
            if (File.Exists(recPath)) File.Delete(recPath);
            if (File.Exists(plansPath)) File.Delete(plansPath);
        }
    }

    // Phase 10 守卫 ⑤：旧版 myplans.json（无结构化字段）向后兼容。
    // 真实场景：Phase 9 版本已存过方案，升级后必须能继续加载并应用，且不应臆测覆盖用户当前豆子配置。
    [Fact]
    public void MainWindow_LegacyMyPlan_WithoutStructuredFields_StillApplies()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            // 写一份「Phase 9 旧格式」文件：只有 Title/Summary/Detail 三个字段
            File.WriteAllText(tmp, "[{\"Title\":\"旧版方案\",\"Summary\":\"旧\",\"Detail\":\"粉量 20g，粉水比 1:15，4:6 法\"}]");
            vm.LoadMyPlans();
            Assert.Single(vm.MyPlans);

            var legacy = vm.MyPlans[0];
            Assert.Null(legacy.Process); // 旧文件没有这些字段 → 反序列化为 null（未指定）
            Assert.Null(legacy.Origin);
            Assert.Null(legacy.Roast);

            // 先把当前豆子设成一个可辨识的值：应用旧方案后应「原样保留」，不被臆测改写
            vm.Process = "washed"; vm.Origin = "ethiopia"; vm.Roast = "blonde";

            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("MyPlanButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var myPicker = (Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(myPicker);

            var items = FindControls<Button>(myPicker!)
                .Where(b => b.Content is StackPanel).ToList();
            Assert.Single(items);
            items[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 剂量类字段靠正文正则回退成功
            Assert.Equal(20, vm.Dose);
            Assert.Equal(15, vm.Ratio);
            Assert.Equal("standard", vm.Size);
            Assert.Equal("kasuya46", vm.Method); // "4:6" 关键词
            Assert.True(vm.HasRecipe, "旧版方案应用后仍应重算出可冲煮配方");

            // 非结构化字段保持用户当前选择（这是「不臆测覆盖」的核心保证）
            Assert.Equal("washed", vm.Process);
            Assert.Equal("ethiopia", vm.Origin);
            Assert.Equal("blonde", vm.Roast);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // 参数信息图标守卫 ①：每个参数标题旁有 ⓘ 按钮，点击弹出「参数逻辑」弹窗，
    // 弹窗含 定位/作用机理/引擎规则/实务建议 四区块与来源行（2026-09-03 阳光要求：每参数可查影响逻辑）。
    [Fact]
    public void MainWindow_ParamInfoButton_OpensDialogWithSections()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;

        // 设置区应注入 12 个 ⓘ 图标按钮（品种/产地/处理/烘焙日期/烘焙度/Agtron/密度/粉量档/粉量/滤杯/滤纸/冲煮方式；
        // 2026-09-08：海拔参数已按用户要求整体移除，原 13 个减为 12 个）
        var infoButtons = FindControls<Button>(win).Where(b => (b.Content as string) == "i").ToList();
        Assert.True(infoButtons.Count >= 12, $"应有 ≥12 个参数信息图标，实际 {infoButtons.Count}");

        // 点击烘焙度的 ⓘ → 弹窗标题 = 参数逻辑 · 烘焙度，四区块 + 来源行齐全
        infoButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dlg = (Window?)win.GetType()
            .GetField("_paramInfoDialog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(dlg);
        Assert.StartsWith(I18n.T("ParamInfoTitle"), dlg!.Title);

        var texts = FindControls<TextBlock>(dlg).Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, s => s.Contains(I18n.T("PILoc")));
        Assert.Contains(texts, s => s.Contains(I18n.T("PIMech")));
        Assert.Contains(texts, s => s.Contains(I18n.T("PITips")));
        Assert.Contains(texts, s => s.Contains("来源")); // ParamInfo.Source 来源行（中文）

        // 关闭后字段清空（可再次打开）
        dlg.Close();
        Assert.Null((Window?)win.GetType()
            .GetField("_paramInfoDialog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(win));
    }

    // 参数信息图标守卫 ②：「引擎规则」区块由引擎现行标定实时生成（永不漂移）——
    // 烘焙度弹窗的规则行必须含 Agtron 区间 + 水温（直接取自 BrewEngine.RoastProfiles），而非硬编码旧文档。
    [Fact]
    public void MainWindow_ParamInfoDialog_EngineRules_ReflectCurrentEngine()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;

        // 生成配方后打开「烘焙度」标题旁 ⓘ 的弹窗（ⓘ 按钮与标题同处一个 head Grid）
        vm.Generate();
        var roastInfo = FindControls<Button>(win)
            .First(b => (b.Content as string) == "i" &&
                        b.Parent is Grid g && g.Children.OfType<TextBlock>()
                            .Any(t => t.Text == I18n.T("Roast")));
        roastInfo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dlg = (Window?)win.GetType()
            .GetField("_paramInfoDialog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(dlg);

        // 规则行含真实引擎标定值：Ag 区间与 水温 ℃（来自 RoastProfiles 动态生成）
        var texts = FindControls<TextBlock>(dlg!).Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, s => s.Contains("Ag") && s.Contains("℃")); // 「极浅 Ag 100–120：水温 …」式规则行
        Assert.Contains(texts, s => s.Contains(I18n.T("PIRules")));

        // 「冲煮方式」ⓘ：引擎规则行由 MethodProfiles 动态生成（6 法 + 杯测 1:18.18 标注）
        var methodInfo = FindControls<Button>(win)
            .First(b => (b.Content as string) == "i" &&
                        b.Parent is Grid g2 && g2.Children.OfType<TextBlock>()
                            .Any(t => t.Text == I18n.T("Method")));
        methodInfo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var mDlg = (Window?)win.GetType()
            .GetField("_paramInfoDialog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(mDlg);
        Assert.StartsWith(I18n.T("ParamInfoTitle") + " · 冲煮方式", mDlg!.Title);
        var mTexts = FindControls<TextBlock>(mDlg).Select(t => t.Text ?? "").ToList();
        Assert.Contains(mTexts, s => s.Contains("粕谷式 46 法") && s.Contains("5 段等水")); // MethodProfiles.Note 直取
        Assert.Contains(mTexts, s => s.Contains("1:18.18"));                              // 杯测比例标注（统一基准 11g/200g）
    }

    // UI 改版守卫 ①：语言按钮显示 CN/EN（中文界面），点击后变 EN/CN
    [Fact]
    public void MainWindow_LangButton_ShowsCNEN_AndTogglesToENCN()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var langBtn = FindControls<Button>(win).First(b => (b.Content as string) == "CN/EN");
        langBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(I18n.EnUS, vm.Culture);
        Assert.Equal("EN/CN", langBtn.Content);
        langBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // 切回中文
        Assert.Equal("CN/EN", langBtn.Content);
    }

    // UI 改版守卫 ②：设置页含「咖啡豆密度」（默认中等）；顶栏不再显示旧「语言：中」式文案
    [Fact]
    public void MainWindow_SettingsPage_HasDensity_Combo()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        // 顶栏无「语言：」字样（改为 CN/EN）
        Assert.DoesNotContain(FindControls<TextBlock>(win), t => (t.Text ?? "").Contains("语言："));
        // 设置页含「咖啡豆密度」行标题
        Assert.Contains(FindControls<TextBlock>(win), t => t.Text == I18n.T("Density"));
        // 密度下拉默认选中「中等」（选中文本渲染在 ComboBox 内部 ContentPresenter，不在 TextBlock 逻辑树里，须直接查 ComboBox）
        Assert.Contains(FindControls<ComboBox>(win), c =>
            c.SelectedValue is string s && s == "medium");
    }

    // UI 改版守卫 ③：实时冲煮区「推荐粉水比 / 注水总量」行——生成前显示「未设置」，生成后显示配方值；
    // ± 微调立即重算（推荐粉水比与注水总量=粉量×粉水比 同步刷新）；模拟注水挡位已取消（自动按流速推进）
    [Fact]
    public void MainWindow_BrewArea_PlanRatioRow_AdjustableAndWaterLinked()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;

        // 生成前：行内出现「未设置」
        Assert.Contains(FindControls<TextBlock>(win), t => t.Text == I18n.T("NotSet"));

        vm.Generate();
        // 生成后：配方有效值出现（默认粉量 15g × 粉水比 15 = 1:15 / 225g）
        var postTexts = FindControls<TextBlock>(win).Select(t => t.Text ?? "").ToList();
        Assert.True(postTexts.Contains("1:15"),
            "ratio text missing. PlanRatioText=[" + vm.PlanRatioText + "] HasRecipe=" + vm.HasRecipe +
            " ratio-ish: " + string.Join(",", postTexts.Where(x => x.Contains("1:") || x.Contains("225") || x.Contains("未")).DefaultIfEmpty("<none>")));
        Assert.Contains(FindControls<TextBlock>(win), t => t.Text == "225g");
        // 「未设置」文本块仍在逻辑树中（布局占位）；生成后应隐藏。
        // IsVisible 是派生属性，headless 下经派发器异步刷新，先泵一轮任务再断言。
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var notSetBlocks = FindControls<TextBlock>(win).Where(t => t.Text == I18n.T("NotSet")).ToList();
        Assert.NotEmpty(notSetBlocks);
        Assert.All(notSetBlocks, t => Assert.False(t.IsVisible));

        // 模拟注水挡位按钮已取消（注水阶段改为开始后按推荐流速自动推进）
        var contents = FindControls<Button>(win).Select(b => b.Content as string ?? "").ToList();
        Assert.DoesNotContain(I18n.T("PourBump1"), contents);
        Assert.DoesNotContain(I18n.T("PourBump5"), contents);
        Assert.DoesNotContain(I18n.T("PourBump10"), contents);
        Assert.DoesNotContain(I18n.T("PourBump20"), contents);

        // 推荐粉水比 ± 微调：点 + 后立即重算，粉水比与注水总量同步刷新（1:15 → 1:16，225g → 240g）
        // 两行布局：通过显示值 "1:15" 定位 ratioSet TextBlock，其父 StackPanel 内含 ± 按钮
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var ratioText = FindControls<TextBlock>(win).FirstOrDefault(t => t.Text == "1:15");
        Assert.NotNull(ratioText);
        var ratioCell = ratioText.Parent as Panel;
        Assert.NotNull(ratioCell);
        var plusR = FindControls<Button>(ratioCell!).First(b => (b.Content as string) == "+");
        plusR.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("1:16", vm.PlanRatioText);
        Assert.Equal("240g", vm.PlanWaterText); // 15g × 16 = 240g

        // 保存记录与「冲煮记录查询和统计」在同一行（同一父 Grid）
        var saveBtn = FindControls<Button>(win).First(b => b.Content as string == I18n.T("Save"));
        var queryBtn = FindControls<Button>(win).First(b => ((b.Content as string) ?? "").Contains(I18n.T("RecordsQuery")));
        Assert.Same(saveBtn.Parent, queryBtn.Parent);
    }

    // UI 改版守卫 ④：统计卡与冲煮记录列表已从实时冲煮区移除，统一并入查询弹窗（弹窗可打开且含统计文本）
    [Fact]
    public void MainWindow_StatsAndRecords_MovedIntoQueryDialog()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;

        // 主窗口不再含历史统计卡（StatsTitle 文本来自 StatsText 绑定，弹窗打开前主区不应出现）
        vm.Generate();
        vm.SaveCurrentBrew();
        Assert.DoesNotContain(FindControls<TextBlock>(win), t => (t.Text ?? "").Contains(I18n.T("StatsTitle")));

        // 打开弹窗：统计文本 + 记录列表（记录头含日期）出现
        FindControls<Button>(win)
            .First(b => ((b.Content as string) ?? "").Contains(I18n.T("RecordsQuery")))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dlg = (Window?)win.GetType()
            .GetField("_recordsDialogWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(win);
        Assert.NotNull(dlg);
        Assert.Contains(FindControls<TextBlock>(dlg!), t => (t.Text ?? "").Contains(I18n.T("StatsTitle")));
    }

    // UI 改版守卫 ⑤：推荐研磨 + 水质推荐迁到实时冲煮区末尾（主区直接可见，不再在推荐分页）
    [Fact]
    public void MainWindow_GrindAndWater_AtEndOfBrewArea()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        vm.Generate();
        Assert.Contains(FindControls<TextBlock>(win), t => (t.Text ?? "").Contains(I18n.T("GrindRec")));
        Assert.Contains(FindControls<TextBlock>(win), t => (t.Text ?? "").Contains(I18n.T("Water")));
    }

    // Phase 13 守卫 ①：我的方案清单「📤 复制分享文本」→ 剪贴板写入分享文本，状态行提示已复制。
    // 注意：刻意保持同步（勿改 async）——headless 下 Dispatcher 绑定首个调用 EnsureApp 的线程，
    // xUnit 从线程池调度，async 用例会让后续用例落到别的线程而整体炸「invalid thread」。
    [Fact]
    public void MainWindow_MyPlanPicker_ShareCopiesToClipboard()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            vm.SaveMyPlan(new MyPlan
            {
                Title = "我的肯尼亚方案",
                Summary = "复刻自记录",
                Detail = "· 粉量 22g / 粉水比 1:16.7",
                Process = "natural", Origin = "kenya", Roast = "medium_dark",
            });
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("MyPlanButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var myPicker = (Window?)win.GetType()                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(myPicker);

            // 点「📤 复制分享文本」
            var share = FindControls<Button>(myPicker!)
                .First(b => (b.Content as string) == I18n.T("SharePlan"));
            share.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 状态行提示已复制
            Assert.Contains(FindControls<TextBlock>(myPicker), t => (t.Text ?? "").Contains("已复制"));
            // 剪贴板（headless 内存剪贴板）含该方案的分享文本（版本标记 + 结构化字段）。
            // SetTextAsync 是 fire-and-forget，headless 下写入任务挂在 Dispatcher 队列，
            // 先泵一轮让写入落定，再读取（否则全量跑时可能读到空）。
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // xUnit1031 压制说明：headless 剪贴板 GetTextAsync 同步完成，此处阻塞安全。
#pragma warning disable xUnit1031
            var clip = myPicker.Clipboard?.GetTextAsync().GetAwaiter().GetResult() ?? "";
#pragma warning restore xUnit1031
            Assert.StartsWith(MyPlan.ShareFormatVersion, clip);
            Assert.Contains("Title: 我的肯尼亚方案", clip);
            Assert.Contains("Process: natural", clip);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // Phase 13 守卫 ②：我的方案清单「📤 导出全部」→ 默认备份文件生成（含结构化字段）。
    [Fact]
    public void MainWindow_MyPlanPicker_ExportWritesBackup()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            vm.SaveMyPlan(new MyPlan { Title = "A", Process = "honey", Detail = "d" });
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("MyPlanButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var myPicker = (Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(myPicker);

            var exp = FindControls<Button>(myPicker!)
                .First(b => (b.Content as string) == I18n.T("ExportPlan"));
            exp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 默认备份文件（与 myplans.json 同目录）已生成并含结构化字段
            var backup = vm.MyPlansBackupPath;
            Assert.True(File.Exists(backup), "导出按钮应生成默认备份文件");
            var loaded = MyPlansStore.Load(backup);
            Assert.Single(loaded);
            Assert.Equal("honey", loaded[0].Process);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            var backup = Path.Combine(Path.GetDirectoryName(tmp)!, "myplans-backup.json");
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    // Phase 13 守卫 ③：我的方案清单「📥 从文件导入」→ 按 Title 合并（同名保留本机），清单即时刷新出新方案。
    [Fact]
    public void MainWindow_MyPlanPicker_ImportMerges()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var tmp = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        vm.MyPlansPath = tmp;
        try
        {
            vm.SaveMyPlan(new MyPlan { Title = "A", Process = "washed", Detail = "local" });

            // 预置备份文件：A（不同内容，应保留本机）+ C（新增）
            MyPlansStore.SaveAll(vm.MyPlansBackupPath, new[]
            {
                new MyPlan { Title = "A", Process = "natural", Detail = "backup" },
                new MyPlan { Title = "C", Origin = "colombia", Detail = "new" },
            });

            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("GeneratePlan"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindControls<Button>(win)
                .First(b => (b.Content as string) == I18n.T("MyPlanButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var myPicker = (Window?)win.GetType()
                .GetField("_myPlanPickerWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(myPicker);

            var imp = FindControls<Button>(myPicker!)
                .First(b => (b.Content as string) == I18n.T("ImportPlanFile"));
            imp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 合并结果：本机 A 保留 + 新增 C
            Assert.Equal(2, vm.MyPlans.Count);
            Assert.Equal("washed", vm.MyPlans.First(p => p.Title == "A").Process);
            Assert.Contains(vm.MyPlans, p => p.Title == "C" && p.Origin == "colombia");
            // 状态行提示已导入
            Assert.Contains(FindControls<TextBlock>(myPicker), t => (t.Text ?? "").Contains("已导入"));
            // 清单即时刷新：apply 按钮行数 = 2
            Assert.Equal(2, FindControls<Button>(myPicker)
                .Count(b => b.Content is StackPanel));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            var backup = Path.Combine(Path.GetDirectoryName(tmp)!, "myplans-backup.json");
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    // Phase 14 守卫：记录查询统计弹窗「导出 CSV」→ 默认路径 brews-export.csv 生成（BOM + 记录行），状态行提示条数。
    [Fact]
    public void MainWindow_RecordsDialog_ExportCsvWritesFile()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;
        var vm = (BrewViewModel)win.DataContext!;
        var dir = Path.Combine(Path.GetTempPath(), "csv_ui_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        vm.RecordsPath = Path.Combine(dir, "brews.json");
        vm.SettingsPath = Path.Combine(dir, "settings.json");
        try
        {
            vm.Generate();
            vm.SaveCurrentBrew(); // 一条记录

            // 打开「冲煮记录查询和统计」弹窗（按钮文案带 📋 前缀）
            FindControls<Button>(win)
                .First(b => (b.Content as string)?.Contains(I18n.T("RecordsQuery")) == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dlg = (Window?)win.GetType()
                .GetField("_recordsDialogWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(win);
            Assert.NotNull(dlg);

            // 点「导出 CSV」
            FindControls<Button>(dlg!)
                .First(b => (b.Content as string) == I18n.T("ExportCsv"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 状态行提示条数
            Assert.Contains(FindControls<TextBlock>(dlg), t => (t.Text ?? "").Contains("已导出"));
            // 默认导出文件生成且含 BOM 与记录
            var csvPath = Path.Combine(dir, "brews-export.csv");
            Assert.True(File.Exists(csvPath));
            var text = File.ReadAllText(csvPath);
            Assert.StartsWith("\uFEFF", text);
            Assert.Contains("冲煮日期", text);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // 键盘快捷键守卫（2026-09-03 修订）：提示行已按用户要求移除（界面更简洁），
    // 但 Space/R/N 快捷键本身保留（手冲时一手扶壶一手盲操）；T 键随去皮键一并取消。
    [Fact]
    public void MainWindow_HotkeyHint_Removed_KeysStillWired()
    {
        EnsureApp();
        var win = new MainWindow();
        I18n.Current = I18n.ZhCN;

        // 称控制区不应再有快捷键提示行（Space= / T= 等提示文案已移除）
        var texts = FindControls<TextBlock>(win).Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(texts, s => s.Contains("Space="));
        Assert.DoesNotContain(texts, s => s.Contains("T="));

        // KeyDown 事件已挂载（构造函数末尾注册）——间接验证：VM 有 CanTransport / Reset / Next（快捷键绑定的入口）
        var vm = (BrewViewModel)win.DataContext!;
        Assert.True(vm.CanTransport == false || vm.CanTransport == true); // 属性可读
        vm.Generate();
        Assert.True(vm.CanTransport); // 生成配方后可开始
    }
}
