using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CoffeeScale.Core;
using CoffeeScale.ViewModels;

namespace CoffeeScale.UI;

// Android/iOS 单视图后端不支持 Window 构造（WindowingPlatformStub.CreateWindow 抛
// NotSupportedException），故移动端编译为 UserControl；桌面端仍为 Window。
#if ANDROID || IOS || MACCATALYST
public partial class MainWindow : UserControl
#else
public partial class MainWindow : Window
#endif
{
    private readonly BrewViewModel _vm = new();
    private readonly DockPanel _root = new();
    private readonly Border _safeBorder = new(); // 移动端安全区承载层（Border.Padding 消化状态栏/手势条 inset）
    private readonly Border _settingsHost = new();
    private readonly Border _brewHost = new();
    private ItemsControl _recordsList = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private Button? _transportBtn; // 称控制主运输圆形键（颜色/文案随 VM.TransportState 联动，避免绑定与按下反馈冲突）
    private Grid _bodyGrid = new(); // 顶栏之下的主体（左设置 / 右冲煮）
    private StackPanel _titleAuthor = new(); // 顶栏左侧分组（标题 + 作者）；提升为字段以在语言切换时刷新文本
    private TextBlock _titleBlock = null!;   // 顶栏软件名文本块
    private TextBlock _authorBlock = null!;  // 顶栏作者文本块（保留原文，不翻译）

    // —— 四模块入口主界面（Home）：新手 / 专业 / 简易计算器 / 手冲大师方案 ——
    private Border? _homeHost;        // Home 层（覆盖在主体之上，IsVisible 切换进出）
    private Button? _homeBackBtn;     // 顶栏 ⌂ 返回主界面按钮（仅进入专业模式后显示）
    private bool _onHome = true;      // 当前是否在入口主界面（启动默认显示 Home）
    private bool _homeCompact = true; // Home 上次构建时的布局形态（紧凑↔宽屏变化时重建卡片网格）
    private bool _homeShort = false;  // Home 宽屏但高度矮（手机横屏/矮窗口）：Hero 与卡片压缩排版

    // —— 简易咖啡计算器（Home 第三模块，独立覆盖层）——
    private Border? _calcHost;                 // 计算器层（覆盖在 Home 之上，IsVisible 切换）
    private bool _calcOpen;                    // 计算器是否打开
    private bool _calcCompact = true;          // 上次构建时的布局形态
    private bool _calcShort = false;           // 宽屏但高度矮（手机横屏）：压缩排版
    private bool _calcUpdating;                // 程序化赋值 Text 时抑制 TextChanged 重入
    private TextBox? _calcDoseBox, _calcRatioBox, _calcWaterBox;   // 粉量 / 比值 N / 总注水量
    private TextBlock? _calcWarnText;                              // 范围校验提示（无违规时隐藏）
    private System.Diagnostics.Stopwatch? _calcSw;                 // 计时秒表（毫秒精度）
    private readonly List<double> _calcSegs = new();               // 已结束分段的时长（ms）
    private double _calcSegStartMs;                                // 当前分段起点（秒表毫秒）
    private bool _calcRunning;                                     // 计时进行中
    private StackPanel? _calcTimerList;                            // 计时清单容器
    private TextBlock? _calcLiveText;                              // 当前进行中分段的实时文本
    private DispatcherTimer? _calcTick;                            // 实时刷新（50ms）
    private Button? _calcStartBtn, _calcNextBtn, _calcStopBtn;
    private TextBlock? _calcRingText;                              // 表盘下方状态文字（待启动 / 计时中 / 已停止）
    private TextBlock? _calcTotalText;                             // 右侧电子秒表读数（总耗时 mm:ss.ff）
    private double _calcRingD;                                     // 表盘实际直径（构建时按 _scale 计算，供指针重绘复用）
    private Avalonia.Controls.Shapes.Line? _calcRingHand;          // 机械秒表：大秒针（60 秒一圈）
    private Avalonia.Controls.Shapes.Line? _calcRingMinHand;       // 机械秒表：小分针（30 分钟一圈）

    // —— 新手模式（Home 第一模块，独立覆盖层，单屏引导；专业模式的简化版）——
    private Border? _beginnerHost;                 // 新手模式层（覆盖在 Home 之上）
    private bool _begOpen;
    private string? _begDripper, _begRoast;        // 已选滤杯 / 烘焙度 key
    // —— 新手模式选择区折叠：选齐后自动收起滤杯/滤纸/烘焙度，点击摘要可再展开 ——
    private bool _begSelCollapsed;                 // 选择区是否已折叠
    private bool _begSelUserExpanded;              // 用户是否手动展开过（避免改选时反复自动收起）
    private Recipe? _begRecipe;                   // 引擎生成的建议配方
    private EngineState? _begState;               // 冲煮模拟运行时状态
    private DispatcherTimer? _begTick;            // 模拟冲煮实时刷新（100ms）
    private double _begSimMs;                      // 模拟累计时长（ms，自增时钟，便于 headless 步进）
    private double _begSimWeight;                 // 模拟当前累计注水量（g）
    private WrapPanel? _begDripperChips, _begRoastChips;
    private StackPanel? _begPhaseList, _begFilterPanel;
    private Border? _begSuggestCard;              // ③ 建议卡（选齐后显示）
    private Border? _begBrewPanel;                // 冲煮模拟面板（开始模拟后显示，替代建议卡）
    private TextBlock? _begPhaseText, _begTimerText, _begSimHint, _begProgressText;
    private List<Avalonia.Controls.Shapes.Line>? _begRingTicks;   // 进度环刻度（按整体进度着色，规避 PathDashArray）
    private Button? _begStartBtn, _begResetBtn;
    private ProgressBar? _begPhaseBar;            // 模拟冲煮当前阶段进度条（注水/等待）
    private TextBlock? _begPhaseBarLabel;        // 进度条数值提示（当前/目标 g 或 剩余/总 s）
    // —— 主界面 Hero 光晕呼吸动画（2026-09-10）——
    private DispatcherTimer? _heroPulseTimer;     // 50ms 节拍；用 Math.Sin 平滑缩放，无需累积状态
    private TextBlock? _heroIconText;             // 持有 emoji 引用以便应用 RenderTransform
    // 咖啡大师方案（主界面入口）内联清单层：覆盖在 Home 之上，点击名字内联展开详情（不再弹窗）
    private Border? _masterListHost;
    private StackPanel? _masterListPanel;
    private TextBox? _masterListSearch;
    private string? _expandedMasterTitle;        // 当前内联展开的方案标题（手风琴：仅一个展开）
    private StackPanel? _begSelDetail;             // 选择区明细（滤杯/滤纸/烘焙度）
    private Button? _begSelSummary;               // 折叠后的紧凑摘要（点击展开）
    private Button? _begCollapseBtn;              // 明细内的「收起」按钮
    // 新手模式固定 4 款常用滤杯（简化选择）
    private static readonly string[] BegDrippers = { "v60", "origami", "wave", "chemex" };
    // 各滤杯对应的具体滤纸建议 key（通俗易懂、明确形状）
    private static readonly Dictionary<string, string> BegFilterKey = new()
    {
        ["v60"] = "BegFilterV60", ["origami"] = "BegFilterOrigami",
        ["wave"] = "BegFilterWave", ["chemex"] = "BegFilterChemex",
    };
    private const double BegRingDiam = 132;       // 进度环基准直径（逻辑像素，按 _scale 缩放）

    // —— 输入合法区间（2026-09-10 按需求锁定；越界即提示，不静默钳制）——
    private const double CalcDoseMin = 5.0, CalcDoseMax = 50.0;      // 粉量 g
    private const int CalcRatioMin = 1, CalcRatioMax = 30;           // 粉水比 1:N（整数）
    private const double CalcWaterMin = 5.0, CalcWaterMax = 1500.0;  // 总注水量 g（2026-09-10 上限由 2000 收紧为 1500）
    private const double CalcRingDiameter = 132;                     // 圆环基准直径（逻辑像素，按 _scale 缩放）

    // 左侧分页：第 1 页「冲煮方案设置」/ 第 2 页「推荐冲煮方案」
    private enum LeftPage { Settings, Recommend }
    private LeftPage _leftPage = LeftPage.Settings;
    private Border? _leftPageHost;     // 左侧内容容器：子元素为当前页（包在 ScrollViewer 内）
    private Button? _tabSettings, _tabRecommend; // 顶部两个分页标签
    private Window? _masterPlanPickerWindow;     // 「咖啡大师重铸方案清单」窗口（headless 测试可经反射访问）
    private Window? _masterPlanDetailWindow;     // 「大师方案详情」窗口（主界面 → 清单 → 详情，headless 可反射访问）
    private Window? _myPlanPickerWindow;         // 「我的方案」窗口（headless 测试可经反射访问）
    private Window? _recordsDialogWindow;        // 「冲煮记录查询和统计」弹窗（headless 测试可经反射访问）
    private Panel? _modalLayer;                  // 移动端模态浮层层（安卓/iOS 单视图不支持二级 Window，弹窗改用整屏遮罩）

    // 响应式状态：随窗口尺寸/横竖屏切换
    private bool _compact;            // 紧凑（手机竖屏）布局：PairRow 转单列、字号略缩
    private bool _pairSingle;         // 横屏双栏但列偏窄（宽<1150）：成对参数行也转单列，避免下拉框截断（2026-09-08）
    private double _scale = 1.0;      // 字号缩放系数（手机 0.92 / 平板 0.97 / 电脑 1.0）
    private const double RootMargin = 14; // 根容器内边距（移动端叠加安全区 inset）

    /// <summary>形态因子（2026-09-10 用户要求「持续改进界面适应电脑、平板、手机的直屏和横屏模式」）：
    /// Phone = 手机竖屏；PhoneLandscape = 手机横屏；Tablet = 平板（含小平板/折叠屏）；
    /// Desktop = 电脑；Wide = 超宽屏（≥1600dp，留出多列与放大空间）。由 ApplyLayout 每次尺寸变化时刷新。</summary>
    internal enum FormFactor { Phone, PhoneLandscape, Tablet, Desktop, Wide }
    private FormFactor _formFactor = FormFactor.Desktop;

    // 紧凑（手机竖屏）布局：主体左右分页（页0=设置，页1=冲煮）+ 滑动手势 + 指示器
    private enum CompactPage { Settings, Brew }
    private CompactPage _compactPage = CompactPage.Settings;
    private Grid? _compactHost;        // 紧凑容器：内含两页 (叠放, 用 IsVisible 显/隐) + 底部指示器
    private Grid? _compactStack;       // 紧凑两页叠放容器（Grid 自动铺满，隐藏页 IsVisible=false，滚动位置不丢）
    private Border? _compactPage0;     // 紧凑页0（设置）：直接引用 _settingsHost
    private Border? _compactPage1;     // 紧凑页1（冲煮）：直接引用 _brewHost
    private TextBlock? _compactIndicator; // 指示器：● 设置 ○ 冲煮（或反过来），带左右滑动提示
    private Button? _compactIndBtnLeft, _compactIndBtnRight; // 指示器两侧点击直接切
    // 滑动手势跟踪（经验405296：不用 PointerGestureRecognizer，用 PointerPressed/Moved/Released 自行计算）
    private Point _pointerStart;
    private bool _pointerTracking;
    private bool _swipeCaptured;       // 横向意图确认后捕获指针（此后移动事件改道 Canvas，ScrollViewer 不再消费）
    private const double SwipeStartX = 18; // 横向意图启动位移（像素）：超过才捕获指针进入滑页判定
    private const double SwipeMinX = 60;   // 水平最小位移（像素），超过才触发切页
    private const double SwipeMaxY = 40;   // 垂直最大位移，超过判定为纵向滚动，取消切页手势

    // 咖啡主题色板（2026-09-10 命名化重构）：使用语义命名（Palette）便于代码可读性与一致性维护
    private const string Bg = Palette.Cocoa;
    private const string Card = Palette.Espresso;
    private const string Fg = Palette.Cream;
    private const string Sub = Palette.Foam;
    private const string Accent = Palette.Caramel;
    private const string AccentBtn = Palette.Cinnamon;
    private const string Panel = Palette.RoastDeep;
    // —— 计算器/按钮辅助配色（2026-09-10）——
    private const string Warn = "#FF9C82";      // 越界提示文字 / 越界输入框描边
    private const string IconInk = "#FFF6EA";   // 图标字形色（2026-09-13）：暖白，落在模块色瓷砖上对比清晰
    private const string Ink = "#1A100A";       // 深墨（徽章文字/描边），与暖白互为正负形
    private const string BtnBlue = "#2E6FD6";   // 「下一段」启动后的蓝色（保留旧 hex 兼容视觉）
    private const string BtnMuted = "#4A3A2C";  // 按钮未激活（未启动时的「下一段」）
    private const string BtnStop = "#C0392B";   // 「停止」红色
    private const string AppVer = "1.5"; // 软件版本号（与 Android ApplicationDisplayVersion 对齐；发版前必改，见 CHANGELOG.md）

    public MainWindow()
    {
#if !ANDROID && !IOS && !MACCATALYST
        Title = I18n.T("AppTitle");
        // 咖啡杯窗口图标：作为 AvaloniaResource 嵌入 CoffeeScale.UI（avares://），随单文件发布、目录可移动；
        // headless 测试环境无 AssetLoader 服务时静默跳过，不阻塞构造。
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://CoffeeScale.UI/Assets/app.ico"));
            Icon = new WindowIcon(iconStream);
        }
        catch { /* 资源缺失/headless：回退默认图标 */ }
#endif
        // 桌面端给默认尺寸与最小尺寸；移动端（Android/iOS/MacCatalyst）由平台决定全屏，不强制 1040×760 以免溢出小屏。
        bool isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();
        if (!isMobile)
        {
            Width = 1040; Height = 760; MinWidth = 480; MinHeight = 640;
        }
        Background = Brush(Bg); Foreground = Brush(Fg);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei, PingFang SC, sans-serif");

        // 把属性回写搬运到 UI 线程（框架无关 ViewModel 的接入点）
        _vm.Marshal = a => Dispatcher.UIThread.InvokeAsync(a);

        DataContext = _vm;
        BuildUI();
        // 外层 Grid：主体 _root + 模态浮层层 _modalLayer（移动端弹窗遮罩，盖在全部内容之上）。
        _modalLayer = new Panel { IsHitTestVisible = true, Background = null };
        _safeBorder = new Border { Child = new Grid { Children = { _root, _modalLayer } } };
        Content = _safeBorder; // 移动端安全区经 _safeBorder.Padding 消化（DockPanel 无 Padding 且根 Margin 在 Android 有渲染双重叠加偏差）

        // 记录变更时刷新列表
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrewViewModel.Records)) RebuildRecords();
            if (e.PropertyName == nameof(BrewViewModel.Culture)) RebuildAllText(); // 语言切换时重建静态文案
            if (e.PropertyName == nameof(BrewViewModel.TransportState)) UpdateTransportButton(); // 主运输键颜色/文案随状态循环
        };
        RebuildRecords();

        // 旋转（横↔竖）与窗口缩放都走 SizeChanged：NewSize 含宽高，ApplyLayout 按横竖屏 + 宽度综合判定布局
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width, e.NewSize.Height);
#if ANDROID || IOS || MACCATALYST
        ApplyLayout(Bounds.Size.Width, Bounds.Size.Height); // UserControl 无 ClientSize，用 Bounds；首启 0 由后续 SizeChanged 修正
#else
        ApplyLayout(ClientSize.Width, ClientSize.Height);
#endif

        // 移动端：让内容紧贴系统状态栏（时间/电量）下方，不重叠、不多余留白。
        // 2026-09-08 修复「应用顶栏与状态栏重叠」：原写法在构造期调用 TopLevel.GetTopLevel(this)，
        // 此时控件尚未挂入可视树必然返回 null → InsetsManager 永远拿不到 → 安全区避让从未生效。
        // 2026-09-09 终极修复（依据 Avalonia 11.2.5 源码 AndroidInsetsManager / TopLevel）：
        //  ① SafeAreaPadding 在 DisplayEdgeToEdge=false 时上报恒为 0（源码按 _displayEdgeToEdge
        //     决定 insets 类型）→ Android 15 上必须保持 edge-to-edge=true 才有 inset 可避让；
        //  ② 框架 AutoSafeAreaPadding 读取的是 TopLevel.Content（=本 UserControl）上的值，
        //     不是 TopLevel 自身。此前用 SetAutoSafeAreaPadding(tl,false) 只关了 TopLevel，
        //     Content 仍默认 true → 框架把 SafeAreaPadding 自动应用为 MainWindow.Padding，
        //     我们又手动在 _safeBorder.Padding 避让一次 → 顶部双倍 inset（真机/模拟器均偏远一倍）。
        //  ③ 正确做法：在内容控件自身关掉自动避让并清零其 Padding，仅保留一次手动避让。
        if (isMobile)
        {
            void SetupSafeArea()
            {
                var tl = TopLevel.GetTopLevel(this);
                var mgr = tl?.InsetsManager;
                if (mgr == null || tl == null) return;
                // Android 15 强制 edge-to-edge：保持开启，SafeAreaPadding 才会上报系统栏/刘海 inset。
                mgr.DisplayEdgeToEdge = true;
                // 关键：在「TopLevel.Content = 本控件」上关闭框架自动避让（同时关 TopLevel 作保险），
                // 并清零 Content/TopLevel 的 Padding，防止框架已自动应用的安全区内边距残留叠加。
                TopLevel.SetAutoSafeAreaPadding(this, false);
                TopLevel.SetAutoSafeAreaPadding(tl, false);
                Padding = new Thickness(0);
                tl.Padding = new Thickness(0);
                void ApplyInsets()
                {
                    var p = mgr.SafeAreaPadding;
                    // 顶部：以 Avalonia 上报 inset 为唯一真值（edge-to-edge 下即系统状态栏高度）。
                    // 兜底：上报为 0 时用安卓原生 status_bar_height；异常偏大（>60dip 明显非状态栏）也钳回原生。
                    double topInset = p.Top;
                    double nativeTop = NativeStatusBarDip();
                    if (topInset <= 0)
                    {
                        topInset = nativeTop > 0 ? nativeTop : 24;
                    }
                    else if (topInset > 60 && nativeTop > 0)
                    {
                        topInset = nativeTop; // 极端偏大视为异常上报
                    }
                    // 顶部 = inset 精确值（0 呼吸距，贴死状态栏下方）；左右 +6 / 底部 +4 避让
                    // 手势条与圆角；无 inset 的边不低于桌面基线 14。
                    var m = new Thickness(
                        Math.Max(RootMargin, p.Left + 6),
                        topInset,
                        Math.Max(RootMargin, p.Right + 6),
                        Math.Max(RootMargin, p.Bottom + 4));
                    // 安全区统一由 _safeBorder.Padding 单次承载：_root 与 _modalLayer 都在其内部 Grid 中
                    // 自动一并避让，不再给 _modalLayer 单独设 Margin（避免二次内缩）。
                    _root.Margin = new Thickness(0);
                    _safeBorder.Padding = m;
                }
                mgr.SafeAreaChanged += (_, _) => ApplyInsets();
                ApplyInsets();
#if ANDROID
                // 真机自检（logcat tag=CoffeeScaleInsets）：确认 Content 自动避让已关闭、仅一次避让生效。
                // 期望 contentAuto=False、winPad.Top=0、borderPad.Top=safeTop（单次）、标题渲染 Y≈safeTop。
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var tpAbs = _titleBlock.TranslatePoint(new Point(0, 0), tl);
                        Android.Util.Log.Info("CoffeeScaleInsets",
                            $"V3 contentAuto={TopLevel.GetAutoSafeAreaPadding(this)} winPad={Padding.Top:0.#} " +
                            $"tlPad={tl.Padding.Top:0.#} safeTop={mgr.SafeAreaPadding.Top:0.##} " +
                            $"borderPad={_safeBorder.Padding.Top:0.#} titleY={tpAbs?.Y:0.#}");
                    }
                    catch { }
                }, DispatcherPriority.Loaded);
#endif
            }
            AttachedToVisualTree += (_, _) => SetupSafeArea();
        }

        // 键盘快捷键：手冲时一手扶壶一手盲操，避免找按钮分心（弹窗打开时禁用，防误触）
        KeyDown += (_, ke) =>
        {
            // 文本输入控件聚焦时不拦截（日期选择器 / 数值输入框）
            if (ke.Source is TextBox or DatePicker) return;
            // 弹窗打开时仅允许 Esc 关闭，其余快捷键禁用（桌面二级窗口 + 移动端浮层均计入）
            bool hasDialog = (_modalLayer != null && _modalLayer.Children.Count > 0)
                || _masterPlanPickerWindow != null || _myPlanPickerWindow != null
                || _recordsDialogWindow != null || _paramInfoDialog != null;
            if (hasDialog) return;

            switch (ke.Key)
            {
                case Key.Space:
                    if (_vm.CanTransport) { _vm.Transport(); ke.Handled = true; }
                    break;
                case Key.R:
                    _vm.Reset(); ke.Handled = true;
                    break;
                case Key.N:
                    _vm.Next(); ke.Handled = true;
                    break;
            }
        };
    }

    // ---------- UI 构建 ----------
    private void BuildUI()
    {
        _root.Margin = new Thickness(RootMargin);

        // 顶部栏：软件名（左）+ 作者（紧随其后：留间隔、字号更小、垂直靠底）+ 语言切换（右，垂直居中）
        // 2026-09-09 由 DockPanel 改为 Grid 两列：原 DockPanel.LastChildFill=true 使最后添加的
        // _titleAuthor 成为 fill 元素，在 Android 单视图后端出现"布局 Y=0、渲染却偏移一个 header 高度"
        // 的偏差（标题离状态栏凭空多 30dip）。Grid 列定位语义明确，绕开 fill 怪癖。
        // 顶栏 titleAuthor/titleBlock/authorBlock 提升为字段，RebuildAllText 时同步刷新文本——保证语言切换后顶栏也能立即重译。
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        // 左侧分组：标题 + 作者（水平排列、整体顶对齐，标题紧贴顶栏上缘）
        _titleAuthor = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Spacing = 12 };
        _titleBlock = Text(I18n.T("AppTitle"), 18, FontWeight.Bold, Accent);
        _authorBlock = Text(I18n.T("Author"), 8, fg: Sub);
        _authorBlock.VerticalAlignment = VerticalAlignment.Bottom;
        _authorBlock.Margin = new Thickness(0, 0, 0, 0); // 与标题底部对齐（Top 对齐后无需基线微调）
        _titleAuthor.Children.Add(_titleBlock);
        _titleAuthor.Children.Add(_authorBlock);
        Grid.SetColumn(_titleAuthor, 0);
        // 右侧按钮组：⌂ 返回主界面（仅进入专业模式后显示）+ 语言切换（CN/EN，矮身位与压缩顶栏匹配）
        Button homeBtn = null!;
        homeBtn = MakeButton("⌂", (_, _) => ShowHome(), bg: "#5A4636", bold: true);
        homeBtn.MinHeight = 30;
        homeBtn.MinWidth = 44;
        homeBtn.Padding = new Thickness(10, 3);
        homeBtn.FontSize = 13;
        homeBtn.VerticalAlignment = VerticalAlignment.Center;
        homeBtn.IsVisible = !_onHome; // 启动即在 Home，返回键隐藏
        _homeBackBtn = homeBtn;
        Button langBtn = null!;
        langBtn = MakeButton(LanguageButtonText(), (_, _) =>
        {
            _vm.Culture = _vm.Culture == I18n.ZhCN ? I18n.EnUS : I18n.ZhCN;
            langBtn.Content = LanguageButtonText();
        }, minW: 72, bg: "#5A4636");
        langBtn.MinHeight = 30;                    // 顶栏按钮瘦身：44 → 30
        langBtn.Padding = new Thickness(10, 3);
        langBtn.FontSize = 12;
        langBtn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(homeBtn, 1);
        Grid.SetColumn(langBtn, 2);
        header.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"); // 原 "*,Auto" 扩为三列（⌂ + 语言）
        header.Children.Add(_titleAuthor);   // 第 0 列：软件名 + 作者
        header.Children.Add(homeBtn);        // 第 1 列：⌂ 返回主界面
        header.Children.Add(langBtn);        // 第 2 列：语言切换在右
        _root.Children.Add(header);
        DockPanel.SetDock(header, Dock.Top);

        // 左：分页（第 1 页「冲煮方案设置」/ 第 2 页「推荐冲煮方案」），标签栏 + 可滚动内容区
        _settingsHost.Background = Brush(Card); CornerRadius = new CornerRadius(12);
        _settingsHost.Padding = new Thickness(16);
        _settingsHost.Child = BuildLeft();

        // 右：实时冲煮（不再含「推荐冲煮方案」内容，推荐区已迁到左侧第 2 页）
        _brewHost.Background = Brush(Card); CornerRadius = new CornerRadius(12);
        _brewHost.Padding = new Thickness(16);
        _brewHost.Child = new ScrollViewer { Content = BuildBrew() };

        var body = new Grid();
        body.Children.Add(_settingsHost);
        body.Children.Add(_brewHost);
        _bodyGrid = body;

        // —— 四模块入口主界面（Home）：覆盖在主体之上的独立层（IsVisible 切换进出专业模式），
        //    启动默认显示 Home；专业模式卡片点击后隐藏 Home 露出下方现有冲煮界面。
        //    Background 必须给不透明底色（Bg）：Border 无背景时视觉穿透，下层专业界面会透出来。
        //    主体控件仍保持在树上且 IsVisible=true，不影响既有 headless 测试的可见性断言。
        _homeHost = new Border { Background = Brush(Bg), Child = BuildHome(), IsVisible = _onHome };
        // —— 简易咖啡计算器层：覆盖在 Home 之上（IsVisible 切换），不透明底色防穿透 ——
        _calcHost = new Border { Background = Brush(Bg), Child = BuildCalc(), IsVisible = _calcOpen };
        _beginnerHost = new Border { Background = Brush(Bg), Child = BuildBeginner(), IsVisible = _begOpen };
        _masterListHost = new Border { Background = Brush(Bg), IsVisible = false }; // #7 大师方案内联清单层（按需构建）
        var mainArea = new Grid();
        mainArea.Children.Add(body);
        mainArea.Children.Add(_homeHost);
        mainArea.Children.Add(_beginnerHost);
        mainArea.Children.Add(_calcHost);
        mainArea.Children.Add(_masterListHost); // 最上层：覆盖其他层
        DockPanel.SetDock(mainArea, Dock.Bottom);
        _root.Children.Add(mainArea);

        // 注意：_root 是 DockPanel（顶栏 Dock.Top + 主体 Dock.Bottom），不再需要 Grid 的 RowDefinitions
    }

    /// <summary>语言切换按钮文案：中文界面显示「CN/EN」，英文界面显示「EN/CN」（当前语言在前）。</summary>
    private string LanguageButtonText()
        => _vm.Culture == I18n.EnUS ? "EN/CN" : "CN/EN";

    /// <summary>
    /// 构建四模块入口主界面（Home）：品牌 Hero 卡（图标 + 软件名 + 工作台标语）
    /// + 四张模块卡（新手 / 专业 / 简易计算器 / 手冲大师方案）+ 底部署名条（作者 + 版本）。
    /// 三档自适应：紧凑（手机竖屏）单列四行；宽屏 2×2 网格；宽屏但高度矮（手机横屏/矮窗口）
    /// 用 _homeShort 压缩 Hero 与卡片排版。语言切换 / 布局形态变化时整体重建。
    /// </summary>
    private Control BuildHome()
    {
        _homeCompact = _compact;
        bool shortMode = _homeShort;
        var p = new StackPanel
        {
            Spacing = shortMode ? 9 : 18,   // 14 → 18：更"呼吸"的纵向节奏（INS/Apple 的留白语言）
            MaxWidth = 780,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,  // 内容不足一屏时垂直居中，消除底部大片死白
        };

        // —— Hero 品牌卡：图标 + 软件名 + 工作台标语（居中排版；作者与版本移至底部署名条）——
        var heroSp = new StackPanel { Spacing = shortMode ? 3 : 6, HorizontalAlignment = HorizontalAlignment.Center };
        // 咖啡图标外圈 = 椭圆径向光晕（2026-09-10 主界面图形化升级）
        double iconSize = shortMode ? 20 : _homeCompact ? 32 : 40;
        double haloSize = iconSize * 3.1; // 光晕直径约图标 3×，保证柔和过渡
        // 径向渐变：从中心 Accent 全饱和过渡到外缘透明——柔和咖啡光晕（2026-09-10 主界面图形化升级）
        var haloBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(255, 232, 199, 154), 0.0),
                new GradientStop(Color.FromArgb(180, 232, 199, 154), 0.45),
                new GradientStop(Color.FromArgb(0,   232, 199, 154), 1.0),
            },
            Opacity = 0.6,
        };
        var halo = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = haloSize, Height = haloSize,
            Fill = haloBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = Text("☕", iconSize, FontWeight.Normal, Accent);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative); // 缩放围绕图标自身中心，避免偏离
        // 用一个固定尺寸的 Grid 作为光晕+图标的容器；图标覆盖在光晕之上
        var iconHost = new Grid
        {
            Width = haloSize, Height = haloSize,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        iconHost.Children.Add(halo);
        iconHost.Children.Add(icon);
        _heroIconText = icon;
        // 启动/重启呼吸动画（多次重建 Home 会先 Stop 再 Start，确保单一计时器）
        StartHeroPulse();
        heroSp.Children.Add(iconHost);
        var heroTitle = Text(I18n.T("AppTitle"), shortMode ? 15 : _homeCompact ? 19 : 26, FontWeight.Bold, Accent, wrap: true);
        heroTitle.TextAlignment = TextAlignment.Center;
        heroTitle.HorizontalAlignment = HorizontalAlignment.Center;
        heroTitle.LetterSpacing = 0.4;   // 品牌字轻微加宽：更像"产品名"而非"一段文字"
        heroSp.Children.Add(heroTitle);
        var heroTag = Text(I18n.T("HomeHero"), shortMode ? 9 : _homeCompact ? 10 : 12, fg: Sub);
        heroTag.TextAlignment = TextAlignment.Center;
        heroTag.HorizontalAlignment = HorizontalAlignment.Center;
        heroTag.Opacity = 0.85;
        heroSp.Children.Add(heroTag);
        // Hero 品牌卡（2026-09-13）：底部深、顶部略暖的竖向渐变 + 24 大圆角 + 柔和投影，
        // 让"品牌区"成为整屏最厚的一块表面（Apple 的 hero panel 手法），与下方模块卡拉开层次。
        var hero = new Border
        {
            Background = Palette.Vertical("#3B2819", Palette.RoastDeep),
            BorderBrush = new SolidColorBrush(Palette.WithAlpha(Palette.Caramel, 46)),
            BorderThickness = new Thickness(1),
            BoxShadow = Palette.Shadow(offsetY: 10, blur: 30, alpha: 120),
            CornerRadius = new CornerRadius(shortMode ? 16 : 24),
            Padding = new Thickness(shortMode ? 14 : 26, shortMode ? 10 : 22),
            Margin = new Thickness(0, 4, 0, 0),
            Child = heroSp,
        };
        p.Children.Add(hero);

        // —— 四模块卡：仅专业模式可进入现有界面，其余三模块弹出「敬请期待」——
        var cards = new (string emoji, string titleKey, string descKey, string? sloganKey, string accentColor, bool open, Action onTap)[]
        {
            ("🌱", "ModeBeginner", "ModeDescBeginner", "ModeSloganBeginner", CardAccents.Beginner, true, ShowBeginner),
            ("⚙️", "ModePro", "ModeDescPro", "ModeSloganPro", CardAccents.Pro, true, EnterProMode),
            ("🧮", "ModeCalc", "ModeDescCalc", null, CardAccents.Calc, true, ShowCalc),
            ("🏆", "ModeMaster", "ModeDescMaster", null, CardAccents.Master, true, ShowHomeMasterPlanPicker),
        };

        if (_homeCompact)
        {
            var sp = new StackPanel { Spacing = shortMode ? 6 : 10 };
            foreach (var c in cards) sp.Children.Add(HomeCard(c.emoji, c.titleKey, c.descKey, c.sloganKey, c.accentColor, c.open, c.onTap));
            p.Children.Add(sp);
        }
        else
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
            };
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                var card = HomeCard(c.emoji, c.titleKey, c.descKey, c.sloganKey, c.accentColor, c.open, c.onTap);
                int row = i / 2, col = i % 2;
                // Avalonia Grid 无 ColumnSpacing/RowSpacing（CS0117 编译坑）：用边距替代
                card.Margin = new Thickness(col == 0 ? 0 : 6, row == 0 ? 0 : 12, col == 0 ? 6 : 0, 0);
                Grid.SetRow(card, row);
                Grid.SetColumn(card, col);
                grid.Children.Add(card);
            }
            p.Children.Add(grid);
        }

        // —— 底部署名条：作者 + 版本（2026-09-09 按需求从 Hero 移至软件底部，全档位呈现）——
        var footer = Text($"{I18n.T("Author")}  ·  v{AppVer}", shortMode ? 9 : _homeCompact ? 10 : 11, fg: Sub, wrap: true);
        footer.TextAlignment = TextAlignment.Center;
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        footer.Opacity = 0.8;
        footer.Margin = new Thickness(0, shortMode ? 2 : 6, 0, 0);
        p.Children.Add(footer);

        // 横向滚动必须 Disabled：ScrollViewer 横向可滚时会以「无穷宽」测量内容，
        // StackPanel(MaxWidth=780) 按满 780 排版 → 小屏上整体右移溢出、文本不换行。
        // 禁用后内容按视口真实宽度测量，自动换行 + 居中。
        // 2026-09-11：外层再包一个 Grid，底层放咖啡氛围装饰（暖光/咖啡渍/蒸汽），不随内容滚动。
        return new Grid
        {
            Children =
            {
                BuildHomeBackdrop(shortMode),
                new ScrollViewer
                {
                    Content = p,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            },
        };
    }

    /// <summary>
    /// 子模块通用氛围背景（2026-09-11 设计系统延伸，轻量版）：仅两团角落咖啡暖光，
    /// 无蒸汽/咖啡渍装饰 —— 让子页面与主界面共享同一"暖仪式"氛围又不喧宾夺主。
    /// IsHitTestVisible=false 不拦截点击。
    /// </summary>
    private Control BuildAmbientBackdrop()
    {
        var g = new Grid { IsHitTestVisible = false };
        // 左上咖啡暖光（焦糖色，非常低调）
        g.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 380, Height = 380,
            Fill = Palette.Glow(Palette.Caramel, 26),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-140, -150, 0, 0),
        });
        // 右下暖光（肉桂色，与左上呼应）
        g.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 340, Height = 340,
            Fill = Palette.Glow(Palette.Cinnamon, 22),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -120, -130),
        });
        return g;
    }

    /// <summary>
    /// 主界面背景装饰层（2026-09-11 UI 设计升级，借鉴 2025-2026 流行 ambient/dark-luxury 设计语言）：
    /// ① 两团角落咖啡暖光（径向渐变、低透明度 ambient glow）营造舞台氛围；
    /// ② 一个大号「咖啡杯印渍」（低透明度粗描边圆环，呼应手冲主题）；
    /// ③ 三条蒸汽贝塞尔曲线从 Hero 咖啡杯升起（错落透明度，静态装饰）。
    /// 纯装饰层：IsHitTestVisible=false 不拦截点击，叠在滚动内容之下不随内容滚动。
    /// </summary>
    private Control BuildHomeBackdrop(bool shortMode)
    {
        // ① 角落暖光 ×2（与子模块共用轻量氛围层，DRY）
        var g = (Grid)BuildAmbientBackdrop();
        // ② 咖啡杯印渍：左下大圆环描边（咖啡渍在"桌面"上的隐喻，极低存在感）
        g.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 300, Height = 300,
            Stroke = new SolidColorBrush(Palette.WithAlpha(Palette.Caramel, 20)),
            StrokeThickness = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-90, 0, 0, -110),
        });

        // ③ 蒸汽曲线 ×3：从 Hero ☕ 上方升起（中心偏上、水平错开、透明度递减）
        // 顶端对齐 Hero 区域（内容 MaxWidth=780 居中，蒸汽面板同样居中即可对齐 ☕）
        double steamH = shortMode ? 78 : 108;
        var steamHost = new Grid
        {
            Height = steamH,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, shortMode ? 6 : 10, 0, 0),
        };
        double[] offsets = { -34, 0, 34 };        // 三条曲线水平错位
        double[] opacities = { 0.20, 0.30, 0.20 }; // 中间最明显
        double[] amps = { 12, 16, 12 };            // 摆动幅度
        for (int i = 0; i < 3; i++)
        {
            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = SteamGeometry(offsets[i], amps[i], steamH),
                Stroke = new SolidColorBrush(Palette.WithAlpha(Palette.Cream, 255)),
                StrokeThickness = 3.2 - i * 0.4,
                Opacity = opacities[i],
                StrokeLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            steamHost.Children.Add(path);
        }
        g.Children.Add(steamHost);
        return g;
    }

    /// <summary>生成一条蒸汽 S 形贝塞尔曲线几何（从 (x,0) 摆动上升至 (x,h)）。</summary>
    private static Avalonia.Media.Geometry SteamGeometry(double x, double amp, double h)
    {
        var geo = new Avalonia.Media.StreamGeometry();
        using (var ctx = geo.Open())
        {
            double third = h / 3;
            ctx.BeginFigure(new Point(x, h), false); // 从底部（杯口）向上画
            ctx.CubicBezierTo(
                new Point(x - amp, h - third * 0.6),
                new Point(x + amp, h - third * 1.4),
                new Point(x, h - third * 2));
            ctx.CubicBezierTo(
                new Point(x - amp, h - third * 2.6),
                new Point(x + amp, h - third * 2.9),
                new Point(x, 0));
        }
        return geo;
    }

    /// <summary>单张模块卡（2026-09-11 视觉升级）：左侧 5dp 模块色条 + 图标渐变瓷砖（iOS 图标质感）
    /// + 半透明玻璃卡身（1px 奶油高光描边）+ 桌面悬停提亮 + 按压淡化。整卡可点。</summary>
    private Button HomeCard(string emoji, string titleKey, string descKey, string? sloganKey, string accentColor, bool open, Action onTap)
    {
        bool shortMode = _homeShort;
        // ① 图标瓷砖：模块色 45° 渐变圆角方块（亮模块色 → 深咖啡底）
        double tile = shortMode ? 30 : _homeCompact ? 34 : 38;
        var icTile = new Border
        {
            Width = tile, Height = tile,
            CornerRadius = new CornerRadius(tile * 0.3),   // iOS 图标圆角比例
            Background = Palette.Diagonal(accentColor, "#1A100A"),
            // 顶部高光细描边 + 轻微投影：把平面色块做成"实体 App 图标"的质感
            BorderBrush = new SolidColorBrush(Palette.WithAlpha("#FFFFFF", 46)),
            BorderThickness = new Thickness(1),
            BoxShadow = Palette.Shadow(offsetY: 3, blur: 10, alpha: 110),
            VerticalAlignment = VerticalAlignment.Center,
            Child = IconText(emoji, tile * 0.54, IconInk),
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(icTile, 0);
        head.Children.Add(icTile);
        var t = Text("  " + I18n.T(titleKey), shortMode ? 13 : _homeCompact ? 15 : 17, FontWeight.Bold, Fg, vCenter: true);
        Grid.SetColumn(t, 1);
        head.Children.Add(t);
        // 状态徽章（2026-09-13）：胶囊形（半径取半高以上的大值即"全圆角"）+ 可进入模块用焦糖渐变，
        // 形成"可点"的暖色信号；未开放模块保持沉稳的皮革底，弱化存在感（信息分层）。
        var badge = new Border
        {
            Background = open ? Palette.Vertical("#E9BE7C", "#C8893A") : Brush("#4A382A"),
            BorderBrush = open ? new SolidColorBrush(Palette.WithAlpha("#FFFFFF", 60)) : null,
            BorderThickness = open ? new Thickness(1) : default,
            CornerRadius = new CornerRadius(999),   // 胶囊
            Padding = new Thickness(shortMode ? 8 : 11, shortMode ? 2 : 3.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(I18n.T(open ? "StatusOpen" : "StatusWip"), 10, FontWeight.SemiBold,
                fg: open ? Ink : Sub),
        };
        Grid.SetColumn(badge, 2);
        head.Children.Add(badge);

        var inner = new StackPanel
        {
            Spacing = shortMode ? 3 : 6,
            Margin = new Thickness(12, shortMode ? 8 : 12, 12, shortMode ? 8 : 12),
        };
        inner.Children.Add(head);
        var d = Text(I18n.T(descKey), 11.5, fg: Sub, wrap: true);
        d.Opacity = 0.9;
        inner.Children.Add(d);
        // 模块标语（#8）：新手模式「冲泡一杯 60 分咖啡」/ 专业模式「冲泡一杯 80 分咖啡」，按模块色高亮
        if (!string.IsNullOrEmpty(sloganKey))
        {
            var s = Text(I18n.T(sloganKey), 12, FontWeight.SemiBold, accentColor, wrap: true);
            s.Opacity = 0.95;
            inner.Children.Add(s);
        }
        // 卡身：左 5dp 色条 + 内容两列（自动铺满，色条贯通全高）
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"Auto,*"),
            RowDefinitions = new RowDefinitions("*"),
        };
        var stripe = new Border
        {
            Background = Brush(accentColor),
            Width = 5,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetColumn(stripe, 0);
        Grid.SetRow(stripe, 0);
        body.Children.Add(stripe);
        Grid.SetColumn(inner, 1);
        Grid.SetRow(inner, 0);
        body.Children.Add(inner);

        // ② 玻璃卡面（2026-09-13 INS × Apple 升级）：大圆角（20）+ 柔和投影 + 1px 奶油高光描边。
        //    投影与描边共同替代"硬边框"，是深色 UI 里最显层次的一招。
        //
        //    【为什么投影画在 Border 而不是 Button 上】Avalonia 11 的 Button 没有 BoxShadow 属性，
        //    而既有测试断言"卡片容器下直接是 4 个 Button"，不能为了投影给卡片外面套一层壳。
        //    折中：Button 自身透明化、ClipToBounds=false，真正的"表面"交给内层 Border，
        //    投影由它绘制并自然溢出到卡片外缘——控件树结构不变，投影照常生效。
        var glassBg = new SolidColorBrush(Palette.WithAlpha(Card, 214));
        var hoverBg = new SolidColorBrush(Palette.WithAlpha(Palette.Caramel, 46));
        var restShadow = Palette.Shadow(offsetY: 7, blur: 22, alpha: 96);
        var surface = new Border
        {
            Background = glassBg,
            BorderBrush = new SolidColorBrush(Palette.WithAlpha(Palette.Cream, 30)),
            BorderThickness = new Thickness(1),
            BoxShadow = restShadow,
            CornerRadius = new CornerRadius(20),
            Child = body,
        };
        var card = new Button
        {
            Content = surface,
            Background = Brushes.Transparent,
            BorderThickness = default,
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(0),
            ClipToBounds = false,   // 让内层 Border 的投影溢出到卡片外缘
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            MinHeight = shortMode || _homeCompact ? 76 : 120,
        };
        card.Click += (_, _) => onTap();
        // 触摸/按压反馈（2026-09-10）：按下整体淡化 15%，松手复原
        card.Opacity = 1.0;
        card.PointerPressed += (_, _) => card.Opacity = 0.85;
        card.PointerReleased += (_, _) => card.Opacity = 1.0;
        card.PointerCaptureLost += (_, _) => card.Opacity = 1.0;
        // 桌面悬停（2026-09-13 升级为"抬升"）：卡面泛模块暖光 + 投影加深 → 明确的"可点、会浮起"反馈；
        // 悬停态切换写在内层 Border 上；指针捕获丢失也要复位，避免悬停态卡死。
        void Lift() { surface.Background = hoverBg; surface.BoxShadow = Palette.ShadowLifted(); }
        void Settle() { surface.Background = glassBg; surface.BoxShadow = restShadow; }
        card.PointerEntered += (_, _) => Lift();
        card.PointerExited += (_, _) => Settle();
        card.PointerCaptureLost += (_, _) => Settle();
        return card;
    }

    /// <summary>进入专业模式：隐藏 Home 露出下方现有冲煮界面，顶栏显示 ⌂ 返回键。</summary>
    private void EnterProMode()
    {
        _onHome = false;
        StopHeroPulse();
        if (_homeHost != null) _homeHost.IsVisible = false;
        if (_homeBackBtn != null) _homeBackBtn.IsVisible = true;
    }

    /// <summary>返回四模块入口主界面（重建一次以吸收语言/布局变化）。</summary>
    private void ShowHome()
    {
        _onHome = true;
        if (_homeHost != null)
        {
            _homeHost.Child = BuildHome();
            _homeHost.IsVisible = true;
        }
        if (_homeBackBtn != null) _homeBackBtn.IsVisible = false;
    }

    /// <summary>未上线模块统一提示：桌面端二级窗 / 移动端整屏浮层（复用 PresentModal）。</summary>
    private void ShowWipModal(string modeName)
        => PresentModal($"{modeName} · {I18n.T("WipTitle")}",
            Text(I18n.T("WipBody"), 13, fg: Fg, wrap: true), 380, 210);

    // —— 主界面 Hero 光晕呼吸（2026-09-10）——
    /// <summary>启动主界面 ☕ 文字的呼吸缩放动画：2.4 秒/周期，幅度 ±5%。
    /// 多次重建 Home 会先 Stop 旧定时器再 Start 新定时器，避免计时器泄漏。</summary>
    private void StartHeroPulse()
    {
        StopHeroPulse();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var start = DateTime.UtcNow;
        t.Tick += (_, _) =>
        {
            var icon = _heroIconText;
            if (icon == null) { StopHeroPulse(); return; }
            double elapsed = (DateTime.UtcNow - start).TotalSeconds;
            // 2.4 秒一周期，sin 平滑缩放
            double scale = 1.0 + 0.05 * Math.Sin(elapsed * 2 * Math.PI / 2.4);
            icon.RenderTransform = new ScaleTransform(scale, scale);
        };
        t.Start();
        _heroPulseTimer = t;
    }

    /// <summary>停止 Hero 呼吸并清空引用，避免 Hero 隐藏后定时器继续占用线程。</summary>
    private void StopHeroPulse()
    {
        if (_heroPulseTimer != null)
        {
            _heroPulseTimer.Stop();
            _heroPulseTimer = null;
        }
        if (_heroIconText != null)
        {
            _heroIconText.RenderTransform = null;
        }
    }

    // ================= 简易咖啡计算器 =================

    /// <summary>打开简易咖啡计算器层（覆盖在 Home 之上；重建以吸收语言/布局变化）。</summary>
    private void ShowCalc()
    {
        _calcOpen = true;
        StopHeroPulse();
        if (_calcHost != null)
        {
            _calcHost.Child = BuildCalc();
            _calcHost.IsVisible = true;
        }
    }

    /// <summary>关闭计算器，回到主界面（计时状态保留在字段中，重开续显）。</summary>
    private void CloseCalc()
    {
        _calcOpen = false;
        if (_calcHost != null) _calcHost.IsVisible = false;
    }

    /// <summary>
    /// 构建简易咖啡计算器：顶部标题行（🧮 标题 + ⌂ 返回主界面）+ 两大功能区。
    /// 四形态统一上下排布：粉水比计算 → 分隔线 → 分段计时 → 分隔线 → 底部署名；
    /// 紧凑（手机竖屏）/矮窗（手机横屏）只压缩间距与字号，宽屏不再左右分栏（2026-09-09 按需求改为上下排）。
    /// </summary>
    private Control BuildCalc()
    {
        _calcCompact = _compact;
        _calcShort = _homeShort;
        bool narrow = _calcCompact || _calcShort;

        var p = new StackPanel
        {
            Spacing = narrow ? 8 : 12,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // —— 页头：统一 PageHeader（🧮 模块瓷砖 + 标题 + ⌂ 返回）——
        p.Children.Add(PageHeader("🧮", "CalcTitle", CardAccents.Calc, CloseCalc));

        // —— 上下排布：计算卡 → 分隔线 → 计时卡 → 分隔线 → 底部署名 ——
        p.Children.Add(BuildCalcRatioCard(narrow));
        p.Children.Add(Divider());
        p.Children.Add(BuildCalcTimerCard(narrow));
        p.Children.Add(Divider());

        // —— 底部署名条：作者 + 版本（与 Home 同款，全档位呈现）——
        var footer = Text($"{I18n.T("Author")}  ·  v{AppVer}", narrow ? 10 : 11, fg: Sub, wrap: true);
        footer.TextAlignment = TextAlignment.Center;
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        footer.Opacity = 0.8;
        footer.Margin = new Thickness(0, narrow ? 2 : 6, 0, 0);
        p.Children.Add(footer);

        // 2026-09-11：外层包 Grid，底层放轻量暖光氛围层（与 Home 同语言）
        return new Grid
        {
            Children =
            {
                BuildAmbientBackdrop(),
                new ScrollViewer
                {
                    Content = p,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, // 横向禁滚：按视口实测宽度排版（防无穷宽测量偏移）
                },
            },
        };
    }

    /// <summary>分区细线（2026-09-13 改为"渐隐 hairline"）：左实右虚、端点自然消失，
    /// 避免整条等亮硬线把版面切碎。这是 iOS/macOS 分隔线的标准做法。</summary>
    private Border Divider() => new Border
    {
        Height = 1,
        Background = Palette.Hairline("#6A523C", 170),
        Margin = new Thickness(24, 2),
    };

    /// <summary>区块小标题（2026-09-11 设计系统组件）：左侧 3dp 焦糖竖条 + Section 字阶（14pt Bold），
    /// 与提示横幅（BegHintBanner）同语法，形成"竖条 = 层级锚点"的一致视觉语言。</summary>
    private Control SectionTitle(string text)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        // 竖向锚点条：4dp 圆头 + 焦糖→透明渐变（顶端实、底端渐隐），比等色实条更"呼吸"
        var bar = new Border
        {
            Background = Palette.Vertical(Palette.Caramel, Palette.Cinnamon),
            Width = 4,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 1, 8, 1),
        };
        Grid.SetColumn(bar, 0);
        row.Children.Add(bar);
        var t = Text(text, 14, FontWeight.Bold, Fg, vCenter: true);
        t.LetterSpacing = 0.3;   // 轻微字距：小标题更"挺"，Apple 排版习惯
        Grid.SetColumn(t, 1);
        row.Children.Add(t);
        return row;
    }

    /// <summary>
    /// 子模块统一页头（2026-09-11 设计系统组件）：玻璃卡底 + 模块色图标瓷砖 + 标题 + ⌂ 返回键。
    /// 三个覆盖层（新手/计算器/大师清单）共用，保证"进入任何模块，页头语法一致"。
    /// </summary>
    private Control PageHeader(string emoji, string titleKey, string accentColor, Action onBack)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Palette.WithAlpha(Card, 224)),
            BorderBrush = new SolidColorBrush(Palette.WithAlpha(Palette.Cream, 30)),
            BorderThickness = new Thickness(1),
            BoxShadow = Palette.Shadow(offsetY: 6, blur: 20, alpha: 92),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 10),
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        // 模块色渐变瓷砖（与 Home 卡同语言，尺寸略小）
        double tile = _compact ? 28 : 32;
        var tileBorder = new Border
        {
            Width = tile, Height = tile,
            CornerRadius = new CornerRadius(tile * 0.3),
            Background = Palette.Diagonal(accentColor, "#1A100A"),
            BorderBrush = new SolidColorBrush(Palette.WithAlpha("#FFFFFF", 46)),
            BorderThickness = new Thickness(1),
            BoxShadow = Palette.Shadow(offsetY: 3, blur: 10, alpha: 110),
            VerticalAlignment = VerticalAlignment.Center,
            Child = IconText(emoji, tile * 0.52, IconInk),
        };
        Grid.SetColumn(tileBorder, 0);
        head.Children.Add(tileBorder);
        // 标题：Title 字阶（页头卡内 Cream 白更利于阅读，模块色已由瓷砖承担）
        var title = Text("  " + I18n.T(titleKey), _compact ? 16 : 19, FontWeight.Bold, Fg, vCenter: true);
        title.LetterSpacing = 0.3;
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        // 返回键：次操作样式（Leather 纯色）→ 胶囊形，与顶部状态徽章、主按钮同族的"圆润"语言
        var back = MakeButton("⌂ " + I18n.T("BackHome"), (_, _) => onBack(),
            bg: Palette.Leather, fg: Fg, bold: true);
        back.CornerRadius = new CornerRadius(999);
        back.Padding = new Thickness(16, 8);
        back.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(back, 2);
        head.Children.Add(back);
        card.Child = head;
        return card;
    }

    /// <summary>粉水比计算卡：粉量 × 比值 N = 总注水量，任一清空可由另两项反推。</summary>
    private Border BuildCalcRatioCard(bool narrow)
    {
        var sp = new StackPanel { Spacing = narrow ? 8 : 12 };

        sp.Children.Add(Text("⚖ " + I18n.T("CalcRatioEquation"), narrow ? 13 : 15, FontWeight.Bold, Accent));
        sp.Children.Add(Text(I18n.T("CalcHint"), 11, fg: Sub, wrap: true));
        sp.Children.Add(Divider());

        // 一行公式布局：粉量 × 1:粉水比 = 总注水量（初始 15.0 × 1:15 = 225.0）
        // 框宽只取「刚好显示量程最大值」，框后不再附带范围文本（2026-09-10）
        sp.Children.Add(CalcEquationRow(narrow));

        // 范围校验提示：仅在有违规时可见（默认折叠，不占位留白）
        _calcWarnText = Text("", narrow ? 11 : 12, fg: Warn, wrap: true);
        _calcWarnText.IsVisible = false;
        sp.Children.Add(_calcWarnText);

        var card = new Border
        {
            Background = Brush(Panel),
            CornerRadius = new CornerRadius(narrow ? 12 : 18),
            Padding = new Thickness(narrow ? 12 : 20, narrow ? 10 : 16),
            Child = sp,
        };
        CalcValidate();   // 初值自检（正常情况下全部合法 → 提示栏保持隐藏）
        return card;
    }

    /// <summary>
    /// 分段计时卡：圆形时间（进度环 + 圆心数字）+ 总耗时数字 + 启动(圆)/下一段(蓝)/停止(红) 三键
    /// + 分段时长清单（毫秒精度），停止后追加总耗时行。
    /// 交互（2026-09-10 按需求）：点「启动」即开始计时；点「下一段」只切段、**不停表**；
    /// 直到点「停止」才结束整个计时。
    /// </summary>
    private Border BuildCalcTimerCard(bool narrow)
    {
        var sp = new StackPanel { Spacing = narrow ? 8 : 12 };

        sp.Children.Add(Text("⏱ " + I18n.T("CalcTimerTitle"), narrow ? 13 : 15, FontWeight.Bold, Accent));

        // —— 圆形时间 + 数字时间（同一行：左环右数字）——
        sp.Children.Add(BuildCalcRing(narrow));

        // —— 三键：启动（圆形）/ 下一段（启动后蓝色）/ 停止（红色）——
        var btnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*") };
        _calcStartBtn = MakeCalcStartButton(narrow);
        _calcNextBtn = MakeButton(I18n.T("CalcNext"), (_, _) => CalcNext(),
            stretch: true, bg: BtnMuted, fg: "#FFFFFF", bold: true);
        _calcStopBtn = MakeButton(I18n.T("CalcStop"), (_, _) => CalcStop(),
            stretch: true, bg: BtnStop, fg: "#FFFFFF", bold: true);
        _calcNextBtn.MinHeight = narrow ? 52 : 48;
        _calcStopBtn.MinHeight = narrow ? 52 : 48;
        _calcNextBtn.VerticalAlignment = VerticalAlignment.Center;
        _calcStopBtn.VerticalAlignment = VerticalAlignment.Center;
        _calcNextBtn.Margin = new Thickness(10, 0, 0, 0);
        _calcStopBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_calcStartBtn, 0); btnRow.Children.Add(_calcStartBtn);
        Grid.SetColumn(_calcNextBtn, 1); btnRow.Children.Add(_calcNextBtn);
        Grid.SetColumn(_calcStopBtn, 2); btnRow.Children.Add(_calcStopBtn);
        sp.Children.Add(btnRow);
        sp.Children.Add(Divider());

        // 分段清单（可滚动；行文本由 RebuildCalcTimerList / 实时 tick 填充）
        _calcTimerList = new StackPanel { Spacing = 6 };
        sp.Children.Add(new ScrollViewer
        {
            Content = _calcTimerList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = narrow ? 180 : 300,
        });

        RebuildCalcTimerList();
        UpdateCalcButtons();

        return new Border
        {
            Background = Brush(Panel),
            CornerRadius = new CornerRadius(narrow ? 12 : 18),
            Padding = new Thickness(narrow ? 12 : 20, narrow ? 10 : 16),
            Child = sp,
        };
    }

    /// <summary>
    /// 机械秒表表盘（2026-09-10）：表盘底 + 金属外圈 + 60 格秒刻度（每 5 秒长刻度）+ 每 10 秒数字
    /// + 大秒针（60 秒一圈，带尾部配重）+ 小分针（30 分钟一圈）+ 中心轴帽。
    /// **表盘上不叠加任何电子数字**；读数交给右侧电子秒表。
    /// 运行方式同机械秒表：点「启动」开始走针，「下一段」只切段、走针不停，直到「停止」定格。
    /// 用 Canvas 承载：子元素按 (0,0) 定位，避免 Shape 自动缩放/居中造成的偏移。
    /// </summary>
    private Grid BuildCalcRing(bool narrow)
    {
        _calcRingD = (narrow ? CalcRingDiameter * 0.86 : CalcRingDiameter) * _scale;
        double d = _calcRingD;
        double cx = d / 2, cy = d / 2;

        var canvas = new Canvas { Width = d, Height = d };

        // 表盘底 + 金属外圈
        canvas.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = d, Height = d, Fill = Brush("#1A120D") });
        var bezel = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = d - 3, Height = d - 3,
            Stroke = Brush("#6B4F33"), StrokeThickness = 4,
        };
        Canvas.SetLeft(bezel, 1.5); Canvas.SetTop(bezel, 1.5);
        canvas.Children.Add(bezel);

        // 60 格秒刻度：每 5 秒长刻度（浅铜色），其余短刻度
        double rTickOut = d / 2 - 11;
        for (int i = 0; i < 60; i++)
        {
            bool major = i % 5 == 0;
            double len = major ? d * 0.075 : d * 0.035;
            double a = i * 6.0;                                     // 每格 6°（60 格一圈）
            canvas.Children.Add(new Avalonia.Controls.Shapes.Line
            {
                StartPoint = CalcRingPoint(cx, cy, rTickOut, a),
                EndPoint = CalcRingPoint(cx, cy, rTickOut - len, a),
                Stroke = Brush(major ? "#C8A06A" : "#5A4433"),
                StrokeThickness = major ? 1.8 : 1,
                StrokeLineCap = PenLineCap.Round,
            });
        }

        // 每 10 秒一个数字（10 / 20 / … / 60，60 落在 12 点方向）
        double rNum = rTickOut - d * 0.12;
        double numFont = Math.Max(8, d * 0.075);
        for (int s = 10; s <= 60; s += 10)
        {
            var num = new TextBlock
            {
                Text = s.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = numFont,
                Foreground = Brush("#C8A06A"),
                Width = d * 0.22,
                TextAlignment = TextAlignment.Center,
            };
            var p = CalcRingPoint(cx, cy, rNum, s * 6.0);
            Canvas.SetLeft(num, p.X - num.Width / 2);
            Canvas.SetTop(num, p.Y - numFont * 0.75);
            canvas.Children.Add(num);
        }

        // 小分针（30 分钟一圈）→ 大秒针（60 秒一圈，含尾部配重）→ 中心轴帽
        _calcRingMinHand = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Point(cx, cy),
            EndPoint = CalcRingPoint(cx, cy, d * 0.28, 0),
            Stroke = Brush("#C8893A"),
            StrokeThickness = 3,
            StrokeLineCap = PenLineCap.Round,
        };
        canvas.Children.Add(_calcRingMinHand);

        _calcRingHand = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = CalcRingPoint(cx, cy, d * 0.09, 180),
            EndPoint = CalcRingPoint(cx, cy, rTickOut - d * 0.10, 0),
            Stroke = Brush("#F6C9A0"),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
        };
        canvas.Children.Add(_calcRingHand);

        var hub = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, Fill = Brush("#C8893A") };
        Canvas.SetLeft(hub, cx - 3.5); Canvas.SetTop(hub, cy - 3.5);
        canvas.Children.Add(hub);

        // 表盘下方状态文字（非电子计时读数）
        _calcRingText = Text(I18n.T("CalcSegIdle"), narrow ? 10.5 : 11.5, fg: Sub);
        _calcRingText.TextAlignment = TextAlignment.Center;
        _calcRingText.HorizontalAlignment = HorizontalAlignment.Center;

        var dialCol = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        dialCol.Children.Add(canvas);
        dialCol.Children.Add(_calcRingText);

        // —— 右侧电子秒表：内嵌 LCD 面板 + 等宽数字（总耗时 mm:ss.ff，2026-09-10）——
        _calcTotalText = Text("00:00.00", narrow ? 21 : 25, FontWeight.Bold, Accent);
        _calcTotalText.FontFamily = new FontFamily("Consolas, Menlo, monospace");   // 等宽：数字不跳动
        _calcTotalText.HorizontalAlignment = HorizontalAlignment.Center;
        _calcTotalText.TextAlignment = TextAlignment.Center;

        var lcd = new Border
        {
            Background = Brush("#170F0A"),
            BorderBrush = Brush("#3A2A1C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(narrow ? 8 : 12, narrow ? 5 : 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _calcTotalText,
        };

        var totalLabel = Text($"{I18n.T("CalcTotalLabel")} · {I18n.T("CalcStopwatch")}", narrow ? 10.5 : 11.5, fg: Sub);
        var hint = Text(I18n.T("CalcRingHint"), narrow ? 9.5 : 10.5, fg: Sub, wrap: true);
        hint.Opacity = 0.7;
        hint.MaxWidth = narrow ? 150 : 210;

        var digital = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(narrow ? 12 : 18, 0, 0, 0),
        };
        digital.Children.Add(totalLabel);
        digital.Children.Add(lcd);
        digital.Children.Add(hint);

        var host = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetColumn(dialCol, 0); host.Children.Add(dialCol);
        Grid.SetColumn(digital, 1); host.Children.Add(digital);

        UpdateCalcRing();   // 初值：指针归零于 12 点
        return host;
    }

    /// <summary>计时主按钮：圆形（直径随布局档位，含按下反馈与禁用态降透明度）。</summary>
    private Button MakeCalcStartButton(bool narrow)
    {
        double size = (narrow ? 68 : 76) * _scale;
        var b = new Button
        {
            Content = "▶ " + I18n.T("CalcStart"),
            Width = size,
            Height = size,
            MinHeight = 0,
            MinWidth = 0,
            CornerRadius = new CornerRadius(size / 2),   // 正圆
            Padding = new Thickness(4),
            FontSize = (narrow ? 12 : 13) * _scale,
            FontWeight = FontWeight.Bold,
            Background = Brush(AccentBtn),
            Foreground = Brush("#FFFFFF"),
            BorderBrush = Brush("#F6C9A0"),
            BorderThickness = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Tag = AccentBtn,
        };
        b.PointerPressed += (_, _) => { b.Background = Brush("#8F5D22"); b.RenderTransform = new TranslateTransform(0, 2); };
        void Release(object? _, RoutedEventArgs __) { b.Background = Brush((string)b.Tag!); b.RenderTransform = null; }
        b.PointerReleased += Release; b.PointerExited += Release; b.PointerCaptureLost += Release;
        b.Bind(Button.OpacityProperty, new Binding { Source = b, Path = nameof(b.IsEnabled), Converter = BoolToDoubleConverter.Instance });
        b.Click += (_, _) => CalcStart();
        return b;
    }

    /// <summary>表盘取点：θ 为自 12 点起顺时针的角度（刻度、数字、指针共用）。</summary>
    private static Point CalcRingPoint(double cx, double cy, double r, double deg)
    {
        double rad = deg * Math.PI / 180.0;
        return new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
    }

    /// <summary>
    /// 计算器输入行：标签 + 定宽输入框 + 右端允许区间提示。
    /// 输入框宽度固定（不铺满整行），保证「留白恰当」——2026-09-10 按需求收窄。
    /// 标签列固定宽度让三行输入框左边缘对齐成列。
    /// </summary>
    /// <summary>
    /// 一行公式布局（2026-09-10）：粉量 × 1:粉水比 = 总注水量 g，三个输入框与运算符、单位同排排完。
    /// 框宽按「刚好显示量程最大值」定制：粉量 50.0 / 粉水比 30 / 总注水量 1500.0；
    /// 框后不再显示范围值，字段名改用 Watermark（空值时可见）。
    /// </summary>
    private Control CalcEquationRow(bool narrow)
    {
        _calcDoseBox = MakeCalcBox("15.0", CalcOnDoseChanged, I18n.T("CalcWmDose"));
        _calcRatioBox = MakeCalcBox("15", CalcOnRatioChanged, I18n.T("CalcWmRatio"));
        _calcWaterBox = MakeCalcBox("225.0", CalcOnWaterChanged, I18n.T("CalcWmWater"));

        _calcDoseBox.Width = narrow ? 64 : 80;      // 最大 50.0
        _calcRatioBox.Width = narrow ? 48 : 60;     // 最大 30（整数）
        _calcWaterBox.Width = narrow ? 84 : 100;    // 最大 1500.0

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = narrow ? 4 : 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(_calcDoseBox);
        row.Children.Add(CalcOpText("g", narrow ? 12 : 13, Sub));          // 粉量单位紧跟输入格
        row.Children.Add(CalcOpText("×", narrow ? 15 : 17, Accent));
        row.Children.Add(CalcOpText("1 :", narrow ? 12.5 : 14, Sub));
        row.Children.Add(_calcRatioBox);
        row.Children.Add(CalcOpText("=", narrow ? 15 : 17, Accent));
        row.Children.Add(_calcWaterBox);
        row.Children.Add(CalcOpText("g", narrow ? 12 : 13, Sub));
        return row;
    }

    /// <summary>公式行内的运算符 / 单位文本：垂直居中、不抢视觉焦点。</summary>
    private TextBlock CalcOpText(string s, double size, string fg)
        => new TextBlock
        {
            Text = s,
            FontSize = size * _scale,
            FontWeight = FontWeight.Bold,
            Foreground = Brush(fg),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

    /// <summary>
    /// 计算器数字输入框：大触摸高度、深底浅字、右对齐便于改数。
    /// 注意：用 GetObservable(TextProperty) 而非 TextChanged——后者对「程序化赋值」不触发，
    /// 三联算需要捕捉代码写入（SetCalcBox）与用户输入两条路径（与 ShowPlanPicker 搜索框同款做法）。
    /// </summary>
    private TextBox MakeCalcBox(string initial, Action<TextBox> onChanged, string? watermark = null)
    {
        var box = new TextBox
        {
            Text = initial,
            Watermark = watermark,   // 空值时提示字段名（一行公式布局下不再依赖框后范围文本）
            Foreground = Brush(Fg),
            Background = Brush("#1B120C"),
            BorderBrush = Brush("#4A3829"),   // 常态描边；越界时切换为 Warn 色
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),  // 与按钮/卡片同族圆角，输入框不再是"方盒子"
            Padding = new Thickness(10, 0),
            MinHeight = 48,   // 触摸目标 ≥48（拇指友好）
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Right,
        };
        box.GetObservable(TextBox.TextProperty)
           .Subscribe(new TextChangeObserver(v => onChanged(box)));
        return box;
    }

    /// <summary>正数解析（兼容逗号小数点；空/非法返回 false）。</summary>
    private static bool TryCalcDouble(string? s, out double v)
    {
        v = 0;
        var t = (s ?? "").Trim().Replace(',', '.');
        if (t.Length == 0) return false;
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
        return v > 0;
    }

    /// <summary>保留数字 + 至多一个小数点（粉量 / 总注水量允许 1 位小数）。</summary>
    private static string FilterCalcDecimal(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool dot = false;
        int decimals = 0;
        foreach (var ch in s)
        {
            if (char.IsDigit(ch))
            {
                if (dot && decimals >= 1) continue;   // 只保留 1 位小数（粉量 / 总注水量）
                if (dot) decimals++;
                sb.Append(ch);
            }
            else if ((ch == '.' || ch == ',') && !dot) { sb.Append('.'); dot = true; }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 比值过滤（整数语义）：小数点及其后的小数部分整体丢弃，其余非数字字符一律剔除。
    /// 例：「15.5」→「15」；「1a2」→「12」。保证输入框内永远不存在小数。
    /// </summary>
    private static string FilterCalcRatio(string s)
    {
        int dot = s.IndexOfAny(new[] { '.', ',' });
        var head = dot >= 0 ? s.Substring(0, dot) : s;
        return new string(head.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// 输入净化：剔除非法字符（比值取整数部分，粉量/总注水量保留数字与一个小数点），
    /// 净化后文本变化才写回（写回时抑制联动重入），并把光标放在「净化后前缀」末尾，避免跳到行尾。
    /// </summary>
    private void SanitizeCalcInput(TextBox box, bool ratioMode)
    {
        var raw = box.Text ?? "";
        var clean = ratioMode ? FilterCalcRatio(raw) : FilterCalcDecimal(raw);
        if (clean == raw) return;
        int caret = Math.Clamp(box.CaretIndex, 0, raw.Length);
        int keptBefore = ratioMode
            ? FilterCalcRatio(raw.Substring(0, caret)).Length
            : FilterCalcDecimal(raw.Substring(0, caret)).Length;
        _calcUpdating = true;
        box.Text = clean;
        _calcUpdating = false;
        box.CaretIndex = Math.Clamp(keptBefore, 0, clean.Length);
    }

    /// <summary>程序化赋值（抑制联动重入）。integer=true 时输出整数（比值），否则保留 1 位小数。</summary>
    private void SetCalcBox(TextBox box, double v, bool integer = false)
    {
        _calcUpdating = true;
        box.Text = integer
            ? Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        _calcUpdating = false;
    }

    /// <summary>数字友好格式：整数不带小数尾巴（15 而非 15.0），否则保留 1 位小数。</summary>
    private static string FmtCalcNum(double v) =>
        v == Math.Floor(v) ? v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                           : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 区间校验（2026-09-10 新增）：粉量 5–50 g / 粉水比 1–30 整数 / 总注水量 5–2000 g。
    /// 只对「能解析但越界」的值报错——空值代表「留空以便反推」，不算违规。
    /// 同步刷新越界框描边与提示栏可见性，并返回三态是否合法供联动逻辑使用。
    /// </summary>
    private (bool DoseOk, bool RatioOk, bool WaterOk) CalcValidate()
    {
        double? d = null, w = null;
        int? r = null;
        if (_calcDoseBox != null && TryCalcDouble(_calcDoseBox.Text, out var dv)) d = dv;
        if (_calcWaterBox != null && TryCalcDouble(_calcWaterBox.Text, out var wv)) w = wv;
        if (_calcRatioBox != null)
        {
            var rt = (_calcRatioBox.Text ?? "").Trim();
            if (rt.Length > 0 && int.TryParse(rt, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var rv)) r = rv;
        }

        bool dBad = d.HasValue && (d.Value < CalcDoseMin || d.Value > CalcDoseMax);
        bool wBad = w.HasValue && (w.Value < CalcWaterMin || w.Value > CalcWaterMax);
        bool rBad = r.HasValue && (r.Value < CalcRatioMin || r.Value > CalcRatioMax);

        var msgs = new List<string>();
        if (dBad) msgs.Add(string.Format(I18n.T("CalcRangeDose"), FmtCalcNum(d!.Value)));
        if (rBad) msgs.Add(string.Format(I18n.T("CalcRangeRatio"), r!.Value));
        if (wBad) msgs.Add(string.Format(I18n.T("CalcRangeWater"), FmtCalcNum(w!.Value)));

        MarkCalcInvalid(_calcDoseBox, dBad);
        MarkCalcInvalid(_calcRatioBox, rBad);
        MarkCalcInvalid(_calcWaterBox, wBad);

        if (_calcWarnText != null)
        {
            _calcWarnText.Text = string.Join("\n", msgs);
            _calcWarnText.IsVisible = msgs.Count > 0;
        }
        return (!dBad, !rBad, !wBad);
    }

    /// <summary>越界输入框红色描边；恢复合法时回到常态描边。</summary>
    private static void MarkCalcInvalid(TextBox? box, bool invalid)
    {
        if (box == null) return;
        box.BorderBrush = Brush(invalid ? Warn : "#3A2A1C");
        box.BorderThickness = new Thickness(invalid ? 1.5 : 1);
    }

    /// <summary>改粉量：比值有效 → 总注水量 = 粉量 × 比值；总注水量有效且比值空 → 比值 = 总注水量 ÷ 粉量。</summary>
    private void CalcOnDoseChanged(TextBox box)
    {
        if (_calcUpdating || _calcDoseBox == null || _calcRatioBox == null || _calcWaterBox == null) return;
        SanitizeCalcInput(box, ratioMode: false);
        if (!CalcValidate().DoseOk) return;                  // 粉量越界 → 不把非法值扩散到另两框
        if (!TryCalcDouble(_calcDoseBox.Text, out var d)) return;
        if (TryCalcDouble(_calcRatioBox.Text, out var r)) SetCalcBox(_calcWaterBox, d * r);
        else if (TryCalcDouble(_calcWaterBox.Text, out var w)) SetCalcBox(_calcRatioBox, w / d, integer: true);
        CalcValidate();                                      // 联动结果可能越界 → 复检提示
    }

    /// <summary>改比值：粉量有效 → 总注水量 = 粉量 × 比值；总注水量有效且粉量空 → 粉量 = 总注水量 ÷ 比值。</summary>
    private void CalcOnRatioChanged(TextBox box)
    {
        if (_calcUpdating || _calcDoseBox == null || _calcRatioBox == null || _calcWaterBox == null) return;
        SanitizeCalcInput(box, ratioMode: true);             // 比值：整数，不接受小数点
        if (!CalcValidate().RatioOk) return;
        if (!TryCalcDouble(_calcRatioBox.Text, out var r)) return;
        if (TryCalcDouble(_calcDoseBox.Text, out var d)) SetCalcBox(_calcWaterBox, d * r);
        else if (TryCalcDouble(_calcWaterBox.Text, out var w)) SetCalcBox(_calcDoseBox, w / r);
        CalcValidate();
    }

    /// <summary>
    /// 改总注水量（反推）：优先反算粉量——比值保持不变，粉量 = 总注水量 ÷ 比值；
    /// 比值为空时改为输出比值 = 总注水量 ÷ 粉量（2026-09-09 按需求：比值不动，粉量优先）。
    /// </summary>
    private void CalcOnWaterChanged(TextBox box)
    {
        if (_calcUpdating || _calcDoseBox == null || _calcRatioBox == null || _calcWaterBox == null) return;
        SanitizeCalcInput(box, ratioMode: false);            // 总注水量：允许 1 位小数
        if (!CalcValidate().WaterOk) return;                 // 总注水量越界 → 不反推
        if (!TryCalcDouble(_calcWaterBox.Text, out var w)) return;
        if (TryCalcDouble(_calcRatioBox.Text, out var r))
            SetCalcBox(_calcDoseBox, w / r);           // 比值保持不变，优先反算粉量
        else if (TryCalcDouble(_calcDoseBox.Text, out var d))
            SetCalcBox(_calcRatioBox, w / d, integer: true);   // 比值为空时输出比值（整数）
        CalcValidate();
    }

    /// <summary>启动计时：清空历史分段，从 0 开始第 1 段（Stopwatch 毫秒精度）。</summary>
    private void CalcStart()
    {
        _calcSegs.Clear();
        _calcSw = System.Diagnostics.Stopwatch.StartNew();
        _calcSegStartMs = 0;
        _calcRunning = true;
        RebuildCalcTimerList();
        UpdateCalcButtons();
        EnsureCalcTick();
        UpdateCalcRing();
    }

    /// <summary>
    /// 下一段：结束当前段（起点→此刻）、开启下一行计时，段落序号递增。
    /// **不停表**——计时继续走，圆环归零重扫，只有点「停止」才结束整个计时（2026-09-10 按需求）。
    /// </summary>
    private void CalcNext()
    {
        if (!_calcRunning || _calcSw == null) return;
        double now = _calcSw.Elapsed.TotalMilliseconds;
        _calcSegs.Add(now - _calcSegStartMs);
        _calcSegStartMs = now;
        RebuildCalcTimerList();
        UpdateCalcButtons();
        UpdateCalcRing();      // 圆环归零、圆心数字从 00:00.0 重新计
        EnsureCalcTick();      // 确保时钟未停（下一段不打断计时）
    }

    /// <summary>停止：结束当前段（起点→停止时刻）并在清单末尾追加「总耗时」行。</summary>
    private void CalcStop()
    {
        if (!_calcRunning || _calcSw == null) return;
        double now = _calcSw.Elapsed.TotalMilliseconds;
        _calcSegs.Add(now - _calcSegStartMs);
        _calcSegStartMs = now;
        _calcRunning = false;
        _calcSw.Stop();
        RebuildCalcTimerList();
        UpdateCalcButtons();
        _calcTick?.Stop();
        UpdateCalcRing();      // 停止后圆环清空、圆心显示本段终值 + 说明改「已停止」
    }

    /// <summary>毫秒精度时长格式：mm:ss.fff。</summary>
    private static string CalcFormatMs(double ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }

    /// <summary>累计总时长（ms）：计时中 = 秒表总时长；未启动 = 0；已停止 = 各段之和。</summary>
    private double CalcTotalMs()
    {
        if (_calcRunning && _calcSw != null) return _calcSw.Elapsed.TotalMilliseconds;
        double total = 0;
        foreach (var s in _calcSegs) total += s;
        return total;
    }

    /// <summary>
    /// 刷新机械秒表：大秒针（60 秒一圈）与小分针（30 分钟一圈）按「总耗时」走针，
    /// 表盘下方状态文字随状态切换（计时中 / 待启动 / 已停止），右侧电子秒表同步刷新。
    /// </summary>
    private void UpdateCalcRing()
    {
        // 机械秒表走针：以「总耗时」驱动，大秒针 60 秒一圈、小分针 30 分钟一圈
        double totalMs = CalcTotalMs();
        double secFrac = (totalMs % 60000.0) / 60000.0;
        double minFrac = (totalMs % 1800000.0) / 1800000.0;
        if (_calcRingD > 0)
        {
            double cx = _calcRingD / 2, cy = _calcRingD / 2;
            double rSec = Math.Max(6, _calcRingD / 2 - 11 - _calcRingD * 0.10);
            if (_calcRingHand != null)
            {
                _calcRingHand.StartPoint = CalcRingPoint(cx, cy, _calcRingD * 0.09, secFrac * 360.0 + 180.0); // 尾部配重
                _calcRingHand.EndPoint = CalcRingPoint(cx, cy, rSec, secFrac * 360.0);
            }
            if (_calcRingMinHand != null)
                _calcRingMinHand.EndPoint = CalcRingPoint(cx, cy, _calcRingD * 0.28, minFrac * 360.0);
        }
        if (_calcRingText != null)
            _calcRingText.Text = I18n.T(_calcRunning ? "CalcSegCur" : (_calcSegs.Count > 0 ? "CalcSegStopped" : "CalcSegIdle"));
        if (_calcTotalText != null)
            _calcTotalText.Text = CalcFormatMsHundredths(totalMs);   // 右侧电子秒表 = 累计总耗时
    }

    /// <summary>百分秒精度格式：mm:ss.ff（电子秒表读数，等宽显示不跳动）。</summary>
    private static string CalcFormatMsHundredths(double ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
    }

    /// <summary>重建计时清单：已结束段逐行（序号递增）；进行中段实时行；停止后追加总耗时行。</summary>
    private void RebuildCalcTimerList()
    {
        if (_calcTimerList == null) return;
        _calcTimerList.Children.Clear();
        _calcLiveText = null;
        for (int i = 0; i < _calcSegs.Count; i++)
            _calcTimerList.Children.Add(Text($"{string.Format(I18n.T("CalcSeg"), i + 1)}   {CalcFormatMs(_calcSegs[i])}",
                13, FontWeight.SemiBold, Fg));

        if (_calcRunning && _calcSw != null)
        {
            // 与圆环一致：显示「本段」已用时长（总耗时另有环下数字行 + 停止后的总耗时行）
            _calcLiveText = Text($"{string.Format(I18n.T("CalcSeg"), _calcSegs.Count + 1)}   {CalcFormatMs(_calcSw.Elapsed.TotalMilliseconds - _calcSegStartMs)}",
                13, FontWeight.SemiBold, Accent);
            _calcTimerList.Children.Add(_calcLiveText);
        }
        else if (_calcSegs.Count > 0)
        {
            double total = 0;
            foreach (var s in _calcSegs) total += s;
            _calcTimerList.Children.Add(Text($"{I18n.T("CalcTotal")}   {CalcFormatMs(total)}",
                14, FontWeight.Bold, Accent));
        }
    }

    /// <summary>
    /// 三枚计时按钮随状态联动（2026-09-10）：
    /// 启动 = 空闲可用（圆形，常态琥珀色）；下一段 = 计时中可用且**变蓝**、未启动时灰底禁用；停止 = 计时中可用（红），空闲禁用。
    /// </summary>
    private void UpdateCalcButtons()
    {
        if (_calcStartBtn != null) _calcStartBtn.IsEnabled = !_calcRunning;
        if (_calcNextBtn != null)
        {
            _calcNextBtn.IsEnabled = _calcRunning;
            _calcNextBtn.Background = Brush(_calcRunning ? BtnBlue : BtnMuted);   // 启动后才变蓝
        }
        if (_calcStopBtn != null) _calcStopBtn.IsEnabled = _calcRunning;
    }

    /// <summary>实时刷新定时器（50ms）：进行中分段的行文本 + 圆形时间（数字与进度弧）。</summary>
    private void EnsureCalcTick()
    {
        if (_calcTick != null) { _calcTick.Start(); return; }
        _calcTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _calcTick.Tick += (_, _) =>
        {
            if (!_calcRunning) return;
            if (_calcSw != null && _calcLiveText != null)
                _calcLiveText.Text = $"{string.Format(I18n.T("CalcSeg"), _calcSegs.Count + 1)}   {CalcFormatMs(_calcSw.Elapsed.TotalMilliseconds - _calcSegStartMs)}";
            UpdateCalcRing();
        };
        _calcTick.Start();
    }

    /// <summary>移动端单视图平台（Android/iOS/MacCatalyst）不支持构造/显示二级 Window（会抛 NotSupportedException）。</summary>
    private static bool IsMobilePlatform =>
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    // =====================================================================
    // 新手模式（Beginner Mode）：专业模式的单屏简化引导版。
    // 流程：① 图形化选滤杯 → ② 选烘焙度（深/中/浅）→ ③ 引擎自动给冲煮建议
    // （研磨度/水温/粉水比/总注水量）+ 一键「模拟冲煮节奏」（驱动 BrewEngine 自动走段）。
    // =====================================================================

    /// <summary>打开新手模式层（覆盖在 Home 之上；重建以吸收语言/布局变化）。</summary>
    private void ShowBeginner()
    {
        _begOpen = true;
        StopHeroPulse();
        _begDripper = null; _begRoast = null; _begRecipe = null;
        _begSelCollapsed = false; _begSelUserExpanded = false;
        _begState = null; _begTick?.Stop();
        if (_beginnerHost != null)
        {
            _beginnerHost.Child = BuildBeginner();
            _beginnerHost.IsVisible = true;
        }
    }

    /// <summary>关闭新手模式，回到主界面。</summary>
    private void CloseBeginner()
    {
        _begOpen = false;
        _begTick?.Stop();
        if (_beginnerHost != null) _beginnerHost.IsVisible = false;
    }

    /// <summary>构建新手模式主界面：单屏纵向引导（MaxWidth 420，手机直屏）。选择区（滤杯/滤纸/烘焙度）选齐后自动折叠，点击摘要可再展开。</summary>
    private Control BuildBeginner()
    {
        var p = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // —— 页头：统一 PageHeader（🌱 模块瓷砖 + 标题 + ⌂ 返回）——
        p.Children.Add(PageHeader("🌱", "BegTitle", CardAccents.Beginner, CloseBeginner));
        p.Children.Add(Text(I18n.T("BegSubtitle"), 12, fg: Sub, wrap: true));

        // 2026-09-11：模块色渐变分隔条（草绿），呼应页头瓷砖，强化模块归属感
        p.Children.Add(new Border
        {
            Height = 3,
            Margin = new Thickness(70, 5, 70, 9),
            CornerRadius = new CornerRadius(1.5),
            Background = Palette.Diagonal(CardAccents.Beginner, "#1A100A"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // —— 折叠摘要（选齐后显示，点击展开选择区）——
        _begSelSummary = MakeButton(I18n.T("BegSelectPrompt"), (_, _) =>
        {
            _begSelCollapsed = false; _begSelUserExpanded = true; BeginnerApplySelCollapse();
        }, stretch: true, bg: "#2E2418", fg: Fg, bold: false);
        _begSelSummary.HorizontalContentAlignment = HorizontalAlignment.Left;
        _begSelSummary.IsVisible = false;
        p.Children.Add(_begSelSummary);

        // —— 选择区明细（可折叠）：① 滤杯 + 滤纸建议 + ② 烘焙度 ——
        _begSelDetail = new StackPanel { Spacing = 10 };
        // ① 图形化选滤杯（仅常用 4 款）
        _begSelDetail.Children.Add(SectionTitle(I18n.T("BegStep1")));
        _begDripperChips = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var key in BegDrippers)
            _begDripperChips.Children.Add(BeginnerDripperCard(key, I18n.T("Dripper_" + key)));
        _begSelDetail.Children.Add(_begDripperChips);
        // 滤纸建议行（选滤杯后显示）
        _begFilterPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 2) };
        _begSelDetail.Children.Add(_begFilterPanel);
        // ② 选烘焙度
        _begSelDetail.Children.Add(SectionTitle(I18n.T("BegStep2")));
        _begRoastChips = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var roasts = new (string key, string label)[]
        {
            ("dark", I18n.T("BegRoastDeep")),
            ("medium", I18n.T("BegRoastMid")),
            ("light", I18n.T("BegRoastLight")),
        };
        foreach (var r in roasts)
        {
            var chip = MakeButton(r.label, (_, _) => BeginnerSelectRoast(r.key), stretch: false, bg: "#3A2A1C", fg: Fg, bold: false);
            chip.Tag = r.key;
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.MinWidth = 96;
            _begRoastChips.Children.Add(chip);
        }
        _begSelDetail.Children.Add(_begRoastChips);
        // 收起按钮（仅选齐后出现）
        _begCollapseBtn = MakeButton(I18n.T("BegCollapse"), (_, _) =>
        {
            _begSelCollapsed = true; _begSelUserExpanded = false; BeginnerApplySelCollapse();
        }, stretch: false, bg: "#5A4636", fg: Sub, bold: false);
        _begCollapseBtn.HorizontalAlignment = HorizontalAlignment.Right;
        _begCollapseBtn.IsVisible = false;
        _begSelDetail.Children.Add(_begCollapseBtn);
        p.Children.Add(_begSelDetail);

        // —— ③ 冲煮建议卡 + 冲煮模拟面板（选齐后显示）——
        p.Children.Add(SectionTitle(I18n.T("BegStep3")));
        _begSuggestCard = new Border { Background = Brush(Panel), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12), IsVisible = false, BorderBrush = new SolidColorBrush(Palette.WithAlpha(CardAccents.Beginner, 64)), BorderThickness = new Thickness(1) };
        p.Children.Add(_begSuggestCard);
        _begBrewPanel = new Border { Background = Brush(Panel), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12), IsVisible = false, BorderBrush = new SolidColorBrush(Palette.WithAlpha(CardAccents.Beginner, 64)), BorderThickness = new Thickness(1) };
        p.Children.Add(_begBrewPanel);

        // 2026-09-11：新手模式底部署名条（版本 + 作者），呼应主界面页脚
        var begFooter = Text($"{I18n.T("Author")}  ·  v{AppVer}", 10, fg: Sub, wrap: true);
        begFooter.TextAlignment = TextAlignment.Center;
        begFooter.HorizontalAlignment = HorizontalAlignment.Center;
        begFooter.Opacity = 0.8;
        begFooter.Margin = new Thickness(0, 10, 0, 4);
        p.Children.Add(begFooter);

        // 2026-09-11：外层包 Grid，底层放轻量暖光氛围层（与 Home 同语言）
        return new Grid
        {
            Children =
            {
                BuildAmbientBackdrop(),
                new ScrollViewer
                {
                    Content = p,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            },
        };
    }

    /// <summary>单张滤杯选择卡：上方矢量线稿图标 + 下方标签，整卡可点；记录 glyph 形状以便选中时改色。</summary>
    private Button BeginnerDripperCard(string key, string label)
    {
        var (canvas, glyphs) = BeginnerDripperGlyph(key, Sub);
        var sp = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(canvas);
        var lab = Text(label, 10.5, fg: Sub, wrap: true);
        lab.HorizontalAlignment = HorizontalAlignment.Center;
        lab.TextAlignment = TextAlignment.Center;
        sp.Children.Add(lab);
        var card = new Button
        {
            Content = sp,
            Background = Brush("#3A2A1C"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 8),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 84,
        };
        card.Tag = (key, glyphs, lab);   // 选中时重绘描边 + 标签色
        card.Click += (_, _) => BeginnerSelectDripper(key);
        return card;
    }

    /// <summary>滤杯矢量线稿（64×64 Canvas，纯 Line/Ellipse，无位图）：锥形 / 平底 / 滤瓶 / 浸泡阀四类轮廓。</summary>
    private (Canvas canvas, List<Avalonia.Controls.Shapes.Shape> glyphs) BeginnerDripperGlyph(string key, string stroke)
    {
        double w = 64, h = 64;
        var c = new Canvas { Width = w, Height = h };
        var glyphs = new List<Avalonia.Controls.Shapes.Shape>();
        void Add(double x1, double y1, double x2, double y2)
        {
            var ln = new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2),
                Stroke = Brush(stroke), StrokeThickness = 2, StrokeLineCap = PenLineCap.Round,
            };
            c.Children.Add(ln); glyphs.Add(ln);
        }
        void Ell(double cx, double cy, double r)
        {
            var e = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = r * 2, Height = r * 2, Stroke = Brush(stroke), StrokeThickness = 2,
            };
            Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r);
            c.Children.Add(e); glyphs.Add(e);
        }
        switch (key)
        {
            case "v60":       // 锥形：顶宽底尖
                Add(8, 12, 56, 12);     // 顶沿
                Add(56, 12, 32, 54);    // 右壁
                Add(32, 54, 8, 12);     // 左壁
                Add(8, 12, 20, 34); Add(20, 34, 32, 54);     // 左肋骨
                Add(56, 12, 44, 34); Add(44, 34, 32, 54);     // 右肋骨
                break;
            case "origami":
            case "wave":
            case "hario":     // 平底漏斗：上窄下宽
                Add(20, 12, 44, 12);    // 顶沿
                Add(44, 12, 56, 54);    // 右壁
                Add(56, 54, 8, 54);     // 底沿
                Add(8, 54, 20, 12);     // 左壁
                Add(14, 54, 50, 54);    // 底沿加粗
                break;
            case "chemex":    // 滤瓶（烧瓶轮廓）
                Add(10, 10, 54, 10);    // 瓶口
                Add(54, 10, 40, 30);    // 右颈
                Add(40, 30, 54, 56);    // 右腹
                Add(54, 56, 10, 56);    // 瓶底
                Add(10, 56, 24, 30);    // 左腹
                Add(24, 30, 10, 10);    // 左颈
                Add(22, 30, 42, 30);    // 颈部
                break;
            default:          // switch / smart / gina：浸泡杯 + 阀
                Add(16, 10, 48, 10);    // 杯口
                Add(48, 10, 48, 48);    // 右壁
                Add(48, 48, 16, 48);    // 杯底
                Add(16, 48, 16, 10);    // 左壁
                Add(16, 30, 48, 30);    // 水位线
                Ell(32, 56, 5);   // 阀
                break;
        }
        return (c, glyphs);
    }

    /// <summary>选滤杯：高亮该卡、刷新滤纸建议、双选齐则生成冲煮建议。</summary>
    private void BeginnerSelectDripper(string key)
    {
        _begDripper = key;
        if (_begDripperChips != null)
            foreach (var b in _begDripperChips.Children.OfType<Button>())
            {
                var tag = ((string, List<Avalonia.Controls.Shapes.Shape> glyphs, TextBlock lab))b.Tag!;
                bool on = tag.Item1 == key;
                // 2026-09-11 选中态强化：模块草绿底 + 1px 深描边（未选 = 皮革棕 + 透明描边），
                // glyph 与标签同步反色，选中卡在四个候选中一眼可辨。
                b.Background = Brush(on ? CardAccents.Beginner : Palette.Leather);
                b.BorderBrush = on ? Brush("#1A100A") : Avalonia.Media.Brushes.Transparent;
                b.BorderThickness = new Thickness(1);
                foreach (var g in tag.glyphs) g.Stroke = Brush(on ? "#1A100A" : Sub);
                tag.lab.Foreground = Brush(on ? "#1A100A" : Sub);
            }
        BeginnerRefreshFilter();
        BeginnerRecompute();
    }

    /// <summary>选烘焙度：高亮该卡（模块草绿 + 描边，与滤杯卡同语言）、双选齐则生成冲煮建议。</summary>
    private void BeginnerSelectRoast(string key)
    {
        _begRoast = key;
        if (_begRoastChips != null)
            foreach (var b in _begRoastChips.Children.OfType<Button>())
            {
                bool on = (string)b.Tag! == key;
                b.Background = Brush(on ? CardAccents.Beginner : Palette.Leather);
                b.BorderBrush = on ? Brush("#1A100A") : Avalonia.Media.Brushes.Transparent;
                b.BorderThickness = new Thickness(1);
            }
        BeginnerRecompute();
    }

    /// <summary>滤纸建议：按所选滤杯直接给出具体、通俗的滤纸（如 V60 漂白滤纸 / 蛋糕杯漂白滤纸），无需「放入滤纸」操作。</summary>
    private void BeginnerRefreshFilter()
    {
        if (_begFilterPanel == null) return;
        _begFilterPanel.Children.Clear();
        if (_begDripper == null) return;
        string fk = BegFilterKey.TryGetValue(_begDripper, out var key) ? I18n.T(key) : I18n.T("Filter_bleached");
        _begFilterPanel.Children.Add(Text($"{I18n.T("BegFilterSuggest")}：{fk}", 12.5, FontWeight.Bold, Accent));
        _begFilterPanel.Children.Add(Text(I18n.T("BegFilterPlace"), 11, fg: Sub, wrap: true));
        BeginnerRefreshSelSummary();
    }

    /// <summary>双选齐 → 引擎给配方；重建建议卡；首次选齐自动折叠选择区（用户手动展开后不再强制收起）。</summary>
    private void BeginnerRecompute()
    {
        if (_begDripper == null || _begRoast == null) return;
        _begRecipe = BrewEngine.ComputeRecipe(new RecipeOptions
        {
            Dripper = _begDripper,
            Roast = _begRoast,
            Dose = 15,
            Method = "classic",
        });
        BeginnerBuildSuggest();
        if (!_begSelUserExpanded)
        {
            _begSelCollapsed = true;
            BeginnerApplySelCollapse();
        }
    }

    /// <summary>折叠/展开选择区：同步明细与摘要可见性、收起按钮可见性，并刷新摘要文案。</summary>
    private void BeginnerApplySelCollapse()
    {
        if (_begSelDetail != null) _begSelDetail.IsVisible = !_begSelCollapsed;
        if (_begSelSummary != null) _begSelSummary.IsVisible = _begSelCollapsed;
        if (_begCollapseBtn != null) _begCollapseBtn.IsVisible = _begRecipe != null;
        BeginnerRefreshSelSummary();
    }

    /// <summary>刷新折叠摘要文案：滤杯 · 滤纸 · 烘焙度（未选齐显示引导语）。</summary>
    private void BeginnerRefreshSelSummary()
    {
        if (_begSelSummary == null) return;
        if (_begDripper == null && _begRoast == null)
        {
            _begSelSummary.Content = I18n.T("BegSelectPrompt");
            return;
        }
        var parts = new List<string>();
        if (_begDripper != null)
        {
            parts.Add(I18n.T("Dripper_" + _begDripper));
            if (BegFilterKey.TryGetValue(_begDripper, out var fk)) parts.Add(I18n.T(fk));
        }
        if (_begRoast != null) parts.Add(I18n.T("Roast_" + _begRoast));
        _begSelSummary.Content = string.Join(" · ", parts);
    }

    /// <summary>建议卡内容：跟随提示横幅 + 2 列参数 stat 卡（标签在上、数值在下，绝无重叠）+ 开始模拟按钮。</summary>
    private void BeginnerBuildSuggest()
    {
        if (_begSuggestCard == null || _begRecipe == null) return;
        var r = _begRecipe;
        var sp = new StackPanel { Spacing = 10 };

        // 跟随模拟节奏提示横幅（#2）
        sp.Children.Add(BegHintBanner(I18n.T("BegFollowHint")));

        // 参数 stat 卡：2 列 WrapPanel（每卡固定宽度并列，标签/数值纵向排布，彻底消除重叠）
        var grid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 186,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        grid.Children.Add(BegStatCard("⚖", I18n.T("BegDose"), $"{FmtCalcNum(r.Dose)} {I18n.T("BegGram")}"));
        grid.Children.Add(BegStatCard("🌡", I18n.T("BegTemp"), $"{r.Temp} ℃"));
        grid.Children.Add(BegStatCard("⚙", I18n.T("BegGrind"),
            $"{r.GrindLabel}\n{I18n.T("BegGrindUnit")}  C40≈{r.GrindC40} / EK≈{r.GrindEK}"));
        grid.Children.Add(BegStatCard("💧", I18n.T("BegRatio"), $"1 : {(int)Math.Round(r.Ratio)}"));
        grid.Children.Add(BegStatCard("🫗", I18n.T("BegWater"), $"{(int)Math.Round(r.TotalWater)} g"));
        sp.Children.Add(grid);

        _begStartBtn = MakeButton("▶ " + I18n.T("BegStartSim"), (_, _) => BeginnerStartSim(),
            stretch: true, bg: AccentBtn, fg: "#1A100A", bold: true);
        sp.Children.Add(_begStartBtn);
        _begSuggestCard.Child = sp;
        _begSuggestCard.IsVisible = true;
    }

    /// <summary>单个参数 stat 卡：左侧 4px 草绿(SageGreen)竖条 + 图标瓷砖 + 标签(弱) + 焦糖金大数值（纵向排布，天然不重叠）。</summary>
    private Border BegStatCard(string icon, string label, string value)
    {
        // 2026-09-11 视觉升级：数值放大为视觉主角（17pt SemiBold 焦糖金），标签缩小弱化（10.5pt），
        // 左侧草绿竖条 + 图标瓷砖强化模块识别；卡底暖深棕 + 1px 内描边高光，形成"数据瓷片"质感。
        var c = new Border
        {
            Background = Brush("#2A1E13"),
            BorderBrush = new SolidColorBrush(Palette.WithAlpha(Palette.Caramel, 36)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 6, 6),
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), VerticalAlignment = VerticalAlignment.Center };
        // 左侧草绿竖条
        var bar = new Border
        {
            Background = Brush(CardAccents.Beginner),
            Width = 4,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(bar, 0);
        row.Children.Add(bar);
        // 图标瓷砖
        var iconTile = new Border
        {
            Background = new SolidColorBrush(Palette.WithAlpha(CardAccents.Beginner, 38)),
            CornerRadius = new CornerRadius(8),
            Width = 32, Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var iconT = Text(icon, 16, FontWeight.SemiBold, CardAccents.Beginner);
        iconT.HorizontalAlignment = HorizontalAlignment.Center;
        iconT.VerticalAlignment = VerticalAlignment.Center;
        iconTile.Child = iconT;
        Grid.SetColumn(iconTile, 1);
        row.Children.Add(iconTile);
        // 文案（标签 + 数值）
        var sp = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var lb = Text(label, 10.5, fg: Sub);
        lb.Opacity = 0.85;
        sp.Children.Add(lb);
        var val = Text(value, 17, FontWeight.Bold, Palette.Caramel, wrap: true);
        sp.Children.Add(val);
        Grid.SetColumn(sp, 2);
        row.Children.Add(sp);

        c.Child = row;
        return c;
    }

    /// <summary>通用提示横幅：暖色底 + 左侧 accent 竖条 + 可换行文案（用于跟随提示 / 专家模式提示）。
    /// 2026-09-11 升级：左侧 3dp 焦糖竖条强化信息层级，文案微升至 12.5→13。</summary>
    private Border BegHintBanner(string text)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var bar = new Border
        {
            Background = Brush(Palette.Caramel),
            Width = 3,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(bar, 0);
        row.Children.Add(bar);
        var t = Text(text, 13, fg: Palette.Caramel, wrap: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        row.Children.Add(t);
        return new Border
        {
            Background = Brush("#2E2418"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8),
            Child = row,
        };
    }

    /// <summary>开始模拟冲煮：建运行时状态、清零自增时钟、启动 100ms 定时器自动走段。</summary>
    private void BeginnerStartSim()
    {
        if (_begRecipe == null) return;
        BeginnerBuildBrew();
        _begState = BrewEngine.CreateState(_begRecipe);
        BrewEngine.Start(_begState, 0);
        _begSimMs = 0; _begSimWeight = 0;
        EnsureBeginnerTick();
        BeginnerRender(BrewEngine.SnapshotOf(_begState));
    }

    /// <summary>构建冲煮模拟面板：圆形进度环 + 当前步骤 + 计时 + 阶段时间线 + 重新开始。</summary>
    private void BeginnerBuildBrew()
    {
        if (_begBrewPanel == null || _begRecipe == null) return;
        double d = BegRingDiam * _scale;
        var sp = new StackPanel { Spacing = 10 };

        // 圆形进度环：底环 + 60 格刻度（按整体进度着色，规避 PathDashArray）+ 中心「阶段 n/m」
        var ringGrid = new Grid { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Center };
        ringGrid.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = d, Height = d, Stroke = Brush("#3A2A1C"), StrokeThickness = 8,
        });
        double cx = d / 2, cy = d / 2;
        double rTick = d / 2 - 8;
        _begRingTicks = new List<Avalonia.Controls.Shapes.Line>();
        for (int i = 0; i < 60; i++)
        {
            double a = i * 6.0;
            double rIn = rTick - (i % 5 == 0 ? 9 : 5);
            var ln = new Avalonia.Controls.Shapes.Line
            {
                StartPoint = CalcRingPoint(cx, cy, rIn, a),
                EndPoint = CalcRingPoint(cx, cy, rTick, a),
                Stroke = Brush("#3A2A1C"),
                StrokeThickness = i % 5 == 0 ? 2.2 : 1.3,
                StrokeLineCap = PenLineCap.Round,
            };
            ringGrid.Children.Add(ln);
            _begRingTicks.Add(ln);
        }
        _begProgressText = Text("0/0", d * 0.16, FontWeight.Bold, Fg);
        _begProgressText.HorizontalAlignment = HorizontalAlignment.Center;
        _begProgressText.VerticalAlignment = VerticalAlignment.Center;
        ringGrid.Children.Add(_begProgressText);
        sp.Children.Add(ringGrid);

        _begPhaseText = Text(I18n.T("BegSimRunning"), 13, fg: Fg, wrap: true);
        _begPhaseText.TextAlignment = TextAlignment.Center;
        _begTimerText = Text("00:00", 22, FontWeight.Bold, Accent);
        _begTimerText.FontFamily = new FontFamily("Consolas, Menlo, monospace");
        _begTimerText.HorizontalAlignment = HorizontalAlignment.Center;
        _begSimHint = Text(I18n.T("BegSimRunning"), 11, fg: Sub);
        _begSimHint.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(_begPhaseText);
        sp.Children.Add(_begTimerText);
        sp.Children.Add(_begSimHint);

        // 当前阶段进度条 + 数值提示（注水/等待各显其意）(#4)
        _begPhaseBarLabel = Text("", 12, fg: Fg, wrap: true);
        _begPhaseBarLabel.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(_begPhaseBarLabel);
        _begPhaseBar = new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = 0, Height = 12,
            Foreground = Brush(Accent), Background = Brush("#3A2A1C"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 4),
        };
        sp.Children.Add(_begPhaseBar);

        _begPhaseList = new StackPanel { Spacing = 4 };
        foreach (var ph in _begRecipe.Phases)
            _begPhaseList.Children.Add(Text(
                $"{(ph.Type == PhaseType.Pour ? "💧" : "⏳")} {ph.Name}", 12, fg: Sub));
        sp.Children.Add(new ScrollViewer
        {
            Content = _begPhaseList,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        _begResetBtn = MakeButton("↺ " + I18n.T("BegSimReset"), (_, _) => BeginnerReset(),
            stretch: true, bg: "#5A4636", fg: Fg, bold: true);
        sp.Children.Add(_begResetBtn);

        _begBrewPanel.Child = sp;
        _begBrewPanel.IsVisible = true;
        _begSuggestCard!.IsVisible = false;   // 模拟中隐藏建议卡
    }

    /// <summary>每帧渲染：进度弧 + 中心阶段号 + 计时 + 当前步骤提示 + 时间线高亮。</summary>
    private void BeginnerRender(Snapshot snap)
    {
        if (_begRingTicks != null && _begRecipe != null)
        {
            int lit = (int)Math.Round(Math.Clamp(snap.OverallProgress, 0, 1) * _begRingTicks.Count);
            for (int i = 0; i < _begRingTicks.Count; i++)
                _begRingTicks[i].Stroke = Brush(i < lit ? Accent : "#3A2A1C");
        }
        if (_begProgressText != null)
            _begProgressText.Text = snap.Done ? "✓" : $"{snap.PhaseNo}/{snap.TotalPhases}";
        if (_begTimerText != null)
            _begTimerText.Text = CalcFormatMsHundredths(snap.ElapsedMs);
        if (_begPhaseText != null)
            _begPhaseText.Text = snap.Done ? I18n.T("BegSimDone") : (snap.Phase?.Tip ?? I18n.T("BegSimRunning"));
        if (_begSimHint != null)
            _begSimHint.Text = snap.Done ? "" : I18n.T("BegSimRunning");
        // 当前阶段进度条 + 数值提示（注水/等待，颜色区分）(#4)
        if (_begPhaseBar != null && _begPhaseBarLabel != null)
        {
            if (snap.Done)
            {
                _begPhaseBar.Value = 1;
                _begPhaseBarLabel.Text = I18n.T("BrewDone");
            }
            else
            {
                var ph = snap.Phase;
                _begPhaseBar.Value = ph?.Progress ?? 0;
                if (ph != null)
                {
                    if (ph.Type == PhaseType.Pour)
                    {
                        _begPhaseBar.Foreground = Brush("#6FA8DC");   // 注水：蓝
                        double cur = Math.Min(_begSimWeight, ph.Target);
                        _begPhaseBarLabel.Text = $"💧 {I18n.T("PhasePourBar")}  {FmtCalcNum(cur)} / {FmtCalcNum(ph.Target)} g";
                    }
                    else
                    {
                        _begPhaseBar.Foreground = Brush("#E8C79A");   // 等待：琥珀
                        int dur = (_begRecipe != null && snap.PhaseIndex < _begRecipe.Phases.Count)
                            ? _begRecipe.Phases[snap.PhaseIndex].DurationSec : 0;
                        double rem = ph.RemainingSec ?? 0;
                        _begPhaseBarLabel.Text = $"⏳ {I18n.T("PhaseWaitBar")}  {rem:0} / {dur}s";
                    }
                }
            }
        }
        if (_begPhaseList != null)
        {
            int cur = snap.Done ? -1 : snap.PhaseIndex;
            int i = 0;
            foreach (var tb in _begPhaseList.Children.OfType<TextBlock>().ToList())
            {
                tb.Foreground = Brush(i == cur ? Accent : Sub);
                tb.FontWeight = i == cur ? FontWeight.Bold : FontWeight.Normal;
                i++;
            }
        }
    }

    /// <summary>单步推进模拟（自增时钟 + 按推荐流速注水；headless 可反射调用步进）。</summary>
    private void BeginnerStep(double dtMs)
    {
        if (_begState == null || !_begState.Running || _begRecipe == null) return;
        _begSimMs += dtMs;
        var ph = _begState.PhaseIndex < _begRecipe.Phases.Count ? _begRecipe.Phases[_begState.PhaseIndex] : null;
        if (ph != null && ph.Type == PhaseType.Pour && _begSimWeight < ph.Target)
            _begSimWeight = Math.Min(ph.Target, _begSimWeight + _begRecipe.RecommendedFlow * (dtMs / 1000.0));
        var snap = BrewEngine.Tick(_begState, _begSimMs, _begSimWeight);
        BeginnerRender(snap);
        if (_begState.Done) _begTick?.Stop();
    }

    /// <summary>实时刷新定时器（100ms）；完成自动停。</summary>
    private void EnsureBeginnerTick()
    {
        if (_begTick != null) { _begTick.Start(); return; }
        _begTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _begTick.Tick += (_, _) => BeginnerStep(100);
        _begTick.Start();
    }

    /// <summary>重新开始：停表、清空状态、回到建议卡（可再次模拟）。</summary>
    private void BeginnerReset()
    {
        _begTick?.Stop();
        _begState = null;
        _begBrewPanel!.IsVisible = false;
        if (_begRecipe != null) BeginnerBuildSuggest();
    }

    /// <summary>
    /// 模态弹窗统一入口：桌面端弹出二级 <see cref="Window"/>（保持既有行为与 headless 可反射字段）；
    /// 移动端改用整屏遮罩浮层（半透明黑底 + 卡片 + ✕ 关闭），内容可滚动、自带 44px 触摸目标。
    /// 返回 (close 动作, 桌面端 Window 实例（移动端为 null，供外部 holder 字段赋值）)。
    /// </summary>
    private (Action close, Window? window) PresentModal(
        string title, Control content, double deskW, double deskH, Action? onClosed = null)
    {
#if !ANDROID && !IOS && !MACCATALYST
        if (!IsMobilePlatform)
        {
            var dlg = new Window
            {
                Title = title, Width = deskW, Height = deskH,
                Background = Brush(Bg), Foreground = Brush(Fg),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = content,
            };
            dlg.Closed += (_, _) => onClosed?.Invoke();
            // 有可见主窗时以它为 owner（居中于主窗）；headless 等主窗未 Show 的场景用无主 Show（否则抛 "non-visible owner"）
            if (IsVisible) dlg.Show(this); else dlg.Show();
            return (() => dlg.Close(), dlg);
        }
#endif

        // —— 移动端浮层 ——
        var overlay = new Border { Background = Brush("#E6000000") };
        var titleText = Text(title, 15, FontWeight.Bold, Accent);
        titleText.TextWrapping = TextWrapping.Wrap;
        titleText.VerticalAlignment = VerticalAlignment.Center;
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(titleText, 0);
        titleRow.Children.Add(titleText);
        var x = MakeButton("✕", (_, _) => close(), bg: "#5A4636", bold: true);
        x.MinHeight = 40; x.MinWidth = 48;
        Grid.SetColumn(x, 1);
        titleRow.Children.Add(x);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(titleRow);
        panel.Children.Add(content);
        var card = new Border
        {
            Background = Brush(Bg),
            BorderBrush = Brush("#3A2A1C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        overlay.Child = card;
        _modalLayer!.Children.Add(overlay);

        void close()
        {
            if (overlay.Parent is Panel p) p.Children.Remove(overlay);
            onClosed?.Invoke();
        }
        return (close, null);
    }

    /// <summary>语言切换时重建所有静态文案（标题/作者/设置区/按钮/记录列表等）。</summary>
    private void RebuildAllText()
    {
        // 顶栏随语言切换刷新：软件名翻译；作者信息保留原文（Author 中英值均含「阳光」，不音译）。
        _titleBlock.Text = I18n.T("AppTitle");
        _authorBlock.Text = I18n.T("Author");
        // 重建左右面板；左侧分页（BuildLeft 内部按当前页重建对应面板），右侧仅实时冲煮（不含推荐区）
        _settingsHost.Child = BuildLeft();
        _brewHost.Child = new ScrollViewer { Content = BuildBrew() };
        // Home 主界面随语言切换整体重建：Hero 标语 / 四模块卡名称描述徽标 / 底部署名条全部重译
        // （2026-09-09 复盘补回：此行曾被外部同步回退，导致「只有标题翻译、模块卡回退中文」）
        if (_homeHost != null) _homeHost.Child = BuildHome();
        // 简易咖啡计算器随语言切换重建（打开状态下；计时清单由状态字段重建，运行中计时不受影响）
        if (_calcHost != null && _calcOpen) _calcHost.Child = BuildCalc();
        // 新手模式随语言切换重建（仅未运行时，避免打断正在进行的模拟冲煮）(#5)
        if (_beginnerHost != null && _begOpen && (_begState == null || !_begState.Running))
            _beginnerHost.Child = BuildBeginner();
        RebuildRecords();
    }

    /// <summary>主运输圆形键：随 VM.TransportState 同步文案与底色（红/黄/蓝/灰）；同时更新 Tag 供按下反馈松手时恢复。</summary>
    private void UpdateTransportButton()
    {
        if (_transportBtn == null) return;
        string color = _vm.TransportColorHex;
        _transportBtn.Tag = color;                 // 按下反馈松手时恢复到此色
        _transportBtn.Background = Brush(color);
        _transportBtn.Content = _vm.TransportLabel;
    }

    private StackPanel BuildSettings()
    {
        var p = new StackPanel { Spacing = 10 };

        // 标题行：冲煮方案设置（左）+ 重置（红色）+ 生成冲煮方案（靠右）
        var headRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var title = Text("☕ " + I18n.T("Settings"), 20, FontWeight.Bold, Accent);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0); headRow.Children.Add(title);
        var resetBtn = MakeButton(I18n.T("Reset"), (_, _) => _vm.ResetAll(), bg: "#C0392B", fg: "#FFFFFF", bold: true, stretch: false);
        resetBtn.VerticalAlignment = VerticalAlignment.Center;
        resetBtn.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(resetBtn, 1); headRow.Children.Add(resetBtn);
        var genBtn = MakeButton(I18n.T("GeneratePlan"), (_, _) => ShowRecommendPlan(), bg: AccentBtn, fg: "#1A100A", bold: true, stretch: false);
        genBtn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(genBtn, 2); headRow.Children.Add(genBtn);
        p.Children.Add(headRow);

        // 醒目提示：设置参数生成方案，往右进入实时冲煮模式（#6）
        p.Children.Add(BegHintBanner(I18n.T("ProSetupHint")));

        // 成对行辅助：两控件并排（紧凑模式或横屏窄列时降级单列，避免下拉框/日期框过窄截断）
        StackPanel PairRow(Control left, Control right)
        {
            if (_compact || _pairSingle)
            {
                // 手机：上下单行堆叠，控件占满宽度，避免两列挤压
                var sp = new StackPanel { Spacing = 10 };
                sp.Children.Add(left);
                sp.Children.Add(right);
                return sp;
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            left.Margin = new Thickness(0, 0, 6, 0);
            right.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(left, 0); Grid.SetColumn(right, 1);
            row.Children.Add(left); row.Children.Add(right);
            var wrap = new StackPanel(); // 包装便于外层 p 的 Spacing 统一处理
            wrap.Children.Add(row);
            return wrap;
        }

        // 1) 豆种 + 产地（同一行，节省竖向空间；选项标签走 I18n 双语）
        var varietyTitled = TitledInfo("variety", I18n.T("Variety"), Combo(
            BrewEngine.VarietyProfiles.Values.Select(v => new ComboItem(v.Id,
                I18n.T("Variety_" + v.Id) + (v.Species == "Robusta" ? I18n.T("SpeciesRobusta") : v.Species == "Blend" ? I18n.T("SpeciesBlend") : ""))).ToArray(),
            nameof(_vm.Variety)));
        var originTitled = TitledInfo("origin", I18n.T("Origin"), Combo(
            BrewEngine.OriginProfiles.Values.Select(o => new ComboItem(o.Id, I18n.T("Origin_" + o.Id))).ToArray(),
            nameof(_vm.Origin)));
        p.Children.Add(PairRow(varietyTitled, originTitled));

        // 2) 处理方式 + 烘焙日期（同一行；选项标签走 I18n 双语）
        var processTitled = TitledInfo("process", I18n.T("Process"), Combo(new[]
        {
            new ComboItem("washed",      I18n.T("Process_washed")),
            new ComboItem("natural",     I18n.T("Process_natural")),
            new ComboItem("honey",       I18n.T("Process_honey")),
            new ComboItem("anaerobic",   I18n.T("Process_anaerobic")),
            new ComboItem("wet_hulled",  I18n.T("Process_wet_hulled")),
            new ComboItem("carbonic",    I18n.T("Process_carbonic")),
            new ComboItem("barrel",      I18n.T("Process_barrel")),
            new ComboItem("k72",         I18n.T("Process_k72")),
        }, nameof(_vm.Process)));
        var roastDateTitled = TitledInfo("roastDate", I18n.T("RoastDate"), MakeDatePicker(true));
        p.Children.Add(PairRow(processTitled, roastDateTitled));

        // 3) 烘焙度 + 烘焙值 Ag（同一行）
        //    · 烘焙值 Ag 与烘焙度档位强关联：切换档位自动取该档 Ag 区间中值（整数），可手动微调；
        //    · 选项标签走 I18n 双语，Ag 区间由引擎 RoastProfiles 注入避免漂移。
        var roastTitled = TitledInfo("roast", I18n.T("Roast"), Combo(
            BrewEngine.RoastProfiles.Values.Select(r => new ComboItem(r.Id,
                $"{I18n.T("Roast_" + r.Id)} (Ag {r.AgMin}–{r.AgMax})")).ToArray(),
            nameof(_vm.Roast)));
        var agWrap = new StackPanel { Spacing = 3 };
        agWrap.Children.Add(MakeAgBox());
        var agTitled = TitledInfo("roastAg", I18n.T("RoastAg"), agWrap);
        p.Children.Add(PairRow(roastTitled, agTitled));

        // 3.5) 咖啡豆海拔 + 密度（简约版 · 2026-09-14）
        //      海拔即密度下拉框的「便捷预填」：改海拔自动刷新密度档（≥1700 致密 / 1200–1699 中等 / <1200 疏松）；
        //      密度下拉始终可用、可直接手选覆盖（手选后不再被海拔回写）。已去掉旧「自动（由海拔推导）」开关。
        //      两控件同一行并排，风格与设备其他输入框一致（12 圆角 / 暖深底 / 细描边）。海拔不直接改水温。
        var altBox = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = _vm.BeanAltitudeM.ToString(),
            Foreground = Brush(Fg),
            Background = Brush(Panel),
            BorderBrush = new SolidColorBrush(Palette.WithAlpha(Palette.Cream, 26)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            MinHeight = 44,
        };
        var altTitled = TitledInfo("beanAltitudeM", I18n.T("BeanAltitudeM"), altBox);

        var densityCombo = Combo(new[]
        {
            new ComboItem("light",  I18n.T("Density_light")),
            new ComboItem("medium", I18n.T("Density_medium")),
            new ComboItem("dense",  I18n.T("Density_dense")),
        }, nameof(_vm.Density));
        // 密度下拉沿用统一 Combo 风格；补足圆角/内边距使其与海拔框视觉等高对齐。
        densityCombo.CornerRadius = new CornerRadius(12);
        densityCombo.Padding = new Thickness(12, 0, 12, 0);
        densityCombo.FontSize = 14;
        densityCombo.FontWeight = FontWeight.SemiBold;
        densityCombo.MinHeight = 44;
        var densityTitled = TitledInfo("density", I18n.T("Density"), densityCombo);

        // 海拔（int）文本变化写回 VM 并钳制 0–3000；VM 侧在「未手选密度」时自动联动密度下拉。
        altBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(altBox.Text, out var v))
            {
                v = Math.Clamp(v, 0, 3000);
                if (v != _vm.BeanAltitudeM) _vm.BeanAltitudeM = v;
            }
        };
        // VM 反向同步：重置/读取设置等场景把海拔与密度回灌到控件。
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.BeanAltitudeM) && altBox.Text != _vm.BeanAltitudeM.ToString())
                altBox.Text = _vm.BeanAltitudeM.ToString();
        };

        p.Children.Add(PairRow(altTitled, densityTitled));

        // 4) 粉量档位 + 粉量（成对；选项标签走 I18n 双语，范围由引擎 SizeProfiles 注入避免漂移）
        var sizeCombo = TitledInfo("size", I18n.T("Size"), Combo(
            BrewEngine.SizeProfiles.Values.Select(s => new ComboItem(s.Id,
                $"{I18n.T("Size_" + s.Id)} ({s.MinDose}–{s.MaxDose}g)")).ToArray(),
            nameof(_vm.Size)));
        var doseStepper = TitledInfo("dose", I18n.T("Dose"), MakeDoseStepper(titled: false));
        var doseRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        sizeCombo.Margin = new Thickness(0, 0, 6, 0);
        doseStepper.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(sizeCombo, 0); Grid.SetColumn(doseStepper, 1);
        doseRow.Children.Add(sizeCombo); doseRow.Children.Add(doseStepper);
        p.Children.Add(doseRow);

        // 5) 滤杯 + 滤纸（成对；选项标签走 I18n 双语）
        var dripperTitled = TitledInfo("dripper", I18n.T("Dripper"), Combo(new[]
        {
            new ComboItem("v60", I18n.T("Dripper_v60")),
            new ComboItem("wave", I18n.T("Dripper_wave")),
            new ComboItem("origami", I18n.T("Dripper_origami")),
            new ComboItem("chemex", I18n.T("Dripper_chemex")),
            new ComboItem("switch", I18n.T("Dripper_switch")),
            new ComboItem("gina", I18n.T("Dripper_gina")),
            new ComboItem("smart", I18n.T("Dripper_smart")),
            new ComboItem("hario", I18n.T("Dripper_hario")),
        }, nameof(_vm.Dripper)));
        var filterTitled = TitledInfo("filter", I18n.T("Filter"), Combo(new[]
        {
            new ComboItem("bleached", I18n.T("Filter_bleached")),
            new ComboItem("abaca", I18n.T("Filter_abaca")),
            new ComboItem("unbleached", I18n.T("Filter_unbleached")),
            new ComboItem("sisal", I18n.T("Filter_sisal")),
            new ComboItem("metal", I18n.T("Filter_metal")),
            new ComboItem("cloth", I18n.T("Filter_cloth")),
        }, nameof(_vm.Filter)));
        p.Children.Add(PairRow(dripperTitled, filterTitled));

        // 6) 冲煮方式（通俗描述 + 专业名） + 查看介绍
        var methodRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        var methodCombo = Combo(new[]
        {
            new ComboItem("classic",   I18n.T("Method_classic")),
            new ComboItem("oneshot",   I18n.T("Method_oneshot")),
            new ComboItem("dongdong", I18n.T("Method_dongdong")),
            new ComboItem("osl",       I18n.T("Method_osl")),
            new ComboItem("rao",       I18n.T("Method_rao")),
            new ComboItem("kasuya46",  I18n.T("Method_kasuya46")),
            new ComboItem("light",     I18n.T("Method_light")),
            new ComboItem("reverse",   I18n.T("Method_reverse")),
            new ComboItem("swiss",     I18n.T("Method_swiss")),
            new ComboItem("cupping",   I18n.T("Method_cupping")),
        }, nameof(_vm.Method));
        Grid.SetColumn(methodCombo, 0); methodRow.Children.Add(methodCombo);
        var introBtn = MakeButton(I18n.T("MethodIntro"), (_, _) => ShowMethodIntro(), minW: 0, bg: "#5A4636", fg: Fg);
        Grid.SetColumn(introBtn, 1); introBtn.Margin = new Thickness(6, 0, 0, 0);
        methodRow.Children.Add(introBtn);
        p.Children.Add(TitledInfo("method", I18n.T("Method"), methodRow));

        // 8) 操作按钮已移到标题行（重置红色 + 生成冲煮方案靠右）
        // 底部操作行（2026-09-09 用户要求：移动版第一滑动页底部也要有 重置 + 生成冲煮方案，滚动到底即可一键操作、无需回顶部）。
        // 分割线 + 等宽双列按钮，横屏/桌面与竖屏分页共用同一份面板，行为与标题行一致。
        // 醒目提示（与标题行下一致）：底部按钮上一行再次强调「往右进入实时冲煮模式」（#6）
        p.Children.Add(BegHintBanner(I18n.T("ProSetupHint")));
        var footSep = new Border { Height = 1, Background = Brush("#4A3826"), Margin = new Thickness(0, 2, 0, 2) };
        p.Children.Add(footSep);
        // 注：Avalonia Grid 无 ColumnSpacing，双列间距用左右按钮 Margin 实现（与全库 PairRow 同手法）
        var footRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var footReset = MakeButton(I18n.T("Reset"), (_, _) => _vm.ResetAll(),
            bg: "#C0392B", fg: "#FFFFFF", bold: true, stretch: true);
        footReset.MinHeight = 46;                 // 底部主操作：加高到 46 便于拇指触达
        footReset.Margin = new Thickness(0, 0, 5, 0);
        Grid.SetColumn(footReset, 0); footRow.Children.Add(footReset);
        var footGen = MakeButton(I18n.T("GeneratePlan"), (_, _) => ShowRecommendPlan(),
            bg: AccentBtn, fg: "#1A100A", bold: true, stretch: true);
        footGen.MinHeight = 46;
        footGen.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(footGen, 1); footRow.Children.Add(footGen);
        p.Children.Add(footRow);
        return p;
    }

    /// <summary>左侧分页容器：顶部两个标签（冲煮方案设置 / 推荐冲煮方案）+ 可滚动内容区，按 _leftPage 显示对应页。</summary>
    private Control BuildLeft()
    {
        var dock = new DockPanel();
        // 分页标签栏（2026-09-09 由 StackPanel 改 Grid 三段式：两个标签 | 弹性空隙 | 返回主界面）
        var tabs = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        _tabSettings = MakeTab(I18n.T("Settings"), () => ShowLeftPage(LeftPage.Settings));
        Grid.SetColumn(_tabSettings, 0);
        tabs.Children.Add(_tabSettings);
        _tabRecommend = MakeTab(I18n.T("RecommendBottom"), () => ShowLeftPage(LeftPage.Recommend));
        Grid.SetColumn(_tabRecommend, 1);
        tabs.Children.Add(_tabRecommend);
        // 返回主界面：关闭专业模式界面（隐藏 Home 之下的主体）并回到四模块入口主界面
        var backHomeBtn = MakeButton("⌂ " + I18n.T("BackHome"), (_, _) => ShowHome(),
            stretch: false, bg: "#5A4636", fg: Fg, bold: true);
        backHomeBtn.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(backHomeBtn, 3);
        tabs.Children.Add(backHomeBtn);
        DockPanel.SetDock(tabs, Dock.Top);
        dock.Children.Add(tabs);
        // 内容区（可滚动切换当前页）
        _leftPageHost = new Border { Padding = new Thickness(0) };
        dock.Children.Add(_leftPageHost);
        ShowLeftPage(_leftPage); // 应用当前页（默认「冲煮方案设置」）
        return dock;
    }

    /// <summary>切换左侧分页：重建对应页内容并高亮当前标签。</summary>
    private void ShowLeftPage(LeftPage page)
    {
        _leftPage = page;
        var content = page == LeftPage.Settings ? (Control)BuildSettings() : BuildRecommend();
        _leftPageHost!.Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (_tabSettings != null) HighlightTab(_tabSettings, page == LeftPage.Settings);
        if (_tabRecommend != null) HighlightTab(_tabRecommend, page == LeftPage.Recommend);
    }

    /// <summary>分页标签按钮：左对齐，避免拉伸占满整行。</summary>
    private Button MakeTab(string text, System.Action onClick)
    {
        var b = MakeButton(text, (_, _) => onClick(), bg: "#3A2A1C", fg: Fg, bold: true);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return b;
    }

    /// <summary>分页标签高亮（2026-09-11 设计系统对齐）：当前页 = 主按钮同款肉桂垂直渐变 + 深描边；
    /// 非当前页 = 皮革棕纯色。选中/未选形成"一次只强调一件事"的仪式层级。</summary>
    private void HighlightTab(Button tab, bool active)
    {
        tab.Background = active ? Palette.Vertical("#D9974A", "#B3742E") : Brush(Palette.Leather);
        tab.Foreground = Brush(active ? "#1A100A" : Fg);
        tab.BorderBrush = active ? Brush("#1A100A") : null;
        tab.BorderThickness = active ? new Thickness(1) : default;
    }

    private StackPanel BuildBrew()
    {
        var p = new StackPanel { Spacing = 10 };

        // ===== 顶部标题行：冲煮标题 + 顶部计时 chip + 冲煮日期（紧凑并排，计时不裁切）=====
        var headRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 2),
        };
        var title = Text("🫖 " + I18n.T("Brew"), 17, FontWeight.Bold, Accent);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0); headRow.Children.Add(title);

        // 顶部计时 chip（⏱ 图标 + 计时，绑定 TimerText，永不裁切：Auto 列自适应宽度）
        var timerChip = new Border
        {
            Background = Brush("#0E1A24"), CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 3, 8, 3),
            BorderBrush = Brush("#2A4A5E"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var timerChipWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        timerChipWrap.Children.Add(Text("⏱", 13, fg: "#7FD3F0"));
        var timerChipVal = Text("0:00", 14, FontWeight.Bold, "#7FD3F0"); timerChipVal.Bind(TextBlock.TextProperty, B(nameof(_vm.TimerText)));
        timerChipWrap.Children.Add(timerChipVal);
        timerChip.Child = timerChipWrap;
        Grid.SetColumn(timerChip, 1); headRow.Children.Add(timerChip);

        var dateBox = MakeDatePicker(false); dateBox.VerticalAlignment = VerticalAlignment.Center; dateBox.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(dateBox, 2); headRow.Children.Add(dateBox);
        p.Children.Add(headRow);

        // ===== 推荐粉水比 + 注水总量 + 粉量（两行布局：第一行名目，第二行值）=====
        // 2026-09-03 用户要求：三列两行，第一行是标签名，第二行是对应的值，更直观。
        var planCard = new Border
        {
            Background = Brush("#1A120C"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = Brush("#4A3826"), BorderThickness = new Thickness(1),
        };
        var planGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        // 第一行：标签名（推荐粉水比 | 注水总量 | 粉量）
        var lblRatio = Text("🧮 " + I18n.T("RatioRec"), 12, FontWeight.SemiBold, Sub);
        lblRatio.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(lblRatio, 0); Grid.SetRow(lblRatio, 0); planGrid.Children.Add(lblRatio);
        var lblWater = Text("🚰 " + I18n.T("TotalWaterPlan"), 12, FontWeight.SemiBold, Sub);
        lblWater.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(lblWater, 1); Grid.SetRow(lblWater, 0); planGrid.Children.Add(lblWater);
        var lblDose = Text("☕ " + I18n.T("Dose"), 12, FontWeight.SemiBold, Sub);
        lblDose.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(lblDose, 2); Grid.SetRow(lblDose, 0); planGrid.Children.Add(lblDose);
        // 第二行：值（1:N + ±按钮 | 总水量g | 粉量g）
        // 推荐粉水比值 + ± 微调（步长 1，整数）
        var ratioCell = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4 };
        var ratioNotSet = Text(I18n.T("NotSet"), 16, FontWeight.Bold, Sub);
        ratioNotSet.Bind(TextBlock.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.HasRecipe), Converter = BoolToVisibilityConverter.Instance, ConverterParameter = "True" });
        var ratioSet = Text("", 18, FontWeight.Bold, Accent);
        ratioSet.Bind(TextBlock.TextProperty, B(nameof(_vm.PlanRatioText)));
        ratioSet.Bind(TextBlock.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.HasRecipe) });
        ratioCell.Children.Add(ratioNotSet);
        var minusR = MakeScaleButton("−", (_, _) => _vm.AdjustPlanRatio(-1), bg: "#3A2A1C", fg: Fg, minW: 32);
        minusR.FontSize = 14; minusR.Padding = new Thickness(6, 2, 6, 2);
        var plusR = MakeScaleButton("+", (_, _) => _vm.AdjustPlanRatio(+1), bg: "#3A2A1C", fg: Fg, minW: 32);
        plusR.FontSize = 14; plusR.Padding = new Thickness(6, 2, 6, 2);
        ratioCell.Children.Add(ratioSet);
        ratioCell.Children.Add(minusR);
        ratioCell.Children.Add(plusR);
        Grid.SetColumn(ratioCell, 0); Grid.SetRow(ratioCell, 1); planGrid.Children.Add(ratioCell);
        // 注水总量值
        var waterCell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var waterNotSet = Text(I18n.T("NotSet"), 16, FontWeight.Bold, Sub);
        waterNotSet.Bind(TextBlock.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.HasRecipe), Converter = BoolToVisibilityConverter.Instance, ConverterParameter = "True" });
        var waterSet = Text("", 18, FontWeight.Bold, "#F0C27A");
        waterSet.Bind(TextBlock.TextProperty, B(nameof(_vm.PlanWaterText)));
        waterSet.Bind(TextBlock.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.HasRecipe) });
        waterCell.Children.Add(waterNotSet); waterCell.Children.Add(waterSet);
        Grid.SetColumn(waterCell, 1); Grid.SetRow(waterCell, 1); planGrid.Children.Add(waterCell);
        // 粉量值
        var doseVal = Text("", 18, FontWeight.Bold, "#F0C27A");
        doseVal.Bind(TextBlock.TextProperty, new Binding { Source = _vm, Path = nameof(_vm.Dose), StringFormat = "0g" });
        doseVal.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(doseVal, 2); Grid.SetRow(doseVal, 1); planGrid.Children.Add(doseVal);
        planCard.Child = planGrid;
        p.Children.Add(planCard);

        // ===== 彩色 LED 液晶屏（四合一同一行：重量+计时+流速+实际粉水比，模拟智能称）=====
        var led = new Border
        {
            Background = Brush("#0E0A07"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 12, 12, 12),
            BorderBrush = Brush("#3A2A1C"), BorderThickness = new Thickness(1),
        };
        // 单行四宫格：重量(琥珀) | 计时(青) | 流速(绿) | 实际粉水比(橙)
        var lcd = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), VerticalAlignment = VerticalAlignment.Center };
        // 列间分隔线
        lcd.Children.Add(Divider(1)); lcd.Children.Add(Divider(2)); lcd.Children.Add(Divider(3));
        // 重量（大字号，带 g 单位）
        var wCell = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var wBig = Text("0.0 g", 30, FontWeight.Bold, "#F0C27A"); wBig.Bind(TextBlock.TextProperty, B(nameof(_vm.WeightText)));
        wBig.HorizontalAlignment = HorizontalAlignment.Center;
        wCell.Children.Add(wBig);
        wCell.Children.Add(Text(I18n.T("Weight"), 11, fg: Sub));
        Grid.SetColumn(wCell, 0); lcd.Children.Add(wCell);
        // 计时（青）
        LedSub(lcd, 1, nameof(_vm.TimerText), I18n.T("Timer"), "#7FD3F0");
        // 流速（绿）
        LedSub(lcd, 2, nameof(_vm.FlowText), I18n.T("Flow"), "#7FB069");
        // 实际粉水比（橙）
        LedSub(lcd, 3, nameof(_vm.RatioText), I18n.T("RatioAchieved"), "#F0A060");
        led.Child = lcd;
        p.Children.Add(led);

        // ===== 称控制：主运输键（红→黄→蓝循环：开始/暂停/继续）+ 绿色圆形「时间清零 / 重量清零」（小一号）+ 蓝色「下一阶段 / 上一阶段」全部同一行 =====
        // 主运输键按下有明显反馈（背景压暗+整体下沉）；可用态随计时状态联动（完成态禁用）；绿色清零键独立成圆形（64×64，比 80 主运输键小一号）。
        p.Children.Add(Text("🎛 " + I18n.T("ScaleBtns"), 13, FontWeight.SemiBold, Sub));
        // 称控制分三组并整体居中：组1=开始；组2=时间归零 + 重量归零；组3=下一阶段 + 上一阶段。
        // 用 WrapPanel：窄屏（手机 380px 单列）放不下时自动换行，不再溢出裁切（2026-09-03 体验修复）。
        var scaleRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        // 1) 主运输键：圆形（80×80），颜色随状态循环（红=空闲开始 / 黄=计时中暂停 / 蓝=已暂停继续），文案由 VM.TransportLabel 驱动
        var transportBtn = MakeScaleButton(I18n.T("Start"), (_, _) => _vm.Transport(), circular: true, fg: "#FFFFFF", bold: true);
        transportBtn.Bind(Button.IsEnabledProperty, B(nameof(_vm.CanTransport)));
        transportBtn.Content = _vm.TransportLabel;
        transportBtn.Tag = _vm.TransportColorHex;
        transportBtn.Background = Brush(_vm.TransportColorHex);
        _transportBtn = transportBtn;
        // 2) 时间清零（绿色圆形，小一号 64×64）：中断并清空实时冲煮
        var resetBtn = MakeScaleButton(I18n.T("TimerReset"), (_, _) => _vm.Reset(), circular: true, bg: "#2E8B57", fg: "#FFFFFF", bold: true);
        resetBtn.Width = 64; resetBtn.Height = 64; resetBtn.FontSize = 12;
        // 3) 重置（黄色圆形，小一号 64×64）：重置实时冲煮全部状态为默认（中断+计时/重量/阶段归零）（2026-09-03 用户要求，替代原「重量归零」去皮键）
        var tareBtn = MakeScaleButton(I18n.T("Reset"), (_, _) => _vm.Reset(), circular: true, bg: "#C8881E", fg: "#FFFFFF", bold: true);
        tareBtn.Width = 64; tareBtn.Height = 64; tareBtn.FontSize = 12;
        // 4) 下一阶段 / 上一阶段：蓝色矩形按钮（#2E6FB0，与主运输键蓝态同色）
        var nextBtn = MakeButton(I18n.T("Next"), (_, _) => _vm.Next(), bg: "#2E6FB0", fg: "#FFFFFF", bold: true);
        nextBtn.VerticalAlignment = VerticalAlignment.Center;
        var prevBtn = MakeButton(I18n.T("Prev"), (_, _) => _vm.Prev(), bg: "#2E6FB0", fg: "#FFFFFF", bold: true);
        prevBtn.VerticalAlignment = VerticalAlignment.Center;

        // 三组各自成行内小分组；组间水平留 30、换行后垂直留 6（WrapPanel 无 Spacing，用外边距替代）
        var grpStart = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        grpStart.Children.Add(transportBtn);
        var grpReset = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 14, Margin = new Thickness(30, 6, 0, 0) };
        grpReset.Children.Add(resetBtn); grpReset.Children.Add(tareBtn);
        var grpNav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 14, Margin = new Thickness(30, 6, 0, 0) };
        grpNav.Children.Add(nextBtn); grpNav.Children.Add(prevBtn);
        scaleRow.Children.Add(grpStart);
        scaleRow.Children.Add(grpReset);
        scaleRow.Children.Add(grpNav);
        p.Children.Add(scaleRow);

        // 快捷键提示行已移除（2026-09-03 用户要求）；Space/R/N 键盘快捷键本身保留（无 T——去皮键已由重置键取代）

        // ===== 冲煮流程管道图已取消（2026-09-03 用户要求）=====
        // 原来的闷蒸 → 注水1 → 注水2 → 冲煮结束 横向节点流已移除；
        // 下面每一阶段的注水计时（阶段名+提示、进度条、整体进度）保留不动。

        // 阶段名 + 提示（醒目一行：阶段名加大加粗着色，注意事项紧随其后）
        var phaseLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        var phaseName = Text("未开始", 16, FontWeight.Bold, Accent); phaseName.Bind(TextBlock.TextProperty, B(nameof(_vm.PhaseName)));
        var phaseTip = Text("", 12, fg: Sub, wrap: true); phaseTip.Bind(TextBlock.TextProperty, B(nameof(_vm.PhaseTip)));
        phaseLine.Children.Add(phaseName); phaseLine.Children.Add(phaseTip);
        p.Children.Add(phaseLine);

        // 阶段进度条：注水阶段（蓝）/ 等待阶段（琥珀）分色直观区分，右侧标注本段注水量或剩余时间。
        // 点击「开始」后按时间推进；重量到达本段目标（注水段）或时长走完（等待段）即自动切换下一阶段。
        var phaseBarCard = new Border
        {
            Background = Brush("#14100B"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 0), BorderBrush = Brush("#3A2A1C"), BorderThickness = new Thickness(1),
        };
        var phaseBarRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        // 左侧标签：注水中（蓝）/ 等待中（琥珀），按 IsPourPhase 切换显隐
        var pourTag = Text("💧 " + I18n.T("PhasePourBar"), 12, FontWeight.SemiBold, "#7FB8E8");
        pourTag.Bind(TextBlock.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.IsPourPhase) });
        Grid.SetColumn(pourTag, 0); phaseBarRow.Children.Add(pourTag);
        var waitTag = Text("⏳ " + I18n.T("PhaseWaitBar"), 12, FontWeight.SemiBold, "#E8A94A");
        waitTag.Bind(TextBlock.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.IsPourPhase), Converter = BoolToVisibilityConverter.Instance, ConverterParameter = "True" });
        Grid.SetColumn(waitTag, 0); phaseBarRow.Children.Add(waitTag);
        // 中间进度条：同一格内两条 ProgressBar（注水蓝 / 等待琥珀），互斥显隐
        var barGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        var pourBar = new ProgressBar { Maximum = 1, Height = 12, Foreground = Brush("#4A9BD8"), Background = Brush("#1E2430"), MinWidth = 120 };
        pourBar.Bind(ProgressBar.ValueProperty, B(nameof(_vm.PhaseProgress)));
        pourBar.Bind(ProgressBar.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.IsPourPhase) });
        barGrid.Children.Add(pourBar);
        var waitBar = new ProgressBar { Maximum = 1, Height = 12, Foreground = Brush("#E8A94A"), Background = Brush("#241E14"), MinWidth = 120 };
        waitBar.Bind(ProgressBar.ValueProperty, B(nameof(_vm.PhaseProgress)));
        waitBar.Bind(ProgressBar.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.IsPourPhase), Converter = BoolToVisibilityConverter.Instance, ConverterParameter = "True" });
        barGrid.Children.Add(waitBar);
        Grid.SetColumn(barGrid, 1); barGrid.Margin = new Thickness(10, 0, 10, 0); phaseBarRow.Children.Add(barGrid);
        // 右侧文本：本段注水 当前/目标 g（注水段）或 剩余秒数（等待段）
        var phaseAmt = Text("", 12, fg: "#E8D8C7");
        phaseAmt.Bind(TextBlock.TextProperty, B(nameof(_vm.PhasePourText)));
        Grid.SetColumn(phaseAmt, 2); phaseBarRow.Children.Add(phaseAmt);
        phaseBarCard.Child = phaseBarRow;
        p.Children.Add(phaseBarCard);

        // 下一阶段预告（⏭ 图标 + 节点标题 + 副标题）：未开始预告第一步，随冲煮推进自动切换，完成后隐藏
        var nextPhaseLine = Text("", 12, fg: "#9FC3D8", wrap: true);
        nextPhaseLine.Margin = new Thickness(0, 2, 0, 0);
        nextPhaseLine.Bind(TextBlock.TextProperty, B(nameof(_vm.NextPhaseText)));
        nextPhaseLine.Bind(TextBlock.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.NextPhaseText), Converter = IsNullOrEmptyConverter.Instance, ConverterParameter = "True" });
        p.Children.Add(nextPhaseLine);

        // 整体进度（细条）
        var overallBar = new ProgressBar { Maximum = 1, Height = 8, Foreground = Brush("#7FB069"), Background = Brush(Panel) };
        overallBar.Bind(ProgressBar.ValueProperty, B(nameof(_vm.OverallProgress)));
        p.Children.Add(overallBar);

        // 警告（精简）
        var warn = new Border { Background = Brush("#3A1E16"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 0) };
        var warnText = Text("", 12, fg: "#F2A65A", wrap: true); warnText.Bind(TextBlock.TextProperty, B(nameof(_vm.WarningsText)));
        warn.Child = warnText;
        warn.Bind(Border.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.WarningsText), Converter = new IsNullOrEmptyConverter(), ConverterParameter = "True" });
        p.Children.Add(warn);

        // ===== 实时冲煮区尾部：保存本次冲煮记录 + 冲煮记录查询和统计（同一行）=====
        // 模拟注水卡已取消（2026-09-02 用户要求）：注水阶段改为点击「开始」后按推荐流速自动推进，
        // 各阶段进度/注水量/注意事项由上方「阶段进度条」直观呈现，无需手动模拟注水。

        // 保存记录 + 冲煮记录查询和统计（同一行）：保存后点右侧按钮即可查看全部记录与历史统计（原尾部的统计卡 / 冲煮记录列表已并入该弹窗）
        var saveRow = new Grid { ColumnDefinitions = new ColumnDefinitions("1.2*,*"), Margin = new Thickness(0, 8, 0, 0) };
        var saveBtn = MakeButton(I18n.T("Save"), (_, _) => _vm.SaveCurrentBrew(), stretch: true, bg: AccentBtn, fg: "#1A100A", bold: true);
        Grid.SetColumn(saveBtn, 0); saveRow.Children.Add(saveBtn);
        var queryBtn = MakeButton("📋 " + I18n.T("RecordsQuery"), (_, _) => ShowRecordsDialog(), stretch: true, bg: "#2E6FB0", fg: "#FFFFFF", bold: true);
        Grid.SetColumn(queryBtn, 1); queryBtn.Margin = new Thickness(8, 0, 0, 0); saveRow.Children.Add(queryBtn);
        p.Children.Add(saveRow);

        // ===== 实时冲煮区最后：推荐研磨 + 水质推荐（从左区推荐页迁入，字号加大更醒目）=====
        var grindEndCard = new Border { Background = Brush("#2A1F14"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
        // 推荐研磨：标题和说明放同一行（标题 Auto + 值 * 占满剩余并换行）
        var grindRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var grindLabel = Text("⚙ " + I18n.T("GrindRec") + "：", 15, FontWeight.Bold, Accent);
        grindLabel.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(grindLabel, 0); grindRow.Children.Add(grindLabel);
        var grindEndVal = Text("", 14, fg: "#E8D8C7", wrap: true);
        grindEndVal.Bind(TextBlock.TextProperty, B(nameof(_vm.GrindRecText)));
        grindEndVal.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(grindEndVal, 1); grindRow.Children.Add(grindEndVal);
        grindEndCard.Child = grindRow;
        p.Children.Add(grindEndCard);

        // ===== 推荐水温（2026-09-08 用户要求：置于推荐水质之前）=====
        // 左=标题，右=大号水温值 + 微调说明；值随 Generate 由引擎按烘焙度/处理法/研磨确定。
        var tempEndCard = new Border { Background = Brush("#2B1D10"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
        var tempEndRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var tempEndLabel = Text("🌡 " + I18n.T("TempRec") + "：", 15, FontWeight.Bold, Accent);
        tempEndLabel.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(tempEndLabel, 0); tempEndRow.Children.Add(tempEndLabel);
        var tempEndInner = new StackPanel { Spacing = 2 };
        var tempEndVal = Text("", 20, FontWeight.Bold, "#F0A060");
        tempEndVal.Bind(TextBlock.TextProperty, B(nameof(_vm.TempRecText)));
        tempEndInner.Children.Add(tempEndVal);
        tempEndInner.Children.Add(Text(I18n.T("TempRecHint"), 12, fg: "#E8D8C7", wrap: true));
        Grid.SetColumn(tempEndInner, 1); tempEndRow.Children.Add(tempEndInner);
        tempEndCard.Child = tempEndRow;
        p.Children.Add(tempEndCard);

        var waterEndCard = new Border { Background = Brush("#16242E"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
        var waterEnd = new StackPanel { Spacing = 4 };
        waterEnd.Children.Add(Text("💧 " + I18n.T("Water"), 15, FontWeight.Bold, Accent));
        var waterEndVal = Text("", 14, fg: "#A9D6E8", wrap: true); waterEndVal.Bind(TextBlock.TextProperty, B(nameof(_vm.WaterText)));
        waterEnd.Children.Add(waterEndVal);
        waterEndCard.Child = waterEnd;
        p.Children.Add(waterEndCard);

        // 页脚：软件版本 + 作者（2026-09-09 用户要求：实施冲煮界面底部显示软件版本与作者信息）。
        // 第一行 AppTitle + v{AppVer}（强调色），第二行作者（灰）；居中、可换行，随 RebuildAllText 重建时重新取 I18n。
        var footSep = new Border { Height = 1, Background = Brush("#3A2A1C"), Margin = new Thickness(0, 4, 0, 0) };
        p.Children.Add(footSep);
        var footInner = new StackPanel { Spacing = 2 };
        var footTitle = Text($"{I18n.T("AppTitle")}  v{AppVer}", 11, FontWeight.SemiBold, Accent);
        footTitle.HorizontalAlignment = HorizontalAlignment.Center;
        footTitle.TextWrapping = TextWrapping.Wrap;
        var footAuthor = Text(I18n.T("Author"), 10, fg: Sub);
        footAuthor.HorizontalAlignment = HorizontalAlignment.Center;
        footAuthor.TextWrapping = TextWrapping.Wrap;
        footInner.Children.Add(footTitle);
        footInner.Children.Add(footAuthor);
        p.Children.Add(footInner);

        return p;
    }

    /// <summary>左侧第 2 页「推荐冲煮方案」：风味 / 养豆 / 报告 / 黄金杯 / 大师方案 / 我的方案。
    /// 模拟注水、推荐粉水比、推荐研磨、水质推荐已迁至右侧实时冲煮区（用户要求，2026-09-02）。</summary>
    private StackPanel BuildRecommend()
    {
        var p = new StackPanel { Spacing = 10 };

        // 标题行：推荐冲煮方案（左）+ 导入咖啡大师方案按钮（靠右）
        var headRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = Text(I18n.T("RecommendBottom"), 15, FontWeight.Bold, Accent);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0); headRow.Children.Add(title);
        var importBtn = MakeButton("📥 " + I18n.T("ImportPlan"), (_, _) => ShowMasterPlanPicker(), stretch: false, bg: "#5A4636", fg: Fg);
        importBtn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(importBtn, 1); headRow.Children.Add(importBtn);
        p.Children.Add(headRow);

        // 咖啡大师方案卡（从「咖啡大师方案清单」选中后显示，非空可见）——置于推荐区最顶部（2026-09-03 用户要求）
        {
            var mpCard = new Border { Background = Brush("#2A1E16"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), BorderBrush = Brush("#4A3826"), BorderThickness = new Thickness(1) };
            var mpInner = new StackPanel { Spacing = 4 };
            mpInner.Children.Add(Text(I18n.T("MasterPlanCard"), 13, FontWeight.SemiBold, Accent));
            var mpBody = Text("", 12.5, fg: "#E8D8C7", wrap: true);
            mpBody.Bind(TextBlock.TextProperty, B(nameof(_vm.MasterPlanText)));
            mpInner.Children.Add(mpBody);
            mpCard.Child = mpInner;
            mpCard.Bind(Border.IsVisibleProperty, new Binding { Source = _vm, Path = nameof(_vm.MasterPlanText), Converter = new IsNullOrEmptyConverter(), ConverterParameter = "True" });
            p.Children.Add(mpCard);
        }

        // 产地风味参考（只读）
        p.Children.Add(MakeRecCard(I18n.T("FlavorCard"), nameof(_vm.OriginInfoText), "#1A2630", "#BCD8E8"));

        // 养豆期推荐
        p.Children.Add(MakeRecCard(I18n.T("RestRec"), nameof(_vm.RestRecText), "#1E2A16", "#CDE6B0"));

        // 专业冲煮报告（替代冗长摘要，参考 SCA 记录表）
        p.Children.Add(MakeRecCard(I18n.T("Report"), nameof(_vm.BrewReportText), Panel, "#E8D8C7"));

        // 黄金杯诊断（文档2 技术报告核心）：预估 EY/TDS + 判据 + 海拔沸点封顶提示
        p.Children.Add(MakeRecCard(I18n.T("GoldenCup"), nameof(_vm.GoldenCupText), "#221A2E", "#D8C7E8"));

        // 我的方案：弹出用户收藏的大师方案（离线可复用，可选、删除）
        p.Children.Add(MakeButton(I18n.T("MyPlanButton"), (_, _) => ShowMyPlanPicker(), stretch: true, bg: "#3A2A1C", fg: Fg));
        // 保存当前为我的方案：把当前整套参数（含 处理方式 / 产地 / 烘焙度）存为「我的方案」，日常一键复用
        p.Children.Add(MakeButton(I18n.T("SaveCurrentPlan"), (_, _) => _vm.SaveCurrentAsMyPlan(), stretch: true, bg: "#3A2A1C", fg: Accent));

        return p;
    }

    /// <summary>
    /// 推荐方案统一卡片：标题（13 号半粗体·强调色）+ 正文（12.5 号·自动换行）。
    /// 各推荐项（研磨 / 水质 / 风味 / 养豆 / 报告）统一走这里，保证字体与间距一致。
    /// </summary>
    private Border MakeRecCard(string title, string vmPath, string bg, string fg)
    {
        var card = new Border { Background = Brush(bg), CornerRadius = new CornerRadius(8), Padding = new Thickness(10) };
        var s = new StackPanel { Spacing = 4 };
        s.Children.Add(Text(title, 13, FontWeight.SemiBold, Accent));
        var body = Text("", 12.5, fg: fg, wrap: true);
        body.Bind(TextBlock.TextProperty, B(vmPath));
        s.Children.Add(body);
        card.Child = s;
        return card;
    }

    // ===== 流程管道图死代码已清理（2026-09-03）=====
    // MakePhasePipeline() + BuildPipelineNode() + PipelineBrushConverter + PipelineMarkConverter 已删除，
    // 移除原因：右区「冲煮流程」管道图已取消（用户要求），这些渲染代码不再有调用方。
    // VM 中 _pipeline 集合保留（NextPhaseText 依赖它计算下一阶段预告）。

    /// <summary>LED 副行小读数：值（绑定）+ 标签，模拟智能称底部三连彩色显示。</summary>
    private void LedSub(Grid parent, int col, string vmPath, string caption, string color)
    {
        var cell = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center };
        var v = Text("", 30, FontWeight.Bold, color); v.Bind(TextBlock.TextProperty, B(vmPath));
        cell.Children.Add(v);
        cell.Children.Add(Text(caption, 11, fg: Sub));
        Grid.SetColumn(cell, col); parent.Children.Add(cell);
    }

    /// <summary>LCD 列间竖直分隔线（位于第 col 列左边界）。</summary>
    private Border Divider(int col)
    {
        var d = new Border
        {
            Background = Brush("#2A1E14"), Width = 1, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4),
        };
        Grid.SetColumn(d, col);
        return d;
    }

    /// <summary>导入咖啡大师方案：弹出输入框（用简单的文本框浮层）解析 JSON 并映射到 VM。</summary>
    private void ImportPlanFromInput()
    {
        // JSON 输入框 + 确认按钮（桌面二级窗口 / 移动浮层，由 PresentModal 按平台选择）
        var sp = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        sp.Children.Add(Text(I18n.T("PastePlanHint"), 13, fg: Sub, wrap: true));
        var tb = new TextBox { AcceptsReturn = true, Height = 180, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Brush(Fg), Background = Brush(Panel) };
        sp.Children.Add(tb);
        Action close = null!;
        var ok = MakeButton(I18n.T("ImportAndGenerate"), (_, _) =>
        {
            try { _vm.ImportPlan(tb.Text ?? ""); close(); }
            catch (Exception ex) { tb.Text = string.Format(I18n.T("ImportPlanFailed"), ex.Message); }
        }, stretch: true, bg: AccentBtn, fg: "#1A100A", bold: true);
        sp.Children.Add(ok);
        (close, _) = PresentModal(I18n.T("ImportPlan"), sp, 480, 360);
    }

    /// <summary>极简 IObserver&lt;string?&gt; 适配器：把 TextBox.Text 属性变更转成 Action 回调。
    /// 用于规避 System.Reactive 与 BCL 在 Subscribe(Action&lt;&gt;) 上的重载歧义——直接调用 IObservable&lt;T&gt; 自带的
    /// Subscribe(IObserver&lt;T&gt;) 实例方法（而非扩展方法），既编译无歧义，又能在 headless 程序化赋值与真实输入两条路径都触发筛选。</summary>
    private sealed class TextChangeObserver : IObserver<string?>
    {
        private readonly Action<string?> _onNext;
        public TextChangeObserver(Action<string?> onNext) => _onNext = onNext;
        public void OnNext(string? value) => _onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    /// <summary>方案清单选择器（大师清单 / 我的方案 共用）：列出方案，点击即采用并关闭；可选「☆ 收藏」「📤 复制分享文本」「删除」。
    /// 顶部搜索框按 标题/摘要/正文 不区分大小写实时筛选；无匹配显示空态。
    /// onShare 返回分享文本（非 null 时写入剪贴板并显示「已复制」）；decorate 供「我的方案」追加 导出/导入 工具栏。</summary>
    private void ShowPlanPicker(
        string title, string hint, string emptyText,
        Func<IEnumerable<MasterPlanItem>> getPlans,
        Action<MasterPlanItem> onApply,
        Action<MasterPlanItem>? onStar,
        Action<string>? onDelete,
        Func<MasterPlanItem, string?>? onShare,
        Action<Window?> setHolder,
        string? searchWatermark = null,
        Action<StackPanel, Action<string>, Action>? decorate = null)
    {
        var sp = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        sp.Children.Add(Text(hint, 13, fg: Sub, wrap: true));

        // 搜索框：按标题 / 摘要 / 正文实时筛选
        var search = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Watermark = searchWatermark ?? I18n.T("SearchMasterHint"),
            Foreground = Brush(Fg), Background = Brush(Panel), MinHeight = 40,
        };
        sp.Children.Add(search);

        // 状态行（导出/导入/复制结果反馈；大师清单不使用则为空）
        var status = Text("", 12, fg: "#E8C79A", wrap: true);
        sp.Children.Add(status);

        // 方案列表容器与当前查询：需在 decorate 之前声明（decorate 闭包与 Populate 都要引用）
        var list = new StackPanel { Spacing = 8 };
        string currentQuery = "";
        Action close = null!; // 弹窗关闭动作（桌面 Window.Close / 移动浮层移除），末尾由 PresentModal 赋值

        // 「我的方案」专属工具栏（导出全部 / 从文件导入；导入成功后即时重列清单）
        decorate?.Invoke(sp, msg => status.Text = msg, () => Populate(currentQuery));

        // 方案列表：可滚动浏览（MaxHeight 限高 380 = 480 窗口减去标题/搜索/工具栏/按钮约 100px）
        sp.Children.Add(new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 380,
        });

        void Populate(string q)
        {
            currentQuery = (q ?? "").Trim();
            list.Children.Clear();
            int shown = 0;
            foreach (var plan in getPlans())
            {
                if (currentQuery.Length > 0 &&
                    !(plan.Title.Contains(currentQuery, System.StringComparison.OrdinalIgnoreCase) ||
                      plan.Summary.Contains(currentQuery, System.StringComparison.OrdinalIgnoreCase) ||
                      plan.Detail.Contains(currentQuery, System.StringComparison.OrdinalIgnoreCase)))
                    continue;

                // 每行：左=采用按钮（标题+摘要，点击即应用并关闭），右=可选 收藏/分享/删除
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
                var item = MakeButton("", (_, _) => { onApply(plan); close(); }, stretch: true, bg: "#241A12", fg: Fg);
                item.CornerRadius = new CornerRadius(8);
                var inner = new StackPanel { Spacing = 3 };
                inner.Children.Add(Text(plan.Title, 14, FontWeight.Bold, Accent));
                inner.Children.Add(Text(plan.Summary, 12, fg: "#E8D8C7", wrap: true));
                item.Content = inner;
                Grid.SetColumn(item, 0);
                row.Children.Add(item);

                if (onStar != null)
                {
                    var star = MakeButton(I18n.T("SaveMyPlan"), (_, _) => onStar(plan), stretch: false, bg: "#3A2A1C", fg: Accent);
                    star.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(star, 1);
                    row.Children.Add(star);
                }
                if (onShare != null)
                {
                    var share = MakeButton(I18n.T("SharePlan"), (_, _) =>
                    {
                        var txt = onShare(plan);
                        if (txt != null)
                        {
                            _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(txt);   // 写入剪贴板（取宿主 TopLevel，兼容移动浮层；headless 下为内存剪贴板）
                            status.Text = I18n.T("ShareCopied");
                        }
                    }, stretch: false, bg: "#3A2A1C", fg: "#9FC8E8");
                    share.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(share, 2);
                    row.Children.Add(share);
                }
                if (onDelete != null)
                {
                    var del = MakeButton(I18n.T("DeleteMyPlan"), (_, _) => { onDelete(plan.Title); Populate(currentQuery); }, stretch: false, bg: "#3A2A1C", fg: "#E8A08A");
                    del.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(del, 3);
                    row.Children.Add(del);
                }
                list.Children.Add(row);
                shown++;
            }
            if (shown == 0)
                list.Children.Add(Text(emptyText, 12, fg: Sub, wrap: true));
        }

        // 订阅 Text 属性变更（GetObservable 覆盖「真实输入」与「程序化赋值」两条路径）→ 实时筛选。
        // 自定义 IObserver 规避 System.Reactive / BCL 的 Subscribe(Action<>) 重载歧义；Populate("") 负责初始全量。
        search.GetObservable(TextBox.TextProperty)
              .Subscribe(new TextChangeObserver(v => Populate(v ?? "")));
        Populate("");

        sp.Children.Add(MakeButton(I18n.T("Cancel"), (_, _) => close(), stretch: true, bg: "#3A2A1C", fg: Fg));
        Window? window;
        (close, window) = PresentModal(title, sp, 580, 480, onClosed: () => setHolder(null));
        setHolder(window);   // 桌面端为 Window 实例（headless 可反射）；移动端为 null
    }

    /// <summary>「咖啡大师重铸方案清单」：列出知名的赛事 / 大师冲煮方案，点击后内容填入「推荐冲煮方案」的大师方案卡。
    /// 数据来源：《智能手冲咖啡冲煮系统-项目建设方案 V1.1》表 5-2/5-3/5-4/5-5/5-6 与《手冲咖啡多维冲煮逻辑系统-技术报告》实测参数，
    /// 覆盖 WBrC 世界冠军、2025/2026 决赛前四、经典流派 11 法与 CBrC 国内冠军。每项右侧 ☆ 可存为「我的方案」。</summary>
    private void ShowMasterPlanPicker() =>
        ShowPlanPicker(I18n.T("MasterPlanTitle"), I18n.T("MasterPlanHint"), "未找到匹配的大师方案",
            () => MasterPlans(),
            p => { ApplyMasterPlan(p); _vm.MasterPlanText = p.Detail; },
            // 收藏时连带结构化字段一起落盘，之后离线复用可完整还原「处理方式 / 产地 / 烘焙度」
            p => _vm.SaveMyPlan(p.ToMyPlan()),
            null,
            null,
            w => _masterPlanPickerWindow = w);

    /// <summary>
    /// 主界面「手冲大师方案」入口（#7 2026-09-10 重构）：内联清单覆盖在 Home 之上（不再弹独立窗口），
    /// 点击方案名字下内联展开详情（带「采用此方案」「取消」按钮），点击其他名字收起上一个（手风琴）。
    /// 清单由 MasterPlans() 按年份最新→最旧返回；收藏（☆ 存为我的方案）仍可在每行触发。
    /// </summary>
    private void ShowHomeMasterPlanPicker()
    {
        _expandedMasterTitle = null;
        _masterListHost!.Child = BuildHomeMasterList();
        _masterListHost.IsVisible = true;
        _homeHost!.IsVisible = false;
        StopHeroPulse();
    }

    /// <summary>构建大师方案内联清单：标题 + 返回键 + 搜索 + 列表（按年份最新→最旧，点击内联展开）。</summary>
    private Control BuildHomeMasterList()
    {
        var p = new StackPanel { Spacing = 10, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center };
        // —— 页头：统一 PageHeader（🏆 模块瓷砖 + 标题 + ⌂ 返回）——
        p.Children.Add(PageHeader("🏆", "MasterPlanTitle", CardAccents.Master, () =>
        {
            _masterListHost!.IsVisible = false;
            _homeHost!.IsVisible = true;
            _expandedMasterTitle = null;
        }));
        p.Children.Add(Text(I18n.T("MasterPlanHint"), 12, fg: Sub, wrap: true));
        _masterListSearch = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Watermark = I18n.T("SearchMasterHint"),
            Foreground = Brush(Fg), Background = Brush(Panel), MinHeight = 38,
        };
        p.Children.Add(_masterListSearch);
        _masterListPanel = new StackPanel { Spacing = 8 };
        p.Children.Add(new ScrollViewer
        {
            Content = _masterListPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 520,
        });
        _masterListSearch.GetObservable(TextBox.TextProperty)
            .Subscribe(new TextChangeObserver(v => MasterListPopulate(v ?? "")));
        MasterListPopulate("");
        // 2026-09-11：外层包 Grid，底层放轻量暖光氛围层（与 Home 同语言）
        return new Grid
        {
            Children =
            {
                BuildAmbientBackdrop(),
                new ScrollViewer
                {
                    Content = p,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            },
        };
    }

    /// <summary>按当前搜索框重新填充内联清单（保留手风琴展开状态）。</summary>
    private void MasterListPopulate(string q)
    {
        if (_masterListPanel == null) return;
        _masterListPanel.Children.Clear();
        string query = (q ?? "").Trim();
        foreach (var plan in MasterPlans())
        {
            if (query.Length > 0 &&
                !(plan.Title.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
                  plan.Summary.Contains(query, System.StringComparison.OrdinalIgnoreCase) ||
                  plan.Detail.Contains(query, System.StringComparison.OrdinalIgnoreCase)))
                continue;
            _masterListPanel.Children.Add(MasterListRow(plan, _expandedMasterTitle == plan.Title));
        }
        if (_masterListPanel.Children.Count == 0)
            _masterListPanel.Children.Add(Text(I18n.T("MasterPlanEmpty"), 12, fg: Sub, wrap: true));
    }

    /// <summary>清单单行：可点击标题（手风琴展开/收起）+ 摘要；展开时显示详情 + 采用/取消按钮。</summary>
    private Border MasterListRow(MasterPlanItem plan, bool expanded)
    {
        var card = new Border { Background = Brush(Card), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10) };
        var sp = new StackPanel { Spacing = 6 };
        var header = MakeButton("", (_, _) =>
        {
            _expandedMasterTitle = _expandedMasterTitle == plan.Title ? null : plan.Title;
            MasterListPopulate(_masterListSearch?.Text ?? "");
        }, stretch: true, bg: "#241A12", fg: Fg, bold: false);
        header.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var hsp = new StackPanel { Spacing = 3 };
        hsp.Children.Add(Text(plan.Title, 14, FontWeight.Bold, Accent, wrap: true));
        hsp.Children.Add(Text(plan.Summary, 12, fg: "#E8D8C7", wrap: true));
        header.Content = hsp;
        sp.Children.Add(header);
        if (expanded)
        {
            sp.Children.Add(new Border { Height = 1, Background = Brush("#3A2A1C"), Margin = new Thickness(0, 4, 0, 4) });
            sp.Children.Add(new ScrollViewer
            {
                Content = Text(plan.Detail, 12.5, fg: Fg, wrap: true),
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            var btnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            var apply = MakeButton("✅ " + I18n.T("ApplyPlan"), (_, _) =>
            {
                ApplyMasterPlan(plan);
                _vm.MasterPlanText = plan.Detail;
                EnterProMode();
                _masterListHost!.IsVisible = false;
                _expandedMasterTitle = null;
            }, stretch: true, bg: AccentBtn, fg: "#1A100A", bold: true);
            Grid.SetColumn(apply, 0);
            btnRow.Children.Add(apply);
            var cancel = MakeButton(I18n.T("Cancel"), (_, _) =>
            {
                _expandedMasterTitle = null;
                MasterListPopulate(_masterListSearch?.Text ?? "");
            }, stretch: true, bg: "#3A2A1C", fg: Fg);
            cancel.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(cancel, 1);
            btnRow.Children.Add(cancel);
            sp.Children.Add(btnRow);
        }
        card.Child = sp;
        return card;
    }

    /// <summary>
    /// 「咖啡大师重铸方案」详情介绍窗口：完整展示方案的摘要与正文（分段注水路线 / 参数推导），
    /// 底部「采用此方案」= 填入专业模式推荐区并进入专业模式；「关闭」仅浏览。
    /// </summary>
    private void ShowMasterPlanDetail(MasterPlanItem plan)
    {
        var sp = new StackPanel { Spacing = 10, Margin = new Thickness(14) };
        sp.Children.Add(Text(I18n.T("MasterDetailHint"), 12, fg: Sub, wrap: true));
        // 正文可滚动浏览（详情通常远超一屏）
        sp.Children.Add(new ScrollViewer
        {
            Content = Text(plan.Detail, 12.5, fg: Fg, wrap: true),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 380,
        });
        Action close = null!;
        var applyBtn = MakeButton("✅ " + I18n.T("ApplyPlan"), (_, _) =>
        {
            ApplyMasterPlan(plan);              // 结构化字段 + 推荐方案卡填入专业模式
            _vm.MasterPlanText = plan.Detail;
            EnterProMode();                     // 采用后进入专业模式查看完整冲煮界面
            close();
        }, stretch: true, bg: AccentBtn, fg: "#1A100A", bold: true);
        applyBtn.Margin = new Thickness(0, 0, 4, 0);
        var closeBtn = MakeButton(I18n.T("Cancel"), (_, _) => close(), stretch: true, bg: "#3A2A1C", fg: Fg);
        closeBtn.Margin = new Thickness(4, 0, 0, 0);
        var btnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(applyBtn, 0); btnRow.Children.Add(applyBtn);
        Grid.SetColumn(closeBtn, 1); btnRow.Children.Add(closeBtn);
        sp.Children.Add(btnRow);
        Window? window;
        (close, window) = PresentModal(plan.Title, sp, 620, 560, onClosed: () => _masterPlanDetailWindow = null);
        _masterPlanDetailWindow = window;   // 桌面端为 Window 实例（headless 测试可反射）；移动端为 null
    }

    /// <summary>「我的方案」：列出用户收藏的大师方案（离线可复用），点击即应用并关闭；每项右侧 📤 可复制分享文本、× 可删除。
    /// 顶部工具栏提供「导出全部 / 从文件导入」（默认备份文件 myplans-backup.json，与本机 myplans.json 同目录），
    /// 实现整库备份与便携恢复；单条分享文本可跨设备互通（粘贴 / 发送 / 另存 .txt）。</summary>
    private void ShowMyPlanPicker() =>
        ShowPlanPicker(I18n.T("MyPlanTitle"), I18n.T("MyPlanHint"), I18n.T("MyPlanEmpty"),
            () => _vm.MyPlans.Select(m => m.AsItem()),
            p => { ApplyMasterPlan(p); _vm.MasterPlanText = p.Detail; },
            null,
            t => _vm.DeleteMyPlan(t),
            p => _vm.MyPlanShareText(p.Title),
            w => _myPlanPickerWindow = w,
            searchWatermark: I18n.T("SearchMyPlanHint"),
            decorate: (sp, setStatus, refresh) =>
            {
                // 工具栏：导出全部（备份） / 从文件导入（恢复）——共用默认备份路径，一键完成
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 2, 0, 0) };
                var exp = MakeButton(I18n.T("ExportPlan"), (_, _) =>
                {
                    try { setStatus(_vm.ExportAllMyPlans()); }
                    catch (Exception ex) { setStatus(string.Format(I18n.T("ExportPlanFailed"), ex.Message)); }
                }, stretch: false, bg: "#2A4A3C", fg: "#B8E0C0");
                Grid.SetColumn(exp, 0);
                row.Children.Add(exp);
                var imp = MakeButton(I18n.T("ImportPlanFile"), (_, _) =>
                {
                    try
                    {
                        setStatus(_vm.ImportAllMyPlans());
                        refresh(); // 导入后立即重列清单，新方案立即可见
                    }
                    catch (Exception ex) { setStatus(string.Format(I18n.T("ImportPlanFailed"), ex.Message)); }
                }, stretch: false, bg: "#3A2A1C", fg: Accent);
                Grid.SetColumn(imp, 1);
                imp.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(imp);
                sp.Children.Add(row);
            });

    /// <summary>方案联动（大师清单 / 我的方案 共用）：把方案参数回灌冲煮引擎，使推荐方案随方案实时重算、可一键冲煮。
    /// 采用「结构化字段优先、正文正则回退」两段式：
    ///   ① 结构化字段（Method / Dripper / Dose / Ratio / Process / Origin / Roast / Flavor）若已指定，直接写引擎——
    ///      这是「处理方式 / 产地 / 烘焙度」唯一可靠的联动通路：大师方案正文多为赛事参数（水温/分段/时长），
    ///      并不写豆子信息，靠正则从正文猜产地与烘焙度既不可靠也会误伤用户当前选择；
    ///   ② 未指定的剂量类字段退化为正文正则（粉量 / 粉水比 / 方法 / 滤杯在正文里有稳定写法，可安全识别）。
    /// 结构化字段为空 = 「该方案未指定」→ 保留用户当前选择，绝不臆测覆盖。
    /// 解析失败时静默降级（仅保留正文展示，不影响其它功能）。</summary>
    private void ApplyMasterPlan(MasterPlanItem plan)
    {
        try
        {
            string detail = plan.Detail ?? "";

            // ① 方法：结构化优先；否则正文关键词（仅 unambiguous 映射，避免误改配方）
            string? method = NotEmpty(plan.Method) ? plan.Method : null;
            if (method == null)
            {
                if (detail.Contains("4:6") || detail.Contains("粕谷")) method = "kasuya46";
                else if (detail.Contains("瑞士搅拌")) method = "swiss";
                else if (detail.Contains("逆向") || detail.Contains("反冲")) method = "reverse";
                else if (detail.Contains("浅烘加强")) method = "light";
            }
            if (method != null && BrewEngine.MethodProfiles.ContainsKey(method)) _vm.Method = method;

            // ② 滤杯：结构化优先；否则浸泡式大师（关阀浸泡 / 聪明杯 / Switch）→ 聪明杯(浸泡)
            string? dripper = NotEmpty(plan.Dripper) ? plan.Dripper : null;
            if (dripper == null &&
                (detail.Contains("关阀浸泡") || detail.Contains("聪明杯") || detail.Contains("Switch")))
                dripper = "smart";
            if (dripper != null && BrewEngine.DripperProfiles.ContainsKey(dripper)) _vm.Dripper = dripper;

            // ②b 滤纸/滤网：结构化优先（bleached/unbleached/metal/cloth）；引擎不支持的类型静默忽略
            string? filter = NotEmpty(plan.Filter) ? plan.Filter : null;
            if (filter != null && BrewEngine.FilterProfiles.ContainsKey(filter)) _vm.Filter = filter;

            // ③ 粉量：结构化优先（不变文化数值串）；否则正文「粉量 N g」正则
            double? dose = TryNum(plan.Dose);
            if (dose == null)
            {
                var doseM = Regex.Match(detail, @"粉量\s*(\d+)\s*g");
                if (doseM.Success && int.TryParse(doseM.Groups[1].Value, out int d)) dose = d;
            }
            if (dose.HasValue && dose.Value > 0 && dose.Value <= 60)
            {
                // 先选粉量档位（小≤10 / 标≤20 / 大>20），再设粉量，避免被档位范围钳制
                _vm.Size = dose.Value <= 10 ? "small" : dose.Value <= 20 ? "standard" : "large";
                _vm.Dose = dose.Value;
            }

            // ④ 粉水比：结构化优先；否则正文「粉水比 1:N」正则
            double? ratio = TryNum(plan.Ratio);
            if (ratio == null)
            {
                var ratioM = Regex.Match(detail, @"粉水比\s*1:(\d+(?:\.\d+)?)");
                if (ratioM.Success &&
                    double.TryParse(ratioM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out double r))
                    ratio = r;
            }
            if (ratio.HasValue && ratio.Value >= 10 && ratio.Value <= 20) _vm.Ratio = ratio.Value;

            // ⑤ 处理方式 / 产地 / 烘焙度 / 风味：仅结构化字段（正文正则不可靠，不做猜测）
            if (NotEmpty(plan.Process) && BrewEngine.ProcessProfiles.ContainsKey(plan.Process!)) _vm.Process = plan.Process!;
            if (NotEmpty(plan.Origin) && BrewEngine.OriginProfiles.ContainsKey(plan.Origin!)) _vm.Origin = plan.Origin!;
            // 烘焙度 setter 会把 Ag 读数同步回退到该档位区间中值（用户随后仍可微调）
            if (NotEmpty(plan.Roast) && BrewEngine.RoastProfiles.ContainsKey(plan.Roast!)) _vm.Roast = plan.Roast!;
            if (NotEmpty(plan.Flavor) && BrewEngine.FlavorProfiles.ContainsKey(plan.Flavor!)) _vm.Flavor = plan.Flavor!;

            _vm.MarkPlanSource(plan.Title); // 溯源：保存冲煮记录时写入 PlanTitle，记录这杯的配方来源
            _vm.Generate(); // 重新推导推荐方案（研磨/水质/报告/黄金杯/流程），使方案真正可冲煮
        }
        catch (Exception ex) { BrewViewModel.ErrorSink?.Invoke(ex); }
    }

    /// <summary>非空（且非纯空白）判断：结构化字段留空代表「未指定」。</summary>
    private static bool NotEmpty(string? s) => !string.IsNullOrWhiteSpace(s);

    /// <summary>不变文化解析数值字符串（结构化字段的 Dose / Ratio），失败返回 null。</summary>
    private static double? TryNum(string? s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;

    /// <summary>内置的咖啡大师 / 赛事冲煮方案目录（原始正文，数据源自项目文档《建设方案 V1.1》表 5-2/5-3/5-4/5-5/5-6 与《技术报告》实测）。
    /// 结构化联动元数据见 <see cref="MasterPlanMeta"/>，二者由 <see cref="MasterPlans"/> 合成为 <see cref="MasterPlanItem"/>。</summary>
    private static List<(string Title, string Summary, string Detail)> MasterPlanRaw()
    {
        return new List<(string, string, string)>
        {
            // ===== WBrC 世界冠军（按年份排列，表 5-2 扩展至全量）=====
            ("Keith O'Sullivan · Chemex（2011 WBrC 冠军）",
             "仅留存器具 Chemex，粉量/水量/分段均未公开。",
             "Keith O'Sullivan 冲煮（2011 WBrC 冠军）\n" +
             "· 器具：Chemex；粉量/水温/分段均未公开\n" +
             "· 证据等级 B（赛事录像留存）\n" +
             "· 参数待赛事录像补录，不虚构"),
            ("Matt Perger · V60（2012 WBrC 冠军）",
             "仅留存器具 V60，参数待补录。",
             "Matt Perger 冲煮（2012 WBrC 冠军）\n" +
             "· 器具：Hario V60；粉量/水温/分段均未公开\n" +
             "· 证据等级 B（赛事录像留存）\n" +
             "· 参数待补录，不虚构"),
            ("James McCarthy · 两段流速反差（2013 WBrC 冠军）",
             "24g/1:15.8，前段大水流后段限流。",
             "James McCarthy 冲煮（2013 WBrC 冠军）\n" +
             "· 粉量 24g / 总水 380g，粉水比 1:15.8\n" +
             "· 器具：Kalita Wave（蛋糕杯）\n" +
             "· 总时长 3:30；前段大水流、后段限流（两段流速反差控萃取强度）\n" +
             "· 分段结构未公开，仅记总量与总时长"),
            ("Stefanos Domatiotis · V60（2014 WBrC 冠军）",
             "仅留存器具 V60，参数待补录。",
             "Stefanos Domatiotis 冲煮（2014 WBrC 冠军）\n" +
             "· 器具：Hario V60；粉量/水温/分段均未公开\n" +
             "· 证据等级 B（赛事录像留存）\n" +
             "· 参数待赛事录像补录，不虚构"),
            ("Odd-Steinar Tøllefsen · 陶瓷 V60（2015 WBrC 冠军）",
             "20g/1:15，92℃陶瓷 V60 长时萃取。",
             "Odd-Steinar Tøllefsen 冲煮（2015 WBrC 冠军）\n" +
             "· 粉量 20g / 总水 300g，粉水比 1:15\n" +
             "· 水温 92℃；器具：陶瓷 V60\n" +
             "· 总时长 3:30；分段结构未公开\n" +
             "· 陶瓷保温性好，经典长时萃取"),
            ("粕谷哲 · 4:6 法（2016 WBrC 冠军）",
             "总分 5 段等水，前 40% 定酸甜、后 60% 定浓度。",
             "粕谷哲 4:6 冲煮法（2016 WBrC 冠军）\n" +
             "· 粉量 20g / 总水 300g，粉水比 1:15\n" +
             "· 水温 92℃，分 5 次注水，每次间隔约 45s\n" +
             "· 前 2 次共 40%（约 120g）决定酸甜：第1–2次各 60g\n" +
             "· 后 3 次共 60%（约 180g）决定浓度：第3–5次各 60g\n" +
             "· 总时长约 3:00；不单独闷蒸，第1次注水即闷蒸"),
            ("王策 · 短时快冲（2017 WBrC 冠军）",
             "15g/1:16.7，2 分钟短时快冲。",
             "王策冲煮（2017 WBrC 冠军，中国台湾）\n" +
             "· 粉量 15g / 总水 250g，粉水比 1:16.7\n" +
             "· 器具：V60；总时长 2:00\n" +
             "· 分段结构未公开；短时快冲风格"),
            ("Emi Fukahori · GINA 混合式（2018 WBrC 冠军）",
             "17g/1:12.9，变温 80→95℃ 浸泡+滴滤。",
             "Emi Fukahori 冲煮（2018 WBrC 冠军，瑞士）\n" +
             "· 粉量 17g / 总水 220g，粉水比 1:12.9\n" +
             "· 器具：GINA（可关阀混合式）；变温 80→95℃\n" +
             "· 分段：110g 80℃ 浸泡 → 110g 95℃ 滴滤\n" +
             "· 总时长 2:55；浸泡+滴滤混合，变温控风味"),
            ("杜嘉宁 · 分段高萃（2019 WBrC 冠军）",
             "高萃取率导向，多段稳定注水。",
             "杜嘉宁分段萃取（2019 WBrC 冠军）\n" +
             "· 粉量 16g / 总水 240g，粉水比 1:15\n" +
             "· 水温 94℃，研磨中细\n" +
             "· 分段注水：60g → 80g → 50g → 50g（闷蒸后约 4 段）\n" +
             "· 时长 1:40；强调高萃取均匀度，尾段收尾避免过萃"),
            ("Matt Winton · 双壶变温五段（2021 WBrC 冠军）",
             "20g/1:15，首段 93℃ 后四段 88℃，五段等量。",
             "Matt Winton 冲煮（2021 WBrC 冠军，瑞士）\n" +
             "· 粉量 20g / 总水 300g，粉水比 1:15\n" +
             "· 器具：V60；粗研磨（Hario 约 12 格）\n" +
             "· 双壶变温五段：60g×5，首段 93℃ → 后四段 88℃\n" +
             "· 总时长 2:40；每段待粉层将干即注"),
            ("徐诗媛 · 双粒径低温闷蒸（2022 WBrC 冠军）",
             "14g/1:14.3，70℃ 闷蒸 → 95℃，双粒径拼配。",
             "徐诗媛冲煮（2022 WBrC 冠军，中国台湾）\n" +
             "· 粉量 14g / 总水 200g，粉水比 1:14.3\n" +
             "· 器具：Orea V3 + Kalita 185 滤杯\n" +
             "· 双粒径拼配：1000μm 75% + 800μm 25%\n" +
             "· 分段：50g 70℃ 闷蒸 → 50g×3 95℃（每 30s）\n" +
             "· 总时长 2:00；低温闷蒸 + 双粒径控萃取"),
            ("Carlos Medina · 等段等量（2023 WBrC 冠军）",
             "15.5g/1:16.1，5×50g 等段等量 91℃。",
             "Carlos Medina 冲煮（2023 WBrC 冠军，智利）\n" +
             "· 粉量 15.5g / 总水 250g，粉水比 1:16.1\n" +
             "· 器具：Origami；水温 91℃\n" +
             "· 分段：5×50g 等段等量，每 30s 一段\n" +
             "· 总时长 2:30；等段等量均匀萃取"),
            ("Martin Wölfl · 高萃控温（2024 WBrC 冠军）",
             "C40 21–25 格（≈490μm），多段后段加大注水。",
             "Martin Wölfl 冲煮（2024 WBrC 冠军）\n" +
             "· 粉量 17g / 总水 270g，粉水比 1:15.9\n" +
             "· 水温 93℃，研磨 C40 21–25 格（≈490μm）\n" +
             "· 分段注水：60g → 60g → 50g → 100g（前两段小水、后段加大）\n" +
             "· 时长 2:20；后段增量提升醇厚"),
            ("彭近洋 · 三段变温（2025 WBrC 冠军）",
             "分段降温 96→80℃，高目数研磨防过萃。",
             "彭近洋冲煮（2025 WBrC 冠军）\n" +
             "· 粉量 15g / 总水 210g，粉水比 1:14\n" +
             "· 水温分段：96℃ → 96℃ → 80℃（前高后低防过萃）\n" +
             "· 研磨 FM#11（≈600μm 粗）；分段注水：30g → 90g → 90g\n" +
             "· 时长 1:45；粗研磨 + 后段降温提升甜感"),
            ("Nas Jaafar · 关阀浸泡（2026 WBrC 冠军）",
             "高目数 + 关阀浸泡，一注到底后静置。",
             "Nas Jaafar 冲煮（2026 WBrC 冠军）\n" +
             "· 粉量 15g / 总水 200g，粉水比 1:13.3\n" +
             "· 水温 92℃，研磨 #7 目（≈700μm，580rpm 研磨）\n" +
             "· 注水 100g → 100g 后关阀浸泡（Switch 式）\n" +
             "· 时长 2:10；关阀延长粉水接触、均匀萃取"),

            // ===== 2025 WBrC 决赛前四（表 5-3）=====
            ("彭近洋（2025 决赛）",
             "高萃导向，分段 30/90/90。",
             "彭近洋（2025 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 210g，粉水比 1:14\n" +
             "· 水温 96→96→80℃；分段注水 30g → 90g → 90g\n" +
             "· 研磨粗（FM#11 ≈600μm）；时长 1:45"),
            ("Bayu Prawiro（2025 决赛）",
             "三段降温 93→88→88℃，尾段增量。",
             "Bayu Prawiro（2025 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 220g，粉水比 1:14.7\n" +
             "· 水温 93→88→88℃（逐段降温）\n" +
             "· 分段注水 40g → 80g → 100g；时长约 2:00"),
            ("Carlos Escobar（2025 决赛）",
             "双段大水量，高温快萃。",
             "Carlos Escobar（2025 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 200g，粉水比 1:13.3\n" +
             "· 水温 90℃；分段注水 100g → 100g（两段等量）\n" +
             "· 时长约 1:50；高温双段快速萃取"),
            ("Elysia Tan（2025 决赛）",
             "四段阶梯注水，精细控流。",
             "Elysia Tan（2025 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 225g，粉水比 1:15\n" +
             "· 水温 92℃；分段注水 65g → 85g → 50g → 75g\n" +
             "· 时长约 2:10；四段阶梯稳定萃取"),

            // ===== 2026 WBrC 决赛前四（表 5-4）=====
            ("Nas Jaafar（2026 决赛）",
             "关阀浸泡，高目数粗磨。",
             "Nas Jaafar（2026 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 200g，粉水比 1:13.3\n" +
             "· 水温 92℃；注水 100g → 100g 后关阀\n" +
             "· 研磨 #7 目（≈700μm）；时长 2:10"),
            ("Simon Gautherin（2026 决赛）",
             "低温双段大水量，干净取向。",
             "Simon Gautherin（2026 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 200g，粉水比 1:13.3\n" +
             "· 水温 89℃（偏低）；分段注水 100g → 100g\n" +
             "· 时长约 2:00；低温突出干净明亮"),
            ("Bavis Kwong（2026 决赛）",
             "五段小水，#10 目细磨高萃。",
             "Bavis Kwong（2026 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 200g，粉水比 1:13.3\n" +
             "· 水温 94℃；研磨 #10 目（≈900μm）\n" +
             "· 分段注水 15g → 50g → 50g → 50g → 50g（五段小水）\n" +
             "· 时长约 2:20；细磨多段提升萃取"),
            ("Jackie Tran（2026 决赛）",
             "两段变温 94→80℃，先高后低。",
             "Jackie Tran（2026 WBrC 决赛前四）\n" +
             "· 粉量 15g / 总水 200g，粉水比 1:13.3\n" +
             "· 水温 94→80℃；分段注水 100g → 100g（后段降温）\n" +
             "· 时长约 2:00；前高后低突出甜感"),

            // ===== 经典流派 11 法（表 5-6）=====
            ("4:6 法（经典粕谷式）",
             "5 段等水，先定酸甜后定浓度。",
             "4:6 冲煮法（粕谷哲也经典）\n" +
             "· 粉量 20g / 总水 300g，粉水比 1:15\n" +
             "· 水温 92–93℃；5 次注水各 60g，间隔约 45s\n" +
             "· 前 2 次定酸甜、后 3 次定浓度"),
            ("Hoffmann V60 法",
             "大粉量大水量，稳定绕圈。",
             "James Hoffmann V60 法\n" +
             "· 粉量 30g / 总水 500g，粉水比 1:16.7\n" +
             "· 闷蒸后分 3–4 段绕圈注水，单次大水量\n" +
             "· 关注粉墙均匀与滴滤收尾"),
            ("Rao V60 法（Tetsu 风格）",
             "中粉量大水量，强调均匀萃取。",
             "Rao / Tetsu Kasuya V60 法\n" +
             "· 粉量 22g / 总水 360g，粉水比 1:16.4\n" +
             "· 闷蒸后分多段（4 段）稳定注水\n" +
             "· 强调搅动均匀与流速控制"),
            ("日式点滴法",
             "极细研磨、低温、长时间点滴。",
             "日式点滴法（Japanese Drip）\n" +
             "· 粉水比 1:12–1:15（偏浓）\n" +
             "· 研磨细；水温 85–90℃\n" +
             "· 极细水流长时间点滴，缓慢萃取"),
            ("Melodrip 低扰流法",
             "滤片分散水流，低扰流均萃。",
             "Melodrip 低扰流法\n" +
             "· 借助 Melodrip 滤片将水流均匀分散为细雨\n" +
             "· 避免冲散粉层、降低扰流，提升均匀萃取\n" +
             "· 适合浅烘花果豆，干净茶感"),
            ("Osmotic Flow 渗透流法",
             "先浸泡后注水，渗透式萃取。",
             "Osmotic Flow（渗透流）\n" +
             "· 先少量水浸湿全部粉层形成饱和床\n" +
             "· 再缓慢注水，靠渗透压均匀萃取\n" +
             "· 强调粉水充分接触、减少通道效应"),
            ("Switch 混合式（关阀浸泡）",
             "聪明杯式关阀浸泡，两段大水。",
             "Switch 混合式（浸泡 + 滴滤）\n" +
             "· 注水 100g → 100g 后关阀浸泡一段时间\n" +
             "· 开阀滴滤收尾；浸泡式延长粉水接触\n" +
             "· 均匀萃取、醇厚饱满"),
            ("一刀流（李震）",
             "单段注水，靠水流稳定控萃。",
             "一刀流（李震 / 单次注水）\n" +
             "· 粉量 15g / 总水 240g，粉水比 1:16\n" +
             "· 研磨 C40 16 格（≈600μm）；水温 94℃\n" +
             "· 闷蒸 80s 后一次性绕圈注水至总量，不分段"),
            ("三段变温法",
             "三段逐步降温，先高后低。",
             "三段变温法\n" +
             "· 粉水比 1:15 左右；三段注水水温逐步降低（如 94→90→86℃）\n" +
             "· 前段高温提萃、后段降温防过萃\n" +
             "· 适合中深烘，甜感突出"),
            ("Hoffmann 法压壶法",
             "法压浸泡，粗研磨长时。",
             "James Hoffmann 法压壶（French Press）法\n" +
             "· 粉量 30g / 总水 450g，粉水比 1:15\n" +
             "· 研磨粗；注水后浸泡 4:00，轻压活塞\n" +
             "· 浸泡式萃取，醇厚饱满"),
            ("AeroPress 反压法",
             "反压萃取，兑水稀释。",
             "AeroPress 反压（Inverted）法\n" +
             "· 粉量 35g；先 1:5（约 175g）反压萃取 1:30\n" +
             "· 水温 80℃；完成后兑 130g 热水稀释\n" +
             "· 短接触、高浓后兑，干净平衡"),

            // ===== CBrC 国内冠军 + CCL 中国咖啡冲煮大赛（表 5-5 扩展至全量）=====
            ("杜嘉宁 · CBrC 2019（代表中国出战 WBrC）",
             "16g/1:15，Origami 二次研磨，94℃ 四段。",
             "杜嘉宁 CBrC 2019 冲煮（代表中国出战 WBrC）\n" +
             "· 粉量 16g / 总水 240g，粉水比 1:15\n" +
             "· 器具：Origami；水温 94℃；二次研磨\n" +
             "· 分段：60g(6g/s) → 80g(4g/s) → 50g(5g/s) → 50g\n" +
             "· 总时长 2:00；双壶同注，分段控速"),
            ("李震 · 一刀流（2020 CBrC 冠军）",
             "C40 16 格筛 600μm，短闷蒸一刀。",
             "李震冲煮（2020 CBrC 冠军）\n" +
             "· 粉量 15g / 总水 240g，粉水比 1:16\n" +
             "· 水温 94℃；研磨 C40 16 格（过筛 600μm 以上）\n" +
             "· 50g 闷蒸 30s + 一次性注 240g，80s 一刀流"),
            ("彭近洋 · 无差别冲煮（2022 CBrC 冠军）",
             "2.5 倍闷蒸 + 5.5 倍二段高比例。",
             "彭近洋无差别冲煮法（2022 CBrC 冠军）\n" +
             "· 粉量 15g；闷蒸水量 ≈ 2.5 倍粉重（约 37.5g）\n" +
             "· 二段注水 ≈ 5.5 倍粉重（约 82.5g），合计约 1:8 注水节奏\n" +
             "· 长闷蒸 + 大二段，高萃取均匀"),
            ("李晋坤 · CBrC 2023 冠军（李震团队）",
             "V60，参数待补录，代表中国出战 2024 WBrC。",
             "李晋坤冲煮（CBrC 2023 冠军，李震团队）\n" +
             "· 器具：V60；粉量/水温/分段待补录\n" +
             "· 2024 年代表中国出战芝加哥 WBrC\n" +
             "· 参数待补录，不虚构"),
            ("彭近洋（2024 CBrC 冠军）",
             "同 2025 WBrC 冠军参数（三段变温）。",
             "彭近洋冲煮（2024 CBrC 冠军，同 2025 WBrC）\n" +
             "· 粉量 15g / 总水 210g，粉水比 1:14\n" +
             "· 水温 96→96→80℃；分段注水 30g → 90g → 90g\n" +
             "· 研磨粗（FM#11 ≈600μm）；时长 1:45"),

            // ===== CCL 中国咖啡冲煮大赛冠军（新增 2026-09-03）=====
            ("杨啸 · CCL 2023 冠军（云南昆明）",
             "云南豆，参数待补录。",
             "杨啸冲煮（CCL 2023 冠军，云南昆明）\n" +
             "· 产区：中国云南；参数待补录\n" +
             "· 2024 年 1 月昆明全国总决赛夺冠\n" +
             "· 参数待补录，不虚构"),
            ("张乐文 · CCL 2025 冠军（湖南米町咖啡）",
             "15g/1:15，94℃与75℃双水温，云南豆。",
             "张乐文冲煮（CCL 2025 冠军，湖南米町咖啡）\n" +
             "· 粉量 15g / 总水 225g，粉水比 1:15\n" +
             "· 双水温：94℃ 与 75℃；产区：中国云南\n" +
             "· 带白色花香、柑橘与核果风味\n" +
             "· 湖南选手 15 年来首次夺冠；总时长待核"),
        };
    }

    /// <summary>大师方案的可确证「结构化联动元数据」（按 Title 关联，键必须命中 <see cref="MasterPlanRaw"/> 中某一项，另有单元测试守卫）。
    /// 设计原则（重要）：赛事冲煮方案通常只公开水温 / 分段 / 时长 / 研磨，不公开豆子信息（产地与处理方式由选手自选），
    /// 烘焙度也多不写明。因此这里**只登记正文明示、可确证的项**，其余一律留空：
    /// 留空 = 「该方案未指定」→ 应用时保留用户当前选择，绝不臆测覆盖（避免把用户的肯尼亚水洗豆改成凭空猜的产地）。
    /// 2026-09-03 扩展：从正文补充全部可确证参数——滤杯(v60/smart/french_press)、滤纸(bleached/unbleached)、粉量、粉水比，
    /// 让 ApplyMasterPlan 不再依赖正文正则回退（更可靠）。</summary>
    private static readonly Dictionary<string, (string? Method, string? Dripper, string? Filter, string? Dose, string? Ratio, string? Roast)> MasterPlanMeta = new()
    {
        // ===== 早期 WBrC 冠军（参数未公开的不登记结构化字段）=====
        // 2011 Keith O'Sullivan: 仅知 Chemex，引擎不支持该滤杯
        // 2012 Matt Perger: 仅知 V60
        ["Matt Perger · V60（2012 WBrC 冠军）"] = (null, "v60", null, null, null, null),
        // 2013 James McCarthy: Kalita Wave = 平底蛋糕杯
        ["James McCarthy · 两段流速反差（2013 WBrC 冠军）"] = ("classic", "wave", null, "24", "15.8", null),
        // 2014 Stefanos: 仅知 V60
        ["Stefanos Domatiotis · V60（2014 WBrC 冠军）"] = (null, "v60", null, null, null, null),
        // 2015 Odd-Steinar: V60, 20g/1:15
        ["Odd-Steinar Tøllefsen · 陶瓷 V60（2015 WBrC 冠军）"] = (null, "v60", null, "20", "15", null),

        // ===== WBrC 世界冠军 =====
        // 粕谷哲 4:6 法：V60 + 漂白滤纸，20g/1:15
        ["粕谷哲 · 4:6 法（2016 WBrC 冠军）"] = ("kasuya46", "v60", "bleached", "20", "15", null),
        // 杜嘉宁分段高萃：V60 + 漂白滤纸，16g/1:15
        ["杜嘉宁 · 分段高萃（2019 WBrC 冠军）"] = ("classic", "v60", "bleached", "16", "15", null),
        // 2017 王策: V60, 15g/1:16.7
        ["王策 · 短时快冲（2017 WBrC 冠军）"] = (null, "v60", null, "15", "16.7", null),
        // 2018 Emi Fukahori: GINA=可关阀混合式→smart, 17g/1:12.9
        ["Emi Fukahori · GINA 混合式（2018 WBrC 冠军）"] = (null, "smart", null, "17", "12.9", null),
        // 2021 Matt Winton: V60, 20g/1:15
        ["Matt Winton · 双壶变温五段（2021 WBrC 冠军）"] = ("classic", "v60", null, "20", "15", null),
        // 2022 徐诗媛: Orea V3≈V60锥形, 14g/1:14.3
        ["徐诗媛 · 双粒径低温闷蒸（2022 WBrC 冠军）"] = (null, "v60", null, "14", "14.3", null),
        // 2023 Carlos Medina: Origami≈V60锥形, 15.5g/1:16.1
        ["Carlos Medina · 等段等量（2023 WBrC 冠军）"] = ("classic", "v60", null, "15.5", "16.1", null),
        // Martin Wölfl 高萃控温：V60 + 漂白滤纸，17g/1:15.9
        ["Martin Wölfl · 高萃控温（2024 WBrC 冠军）"] = ("classic", "v60", "bleached", "17", "15.9", null),
        // 彭近洋三段变温：V60 + 漂白滤纸，15g/1:14
        ["彭近洋 · 三段变温（2025 WBrC 冠军）"] = ("classic", "v60", "bleached", "15", "14", null),
        // Nas Jaafar 关阀浸泡：聪明杯 + 无滤纸(金属阀)，15g/1:13.3
        ["Nas Jaafar · 关阀浸泡（2026 WBrC 冠军）"] = (null, "smart", "metal", "15", "13.3", null),

        // ===== 2025 WBrC 决赛前四 =====
        ["彭近洋（2025 决赛）"] = ("classic", "v60", "bleached", "15", "14", null),
        ["Bayu Prawiro（2025 决赛）"] = ("classic", "v60", "bleached", "15", "14.7", null),
        ["Carlos Escobar（2025 决赛）"] = ("classic", "v60", "bleached", "15", "13.3", null),
        ["Elysia Tan（2025 决赛）"] = ("classic", "v60", "bleached", "15", "15", null),

        // ===== 2026 WBrC 决赛前四 =====
        ["Nas Jaafar（2026 决赛）"] = (null, "smart", "metal", "15", "13.3", null),
        ["Simon Gautherin（2026 决赛）"] = ("classic", "v60", "bleached", "15", "13.3", null),
        ["Bavis Kwong（2026 决赛）"] = ("classic", "v60", "bleached", "15", "13.3", null),
        ["Jackie Tran（2026 决赛）"] = ("classic", "v60", "bleached", "15", "13.3", null),

        // ===== 经典流派 11 法 =====
        ["4:6 法（经典粕谷式）"] = ("kasuya46", "v60", "bleached", "20", "15", null),
        ["Hoffmann V60 法"] = ("classic", "v60", "bleached", "30", "16.7", null),
        ["Rao V60 法（Tetsu 风格）"] = ("classic", "v60", "bleached", "22", "16.4", null),
        ["日式点滴法"] = ("classic", "v60", "unbleached", null, null, null), // 粉量/比例因方案而异
        ["Melodrip 低扰流法"] = (null, "v60", "bleached", null, null, "light"), // 适合浅烘
        ["Osmotic Flow 渗透流法"] = (null, "v60", "bleached", null, null, null),
        ["Switch 混合式（关阀浸泡）"] = (null, "smart", "metal", null, null, null),
        ["一刀流（李震）"] = ("classic", "v60", "bleached", "15", "16", null),
        ["三段变温法"] = ("classic", "v60", "bleached", null, "15", "medium_dark"), // 适合中深烘
        ["Hoffmann 法压壶法"] = (null, "french_press", null, "30", "15", null), // 法压壶无需滤纸
        ["AeroPress 反压法"] = (null, "aeropress", null, "35", "5", null), // AeroPress 先 1:5 反压再兑水

        // ===== CBrC 国内冠军 =====
        ["李震 · 一刀流（2020 CBrC 冠军）"] = ("classic", "v60", "bleached", "15", "16", null),
        ["彭近洋 · 无差别冲煮（2022 CBrC 冠军）"] = ("classic", "v60", "bleached", "15", null, null),
        // CBrC 2019 杜嘉宁: Origami≈V60, 16g/1:15
        ["杜嘉宁 · CBrC 2019（代表中国出战 WBrC）"] = ("classic", "v60", null, "16", "15", null),
        ["彭近洋（2024 CBrC 冠军）"] = ("classic", "v60", "bleached", "15", "14", null),

        // ===== CCL 中国咖啡冲煮大赛 =====
        // CCL 2025 张乐文: 15g/1:15（滤杯未公开）
        ["张乐文 · CCL 2025 冠军（湖南米町咖啡）"] = (null, null, null, "15", "15", null),
    };

    /// <summary>合成最终的大师方案清单：原始正文 + 结构化联动元数据 → <see cref="MasterPlanItem"/>。</summary>
    private static List<MasterPlanItem> MasterPlans() => MasterPlanRaw().Select(t =>
    {
        MasterPlanMeta.TryGetValue(t.Title, out var meta);
        return new MasterPlanItem
        {
            Title = t.Title, Summary = t.Summary, Detail = t.Detail,
            Method = meta.Method, Dripper = meta.Dripper, Filter = meta.Filter,
            Dose = meta.Dose, Ratio = meta.Ratio, Roast = meta.Roast,
        };
    })
    // 咖啡大师清单：按年份最新→最旧排序（从标题解析 4 位年份），无年份的沉底
    .OrderByDescending(p => MasterPlanYear(p.Title))
    .ThenBy(p => p.Title, System.StringComparer.Ordinal)
    .ToList();

    /// <summary>从方案标题解析 4 位年份（如「2011 WBrC 冠军」→ 2011）；无年份返回 0 用于沉底。</summary>
    private static int MasterPlanYear(string title)
    {
        if (string.IsNullOrEmpty(title)) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(title, @"\b(?:19|20)\d{2}\b");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    /// <summary>冲煮方式科普介绍弹窗：标题 + 可滚动正文（科普说明 + 当前参数实时分段 / 水温 / 总水量）。</summary>
    private void ShowMethodIntro()
    {
        var sp = new StackPanel { Spacing = 10, Margin = new Thickness(14) };
        sp.Children.Add(Text(_vm.MethodLabel, 16, FontWeight.Bold, Accent));
        var body = Text(_vm.MethodIntroBody, 13, fg: Fg, wrap: true);
        var scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        sp.Children.Add(scroller);
        Action close = null!;
        sp.Children.Add(MakeButton(I18n.T("OK") ?? "确定", (_, _) => close(), stretch: true, bg: AccentBtn, fg: "#1A100A", bold: true));
        (close, _) = PresentModal(I18n.T("MethodIntroTitle"), sp, 520, 420);
    }

    /// <summary>「生成推荐冲煮方案」：生成配方（同步联动数据），并直接切到左侧第 2 页「推荐冲煮方案」展示（不再弹独立窗口）。</summary>
    private void ShowRecommendPlan()
    {
        _vm.Generate(); // 生成并更新联动数据
        ShowLeftPage(LeftPage.Recommend); // 直接展示左侧「推荐冲煮方案」页
    }

    /// <summary>「冲煮记录查询和统计」弹窗：历史统计卡 + 冲煮记录列表（原实时冲煮区尾部的统计 / 记录区块并入此窗，2026-09-02）。
    /// 打开时把 _recordsList 指向弹窗内列表（记录变更自动刷新）；关闭时复位为脱离的空 ItemsControl，避免触碰已关闭窗口。</summary>
    private void ShowRecordsDialog()
    {
        var sp = new StackPanel { Spacing = 10, Margin = new Thickness(14) };
        // 历史统计卡
        var statsCard = new Border { Background = Brush("#241A12"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
        var statsText = Text("", 13, fg: "#E8D8C7", wrap: true); statsText.Bind(TextBlock.TextProperty, B(nameof(_vm.StatsText)));
        statsCard.Child = statsText;
        sp.Children.Add(Text("📊 " + I18n.T("Stats"), 15, FontWeight.Bold, Accent));
        sp.Children.Add(statsCard);
        // 导出 CSV（一键备份全量记录到 brews-export.csv，与 brews.json 同目录）+ 状态行
        var expStatus = Text("", 12, fg: "#E8C79A", wrap: true);
        var expRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 2, 0, 0) };
        var expBtn = MakeButton(I18n.T("ExportCsv"), (_, _) =>
        {
            try
            {
                var dir = Path.GetDirectoryName(_vm.RecordsPath);
                if (string.IsNullOrEmpty(dir)) dir = ".";
                expStatus.Text = _vm.ExportRecordsCsv(Path.Combine(dir, "brews-export.csv"));
            }
            catch (Exception ex) { expStatus.Text = string.Format(I18n.T("ExportCsvFailed"), ex.Message); }
        }, stretch: false, bg: "#2E6FB0", fg: "#FFFFFF", bold: true);
        Grid.SetColumn(expBtn, 0);
        expRow.Children.Add(expBtn);
        Grid.SetColumn(expStatus, 1); expStatus.Margin = new Thickness(8, 0, 0, 0); expStatus.VerticalAlignment = VerticalAlignment.Center;
        expRow.Children.Add(expStatus);
        sp.Children.Add(expRow);
        // 冲煮记录列表（点击展开详情；随保存/删除实时刷新）
        sp.Children.Add(Text("📋 " + I18n.T("Records"), 15, FontWeight.Bold, Accent));
        var list = new ItemsControl { HorizontalAlignment = HorizontalAlignment.Stretch };
        _recordsList = list;
        sp.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360 });
        var content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Window? window;
        // 弹窗关闭：把记录列表复位为脱离的空实例，后续 RebuildRecords（保存/删除触发）不再触碰已关闭的弹窗
        (_, window) = PresentModal(I18n.T("RecordsQuery"), content, 560, 540,
            onClosed: () =>
            {
                _recordsList = new ItemsControl { HorizontalAlignment = HorizontalAlignment.Stretch };
                _recordsDialogWindow = null;
            });
        _recordsDialogWindow = window;   // 桌面端 Window（headless 可反射）；移动端为 null（浮层）
        RebuildRecords();
    }

    // ---------- 控件工厂 ----------
    private Binding B(string path, BindingMode mode = BindingMode.OneWay)
        => new() { Source = _vm, Path = path, Mode = mode };

    /// <summary>带 ⓘ 信息图标的标题容器：ⓘ 紧贴参数名文字后方（非推到最右），点击弹出该参数对冲煮的影响因素与逻辑。</summary>
    private StackPanel TitledInfo(string key, string label, Control control)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        var t = Text(label, 14, FontWeight.SemiBold);
        Grid.SetColumn(t, 0);
        head.Children.Add(t);
        var info = MakeParamInfoButton(key);
        info.Margin = new Thickness(6, 0, 0, 0); // 与文字留 6px 间距
        Grid.SetColumn(info, 1);
        head.Children.Add(info);
        var s = new StackPanel { Spacing = 4 };
        s.Children.Add(head);
        s.Children.Add(control);
        return s;
    }

    /// <summary>ⓘ 信息图标按钮：caramel 底白字，与整体配色一致。移动端放大到 ≥30px 命中区（触摸友好）。</summary>
    private Button MakeParamInfoButton(string key)
    {
        double infoSize = IsMobilePlatform ? 30 : _compact ? 24 : 20;
        var b = new Button
        {
            Content = "i",
            FontSize = infoSize * 0.55,
            FontStyle = FontStyle.Italic,
            FontWeight = FontWeight.Bold,
            Width = infoSize, Height = infoSize, MinHeight = infoSize,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(infoSize / 2),
            Background = Brush(Accent),
            Foreground = Brush("#FFF7EC"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        ToolTip.SetTip(b, I18n.T("ParamInfoTip"));
        b.Click += (_, _) => ShowParamInfo(key);
        return b;
    }

    /// <summary>参数知识弹窗：定位 / 作用机理 / 引擎规则（当前标定实时生成，与引擎行为永不漂移） / 实务建议 + 来源行。</summary>
    private Window? _paramInfoDialog;            // 参数逻辑弹窗（headless 测试可经反射访问）

    private void ShowParamInfo(string key)
    {
        var e = ParamInfo.Entries.GetValueOrDefault(key);
        if (e == null) return;
        bool zh = I18n.Current == I18n.ZhCN;

        var sp = new StackPanel { Spacing = 9, Margin = new Thickness(16) };
        sp.Children.Add(Text("ℹ️ " + (zh ? e.Title : e.TitleEn), 17, FontWeight.Bold, Accent));
        sp.Children.Add(InfoSection(I18n.T("PILoc"), zh ? e.Role : e.RoleEn));
        sp.Children.Add(InfoSection(I18n.T("PIMech"), zh ? e.Mechanism : e.MechanismEn));
        if (e.EngineRules != null)
            sp.Children.Add(InfoSection(I18n.T("PIRules"), e.EngineRules().Split('\n')));
        sp.Children.Add(InfoSection(I18n.T("PITips"), (zh ? e.Tips : e.TipsEn).Select(t => "• " + t)));
        sp.Children.Add(Text(ParamInfo.SourceLocalized, 10.5, fg: Sub, wrap: true));
        Action close = null!;
        sp.Children.Add(MakeButton(I18n.T("OK") ?? "确定", (_, _) => close(), stretch: true, bg: AccentBtn, fg: "#1A100A", bold: true));
        var content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Window? window;
        (close, window) = PresentModal(
            I18n.T("ParamInfoTitle") + " · " + (zh ? e.Title : e.TitleEn),
            content, 560, 580,
            onClosed: () => _paramInfoDialog = null);
        _paramInfoDialog = window;   // 桌面端 Window（headless 可反射）；移动端为 null（浮层）
    }

    /// <summary>知识弹窗区块：区块小标题 + 可换行正文（支持多行/多段）。</summary>
    private StackPanel InfoSection(string title, IEnumerable<string> bodies)
    {
        var s = new StackPanel { Spacing = 3 };
        s.Children.Add(Text(title, 12.5, FontWeight.Bold, Accent));
        foreach (var b in bodies)
            s.Children.Add(Text(b.TrimEnd(), 12.5, fg: Fg, wrap: true));
        return s;
    }

    private StackPanel InfoSection(string title, string body) => InfoSection(title, new[] { body });

    private ComboBox Combo(IEnumerable<ComboItem> items, string vmPath)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = Brush(Fg), MinHeight = 40 };
        c.ItemsSource = items;
        c.DisplayMemberBinding = new Binding("Label");
        c.SelectedValueBinding = new Binding("Key");
        c.Bind(ComboBox.SelectedValueProperty, B(vmPath, BindingMode.TwoWay));
        return c;
    }

    /// <summary>粉量输入框 + −/+ 按钮（触摸友好），数值钳制在所选档位范围内（DoseMin..DoseMax）。titled=false 时不内置标题行（由外部 TitledInfo 提供，带 ⓘ 图标）。</summary>
    private StackPanel MakeDoseStepper(bool titled = true)
    {
        var s = new StackPanel { Spacing = 4 };
        if (titled) s.Children.Add(Text(I18n.T("Dose"), 14, FontWeight.SemiBold));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var minus = MakeButton("−", (_, _) =>
        {
            _vm.Dose = Math.Max(_vm.DoseMin, Math.Round(_vm.Dose - 1));
        }, minW: 48, bg: "#5A4636", bold: true);
        var plus = MakeButton("+", (_, _) =>
        {
            _vm.Dose = Math.Min(_vm.DoseMax, Math.Round(_vm.Dose + 1));
        }, minW: 48, bg: "#5A4636", bold: true);
        var box = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = _vm.Dose.ToString("0"),
            Foreground = Brush(Fg), Background = Brush(Panel),
            MinHeight = 40,
        };
        box.Bind(TextBox.TextProperty, new Binding
        {
            Source = _vm, Path = nameof(_vm.Dose), Mode = BindingMode.TwoWay,
            Converter = new NumericClampConverter(_vm, nameof(_vm.DoseMin), nameof(_vm.DoseMax), 1),
        });
        Grid.SetColumn(minus, 0); Grid.SetColumn(box, 1); Grid.SetColumn(plus, 2);
        row.Children.Add(minus); row.Children.Add(box); row.Children.Add(plus);
        s.Children.Add(row);
        return s;
    }

    // 日期选择控件：Avalonia DatePicker（Fluent 主题原生日历飞出，完整显示 年/月/日，支持手动输入与选择）
    private static readonly DateValueConverter _dateConv = new();

    /// <summary>统一日期选择器：isRoast=true 绑烘焙日期（默认今天前 3 天），false 绑冲煮日期（默认今天）；均支持手动修改。Avalonia 原生 DatePicker 飞出日历完整显示「2026年9月1日」式年月日。</summary>
    private Control MakeDatePicker(bool isRoast)
    {
        var dp = new DatePicker
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(Fg),
            Background = Brush(Panel),
            BorderBrush = Brush("#3A2A1C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinHeight = 40, // 触摸目标统一 ≥40（与其他输入控件一致，2026-09-08 触摸体验优化）
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 13,
        };
        dp.Bind(DatePicker.SelectedDateProperty, new Binding
        {
            Source = _vm,
            Path = isRoast ? nameof(_vm.RoastDate) : nameof(_vm.BrewDate),
            Mode = BindingMode.TwoWay,
            Converter = _dateConv,
        });
        return dp;
    }

    // 烘焙值 Ag 输入框：与烘焙度档位强关联（切换档位自动取区间中值），可手动微调（钳制 20–100）
    private TextBox MakeAgBox()
    {
        var box = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = _vm.RoastAg.ToString("0"),
            Foreground = Brush(Fg),
            Background = Brush(Panel),
            MinHeight = 40,
        };
        box.Bind(TextBox.TextProperty, new Binding
        {
            Source = _vm,
            Path = nameof(_vm.RoastAg),
            Mode = BindingMode.TwoWay,
            Converter = new NumericClampConverter(_vm, "RoastAgMin", "RoastAgMax", 0),
        });
        return box;
    }

    // 海拔输入框已重新加回（2026-09-13）：作为「咖啡豆密度」的主要驱动量，仅驱动密度/研磨，不直接改水温
    // （尊重 2026-09-08 用户要求：海拔沸点封顶/反向补偿逻辑已从全部计算移除）。

    private void RebuildRecords()
    {
        _recordsList.Items.Clear();
        foreach (var rec in _vm.Records)
        {
            // 兼容旧记录：部分字段可能为空，做安全展示避免显示"()"
            var region = string.IsNullOrEmpty(rec.OriginRegion) ? "" : $"({rec.OriginRegion})";
            var grind = (rec.GrindC40 == 0 && rec.GrindEK == 0)
                ? rec.GrindLabel
                : $"{rec.GrindLabel}(C40≈{rec.GrindC40}/EK≈{rec.GrindEK})";

            // 2026-09-11 记录卡头（设计系统卡片化）：日期胶囊 + 标题/产地 + 右侧用量摘要（Data 字阶焦糖金）
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var datePill = new Border
            {
                Background = Brush(Panel),
                BorderBrush = new SolidColorBrush(Palette.WithAlpha(Palette.Cream, 22)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Text($"{rec.BrewDate:yyyy-MM-dd}", 11, FontWeight.SemiBold, Palette.Foam),
            };
            Grid.SetColumn(datePill, 0);
            head.Children.Add(datePill);
            var titleSp = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 0, 0) };
            titleSp.Children.Add(Text($"{rec.MethodLabel} · {rec.RoastLabel}", 13, FontWeight.SemiBold, Fg));
            titleSp.Children.Add(Text($"{rec.OriginLabel}{region} · {rec.ProcessLabel}", 11, fg: Sub, wrap: true));
            Grid.SetColumn(titleSp, 1);
            head.Children.Add(titleSp);
            var doseSum = Text($"{rec.Dose:0}g · 1:{rec.Ratio:0}", 13, FontWeight.Bold, Palette.Caramel, vCenter: true);
            Grid.SetColumn(doseSum, 2);
            head.Children.Add(doseSum);

            var detail = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 0, 0) };
            // 专业冲煮报告式详情（参考 SCA 手冲/杯测记录表）
            detail.Children.Add(Text($"{rec.DripperLabel} · {rec.FilterLabel} · {grind}", 12, fg: Sub, wrap: true));
            detail.Children.Add(Text($"{I18n.T("Dose")} {rec.Dose:0}g · {I18n.T("RatioRec")} 1:{rec.Ratio:0} · {I18n.T("Water")} {rec.TotalWater:0}g", 12, fg: Sub));
            detail.Children.Add(Text($"{I18n.T("Roast")} {rec.RoastLabel}（Ag {rec.RoastAgMin}–{rec.RoastAgMax}）· {I18n.T("Bloom")} {rec.BloomWait}s · {I18n.T("Temp")} {rec.Temp}℃", 12, fg: Sub));
            detail.Children.Add(Text($"{I18n.T("Water")} {rec.WaterLabel}", 12, fg: Sub));
            detail.Children.Add(Text($"风味走向：{rec.OriginFlavor} · 等级：{rec.OriginGrade}", 12, fg: Sub, wrap: true));
            if (rec.FinalWeight.HasValue)
                detail.Children.Add(Text($"实测 {I18n.T("Yield")} {rec.FinalWeight:0}g · {I18n.T("RatioAchieved")} 1:{rec.RatioAchieved:0.0}", 12, fg: Palette.SageGreen));
            // 方案溯源（Phase 11）：这杯的配方来自哪个方案
            if (!string.IsNullOrWhiteSpace(rec.PlanTitle))
                detail.Children.Add(Text($"{I18n.T("PlanSource")} {rec.PlanTitle}", 12, fg: Sub, wrap: true));
            // 记录 → 方案复刻：满意的一杯一键沉淀为「我的方案」，随时重冲（与删除并排）
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            actions.Children.Add(MakeButton(I18n.T("SaveMyPlan"), (_, _) => _vm.RecordToMyPlan(rec.Id), minW: 60, bg: "#3A2A1C", fg: Accent));
            actions.Children.Add(MakeButton(I18n.T("Delete"), (_, _) => _vm.DeleteRecord(rec.Id), minW: 60, bg: "#5A2A2A", fg: "#F0C27A"));
            detail.Children.Add(actions);

            // 2026-09-11 记录卡玻璃化：Espresso α214 + 1px Cream α26 描边 + 圆角 12（与模块卡同语言）
            var exp = new Expander
            {
                Background = new SolidColorBrush(Palette.WithAlpha(Card, 214)),
                BorderBrush = new SolidColorBrush(Palette.WithAlpha(Palette.Cream, 26)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Header = head,
                Content = detail,
            };
            _recordsList.Items.Add(exp);
        }
    }

    private TextBlock Text(string text, double size = 14, FontWeight weight = FontWeight.Normal, string? fg = null, bool wrap = false, bool vCenter = false)
    {
        var s = size * _scale;
        if (s < 9) s = 9; // 下限钳制，保证可读
        var t = new TextBlock
        {
            Text = text, FontSize = s, FontWeight = weight,
            Foreground = Brush(fg ?? Fg), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = vCenter ? VerticalAlignment.Center : VerticalAlignment.Top,
        };
        return t;
    }

    /// <summary>
    /// 图标字形（2026-09-13 UI 升级新增）：把 emoji / 符号按"界面图标"而不是"文本"来渲染。
    ///
    /// 与 <see cref="Text"/> 的三点差异，都是为了让图标在四端稳定、可控：
    ///  ① <b>显式绑定内嵌图标字族</b>（<see cref="FontConfig.IconFontFamily"/>）——不依赖平台回退，
    ///     Android 上不会再出现整片图标空白；
    ///  ② <b>显式前景色</b>——这是修掉一个真实显示缺陷：瓷砖字形若不给色，会继承 Button 的主题前景，
    ///     在深色瓷砖上呈深色、对比度极低（旧版本即如此）。默认给 Cream 亮色；
    ///  ③ <b>居中 + 不换行 + 行高收紧</b>——字形在瓷砖里光学居中，不被行高顶偏。
    /// </summary>
    private TextBlock IconText(string glyph, double size, string? color = null, bool vCenter = true)
    {
        var s = size * _scale;
        if (s < 9) s = 9;
        return new TextBlock
        {
            Text = glyph,
            FontSize = s,
            FontFamily = FontConfig.IconFontFamily,
            Foreground = Brush(color ?? Fg),
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = vCenter ? VerticalAlignment.Center : VerticalAlignment.Top,
        };
    }

    private Button MakeButton(string text, EventHandler<RoutedEventArgs> handler, double minW = 0, string? bg = null, string? fg = null, bool bold = false, bool stretch = false)
    {
        // 2026-09-11 按钮设计升级：
        //  · 主操作（AccentBtn 金）→ 垂直渐变（上亮下暗）+ 1px 深描边 = 立体质感；
        //  · 危险红/其他 → 保持纯色（红=警示语义，测试对 SolidColorBrush 断言）；
        //  · 圆角 8→10、水平 Padding 10→12（文字呼吸感）。
        bool isPrimary = bg == AccentBtn;
        IBrush background = isPrimary
            ? Palette.Vertical("#DFA254", "#B3742E")   // 上亮下暗：焦糖黄铜的立体感
            : Brush(bg ?? "#5A4636");
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(14, 9),
            CornerRadius = new CornerRadius(12),       // 10 → 12：更接近 iOS 按钮圆角
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Background = background,
            Foreground = Brush(fg ?? Fg),
            // 主操作：顶部高光描边（模拟受光面）；次操作保持纯净、无色描边。
            // 注：Avalonia 11 的 Button 无 BoxShadow，这里的"立体感"由渐变 + 高光描边承担。
            BorderBrush = isPrimary ? new SolidColorBrush(Palette.WithAlpha("#FFFFFF", 70)) : null,
            BorderThickness = isPrimary ? new Thickness(1) : default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 44, // 触摸目标 ≥44px（Android/iOS HIG 推荐）
        };
        if (minW > 0) b.MinWidth = minW;
        if (stretch) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.Click += handler;
        // 触摸/鼠标按压反馈（2026-09-10 持续改善触摸体验）：按下时整体淡化 15%，松手复原
        b.Opacity = 1.0;
        b.PointerPressed += (_, _) => b.Opacity = 0.85;
        b.PointerReleased += (_, _) => b.Opacity = 1.0;
        b.PointerCaptureLost += (_, _) => b.Opacity = 1.0;
        // 桌面悬停（2026-09-13）：仅主操作按钮提亮（渐变整体上移一档），次操作保持静止，
        // 避免整屏按钮一起闪烁。松开/离开都复位，防止悬停态卡死。
        if (isPrimary)
        {
            var restGrad = Palette.Vertical("#DFA254", "#B3742E");
            var hoverGrad = Palette.Vertical("#EFB266", "#C2833A");
            b.PointerEntered += (_, _) => b.Background = hoverGrad;
            b.PointerExited += (_, _) => b.Background = restGrad;
            b.PointerCaptureLost += (_, _) => b.Background = restGrad;
        }
        return b;
    }

    /// <summary>模拟实体称按钮：圆角+下沿高光描边，营造物理按键质感。按下时背景压暗并整体下沉 2px（明确的可按下反馈）；禁用态自动变灰。circular=true 时渲染为圆形主操作按钮（如红色开始键）。</summary>
    private Button MakeScaleButton(string text, EventHandler<RoutedEventArgs> handler, string? bg = null, string? fg = null, bool bold = false, double minW = 0, bool circular = false)
    {
        var normalBg = bg ?? "#3A2A1C";
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(8, 10),
            CornerRadius = new CornerRadius(10),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Background = Brush(normalBg),
            Foreground = Brush(fg ?? Fg),
            BorderBrush = Brush("#1A100A"),
            BorderThickness = new Thickness(1, 1, 1, 3), // 下沿加粗，模拟按键厚度
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 46,
            Tag = normalBg, // 记录常态背景色，松手时恢复
        };
        // 按下视觉反馈：背景压暗 + 整体下沉 2px，松手/指针离开/失焦时恢复，使「按下」明确可辨
        b.PointerPressed += (_, _) => { b.Background = Brush("#0E0805"); b.RenderTransform = new TranslateTransform(0, 2); };
        void Release(object? _, RoutedEventArgs __) { b.Background = Brush((string)b.Tag); b.RenderTransform = null; }
        b.PointerReleased += Release; b.PointerExited += Release; b.PointerCaptureLost += Release;
        // 禁用态明确变灰（不透明 → 半透明）
        b.Bind(Button.OpacityProperty, new Binding { Source = b, Path = nameof(b.IsEnabled), Converter = BoolToDoubleConverter.Instance });
        if (circular)
        {
            b.Width = 80; b.Height = 80; b.MinHeight = 0; b.MinWidth = 0;
            b.CornerRadius = new CornerRadius(40);
            b.Padding = new Thickness(6);
            b.BorderBrush = Brush("#F6C9A0");
            b.BorderThickness = new Thickness(2);
            b.FontSize = 14;
        }
        if (minW > 0) b.MinWidth = minW;
        b.Click += handler;
        return b;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    // ---------- 紧凑（手机竖屏）左右分页辅助 ----------
    /// <summary>紧凑模式：切到指定页（0=设置 1=冲煮）并刷指示器；非紧凑模式不做事。
    /// 两页 IsVisible 互斥切换（控件不摘树，ScrollViewer 滚动位置保留）。</summary>
    private void GoCompactPage(CompactPage page)
    {
        if (!_compact || _compactHost == null) return;
        _compactPage = page;
        if (_compactPage0 != null) _compactPage0.IsVisible = page == CompactPage.Settings;
        if (_compactPage1 != null) _compactPage1.IsVisible = page == CompactPage.Brew;
        RefreshCompactIndicator();
    }

    private void RefreshCompactIndicator()
    {
        if (_compactIndicator == null) return;
        string dot0 = _compactPage == CompactPage.Settings ? "●" : "○";
        string dot1 = _compactPage == CompactPage.Brew ? "●" : "○";
        string hint = IsMobilePlatform ? " ← 左右滑动 →" : "";
        _compactIndicator.Text = $"{dot0} {I18n.T("Settings")}   {dot1} {I18n.T("Brew")}{hint}";
        if (_compactIndBtnLeft != null) _compactIndBtnLeft.IsEnabled = _compactPage != CompactPage.Settings;
        if (_compactIndBtnRight != null) _compactIndBtnRight.IsEnabled = _compactPage != CompactPage.Brew;
    }

    // Pointer 三事件（经验405296：不用 PointerGestureRecognizer，自算滑动方向 + 纵向竞争排除）
    // 手势三段式：① 按下记录起点；② 移动中先做意图判定——纵向超限让路给内容滚动，
    //   横向意图（|dx|>SwipeStartX 且明显大于 |dy|）才捕获指针进入滑页判定；
    //   捕获后的移动事件改道 Canvas，ScrollViewer 不再消费，滑页判定不被内容滚动打断；
    // ③ 松手按总位移切页（左滑看下一页 / 右滑回上一页），并释放指针捕获。
    private void OnCompactPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerStart = e.GetPosition(sender as Control);
        _pointerTracking = true;
        _swipeCaptured = false;
    }
    private void OnCompactPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pointerTracking) return;
        var pos = e.GetPosition(sender as Control);
        double dx = pos.X - _pointerStart.X, dy = pos.Y - _pointerStart.Y;
        if (!_swipeCaptured)
        {
            // 纵向意图（内容滚动）：放弃切页手势，让 ScrollViewer 正常滚动
            if (Math.Abs(dy) > SwipeMaxY) _pointerTracking = false;
            // 横向意图：捕获指针（此后移动/松手事件固定路由到 Canvas，滑页判定稳定）
            else if (Math.Abs(dx) > SwipeStartX && Math.Abs(dx) > Math.Abs(dy) * 1.5)
            {
                if (sender is IInputElement ie) e.Pointer.Capture(ie);
                _swipeCaptured = true;
            }
        }
    }
    private void OnCompactPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pointerTracking) return;
        _pointerTracking = false;
        ReleaseSwipeCapture(e.Pointer);
        var pos = e.GetPosition(sender as Control);
        double dx = pos.X - _pointerStart.X;
        if (Math.Abs(dx) < SwipeMinX) return;
        // 左滑（手指向左，看右侧页）：页0→页1；右滑：页1→页0
        if (dx < 0 && _compactPage == CompactPage.Settings) GoCompactPage(CompactPage.Brew);
        else if (dx > 0 && _compactPage == CompactPage.Brew) GoCompactPage(CompactPage.Settings);
    }
    private void OnCompactPointerCancelled(object? sender, PointerCaptureLostEventArgs e)
    {
        // 系统取消手势（来电/弹窗抢焦点/指针被其他控件捕获等）：复位状态，避免下一次滑动误判
        _pointerTracking = false;
        _swipeCaptured = false;
    }
    private void ReleaseSwipeCapture(IPointer pointer)
    {
        if (!_swipeCaptured) return;
        _swipeCaptured = false;
        try { pointer.Capture(null); } catch { /* headless/桌面无指针上下文：静默 */ }
    }

    // ---------- 响应式：手机 / 平板 / 电脑 ----------
    /// <summary>
    /// 布局模式判定（静态可测）：移动端按横竖屏——竖屏（高>宽）= 左右分页紧凑模式，横屏 = 双栏完整布局；
    /// 桌面端无横竖屏概念，按宽度 &lt; 640 判定（小窗口复用手机分页）。
    /// </summary>
    internal static bool IsCompactLayout(bool mobile, double width, double height)
    {
        // 未测量（Android 构造期 Bounds 为 0x0）：移动端默认按竖屏分页，待首个 SizeChanged 修正；
        // 桌面端保持原宽度判定语义（0 < 640 → 紧凑），行为不变。
        if (width <= 0 || height <= 0) return mobile || width < 640;
        return mobile ? height > width : width < 640;
    }

    /// <summary>
    /// 形态因子判定（2026-09-10 持续改善响应式）：
    /// 移动端优先按横竖屏二分（Phone ↔ PhoneLandscape），宽高均较小（&lt;6.5″）即归 Phone 系，
    /// 其余按宽度分 Tablet (640-1024) / Desktop (1024-1600) / Wide (≥1600)。
    /// 非测量态回退为 Desktop，避免与 IsCompactLayout 默认值冲突。
    /// </summary>
    internal static FormFactor DetectFormFactor(bool mobile, double width, double height)
    {
        if (width <= 0 || height <= 0) return FormFactor.Desktop;
        if (mobile)
        {
            double shortSide = Math.Min(width, height);
            double longSide = Math.Max(width, height);
            // 6.5″ 参考：短边 ≤420dp 且长边 ≤920dp 归手机；其余移动设备（折叠屏/小平板）按形态进 Tablet
            bool phoneSize = shortSide <= 420 && longSide <= 920;
            if (phoneSize) return height > width ? FormFactor.Phone : FormFactor.PhoneLandscape;
            return FormFactor.Tablet;
        }
        // 桌面端按宽度分档
        if (width >= 1600) return FormFactor.Wide;
        if (width >= 1024) return FormFactor.Desktop;
        if (width >= 640)  return FormFactor.Tablet;
        return FormFactor.Phone; // 桌面端窄窗口复用 Phone 排版（更紧凑）
    }

    /// <summary>顶栏排版随布局模式联动（2026-09-09 用户要求：手机直板时作者放软件名下方第二排）：
    /// 手机竖屏（compact）→ 标题/作者上下两行、作者允许换行；横屏/平板/桌面 → 恢复标题+作者同行水平排列。
    /// 标题始终顶对齐（紧贴安全区下缘），由 ApplyLayout 每次布局变化时调用。</summary>
    private void ApplyHeaderLayout(bool compact)
    {
        if (_titleBlock == null || _authorBlock == null) return; // BuildUI 之前的首帧布局（Bounds=0）直接跳过
        if (compact)
        {
            // 竖屏：作者移到软件名下方第二行 —— 垂直堆叠，左对齐小字，可换行避免窄屏截断
            _titleAuthor.Orientation = Orientation.Vertical;
            _titleAuthor.Spacing = 2;
            _titleBlock.VerticalAlignment = VerticalAlignment.Top;
            _authorBlock.VerticalAlignment = VerticalAlignment.Top;
            _authorBlock.HorizontalAlignment = HorizontalAlignment.Left;
            _authorBlock.TextWrapping = TextWrapping.Wrap;
            _authorBlock.Margin = new Thickness(1, 0, 0, 0);
        }
        else
        {
            // 宽屏/桌面：恢复同行水平排列，作者垂直靠底与标题底部对齐
            _titleAuthor.Orientation = Orientation.Horizontal;
            _titleAuthor.Spacing = 12;
            _titleBlock.VerticalAlignment = VerticalAlignment.Top;
            _authorBlock.VerticalAlignment = VerticalAlignment.Bottom;
            _authorBlock.HorizontalAlignment = HorizontalAlignment.Left;
            _authorBlock.TextWrapping = TextWrapping.NoWrap;
            _authorBlock.Margin = new Thickness(0, 0, 0, 0);
        }
    }

    private void ApplyLayout(double width, double height)
    {
        bool compact = IsCompactLayout(IsMobilePlatform, width, height);
        // 形态因子（2026-09-10 新增）：决定字号缩放与 Home/页面间距档位
        _formFactor = DetectFormFactor(IsMobilePlatform, width, height);
        // 字号缩放：Phone=0.92 / Tablet=0.95 / Desktop=1.0 / Wide=1.05（更宽屏幕字号略增，信息密度适当放松）
        _scale = _formFactor switch
        {
            FormFactor.Phone or FormFactor.PhoneLandscape => 0.92,
            FormFactor.Tablet => 0.95,
            FormFactor.Wide => 1.05,
            _ => 1.0,
        };
        // 顶栏排版随模式联动：手机竖屏（紧凑）时标题/作者上下两行；宽屏/桌面恢复同行。
        ApplyHeaderLayout(compact);

        if (compact)
        {
            // —— 手机竖屏：左右分页（页0=设置，页1=冲煮），左右滑动+指示器切换。
            // 把 _bodyGrid 清空后放入 紧凑容器 Grid（Rows= *,Auto：上分页主体 + 下指示器）。
            _bodyGrid.Children.Clear();
            _bodyGrid.ColumnDefinitions = new ColumnDefinitions("*");
            _bodyGrid.RowDefinitions = new RowDefinitions("*,Auto");

            if (_compactHost == null)
            {
                // 第一次进入紧凑模式：构建紧凑容器。
                // 两页用 Grid 叠放 + IsVisible 切换（不用 Canvas）：
                //   · Grid 自动把两页拉伸铺满视口，无需像 Canvas 那样手工写死 Width/Height；
                //   · 隐藏页只置 IsVisible=false，控件不从树上摘除 → ScrollViewer 滚动位置不丢；
                //   · 之前 Canvas 方案的固定尺寸会在切回横屏双栏时残留，把栏撑出屏幕（真机已复现裁切）。
                _compactHost = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
                _compactStack = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                // Page 0 = 设置；Page 1 = 冲煮（初始页0可见，页1隐藏）
                _compactPage0 = _settingsHost;
                _settingsHost.Margin = new Thickness(0);
                // Page 1 = 冲煮
                _compactPage1 = _brewHost;
                _brewHost.Margin = new Thickness(0);
                _compactStack.Children.Add(_settingsHost);
                _compactStack.Children.Add(_brewHost);
                // 手势挂在叠放容器（同时覆盖两页）；先横向意图确认（捕获指针）再判滑页，纵向滚动让路给内容区 ScrollViewer
                _compactStack.PointerPressed += OnCompactPointerPressed;
                _compactStack.PointerMoved += OnCompactPointerMoved;
                _compactStack.PointerReleased += OnCompactPointerReleased;
                _compactStack.PointerCaptureLost += OnCompactPointerCancelled;

                // —— 底部指示器：左○ + "● 设置   ○ 冲煮" + 右○（可点）——
                var indicatorPanel = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    Margin = new Thickness(0, 6, 0, 0),
                };
                _compactIndBtnLeft = MakeButton("◀", (_, _) => GoCompactPage(CompactPage.Settings),
                    bg: "#5A4636", minW: 44, bold: true);
                _compactIndBtnRight = MakeButton("▶", (_, _) => GoCompactPage(CompactPage.Brew),
                    bg: "#5A4636", minW: 44, bold: true);
                _compactIndicator = new TextBlock
                {
                    Text = "",
                    FontSize = 13,
                    Foreground = Brush(Accent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(_compactIndBtnLeft, 0);
                Grid.SetColumn(_compactIndicator, 1);
                Grid.SetColumn(_compactIndBtnRight, 2);
                indicatorPanel.Children.Add(_compactIndBtnLeft);
                indicatorPanel.Children.Add(_compactIndicator);
                indicatorPanel.Children.Add(_compactIndBtnRight);

                Grid.SetRow(_compactStack, 0);
                Grid.SetRow(indicatorPanel, 1);
                _compactHost.Children.Add(_compactStack);
                _compactHost.Children.Add(indicatorPanel);
                _compactPage = CompactPage.Settings;
                RefreshCompactIndicator();
            }
            else if (_compactStack != null)
            {
                // 已构建过（横屏→竖屏回切）：两页可能还留在 bodyGrid / 无父容器 —— 必须重新挂回叠放容器，
                // 否则旋转回来后两页无宿主、整屏空白（Grid 叠放自动铺满，无需再手工设尺寸）。
                if (!ReferenceEquals(_settingsHost.Parent, _compactStack))
                {
                    DetachFromParent(_settingsHost);
                    _compactStack.Children.Add(_settingsHost);
                }
                if (!ReferenceEquals(_brewHost.Parent, _compactStack))
                {
                    DetachFromParent(_brewHost);
                    _compactStack.Children.Add(_brewHost);
                }
            }
            Grid.SetRow(_compactHost, 0);
            Grid.SetColumn(_compactHost, 0);
            if (!_bodyGrid.Children.Contains(_compactHost)) _bodyGrid.Children.Add(_compactHost);
        }
        else
        {
            // —— 横屏（手机横过来 / 平板 / 电脑）：左右双栏完整布局，整个界面（设置+冲煮）一屏呈现、
            //    两区独立内部滚动，内容不裁切不溢出。
            // 先把两页从紧凑叠放容器移出来（若之前挂在紧凑模式下），再挂到 _bodyGrid；
            // 宽高清成 NaN 交还 Grid 布局（防御：清掉任何历史遗留的固定尺寸，避免撑出屏幕裁切）。
            if (_compactPage0 != null) DetachFromParent(_settingsHost);
            if (_compactPage1 != null) DetachFromParent(_brewHost);
            _settingsHost.Width = double.NaN; _settingsHost.Height = double.NaN;
            _brewHost.Width = double.NaN; _brewHost.Height = double.NaN;
            _settingsHost.IsVisible = true;   // 从分页切回双栏：两页都必须可见
            _brewHost.IsVisible = true;

            _bodyGrid.Children.Clear();
            string cols = width < 1100 ? "*,1.05*" : "*,1.15*";
            _bodyGrid.ColumnDefinitions = new ColumnDefinitions(cols);
            _bodyGrid.RowDefinitions = new RowDefinitions("*");

            _settingsHost.Margin = new Thickness(0, 0, 8, 0);
            _brewHost.Margin = new Thickness(0);

            Grid.SetColumn(_settingsHost, 0); Grid.SetRow(_settingsHost, 0);
            Grid.SetColumn(_brewHost, 1); Grid.SetRow(_brewHost, 0);
            // 横屏确保主机高 = 视口剩余空间（Stretch = 默认已铺满），不设置硬 Height，避免截断内容
            _settingsHost.VerticalAlignment = VerticalAlignment.Stretch;
            _brewHost.VerticalAlignment = VerticalAlignment.Stretch;
            _settingsHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            _brewHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            _bodyGrid.Children.Add(_settingsHost);
            _bodyGrid.Children.Add(_brewHost);
        }

        // 跨布局模式（紧凑↔双栏）或横屏窄列状态变化时重建两面板，使 PairRow 单列 / 字号缩放即时生效（与语言切换重建同机制）
        bool pairSingle = !compact && width < 1150; // 双栏各列 ≈(宽/2)-12：低于 ~570dp 时成对行改单列（手机横屏 / 小窗）
        if (compact != _compact || pairSingle != _pairSingle)
        {
            _compact = compact;
            _pairSingle = pairSingle;
            _settingsHost.Child = BuildLeft();
            _brewHost.Child = new ScrollViewer { Content = BuildBrew() };
            RebuildRecords();
            if (_compact) { GoCompactPage(CompactPage.Settings); RefreshCompactIndicator(); }
        }
        else if (_compact) RefreshCompactIndicator(); // 语言切换等场景重刷指示器文案

        // Home 卡片网格形态（紧凑单列 ↔ 宽屏 2×2）与矮屏压缩档随布局变化重建；
        // 手机横屏（宽>高且高<520dp）走矮屏压缩排版，避免 Hero 占掉半屏。
        bool homeShort = !compact && height < 520;
        if (_homeHost != null && (compact != _homeCompact || homeShort != _homeShort))
        {
            _homeShort = homeShort;
            _homeHost.Child = BuildHome();
        }
        // 简易咖啡计算器层：仅在打开状态下随布局/横竖屏重建（宽屏双栏 ↔ 窄屏堆叠 ↔ 矮屏压缩）
        if (_calcHost != null && _calcOpen && (_calcCompact != compact || _calcShort != homeShort))
        {
            _calcCompact = compact;
            _calcShort = homeShort;
            _calcHost.Child = BuildCalc();
        }
    }

    /// <summary>把一个控件从它的 Parent（Panel/Canvas）中取下，避免重复挂接或跨容器冲突。</summary>
    private static void DetachFromParent(Control c)
    {
        if (c.Parent is Panel p) p.Children.Remove(c);
        else if (c.Parent is Canvas cv) cv.Children.Remove(c);
    }

    /// <summary>
    /// 读取安卓系统真实状态栏高度（返回 dip）。用于校准 Avalonia InsetsManager.SafeAreaPadding 的偏差：
    /// 个别后端/高 density 机型上 Top 可能上报偏大或为 0，用原生 status_bar_height 真值钳制，
    /// 保证顶部避让恰好等于状态栏高度（不重叠、不远离）。取不到时返回 0，调用方回退 Avalonia 上报值。
    /// </summary>
    private static double NativeStatusBarDip()
    {
#if ANDROID
        try
        {
            var res = Android.App.Application.Context?.Resources;
            if (res == null) return 0;
            int id = res.GetIdentifier("status_bar_height", "dimen", "android");
            if (id <= 0) return 0;
            int px = res.GetDimensionPixelSize(id);
            float density = res.DisplayMetrics?.Density ?? 1f;
            return density > 0 ? px / density : 0;
        }
        catch
        {
            return 0;
        }
#else
        return 0;
#endif
    }
}

public record ComboItem(string Key, string Label);

/// <summary>
/// 语义命名的咖啡主题色板（2026-09-10 引入）：
/// 把代码里的 #xxxxxx 字面量收敛成可读命名，方便后续在多模块界面（新手/专业/大师方案）复用。
/// 命名按咖啡制作原料分级（cream/foam/caramel/cocoa/cinnamon/...），与 hero 风味调性一致。
/// </summary>
internal static class Palette
{
    // 主表面/文本
    public const string Cream = "#F3E9DD";     // 主前景文本（Fg）
    public const string Foam = "#C9B7A6";      // 次要文本/副标题（Sub）
    public const string Caramel = "#E8C79A";   // 强调（高亮图标、口号、Accent）
    public const string Cinnamon = "#C8893A";  // 主操作按钮黄铜色（AccentBtn）

    // 表面（卡片/背景）
    public const string Cocoa = "#241710";     // 根背景（Bg）
    public const string Espresso = "#33231A";  // 卡片背景（Card）
    public const string RoastDeep = "#1E130D"; // 内嵌面板深色（Panel）

    // 模块色（用于 Home 卡片左侧色条 / 按钮 on 状态）
    public const string SageGreen = "#7FB069";  // 新手模式（草绿）
    public const string SteelBlue = "#4F8FC4";  // 专业模式（钢蓝）
    public const string SiennaOrange = "#D98246"; // 简易计算器（赭橙）
    public const string RoyalGold = "#D4A24C";  // 手冲大师方案（皇金）

    // 中性辅助
    public const string DustGray = "#5A4636";   // 顶栏按钮/分隔（保留 hex，保持原有用法）
    public const string Leather = "#3A2A1C";    // 未激活按钮底（_begStartBtn 不活跃态等）

    // —— 渐变辅助（2026-09-11 UI 设计升级）——
    /// <summary>垂直线性渐变（上亮下暗），用于主按钮/图标瓷砖的立体质感。</summary>
    public static Avalonia.Media.LinearGradientBrush Vertical(string topHex, string bottomHex) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Avalonia.Media.Color.Parse(topHex), 0.0),
            new GradientStop(Avalonia.Media.Color.Parse(bottomHex), 1.0),
        },
    };

    /// <summary>45° 对角线性渐变（左上亮 → 右下暗），用于模块图标瓷砖。</summary>
    public static Avalonia.Media.LinearGradientBrush Diagonal(string topLeftHex, string bottomRightHex) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Avalonia.Media.Color.Parse(topLeftHex), 0.0),
            new GradientStop(Avalonia.Media.Color.Parse(bottomRightHex), 1.0),
        },
    };

    /// <summary>径向柔和光晕刷（中心 color alpha=peakAlpha → 边缘透明），用于背景装饰。</summary>
    public static Avalonia.Media.RadialGradientBrush Glow(string hex, byte peakAlpha) => new()
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(WithAlpha(hex, peakAlpha), 0.0),
            new GradientStop(WithAlpha(hex, (byte)(peakAlpha * 0.5)), 0.5),
            new GradientStop(WithAlpha(hex, 0), 1.0),
        },
    };

    /// <summary>给 hex 颜色替换 alpha 通道（不改 RGB）。</summary>
    public static Avalonia.Media.Color WithAlpha(string hex, byte alpha)
    {
        var c = Avalonia.Media.Color.Parse(hex);
        return Avalonia.Media.Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    // —— 投影 / 细线（2026-09-13 INS × Apple 风升级）——
    /// <summary>
    /// 柔和投影（Apple 式 elevation）：垂直偏移 + 大模糊 + 低透明度纯黑。
    /// 深色界面上投影不宜过重，否则发灰；用 8%–14% 的黑即可产生"卡片浮起"错觉。
    /// </summary>
    public static Avalonia.Media.BoxShadows Shadow(double offsetY = 6, double blur = 20, byte alpha = 92)
        => new(new Avalonia.Media.BoxShadow
        {
            OffsetX = 0, OffsetY = offsetY, Blur = blur, Spread = 0,
            Color = Avalonia.Media.Color.FromArgb(alpha, 0, 0, 0),
        });

    /// <summary>抬升态投影（悬停/强调卡）：偏移更远、模糊更大，配合背景提亮形成"抬起"反馈。</summary>
    public static Avalonia.Media.BoxShadows ShadowLifted()
        => Shadow(offsetY: 14, blur: 34, alpha: 128);

    /// <summary>
    /// 水平渐隐细线（Apple hairline）：左端实色、右端透明。
    /// 用于替代整条等亮的硬分隔线——分段更"轻"，符合 iOS/macOS 的分隔语言。
    /// </summary>
    public static Avalonia.Media.LinearGradientBrush Hairline(string hex, byte alpha = 130)
        => new()
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithAlpha(hex, alpha), 0.0),
                new GradientStop(WithAlpha(hex, (byte)(alpha * 0.55)), 0.45),
                new GradientStop(WithAlpha(hex, 0), 1.0),
            },
        };
}

/// <summary>
/// 模块卡左侧色条色（2026-09-10）：每张主界面卡用自身主题色，提示用户进入对应模式空间。
/// </summary>
internal static class CardAccents
{
    public const string Beginner = Palette.SageGreen;
    public const string Pro = Palette.SteelBlue;
    public const string Calc = Palette.SiennaOrange;
    public const string Master = Palette.RoyalGold;
}

// 小工具：链式返回自身，便于内联绑定
internal static class AvaloniaExtensions
{
    public static T Also<T>(this T self, System.Action<T> action) where T : class
    {
        action(self);
        return self;
    }
}
