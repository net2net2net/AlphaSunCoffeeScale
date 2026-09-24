using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CoffeeScale.Core;
using System.Collections.ObjectModel;
using System.Text;

[assembly: InternalsVisibleTo("CoffeeScale.ViewModels.Tests")]
[assembly: InternalsVisibleTo("CoffeeScale.UI.Tests")]

namespace CoffeeScale.ViewModels;

/// <summary>
/// 手冲咖啡冲煮称 ViewModel：绑定 Core 引擎，驱动模拟注水与实时快照。
/// 不依赖任何特定 UI 框架（无 MAUI / Avalonia 引用），通过注入的 Marshal 委托把属性回写到 UI 线程，
/// 因此同一份 ViewModel 可被 Avalonia / MAUI / WPF 复用。
/// </summary>
public sealed class BrewViewModel : INotifyPropertyChanged
{
    // ---------- 输入参数 ----------
    private string _method = "classic";
    private string _size = "standard";
    private double _doseMin = 13;   // 标准档下限
    private double _doseMax = 22;   // 标准档上限
    private double _dose = 15;
    private double _ratio = 15;
    private string _roast = "light";          // 默认肉桂烘（粉样 Ag 80–90，中值 85；阳光 2026-09-02 要求默认肉桂烘）
    private int _roastAg = 90;                // 默认浅烘 Light 偏深 (粉样 Ag 80–90 → 90)
    private string _flavor = "balanced";
    private string _process = "washed";
    private string _dripper = "v60";
    private string _filter = "bleached";
    private int _restDays = 10;
    private DateTime _roastDate = DateTime.Today.AddDays(-3); // 烘焙日期默认：现在日期前 3 天（阳光 2026-09-01）
    private DateTime _brewDate = DateTime.Today;            // 冲煮日期
    // 落盘位置统一走 AppPaths：Windows/移动端仍在程序同目录；macOS/Linux 改到标准用户数据目录，
    // 避免往 .app 包内或只读的程序目录写配置与冲煮记录（详见 AppPaths 注释）。
    private string _recordsPath = AppPaths.File("brews.json");
    private string _settingsPath = AppPaths.File("settings.json");
    private string _myPlansPath = AppPaths.File("myplans.json");
    private List<BrewRecord> _records = new();
    private List<MyPlan> _myPlans = new();
    private string _recommendedGrindLabel = "";
    private string _grindRecText = "";       // 推荐研磨（描述 + 磨豆机刻度）
    private string _waterText = "";              // 水质推荐
    private string _tempRecText = "";            // 推荐水温（℃）
    private string _roastAgText = "";
    private string _roastTierHint = "";          // 烘焙值 Ag 与档位联动提示
    private string _restReadyDateText = "";
    private string _origin = "ethiopia";      // 默认产地：埃塞俄比亚
    private string _variety = "heirloom";     // 默认豆种：埃塞原生种
    private string _density = "medium"; // 咖啡豆密度：light(疏松)/medium(中等)/dense(致密)，影响研磨补偿
    private int _beanAltitudeM = 1500;  // 咖啡豆海拔(m)：作为密度的便捷预填来源（海拔越高→越慢熟→密度越高）
    // 用户是否已手动指定密度：false 时改海拔会刷新密度（预填）；true 时保留用户手选，不受海拔编辑影响。
    private bool _densityUserSet;
    private string _originInfoText = "";
    // 冲煮流程管道图节点（Generate 时按推荐方案动态归并生成，运行中只改状态不重建集合）
    private ObservableCollection<PhaseStep> _pipeline = new();

    // ---------- 实时状态 ----------
    private double _simWeight;
    private bool _isPouring;
    private bool _hasRecipe;
    private string _weightText = "0.0";
    private string _timerText = "0:00";
    private string _flowText = "0.0 g/s";
    private string _ratioText = "1:0.0";
    private string _phaseName = I18n.T("NotStarted");
    private string _phaseTip = I18n.T("InitialTip");
    private string _phaseRemainText = "—";
    private bool _isPourPhase;        // 当前阶段是否为注水阶段（UI 进度条分色：注水蓝 / 等待琥珀）
    private string _phasePourText = ""; // 阶段进度条右侧文本：注水量（注水段）或剩余时间（等待段）
    private double _phaseProgress;
    private double _overallProgress;
    private string _warningsText = "";
    private string _summaryText = "";
    private string _restRecText = "";
    private string _suggestRestLabel = "";
    private string _restDaysText = "";
    private string _doseText = "";
    private string _ratioPlanText = "";
    private string _flowRecText = "";
    private bool _running;
    private bool _done;
    private bool _paused;
    private string _startLabel = I18n.T("Start");
    private string _restRecStatus = "";
    // 是否为杯测法：杯测是静态评估，不需要控制流速 / 分段注水（Generate 时定值，运行中不变）
    private bool _isCupping;

    private Recipe? _recipe;
    private EngineState? _state;
    private readonly System.Timers.Timer _timer = new(50) { AutoReset = true };
    private const double PourRate = 6.0; // g/s 模拟注水速度
    private Func<long> _now = () => Environment.TickCount64; // 可注入时钟，便于确定性测试

    /// <summary>把属性写回 UI 线程的搬运器。默认同步执行；在 Avalonia 侧设为 Dispatcher.UIThread.InvokeAsync。</summary>
    public Action<Action>? Marshal { get; set; }

    /// <summary>运行时异常汇出口（由 UI 层注入，用于弹窗 / 写日志）。框架无关，不耦合具体 UI 框架。</summary>
    public static Action<Exception>? ErrorSink { get; set; }

    /// <summary>
    /// 是否在构造时自动从 settings.json 恢复上次参数（默认 true = 生产行为）。
    /// 单元测试程序集通过 [ModuleInitializer] 将其置为 false，避免多个用例之间因共享默认路径而串味。
    /// </summary>
    public static bool AutoLoadSettings { get; set; } = true;

    /// <summary>同 <see cref="AutoLoadSettings"/>，但针对「我的方案」收藏（myplans.json）。
    /// 单元测试程序集通过 [ModuleInitializer] 将其置为 false，避免多个用例之间因共享默认路径而串味。</summary>
    public static bool AutoLoadMyPlans { get; set; } = true;

    /// <summary>当前语言（预留切换）。设置即更新 I18n.Current 并通知 UI 重建文案。</summary>
    public string Culture
    {
        get => I18n.Current;
        set
        {
            if (I18n.Current == value) return;
            I18n.Current = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CultureLabel));
            // 通知所有文案相关属性（UI 通过转换器或直接绑定 T(key) 的需重建）
            OnPropertyChanged(nameof(GenerateLabel));
            OnPropertyChanged(nameof(TransportLabel));
            // 语言切换后重建「走 I18n 前缀」的只读文案与流程节点标题（配方本身不变）
            RefreshRoastTierHint();
            RebuildPipeline();
            if (_recipe != null) BuildStaticTexts(); else RefreshOriginInfo();
        }
    }
    public string CultureLabel => I18n.Current == I18n.EnUS ? "EN" : "中";

    public BrewViewModel()
    {
        _timer.Elapsed += (_, _) => Tick();
        LoadRecords();
        if (AutoLoadSettings) LoadSettings(_settingsPath); // 启动时恢复上次冲煮参数与语言偏好（测试可关闭以隔离）
        if (AutoLoadMyPlans) LoadMyPlans(); // 启动时恢复收藏的「我的方案」（测试可关闭以隔离）
        RefreshOriginInfo();
        RefreshRoastTierHint();
    }

    /// <summary>
    /// 刷新「烘焙值 Ag」的档位联动提示：档位名 + Ag 区间 + 区间中值。
    /// 说明默认值的来源（档位区间中值），并与烘焙度下拉保持同步。
    /// </summary>
    private void RefreshRoastTierHint()
    {
        var prof = BrewEngine.RoastProfiles.GetValueOrDefault(_roast, BrewEngine.RoastProfiles["light"]);
        int mid = (prof.AgMin + prof.AgMax) / 2; // 整数中值：默认取档位区间中点
        RoastTierHint = string.Format(I18n.T("RoastTierHint"), I18n.T("Roast_" + prof.Id), prof.AgMin, prof.AgMax, mid);
    }

    // ---------- 输入属性 ----------
    public string Method
    {
        get => _method;
        set
        {
            if (!Set(ref _method, value)) return;
            // 杯测法统一基准：11g 咖啡粉 + 200g 94℃ 热水（用户仍可在右侧手动微调）
            if (value == "cupping")
            {
                if (Math.Abs(_ratio - 15) < 0.01) Ratio = BrewEngine.CuppingRatio;
                if (Math.Abs(_dose - 15) < 0.01) { DoseMin = 8; DoseMax = 25; Dose = 11; }
            }
        }
    }
    public string Size
    {
        get => _size;
        set
        {
            if (!Set(ref _size, value)) return;
            // 切换粉量档位时，自动把粉量填入该档默认值，并把 ± 边界设为该档范围，
            // 用户可在档位范围内用输入框/± 按钮微调（不超过档位上限/下限）。
            if (BrewEngine.SizeProfiles.TryGetValue(value, out var prof))
            {
                DoseMin = prof.MinDose;
                DoseMax = prof.MaxDose;
                Dose = prof.DefaultDose; // 切换档位自动填入该档默认值（少8/标15/大25），用户可在范围内微调
            }
        }
    }
    // 粉量档位边界（供 UI 输入框/± 按钮/滑块约束，不超过所选档位范围）
    public double DoseMin { get => _doseMin; private set => Set(ref _doseMin, value); }
    public double DoseMax { get => _doseMax; private set => Set(ref _doseMax, value); }
    public double Dose
    {
        get => _dose;
        set => Set(ref _dose, Math.Clamp(Math.Round(value), _doseMin, _doseMax)); // 限定在所选档位范围内，步进 1g
    }
    public double Ratio { get => _ratio; set => Set(ref _ratio, value); }
    public string Roast
    {
        get => _roast;
        set
        {
            if (!Set(ref _roast, value)) return;
            // 切换烘焙度档位时，把烘焙值 Ag 回退到新档位区间中值（整数；用户仍可随后手动微调）
            if (BrewEngine.RoastProfiles.TryGetValue(value, out var prof))
                RoastAg = (prof.AgMin + prof.AgMax) / 2;
            RefreshRoastTierHint();
        }
    }
    /// <summary>具体烘焙度 Agtron 读数（用户可输入，默认所选档位区间中值）。切换档位时回退到新档位中值。</summary>
    public int RoastAg { get => _roastAg; set => Set(ref _roastAg, value); }
    /// <summary>Ag 读数的合法输入范围（供 UI 输入框钳制）。粉样标尺扩展为 10 级后区间为 10–120（最浅档超浅烘 Ag 110–120）。</summary>
    public int RoastAgMin => 10;
    public int RoastAgMax => 120;
    public string Flavor { get => _flavor; set => Set(ref _flavor, value); }
    public string Process { get => _process; set => Set(ref _process, value); }
    public string Dripper { get => _dripper; set => Set(ref _dripper, value); }
    public string Filter { get => _filter; set => Set(ref _filter, value); }
    public string Origin { get => _origin; set { if (Set(ref _origin, value)) RefreshOriginInfo(); } }
    public string Variety { get => _variety; set { if (Set(ref _variety, value)) RefreshOriginInfo(); } }
    /// <summary>咖啡豆密度：light(疏松)/medium(中等，默认)/dense(致密)。致密硬豆研磨自动细一档，疏松豆粗一档（生成方案时生效）。
    /// 由「咖啡豆海拔」预填：改海拔即刷新本值，直到用户在下拉框手选一次（此后手选值优先，不再被海拔覆盖）。</summary>
    public string Density
    {
        get => _density;
        set
        {
            if (!Set(ref _density, value)) return;
            // 用户显式选择了密度 → 标记为「已手选」，之后改海拔不再回写覆盖。
            _densityUserSet = true;
            // 密度影响研磨推荐：已生成的旧配方保持不变（冲煮中不漂移），重新生成方案时生效
        }
    }

    /// <summary>咖啡豆海拔(m)：作为密度的便捷预填来源。海拔越高→越慢熟→细胞壁越厚→密度越高（致密）。
    /// 当用户尚未手选密度（_densityUserSet=false）时，改海拔立即按分档刷新密度下拉框；已手选则保留手选值。</summary>
    public int BeanAltitudeM
    {
        get => _beanAltitudeM;
        set
        {
            if (!Set(ref _beanAltitudeM, value)) return;
            if (!_densityUserSet) ApplyDensityFromAltitude(altitudeDriven: true);
        }
    }

    /// <summary>按当前海拔推导并落定密度档（≥1700 致密 / 1200–1699 中等 / &lt;1200 疏松）。
    /// altitudeDriven=true 表示来自海拔联动的「预填」，不视为用户手选，故不清除手选标记。</summary>
    private void ApplyDensityFromAltitude(bool altitudeDriven = false)
    {
        var derived = BrewEngine.DensityFromAltitude(_beanAltitudeM);
        if (_density == derived) return;
        _density = derived;
        if (!altitudeDriven) _densityUserSet = true;
        OnPropertyChanged(nameof(Density));
    }
    public int RestDays { get => _restDays; set => Set(ref _restDays, value); }
    public DateTime RoastDate { get => _roastDate; set { if (Set(ref _roastDate, value) && value != DateTime.MinValue) UpdateRestFromDate(); } }

    /// <summary>是否已生成推荐方案（供「推荐粉水比 / 注水总量」行在生成前显示「未设置」）。</summary>
    public bool HasRecipe
    {
        get => _hasRecipe;
        private set
        {
            if (!Set(ref _hasRecipe, value)) return;
            OnPropertyChanged(nameof(PlanRatioText));
            OnPropertyChanged(nameof(PlanWaterText));
        }
    }
    /// <summary>实时冲煮区「推荐粉水比」行显示值：生成方案后为 1:N（配方有效值），生成前为空串（UI 显示「未设置」）。</summary>
    public string PlanRatioText => HasRecipe && _recipe != null
        ? "1:" + Math.Round(_recipe.Ratio, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
        : "";
    /// <summary>实时冲煮区「注水总量」行显示值：配方总水量（粉量 × 粉水比），生成前为空串（UI 显示「未设置」）。</summary>
    public string PlanWaterText => HasRecipe && _recipe != null
        ? _recipe.TotalWater.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "g"
        : "";

    /// <summary>实时冲煮区「推荐粉水比」± 微调：改 Ratio 后立即重新生成配方，使推荐粉水比与
    /// 注水总量（= 粉量 × 粉水比）同步刷新（此前 ± 只改输入、显示不随动，是缺陷）。
    /// 冲煮运行中仅记录新值（Generate 自身有 Running 保护），下次生成/重置后生效。</summary>
    public void AdjustPlanRatio(double delta)
    {
        // 步长为 1，整数微调（阳光 2026-09-03：推荐粉水比不要小数）
        int v = (int)Math.Round(Math.Clamp(Ratio + delta, 10, 20), MidpointRounding.AwayFromZero);
        if (v == (int)Math.Round(Ratio, MidpointRounding.AwayFromZero)) return;
        Ratio = v;
        if (HasRecipe && !Running) Generate();
    }
    public bool Running { get => _running; private set => Set(ref _running, value); }
    public bool Done { get => _done; private set => Set(ref _done, value); }
    public bool Paused { get => _paused; private set => Set(ref _paused, value); }
    public bool IsPouring { get => _isPouring; set => Set(ref _isPouring, value); }
    /// <summary>是否为杯测法。杯测为静态评估模式，无需控制流速/分段注水（Generate 时定值）。</summary>
    public bool IsCupping { get => _isCupping; private set => Set(ref _isCupping, value); }
    /// <summary>「开始」按钮可用：未运行、未完成、且尚未开始计时（初始空闲态）。</summary>
    public bool CanStart => !Running && !Done && !_paused && (_state == null || _state.ElapsedMs == 0);
    /// <summary>「继续」按钮可用：已点过「暂停」且尚未「继续」/「归零」，且未完成。</summary>
    public bool IsPaused => _paused && !Done;

    /// <summary>主运输按钮状态机：0 空闲(未开始) / 1 计时中 / 2 已暂停 / 3 完成。UI 据此循环红→黄→蓝圆钮。</summary>
    public int TransportState
    {
        get
        {
            if (Done) return 3;
            if (Running) return 1;
            if (_paused) return 2;
            return 0;
        }
    }
    /// <summary>主运输按钮文案：空闲「开始」/ 计时中「暂停」/ 已暂停「继续」/ 完成「完成」（走 I18n，语言切换时刷新）。</summary>
    public string TransportLabel
    {
        get
        {
            if (Running) return I18n.T("Pause");
            if (_paused) return I18n.T("Resume");
            return I18n.T("Start");
        }
    }
    /// <summary>主运输按钮可用：完成态不禁用，可重新开始冲煮。</summary>
    public bool CanTransport => true;
    /// <summary>主运输按钮常态底色（与状态对应：1 黄 / 2 蓝），空闲和完成都显示红。</summary>
    public string TransportColorHex => TransportState switch
    {
        1 => "#C8881E", // 计时中：黄（暂停）
        2 => "#2E6FB0", // 已暂停：蓝（继续）
        _ => "#D6453B", // 空闲/完成：红（开始）
    };
    public string StartLabel { get => _startLabel; private set => Set(ref _startLabel, value); }
    public string RestRecStatus { get => _restRecStatus; private set => Set(ref _restRecStatus, value); }

    /// <summary>总水量（g），供 UI / 测试读取。</summary>
    public double TotalWater => _recipe?.TotalWater ?? 0;

    // ---------- 实时显示属性 ----------
    public double SimWeight { get => _simWeight; set { if (Set(ref _simWeight, value)) OnPropertyChanged(nameof(SimWeight)); } }
    public string WeightText { get => _weightText; private set => Set(ref _weightText, value); }
    public string TimerText { get => _timerText; private set => Set(ref _timerText, value); }
    public string FlowText { get => _flowText; private set => Set(ref _flowText, value); }
    public string RatioText { get => _ratioText; private set => Set(ref _ratioText, value); }
    public string PhaseName { get => _phaseName; private set => Set(ref _phaseName, value); }
    public string PhaseTip { get => _phaseTip; private set => Set(ref _phaseTip, value); }
    /// <summary>当前阶段剩余时间（mm:ss）。仅等待/杯测注水引导阶段有值；其他为"—"。</summary>
    public string PhaseRemainText { get => _phaseRemainText; private set => Set(ref _phaseRemainText, value); }
    /// <summary>当前阶段是否为注水阶段（UI 据此切换进度条颜色：注水=蓝 / 等待=琥珀，直观区分注水时间与等待时间）。</summary>
    public bool IsPourPhase { get => _isPourPhase; private set => Set(ref _isPourPhase, value); }
    /// <summary>阶段进度条文本：注水段显示本段注水量（当前/目标 g），等待段显示剩余秒数；完成后为空。</summary>
    public string PhasePourText { get => _phasePourText; private set => Set(ref _phasePourText, value); }
    public double PhaseProgress { get => _phaseProgress; private set => Set(ref _phaseProgress, value); }
    public double OverallProgress { get => _overallProgress; private set => Set(ref _overallProgress, value); }
    public string WarningsText { get => _warningsText; private set => Set(ref _warningsText, value); }
    public string SummaryText { get => _summaryText; private set => Set(ref _summaryText, value); }
    public string RestRecText { get => _restRecText; private set => Set(ref _restRecText, value); }
    public string SuggestRestLabel { get => _suggestRestLabel; private set => Set(ref _suggestRestLabel, value); }
    public string RestDaysText { get => _restDaysText; private set => Set(ref _restDaysText, value); }
    public string DoseText { get => _doseText; private set => Set(ref _doseText, value); }
    public string RatioPlanText { get => _ratioPlanText; private set => Set(ref _ratioPlanText, value); }
    public string FlowRecText { get => _flowRecText; private set => Set(ref _flowRecText, value); }
    /// <summary>冲煮流程管道图节点（闷蒸 → 注水1 → 注水2 → 冲煮结束），按推荐方案动态归并生成；运行中只刷新节点状态，不重建集合。</summary>
    public ObservableCollection<PhaseStep> PhasePipeline => _pipeline;
    /// <summary>是否已有流程节点（无配方时供 UI 显示占位提示）。</summary>
    public bool HasPipeline => _pipeline.Count > 0;

    // 推荐研磨度（引擎按滤杯+烘焙度算，非用户选择）
    public string RecommendedGrindLabel { get => _recommendedGrindLabel; private set => Set(ref _recommendedGrindLabel, value); }
    /// <summary>推荐研磨（研磨度描述 + 磨豆机刻度 C40 / EK43），供「推荐研磨」卡展示。</summary>
    public string GrindRecText { get => _grindRecText; private set => Set(ref _grindRecText, value); }
    // 水质推荐
    public string WaterText { get => _waterText; private set => Set(ref _waterText, value); }
    /// <summary>推荐水温（℃），Generate 时随配方确定（2026-09-08 新增：实时冲煮区水温推荐卡）。</summary>
    public string TempRecText { get => _tempRecText; private set => Set(ref _tempRecText, value); }
    // 烘焙度 Ag 值
    public string RoastAgText { get => _roastAgText; private set => Set(ref _roastAgText, value); }
    /// <summary>烘焙值 Ag 的档位联动提示（档位名 + Ag 区间 + 区间中值），说明默认值来源。</summary>
    public string RoastTierHint { get => _roastTierHint; private set => Set(ref _roastTierHint, value); }
    // 养豆期满（可冲煮）日期
    public string RestReadyDateText { get => _restReadyDateText; private set => Set(ref _restReadyDateText, value); }
    // 产地：常规风味走向 + 等级分类 + 豆种（随产地/豆种选择实时更新，只读展示）
    public string OriginInfoText { get => _originInfoText; private set => Set(ref _originInfoText, value); }
    // 冲煮日期
    public DateTime BrewDate { get => _brewDate; set => Set(ref _brewDate, value); }
    // 冲煮记录持久化
    public string RecordsPath { get => _recordsPath; set => _recordsPath = value; }
    /// <summary>参数/偏好持久化文件路径（可注入，便于测试）。</summary>
    public string SettingsPath { get => _settingsPath; set => _settingsPath = value; }
    public IReadOnlyList<BrewRecord> Records { get => _records; private set { _records = value.ToList(); OnPropertyChanged(); } }

    /// <summary>历史统计摘要（基于已保存记录），供 UI 统计卡展示。参考专业冲煮报告格式。</summary>
    public string StatsText
    {
        get
        {
            var recs = _records;
            if (recs.Count == 0) return I18n.T("StatsEmpty");
            int n = recs.Count;
            double avgDose = recs.Average(r => r.Dose);
            double avgRatio = recs.Average(r => r.Ratio);
            double totalWater = recs.Sum(r => r.TotalWater);
            var topOrigin = recs.GroupBy(r => r.OriginLabel).OrderByDescending(g => g.Count()).First().Key;
            var topMethod = recs.GroupBy(r => r.MethodLabel).OrderByDescending(g => g.Count()).First().Key;
            // 烘焙度分布（按 RoastLabel 计数，最多列 3 项）
            var roastDist = recs.GroupBy(r => r.RoastLabel).OrderByDescending(g => g.Count())
                .Take(3).Select(g => $"{g.Key} {g.Count()}").ToList();
            return I18n.T("StatsTitle") + "：" +
                   $"{I18n.T("StatsCount")} {n} · {I18n.T("StatsAvgDose")} {avgDose:F0}g · " +
                   $"{I18n.T("StatsAvgRatio")} 1:{avgRatio:F1} · {I18n.T("StatsTotalWater")} {totalWater:F0}g\n" +
                   $"{I18n.T("StatsTopOrigin")} {topOrigin} · {I18n.T("StatsTopMethod")} {topMethod}\n" +
                   $"{I18n.T("StatsRoastDist")} {string.Join(" / ", roastDist)}";
        }
    }

    /// <summary>当前配方的专业冲煮报告文本（参考 SCA 杯测/手冲记录表格式），供保存前预览与记录详情展示。</summary>
    public string BrewReportText
    {
        get
        {
            if (_recipe == null) return I18n.T("ReportEmpty");
            var r = _recipe;
            var phases = string.Join("\n   ", r.Phases.Select(p =>
                $"· {p.Name}：{Fmt((long)(p.DurationSec * 1000))}{(string.IsNullOrEmpty(p.Tip) ? "" : " — " + p.Tip)}"));
            return I18n.T("ReportTitle") + "\n" +
                $"{I18n.T("Method")}：{r.MethodLabel}\n" +
                $"{I18n.T("Dose")}：{r.Dose:0}g（{r.SizeLabel}）\n" +
                $"{I18n.T("RatioRec")}：1:{r.Ratio:0} · {I18n.T("Water")} {r.TotalWater:0}g\n" +
                $"{I18n.T("Roast")}：{r.RoastLabel}（Ag {r.RoastAg}，区间 {r.RoastAgMin}–{r.RoastAgMax}）\n" +
                $"🌡 {I18n.T("Temp")}：{r.Temp}℃ · {I18n.T("Bloom")}：{r.BloomWait}s\n" +
                $"{I18n.T("GrindRec")}：{r.GrindLabel}（C40≈{r.GrindC40} / EK≈{r.GrindEK}）\n" +
                $"{I18n.T("Origin")}：{r.OriginLabel}（{r.OriginRegion}）· {I18n.T("Variety")}：{r.VarietyLabel}\n" +
                $"{I18n.T("Process")}：{r.ProcessLabel} · {I18n.T("Dripper")}：{r.DripperLabel} · {I18n.T("Filter")}：{r.FilterLabel}\n" +
                $"{I18n.T("Water")}：{r.WaterLabel}\n" +
                $"{I18n.T("Flow")}：{r.RecommendedFlow:0} g/s\n" +
                $"{I18n.T("Phases")}（{r.Phases.Count}）：\n   {phases}\n" +
                $"{I18n.T("EstTotalTime")}：{Fmt((long)(r.TotalTimeSec * 1000))}" +
                (r.Extraction != null
                    ? $"\n{I18n.T("GoldenCup")}：{I18n.T("Ey")} {r.Extraction.Ey:F2}% · {I18n.T("Tds")} {r.Extraction.Tds:F2}%\n   {r.Extraction.Verdict}{(r.Extraction.Notes.Length > 0 ? "\n   " + r.Extraction.Notes : "")}{(r.GoldenCupTune.Length > 0 ? "\n   " + r.GoldenCupTune : "")}"
                    : "");
        }
    }

    /// <summary>黄金杯诊断卡文本：预估 EY/TDS + 判据（2026-09-08 用户要求：移除海拔沸点封顶提示）。</summary>
    public string GoldenCupText
    {
        get
        {
            if (_recipe == null || _recipe.Extraction == null) return I18n.T("GCEmpty");
            var e = _recipe.Extraction;
            var sb = new StringBuilder();
            sb.AppendLine($"{I18n.T("Ey")}：{e.Ey:F2}% · {I18n.T("Tds")}：{e.Tds:F2}%");
            sb.AppendLine(e.Verdict);
            if (e.Notes.Length > 0) sb.AppendLine(e.Notes);
            if (_recipe.GoldenCupTune.Length > 0) sb.AppendLine(_recipe.GoldenCupTune);
            return sb.ToString();
        }
    }

    /// <summary>从「咖啡大师重铸方案清单」选中的大师方案正文，显示在推荐冲煮方案区（可写，区别于只读的 BrewReportText）。</summary>
    private string _masterPlanText = "";
    public string MasterPlanText
    {
        get => _masterPlanText;
        set => Set(ref _masterPlanText, value);
    }

    // ---------- 命令 ----------
    public void Generate()
    {
        if (Running) return; // 冲煮中不允许改配方
        try
        {
            _recipe = BrewEngine.ComputeRecipe(new RecipeOptions
            {
                Dose = _dose, Ratio = _ratio, Method = _method, Size = _size,
                Roast = _roast, RoastAg = _roastAg, Flavor = _flavor, Process = _process,
                Dripper = _dripper, Filter = _filter,
                Origin = _origin, Variety = _variety,
                RestDays = _restDays,
                RoastDate = _roastDate == DateTime.MinValue ? null : _roastDate,
                Density = _density, // 密度已由海拔预填或用户手选确定，此处取最终值
            });
            _state = BrewEngine.CreateState(_recipe);
            SimWeight = 0;
            HasRecipe = true;
            OnPropertyChanged(nameof(PlanRatioText)); // 推荐粉水比/注水总量随新配方刷新（生成前为「未设置」）
            OnPropertyChanged(nameof(PlanWaterText));
            StartLabel = I18n.T("Start");
            IsCupping = _method == "cupping"; // 杯测为静态评估，无需控制流速
            BuildStaticTexts();  // 配方相关、Generate 后不变的文本只算一次（效率优化，避免每帧重算）
            BuildSummary();
            PersistSettings();   // 记住本次参数/语言偏好，下次启动自动恢复
            RebuildPipeline();   // 冲煮流程管道图：按新方案重新归并节点（闷蒸 → 注水1 → 注水2 → 冲煮结束）
            OnPropertyChanged(nameof(BrewReportText)); // 报告随配方刷新
            OnPropertyChanged(nameof(GoldenCupText));  // 黄金杯诊断随配方刷新
            TickNow();
            // 确保计时器正确启动（先停止再启动，避免重复启动）
            _timer.Stop();
            _timer.Start();
        }
        catch (Exception ex)
        {
            ErrorSink?.Invoke(ex); // 配方生成失败也不闪退，交由 UI 层弹窗
        }
    }

    public void Start()
    {
        if (_state == null) { Generate(); return; }
        if (Done) return;
        BrewEngine.Start(_state, Elapsed());
        Running = true;
        _paused = false;
        StartLabel = I18n.T("Pause");
        RaiseTransportState();
    }

    public void Pause()
    {
        if (_state == null || Done || !Running) return;
        BrewEngine.Pause(_state, Elapsed());
        Running = false;
        _paused = true;
        StartLabel = I18n.T("Resume");
        RaiseTransportState();
    }

    public void Resume()
    {
        if (_state == null || Done || Running) return;
        BrewEngine.Start(_state, Elapsed());
        Running = true;
        _paused = false;
        StartLabel = I18n.T("Pause");
        RaiseTransportState();
    }

    /// <summary>
    /// 主运输按钮（单一圆形键）的循环动作：空闲→开始计时（红→黄「暂停」）；计时中→暂停（黄→蓝「继续」）；已暂停→继续计时（蓝→黄「暂停」）。
    /// 完成状态下点击会自动重置并重新开始。
    /// 首次点击若尚未生成配方，则先 Generate 再开始计时（用户期望「开始」即起算）。
    /// </summary>
    public void Transport()
    {
        if (Done)
        {
            // 完成状态下点击，自动重置并重新开始
            if (_state != null) BrewEngine.Reset(_state);
            SimWeight = 0;
            Running = false;
            Done = false;
            _paused = false;
            StartLabel = I18n.T("Start");
            // 重新生成配方并开始
            Generate();
            if (_state != null) Start();
            return;
        }
        if (_state == null) { Generate(); if (_state == null) return; }
        if (Running) Pause();
        else if (_paused) Resume();
        else Start();
    }

    private void RaiseTransportState()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(TransportState));
        OnPropertyChanged(nameof(TransportLabel));
        OnPropertyChanged(nameof(CanTransport));
    }

    public void Reset()
    {
        if (_state == null) return;
        _timer.Stop();
        BrewEngine.Reset(_state);
        SimWeight = 0; Running = false; Done = false; _paused = false; StartLabel = I18n.T("Start");
        TickNow();
        RaiseTransportState();
    }

    /// <summary>
    /// 重置（「冲煮方案设置」底部的重置键）：把所有输入参数充值为默认值，并中断、清空实时冲煮信息（称控制/显示/流程管道归零）。
    /// 默认值与程序首次启动一致（浅烘 ultra_light / 粉量 15g / 粉水比 1:15 …），结果写回 settings.json 使重启后仍生效。
    /// </summary>
    public void ResetAll()
    {
        // 1) 中断并清空实时冲煮
        _timer.Stop();
        if (_state != null) BrewEngine.Reset(_state);
        SimWeight = 0;
        Running = false; Done = false; _paused = false; StartLabel = I18n.T("Start");
        HasRecipe = false;
        _recipe = null; _state = null;
        IsCupping = false;

        // 2) 充值输入参数为默认值（顺序：先设 RoastDate 再设 RestDays，避免 RoastDate 的联动把 RestDays 改掉）
        Method = "classic";
        Size = "standard";   // Size setter 会按档位填入 Dose 默认并约束 DoseMin/Max
        Dose = 15;
        Ratio = 15;
        Roast = "light";       // 肉桂烘（粉样 Ag 80–90，中值 85）；Roast setter 会把 Ag 回到档位中值
        RoastAg = 90;   // 阳光要求默认 90（Light 偏深），覆盖档位中值 85
        Flavor = "balanced";
        Process = "washed";
        Dripper = "v60";
        Filter = "bleached";
        Origin = "ethiopia";
        Variety = "heirloom";
        RoastDate = DateTime.Today.AddDays(-7);
        RestDays = 10;
        BrewDate = DateTime.Today;
        BeanAltitudeM = 1500;
        // 复位密度：清除「已手选」标记，按默认海拔 1500m 预填为「中等」。
        _densityUserSet = false;
        _density = BrewEngine.DensityFromAltitude(1500); // "medium"
        OnPropertyChanged(nameof(Density));

        // 3) 清空实时显示（_state 已置空，TickNow 不会重算，需手动复位）
        WeightText = "0.0"; TimerText = "0:00"; FlowText = "0.0 g/s"; RatioText = "1:0.0";
        PhaseName = I18n.T("NotStarted"); PhaseTip = I18n.T("InitialTip"); PhaseRemainText = "—";
        PhaseProgress = 0; OverallProgress = 0; WarningsText = "";
        SummaryText = ""; RestRecText = ""; RestRecStatus = "";
        RoastAgText = ""; GrindRecText = ""; WaterText = ""; TempRecText = "";
        RestReadyDateText = ""; RestDaysText = ""; SuggestRestLabel = "";
        RefreshRoastTierHint();
        RefreshOriginInfo();   // 复位产地只读参考卡为默认产地
        RebuildPipeline();     // 清空流程管道节点
        RaiseTransportState();
        OnPropertyChanged(nameof(BrewReportText));
        OnPropertyChanged(nameof(GoldenCupText));
        OnPropertyChanged(nameof(StatsText));
        PersistSettings();     // 把默认值写回，使重置在重启后仍生效
    }

    public void Next()
    {
        if (_state == null) return;
        BrewEngine.NextPhase(_state, Elapsed());
        TickNow(); // 立即刷新界面，避免等下一个定时器帧才更新
    }

    /// <summary>手动回到上一阶段（用户可强制回退）。同时把模拟重量回退到目标阶段的起始水量，保证手动步进连贯。</summary>
    public void Prev()
    {
        if (_state == null) return;
        BrewEngine.PrevPhase(_state, Elapsed());
        if (_recipe != null && _state.PhaseIndex >= 0 && _state.PhaseIndex < _recipe.Phases.Count)
            SimWeight = _recipe.Phases[_state.PhaseIndex].StartWeight;
        TickNow(); // 立即刷新界面，避免等下一个定时器帧才更新
    }

    public void PourBump(double g) => SimWeight = Math.Min(SimWeight + g, (_recipe?.TotalWater ?? 300) + 20);

    private void UpdateRestFromDate()
    {
        if (_roastDate == DateTime.MinValue) return;
        var days = (DateTime.Today - _roastDate.Date).Days;
        RestDays = Math.Clamp(days, 0, 120);
    }

    /// <summary>根据当前产地/豆种选择刷新只读信息卡（风味 + 等级 + 豆种）。</summary>
    private void RefreshOriginInfo()
    {
        var o = BrewEngine.OriginProfiles.GetValueOrDefault(_origin, BrewEngine.OriginProfiles["ethiopia"]);
        var v = BrewEngine.VarietyProfiles.GetValueOrDefault(_variety, BrewEngine.VarietyProfiles["heirloom"]);
        OriginInfoText = $"{I18n.T("OriginFlavorLine")}{o.Flavor}\n{I18n.T("OriginGradeLine")}{o.Grade}\n{I18n.T("OriginVarietyLine")}{v.Label}";
    }

    // ---------- 冲煮流程管道图（闷蒸 → 注水1 → 注水2 → 冲煮结束）----------

    /// <summary>归并中的流程节点草稿：累计目标重量与等待秒数，最终转成 <see cref="PhaseStep"/>。</summary>
    private sealed class PipelineDraft
    {
        public string Title = "";
        public string Target = "";   // 注水的目标重量，如 "→ 120g"
        public int Secs;             // 累计等待秒数
        public int From;
        public int To;
        public string Sub => Target.Length > 0
            ? (Secs > 0 ? Target + " · " + Secs + "s" : Target)
            : (Secs > 0 ? Secs + "s" : "");
    }

    /// <summary>按当前配方重建流程节点集合（语言切换 / 生成配方时调用）。</summary>
    private void RebuildPipeline()
    {
        _pipeline = new ObservableCollection<PhaseStep>(BuildPipeline(_recipe?.Phases));
        OnPropertyChanged(nameof(PhasePipeline));
        OnPropertyChanged(nameof(HasPipeline));
        RefreshNextPhase(); // 重建节点后同步刷新下一阶段预告（生成配方 / 重置 / 语言切换）
    }

    /// <summary>
    /// 把引擎阶段列表归并为直观的流程节点。归并规则：
    /// ① 名称含「闷蒸」的注水/等待/搅拌 → 合并为一个「闷蒸」节点；
    /// ② 名称含「滴滤」「浸泡」「完成」的收尾等待 → 合并为最后一个「冲煮结束」节点；
    /// ③ 其余注水阶段 → 按出现顺序编号「注水1 / 注水2 …」；
    /// ④ 其余「等待下降 / 搅拌」→ 并入上一个节点的等待秒数（保持流程简洁）；
    /// ⑤ 其他独立等待（如杯测「破壳」）→ 单独成节点。
    /// </summary>
    private static List<PhaseStep> BuildPipeline(IReadOnlyList<Phase>? phases)
    {
        var drafts = new List<PipelineDraft>();
        if (phases != null)
        {
            int pourNo = 0;
            for (int i = 0; i < phases.Count; i++)
            {
                var ph = phases[i];
                var name = ph.Name ?? "";

                // 归并或新建节点（标题相同则并入上一个节点）
                void Node(string title)
                {
                    if (drafts.Count > 0 && drafts[^1].Title == title) { drafts[^1].To = i; return; }
                    drafts.Add(new PipelineDraft { Title = title, From = i, To = i });
                }

                if (name.Contains("闷蒸"))               Node(I18n.T("StepBloom"));
                else if (name.Contains("静置浸泡"))      Node("静置浸泡");   // 杯测静置浸泡是核心主步骤，单独成节点（不并入「冲煮结束」）
                else if (name.Contains("滴滤") || name.Contains("浸泡") || name.Contains("完成")) Node(I18n.T("StepFinish"));
                else if (ph.Type == PhaseType.Pour)      Node(string.Format(I18n.T("StepPour"), ++pourNo));
                else if (name.Contains("等待") || name.Contains("搅拌"))
                {
                    // 注水后的落水等待 / 搅拌：并入上一个节点，只累计秒数
                    if (drafts.Count > 0) { drafts[^1].To = i; drafts[^1].Secs += ph.DurationSec; continue; }
                    Node(I18n.T("StepWaitDown"));
                }
                else
                {
                    // 独立等待节点（如杯测破壳）：去掉括号内的英文注释，并限制长度避免节点过宽
                    string t = name.Contains("破壳") ? I18n.T("StepBreak") : ShortName(name);
                    Node(t);
                }

                // 往当前节点填内容：注水记目标重量，等待记秒数
                var d = drafts[^1];
                if (ph.Type == PhaseType.Pour) d.Target = "→ " + ph.Target.ToString("0") + "g";
                else d.Secs += ph.DurationSec;
            }
        }

        return drafts.Select((d, i) => new PhaseStep(i + 1, d.Title, d.Sub, IconFor(d.Title), d.From, d.To)).ToList();
    }

    /// <summary>按节点标题映射一个图形化图标（emoji），让流程管道更直观。</summary>
    private static string IconFor(string title)
    {
        if (title.Contains("闷蒸")) return "🌸";
        if (title.Contains("注水")) return "💧";
        if (title.Contains("破壳")) return "🔨";
        if (title.Contains("搅拌")) return "🌀";
        if (title.Contains("浸泡")) return "🛁";
        if (title.Contains("下降") || title.Contains("滴滤")) return "⏳";
        if (title.Contains("结束") || title.Contains("完成")) return "☕";
        return "▸";
    }

    /// <summary>去掉阶段名里的括号注释并限制长度（用于兜底节点标题）。</summary>
    private static string ShortName(string name)
    {
        int cut = name.IndexOfAny(new[] { '（', '(' });
        var s = cut > 0 ? name[..cut] : name;
        return s.Length > 6 ? s[..6] : s;
    }

    /// <summary>按当前阶段索引刷新节点状态（已完成 / 进行中）。仅在状态变化时触发属性通知。</summary>
    private void UpdatePipelineState(int phaseIdx, bool done)
    {
        foreach (var st in _pipeline)
        {
            st.Done = done || phaseIdx > st.ToPhase;
            st.Current = !done && phaseIdx >= st.FromPhase && phaseIdx <= st.ToPhase;
        }
        RefreshNextPhase();
    }

    // ---------- 下一阶段预告 ----------
    private string _nextPhaseText = "";

    /// <summary>下一阶段预告行：当前阶段之后第一个未完成节点的「图标 标题（副标题）」。
    /// 未开始时预告第一步，全部完成后为空（隐藏提示行）。文本仅在变化时通知，供 UI 低成本绑定。</summary>
    public string NextPhaseText
    {
        get => _nextPhaseText;
        private set
        {
            if (_nextPhaseText == value) return;
            _nextPhaseText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>根据当前阶段索引重算下一阶段预告（在管道节点状态刷新时调用）。
    /// 语义：未开始（生成配方/重置后）预告第一步；冲煮中/暂停预告当前阶段之后的节点；完成后清空。</summary>
    private void RefreshNextPhase()
    {
        if (Done || _pipeline.Count == 0) { NextPhaseText = ""; return; }
        bool notStarted = !Running && !_paused; // 生成配方或重置后：还没点开始
        int idx = notStarted ? -1 : (_state?.PhaseIndex ?? -1);
        var next = _pipeline.FirstOrDefault(st => !st.Done && st.FromPhase > idx);
        NextPhaseText = next == null
            ? ""
            : $"{next.Icon} {I18n.T("NextPhase")}：{next.Title}" + (string.IsNullOrEmpty(next.Sub) ? "" : $"（{next.Sub}）");
    }

    // ---------- 计时 ----------
    private long Elapsed() => _now();

    private void Tick()
    {
        try
        {
            if (_state == null) return;
            if (IsPouring && Running)
            {
                SimWeight = Math.Min(SimWeight + PourRate * 0.05, (_recipe?.TotalWater ?? 300) + 10);
            }
            else if (Running && !_isCupping && _recipe != null)
            {
                // 自动注水推进：注水阶段以「推荐流速」向模拟秤注水（每帧 50ms），
                // 重量到达本段目标即由引擎自动切换下一阶段——用户点「开始」后全程无需手动操作。
                // 杯测法为静态评估（按时间推进），不自动注水。
                var ph = _recipe.Phases.Count > 0
                    ? _recipe.Phases[Math.Min(_state.PhaseIndex, _recipe.Phases.Count - 1)]
                    : null;
                if (ph != null && ph.Type == PhaseType.Pour && ph.Target > ph.StartWeight)
                {
                    double flow = Math.Max(1.0, _recipe.RecommendedFlow);
                    SimWeight = Math.Min(SimWeight + flow * 0.05, ph.Target);
                }
            }
            TickNow();
        }
        catch (Exception ex)
        {
            ErrorSink?.Invoke(ex); // 定时器帧异常：记录但不让后台线程崩溃进程
        }
    }

    /// <summary>测试用：同步推进一帧（等价于定时器触发一次），不经过真实计时器。</summary>
    internal void TickForTest() => Tick();

    /// <summary>测试用：停止后台 50ms 定时器，消除异步竞态，使运行时测试可确定性驱动。</summary>
    internal void StopTimer() => _timer.Stop();

    /// <summary>测试用：注入可控时钟（返回毫秒时间戳），使流速/等待类断言不受真实计时抖动影响。</summary>
    internal void SetClockForTest(Func<long> clock) => _now = clock;

    private void TickNow()
    {
        if (_state == null || _recipe == null) return;
        var snap = BrewEngine.Tick(_state, Elapsed(), SimWeight);
        void Apply()
        {
            WeightText = snap.Weight.ToString("F1");
            TimerText = Fmt((long)snap.ElapsedMs);
            FlowText = (snap.Flow > 0 ? snap.Flow : 0).ToString("F1") + " g/s";
            RatioText = "1:" + (snap.RatioAchieved > 0 ? snap.RatioAchieved.ToString("F1") : "0.0");
            PhaseName = snap.Phase?.Name ?? I18n.T("Done");
            PhaseTip = snap.Phase?.Tip ?? I18n.T("BrewDone");
            UpdatePipelineState(snap.PhaseIndex, snap.Done); // 流程管道图：仅刷新节点状态，不重建集合
            PhaseProgress = snap.Phase?.Progress ?? 0;
            // 阶段进度条配套：注水/等待分色 + 本段注水量（注水段）/ 剩余秒数（等待段）
            IsPourPhase = snap.Phase?.Type == PhaseType.Pour;
            PhasePourText = snap.Phase == null ? "" : snap.Phase.Type == PhaseType.Pour
                ? string.Format(I18n.T("PouringSeg"), Math.Max(0, snap.Weight - snap.Phase.StartWeight).ToString("F0"), snap.Phase.Target.ToString("F0"))
                : (snap.Phase.RemainingSec is double rs && rs > 0 ? string.Format(I18n.T("WaitingRemain"), rs.ToString("F0")) : I18n.T("Waiting"));
            // 当前阶段剩余时间（仅等待/杯测注水引导阶段有值）
            PhaseRemainText = snap.Phase?.RemainingSec is double rem && rem > 0
                ? $"剩余 {Fmt((long)(rem * 1000))}"
                : (snap.Phase?.Type == PhaseType.Pour && !_isCupping ? "注水至目标重量" : "—");
            OverallProgress = snap.OverallProgress;
            WarningsText = snap.Warnings.Count > 0 ? string.Join("\n", snap.Warnings) : "";
            DoseText = snap.Dose.ToString("0") + " g";
            RatioPlanText = "1:" + snap.Ratio.ToString("0");
            FlowRecText = "建议流速 " + snap.RecommendedFlow.ToString("0") + " g/s";
            // 以下固定文本（RoastAgText / GrindC40EKText / OriginInfoText / WaterText / SuggestRestLabel /
            // RestDaysText / RestReadyDateText / RecommendedGrind*）由 BuildStaticTexts() 在 Generate 时算一次，
            // 这里只取用，不再每帧重建字符串（性能优化）。
            Running = snap.Running;
            Done = snap.Done;
            _paused = !snap.Running && !snap.Done && _state != null && _state.ElapsedMs > 0;
            RaiseTransportState();
            if (snap.Done)
            {
                // 完成后停止计时器，确保时间真正终止
                _timer.Stop();
                StartLabel = I18n.T("Start");
                // 杯测法完成后提示进入啜吸评分环节（SCA 杯测关键步骤）
                if (_isCupping) PhaseTip = "浸泡完成 🎉 现在开始啜吸评分：大口吸入咖啡液，感受干香/湿香/酸质/甜感/醇厚/余韵。";
            }
        }
        if (Marshal != null) Marshal(Apply);
        else Apply();
    }

    /// <summary>
    /// 配方相关、Generate 后不再变化的只读文本，集中计算一次存入字段，避免 50ms 定时器每帧重复拼接字符串。
    /// 这些字段通过属性暴露给 UI 绑定，TickNow 仅取用不重算。
    /// </summary>
    private void BuildStaticTexts()
    {
        if (_recipe == null) return;
        var r = _recipe;
        RoastAgText = $"{I18n.T("Roast")} {r.RoastLabel}（Ag {r.RoastAgMin}–{r.RoastAgMax}）";
        RecommendedGrindLabel = r.GrindLabel;
        // 推荐研磨 = 研磨度档位 + 磨豆机刻度（C40 / EK43）。用户要求明确显示 C40/EK43 刻度，故直接拼入一行（不再单独剥离）。
        var gp = BrewEngine.GrindProfiles.GetValueOrDefault(r.Grind);
        var desc = gp?.Note ?? "";
        int cut = desc.IndexOf('（'); // 去掉描述里自带的刻度括号（C40/EK43），避免与下面显式刻度重复
        if (cut > 0) desc = desc[..cut];
        GrindRecText = $"{r.GrindLabel}（C40≈{r.GrindC40} / EK≈{r.GrindEK}）" +
            (desc.Length > 0 ? " · " + desc : "") +
            (_isCupping ? $"\n{I18n.T("GrindCuppingRef")}（SCA）：C40 25 / EK43 9" : "");
        OriginInfoText = $"{I18n.T("OriginFlavorLine")}{r.OriginFlavor}\n{I18n.T("OriginGradeLine")}{r.OriginGrade}\n{I18n.T("OriginVarietyLine")}{r.VarietyLabel}";
        // 水质推荐：标签 + TDS/GH/KH 范围 + 说明（修复旧版重复拼接 WaterNote 三次的 bug）
        var wp = BrewEngine.WaterProfiles.GetValueOrDefault(r.Water, BrewEngine.WaterProfiles["balanced"]);
        WaterText = $"{r.WaterLabel}\n{I18n.T("WaterTDS")}{string.Format(I18n.T("WaterRange"), wp.TdsMin, wp.TdsMax)}  {I18n.T("WaterGH")}{string.Format(I18n.T("WaterRange"), wp.GhMin, wp.GhMax)}  {I18n.T("WaterKH")}{string.Format(I18n.T("WaterRange"), wp.KhMin, wp.KhMax)}\n{r.WaterNote}";
        // 推荐水温（2026-09-08 新增）：引擎按烘焙度/处理法/研磨算出的基准水温
        TempRecText = $"{r.Temp}℃";
        SuggestRestLabel = string.Format(I18n.T("RestRecPeriod"), r.RestRec.IdealMin, r.RestRec.IdealMax, r.RoastLabel, r.RoastAgMin, r.RoastAgMax);
        RestDaysText = string.Format(I18n.T("RestAged"), r.RestDays);
        RestReadyDateText = r.RestReadyMaxDate != default
            ? string.Format(I18n.T("RestReadyDate"), r.RestReadyMaxDate, r.RestReadyMinDate)
            : I18n.T("RestReadyHint");
    }

    private void BuildSummary()
    {
        if (_recipe == null) return;
        var r = _recipe;
        SummaryText =
            $"{I18n.T("Method")}：{r.MethodLabel}\n" +
            $"{I18n.T("Dose")}：{r.Dose:0}g · {r.SizeLabel}\n" +
            $"{I18n.T("RatioRec")}：1:{r.Ratio:0} · {I18n.T("TotalWaterPlan")} {r.TotalWater:0}g\n" +
            $"{I18n.T("Bloom")}：{r.BloomWait}s · {I18n.T("Temp")}：{r.Temp}℃\n" +
            $"{I18n.T("Roast")}：{r.RoastLabel}（Ag {r.RoastAgMin}–{r.RoastAgMax}） · {I18n.T("Flavor")}：{r.FlavorLabel}\n" +
            $"{I18n.T("Origin")}：{r.OriginLabel}（{r.OriginRegion} / {r.OriginFlavor} / {r.OriginGrade}） · {I18n.T("Variety")}：{r.VarietyLabel}\n" +
            $"{I18n.T("Process")}：{r.ProcessLabel} · {I18n.T("GrindRec")}：{r.GrindLabel}（C40≈{r.GrindC40} / EK≈{r.GrindEK}）\n" +
            $"{I18n.T("Water")}：{r.WaterLabel}（{r.WaterNote}）\n" +
            $"{I18n.T("Dripper")}：{r.DripperLabel} · {I18n.T("Filter")}：{r.FilterLabel}{(r.NeedRinse ? "（" + I18n.T("Rinse") + "）" : "")}\n" +
            $"{I18n.T("Flow")}：{r.RecommendedFlow:0} g/s\n" +
            $"{I18n.T("BrewDate")}：{_brewDate:yyyy-MM-dd}\n" +
            $"{I18n.T("Phases")}：{string.Join(" → ", r.Phases.Select(p => p.Name))}\n" +
            $"{r.Phases.Count} {I18n.T("Phases")} · {I18n.T("EstTotalTime")}：{Fmt((long)(r.TotalTimeSec * 1000))}";
        RestRecText = $"{string.Format(I18n.T("RestRecPeriod"), r.RestRec.IdealMin, r.RestRec.IdealMax, r.RoastLabel, r.RoastAgMin, r.RoastAgMax)}\n{r.RestRec.Status}\n{r.RestRec.Tip}" +
            (r.RestReadyMaxDate != default ? "\n" + string.Format(I18n.T("RestReadyDate"), r.RestReadyMaxDate, r.RestReadyMinDate) : "");
        RestRecStatus = r.RestRec.Status;
    }

    private static string Fmt(long ms)
    {
        var s = ms / 1000;
        return $"{s / 60}:{s % 60:D2}";
    }

    // ---------- 冲煮记录持久化 ----------
    public void LoadRecords() => ReloadRecords();

    private void ReloadRecords()
    {
        try { _records = RecordsStore.Load(_recordsPath); }
        catch (Exception ex) { ErrorSink?.Invoke(ex); _records = new(); }
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(StatsText)); // 记录变更后刷新统计卡
    }

    // ---------- 参数/偏好持久化（优化：记住上次设置，避免每次启动重置）----------

    /// <summary>从指定路径加载并应用上次保存的冲煮参数与语言偏好。</summary>
    public void LoadSettings(string path)
    {
        _settingsPath = path;
        ApplySettings(SettingsStore.Load(path));
    }

    /// <summary>把一份 <see cref="BrewSettings"/> 应用到当前 VM（仅对已知枚举赋值，脏数据忽略）。</summary>
    private void ApplySettings(BrewSettings s)
    {
        if (BrewEngine.MethodProfiles.ContainsKey(s.Method)) Method = s.Method;
        if (BrewEngine.SizeProfiles.ContainsKey(s.Size)) Size = s.Size;
        if (s.Dose is >= 1 and <= 500) Dose = s.Dose; // 须在 Size 之后（Dose 受档位范围钳制）
        if (s.Ratio is > 0 and <= 25) Ratio = s.Ratio;
        if (BrewEngine.RoastProfiles.ContainsKey(s.Roast)) Roast = s.Roast;
        if (s.RoastAg is >= 10 and <= 120) RoastAg = s.RoastAg;
        if (BrewEngine.FlavorProfiles.ContainsKey(s.Flavor)) Flavor = s.Flavor;
        if (BrewEngine.ProcessProfiles.ContainsKey(s.Process)) Process = s.Process;
        if (BrewEngine.DripperProfiles.ContainsKey(s.Dripper)) Dripper = s.Dripper;
        if (BrewEngine.FilterProfiles.ContainsKey(s.Filter)) Filter = s.Filter;
        if (BrewEngine.OriginProfiles.ContainsKey(s.Origin)) Origin = s.Origin;
        if (BrewEngine.VarietyProfiles.ContainsKey(s.Variety)) Variety = s.Variety;
        if (s.RestDays is >= 0 and <= 120) RestDays = s.RestDays;
        // 密度/海拔：先落地海拔（尚未手选，不会回写）；再恢复保存的密度。
        // 兼容旧配置：旧版 DensityAuto=false 表示用户手选过密度，s.Density 即其选择；旧版 true 则忽略密度、由海拔推导。
        _densityUserSet = false;
        _beanAltitudeM = s.BeanAltitudeM is >= 0 and <= 3000 ? s.BeanAltitudeM : 1500;
        OnPropertyChanged(nameof(BeanAltitudeM));
        bool legacyManual = !s.DensityAuto; // 旧字段：仅用于判断历史手选意图
        bool hasSavedDensity = s.Density is "light" or "medium" or "dense";
        if (legacyManual && hasSavedDensity)
        {
            _density = s.Density;          // 恢复历史手选值
            _densityUserSet = true;
            OnPropertyChanged(nameof(Density));
        }
        else
        {
            _density = BrewEngine.DensityFromAltitude(_beanAltitudeM); // 无手选 → 由海拔预填
            OnPropertyChanged(nameof(Density));
        }
        // 烘焙日期（触发 RestDays 重算），故 RestDays 在上面已应用、此处不受影响
        if (DateTime.TryParse(s.RoastDateIso, out var rd) && rd != DateTime.MinValue) RoastDate = rd;
        else RoastDate = DateTime.Today.AddDays(-7); // 无保存值时回退默认：现在前 7 天
        if (DateTime.TryParse(s.BrewDateIso, out var bd) && bd != DateTime.MinValue) BrewDate = bd;
        // 语言偏好：直接落到 I18n.Current（避免构造函数期触发 UI 重建），并通知绑定
        if (s.Culture == I18n.EnUS || s.Culture == I18n.ZhCN)
        {
            I18n.Current = s.Culture;
            OnPropertyChanged(nameof(Culture));
            OnPropertyChanged(nameof(CultureLabel));
        }
    }

    /// <summary>把当前输入参数捕捉为可持久化的 <see cref="BrewSettings"/>。</summary>
    private BrewSettings CaptureSettings() => new()
    {
        Method = _method, Size = _size, Dose = _dose, Ratio = _ratio,
        Roast = _roast, RoastAg = _roastAg, Flavor = _flavor, Process = _process,
        Dripper = _dripper, Filter = _filter, Origin = _origin, Variety = _variety,
        RestDays = _restDays, Density = _density,
        BeanAltitudeM = _beanAltitudeM,
        // 兼容字段：密度仍由海拔预填 → true；用户已手选 → false（与 LoadSettings 的读取逻辑对称）。
        DensityAuto = !_densityUserSet,
        RoastDateIso = _roastDate == DateTime.MinValue ? null : _roastDate.ToString("o"),
        BrewDateIso = _brewDate == DateTime.MinValue ? null : _brewDate.ToString("o"),
        Culture = I18n.Current,
    };

    /// <summary>保存当前参数/偏好到 settings.json（生成配方或保存记录时调用）。</summary>
    private void PersistSettings() => SettingsStore.Save(_settingsPath, CaptureSettings());

    /// <summary>保存当前配方为一条冲煮记录（含冲煮日期与实测末重 / 实际粉水比）。</summary>
    public void SaveCurrentBrew()
    {
        if (_recipe == null) return;
        var r = _recipe;
        double finalW = SimWeight;
        double ratioAch = finalW > 0 ? finalW / r.Dose : 0;
        var rec = new BrewRecord
        {
            BrewDate = _brewDate,
            Method = r.Method,
            MethodLabel = r.MethodLabel,
            Dose = r.Dose,
            Ratio = r.Ratio,
            TotalWater = r.TotalWater,
            RoastLabel = r.RoastLabel,
            RoastAg = r.RoastAg,
            RoastAgMin = r.RoastAgMin,
            RoastAgMax = r.RoastAgMax,
            ProcessLabel = r.ProcessLabel,
            DripperLabel = r.DripperLabel,
            FilterLabel = r.FilterLabel,
            GrindLabel = r.GrindLabel,
            GrindC40 = r.GrindC40,
            GrindEK = r.GrindEK,
            WaterLabel = r.WaterLabel,
            OriginLabel = r.OriginLabel,
            OriginRegion = r.OriginRegion,
            VarietyLabel = r.VarietyLabel,
            OriginFlavor = r.OriginFlavor,
            OriginGrade = r.OriginGrade,
            BloomWait = r.BloomWait,
            Temp = r.Temp,
            Summary = _summaryText,
            // Phase 11：结构化引擎键直取配方（记录→方案复刻免反查）+ 方案来源溯源
            Roast = r.Roast,
            Process = r.Process,
            Origin = r.Origin,
            Dripper = r.Dripper,
            Flavor = r.Flavor,
            PlanTitle = AppliedPlanTitle,
            FinalWeight = finalW > 0 ? finalW : null,
            RatioAchieved = finalW > 0 ? ratioAch : null,
        };
        try { RecordsStore.Add(_recordsPath, rec); ReloadRecords(); PersistSettings(); }
        catch (Exception ex) { ErrorSink?.Invoke(ex); }
    }

    public void DeleteRecord(Guid id)
    {
        try { RecordsStore.Delete(_recordsPath, id); ReloadRecords(); }
        catch (Exception ex) { ErrorSink?.Invoke(ex); }
    }

    /// <summary>导出全部冲煮记录为 CSV（UTF-8 BOM，Excel/WPS 直接打开）。无记录时返回提示文案。</summary>
    public string ExportRecordsCsv(string path)
    {
        if (_records.Count == 0) return I18n.T("ExportCsvEmpty");
        var csv = CsvExport.BuildRecordsCsv(_records);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, csv, new System.Text.UTF8Encoding(true));
        return string.Format(I18n.T("ExportCsvOk"), _records.Count);
    }

    // ---------- 记录 ↔ 方案 打通（Phase 11：复刻与溯源） ----------

    /// <summary>最近一次应用的方案名（大师清单 / 我的方案），保存记录时写入 <see cref="BrewRecord.PlanTitle"/> 做溯源。
    /// 语义：记录「配方参数的来源」；之后用户手动改参数不主动清除（记录的是最近方案来源，非逐参数追踪）。</summary>
    public string? AppliedPlanTitle { get; private set; }

    /// <summary>标记「当前参数来自某方案」（应用大师方案 / 我的方案时由 UI 调用）。</summary>
    public void MarkPlanSource(string? planTitle) => AppliedPlanTitle = string.IsNullOrWhiteSpace(planTitle) ? null : planTitle;

    /// <summary>把一条历史冲煮记录复刻为「我的方案」，让满意的一杯变成可随时重冲的配方。
    /// 字段优先级：① 新记录直接带引擎键（Phase 11 起落盘）→ 直取；② 旧记录只有展示标签 → 按标签/Ag 反查引擎键
    /// （<see cref="BrewEngine.KeyByLabel{T}"/> / <see cref="BrewEngine.RoastKeyByAg"/>），查不到留空 = 未指定，
    /// 应用时保留用户当前选择（与方案联动语义一致）。正文为可读摘要，含实测结果（如有）。</summary>
    public void RecordToMyPlan(Guid id)
    {
        var rec = Records.FirstOrDefault(x => x.Id == id);
        if (rec == null) return;

        string title = string.IsNullOrWhiteSpace(rec.PlanTitle)
            ? $"{rec.MethodLabel} · {rec.RoastLabel}(Ag {rec.RoastAg}) · {rec.OriginLabel}"
            : rec.PlanTitle!;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"· 粉量 {rec.Dose:0}g / 总水 {rec.TotalWater:0}g，粉水比 1:{rec.Ratio:0.#}");
        sb.AppendLine($"· {rec.MethodLabel} · {I18n.T("Temp")} {rec.Temp}℃ · {I18n.T("Bloom")} {rec.BloomWait}s");
        sb.AppendLine($"· {I18n.T("Roast")}：{rec.RoastLabel}（Ag {rec.RoastAgMin}–{rec.RoastAgMax}，实测读数 {rec.RoastAg}）");
        sb.AppendLine($"· {I18n.T("Origin")}：{rec.OriginLabel} · {I18n.T("Process")}：{rec.ProcessLabel}");
        sb.Append($"· {rec.DripperLabel} · {rec.FilterLabel} · {rec.GrindLabel}");
        if (rec.GrindC40 > 0) sb.Append($"（C40≈{rec.GrindC40}）");
        if (rec.FinalWeight.HasValue)
            sb.Append($"\n· 冲煮日期 {rec.BrewDate:yyyy-MM-dd} · 实测末重 {rec.FinalWeight:0}g · 实际粉水比 1:{rec.RatioAchieved:0.0}");

        SaveMyPlan(new MyPlan
        {
            Title = title,
            Summary = I18n.T("MyPlanFromRecordSummary"),
            Detail = sb.ToString(),
            Method = BrewEngine.MethodProfiles.ContainsKey(rec.Method) ? rec.Method : null,
            Dripper = rec.Dripper ?? BrewEngine.KeyByLabel(BrewEngine.DripperProfiles, p => p.Label, rec.DripperLabel),
            Dose = rec.Dose.ToString("0", CultureInfo.InvariantCulture),
            Ratio = rec.Ratio.ToString("0.##", CultureInfo.InvariantCulture),
            Process = rec.Process ?? BrewEngine.KeyByLabel(BrewEngine.ProcessProfiles, p => p.Label, rec.ProcessLabel),
            Origin = rec.Origin ?? BrewEngine.KeyByLabel(BrewEngine.OriginProfiles, p => p.Label, rec.OriginLabel),
            Roast = rec.Roast ?? BrewEngine.RoastKeyByAg(rec.RoastAg),
            Flavor = rec.Flavor, // 旧记录未存风味标签，无法反查 → 留空（未指定）
        });
    }

    // ---------- 我的方案（收藏大师方案，离线复用）持久化 ----------

    /// <summary>用户收藏的「我的方案」列表（UI 只读展示，落盘由 SaveMyPlan/DeleteMyPlan 触发）。</summary>
    public List<MyPlan> MyPlans
    {
        get => _myPlans;
        private set => Set(ref _myPlans, value);
    }

    /// <summary>「我的方案」文件仓储路径（可注入，便于测试隔离）。</summary>
    public string MyPlansPath
    {
        get => _myPlansPath;
        set => _myPlansPath = value;
    }

    /// <summary>从指定路径加载并应用收藏的「我的方案」（按 Title 去重）。</summary>
    public void LoadMyPlans(string path)
    {
        _myPlansPath = path;
        ReloadMyPlans();
    }

    /// <summary>用当前路径重载「我的方案」。</summary>
    public void LoadMyPlans() => ReloadMyPlans();

    private void ReloadMyPlans()
    {
        try { _myPlans = MyPlansStore.Load(_myPlansPath); }
        catch (Exception ex) { ErrorSink?.Invoke(ex); _myPlans = new(); }
        OnPropertyChanged(nameof(MyPlans));
    }

    /// <summary>保存一条「我的方案」（同名覆盖），写入文件并刷新内存列表。</summary>
    public void SaveMyPlan(MyPlan plan)
    {
        if (plan == null) return;
        try { MyPlansStore.AddOrReplace(_myPlansPath, plan); ReloadMyPlans(); }
        catch (Exception ex) { ErrorSink?.Invoke(ex); }
    }

    /// <summary>按 Title 删除一条「我的方案」，写入文件并刷新内存列表。</summary>
    public void DeleteMyPlan(string title)
    {
        try { MyPlansStore.Delete(_myPlansPath, title); ReloadMyPlans(); }
        catch (Exception ex) { ErrorSink?.Invoke(ex); }
    }

    /// <summary>「我的方案」默认备份文件路径（与 myplans.json 同目录，便于便携 / 分享 / 恢复）。</summary>
    public string MyPlansBackupPath
    {
        get
        {
            var dir = Path.GetDirectoryName(_myPlansPath);
            return Path.Combine(string.IsNullOrEmpty(dir) ? AppPaths.DataDirectory : dir, "myplans-backup.json");
        }
    }

    /// <summary>导出全部「我的方案」到默认备份文件（全量备份）。返回状态文案（含条数与路径）。</summary>
    public string ExportAllMyPlans() => ExportAllMyPlans(MyPlansBackupPath);

    /// <summary>导出全部「我的方案」到指定文件。返回状态文案。</summary>
    public string ExportAllMyPlans(string path)
    {
        MyPlansStore.SaveAll(path, _myPlans);
        return string.Format(I18n.T("ExportPlanDone"), _myPlans.Count, path);
    }

    /// <summary>从默认备份文件导入「我的方案」：按 Title 去重合并（同名保留本机现有），写入并刷新内存列表。返回状态文案。</summary>
    public string ImportAllMyPlans() => ImportAllMyPlans(MyPlansBackupPath);

    /// <summary>从指定文件导入「我的方案」。返回状态文案。</summary>
    public string ImportAllMyPlans(string path)
    {
        var incoming = MyPlansStore.Load(path);
        if (incoming.Count == 0) return I18n.T("ImportPlanEmptyFile");
        int added = 0, kept = 0;
        foreach (var p in incoming)
        {
            if (_myPlans.Any(x => x.Title == p.Title)) kept++;   // 同名：保留本机现有，不覆盖
            else { _myPlans.Add(p); added++; }
        }
        MyPlansStore.SaveAll(_myPlansPath, _myPlans);
        ReloadMyPlans();
        return string.Format(I18n.T("ImportPlanDone"), added, kept);
    }

    /// <summary>生成单条「我的方案」的分享文本（供复制 / 发送 / 另存 .txt）。找不到时抛 <see cref="KeyNotFoundException"/>。</summary>
    public string MyPlanShareText(string title)
    {
        var p = _myPlans.FirstOrDefault(x => x.Title == title)
            ?? throw new KeyNotFoundException(I18n.T("MyPlanNotFound"));
        return p.ToShareText();
    }

    /// <summary>解析分享文本并保存为一条「我的方案」（同名覆盖）。返回新方案标题；格式非法时抛 <see cref="FormatException"/>（中文消息）。</summary>
    public string ImportPlanShareText(string text)
    {
        var plan = MyPlan.FromShareText(text);
        SaveMyPlan(plan);
        return plan.Title;
    }

    /// <summary>从文件读取并导入一条分享文本（便携 .txt）。返回新方案标题。</summary>
    public string ImportPlanShareTextFromFile(string path)
    {
        return ImportPlanShareText(File.ReadAllText(path));
    }

    /// <summary>把「当前整套参数」存为一条「我的方案」（同名按参数覆盖）。
    /// 与「☆ 收藏大师方案」的区别：这里以 VM 当前输入为准，8 个结构化字段全部落盘，
    /// 因此用户自己调好的 <b>处理方式 / 产地 / 烘焙度</b> 也能随方案一起复用——这是日常最常用的一条路径
    /// （收藏来的大师方案通常不携带豆子信息，而自己存的方案必须能完整还原自己的豆子）。
    /// 标题由当前参数自动命名，正文为可读摘要（便于在「我的方案」清单里辨认）。</summary>
    public void SaveCurrentAsMyPlan()
    {
        // _recipe 不会随参数改动自动失效：生成过一次后再改粉量/豆子，_recipe 仍是上一份，
        // 直接取它写正文会存下与实际输入不符的配方（甚至从未生成时是空的）。
        // 因此保存前无条件重算一次，保证「存的方案 == 屏幕上当下的参数」。Generate() 是纯计算，代价可忽略。
        Generate();

        var r = _recipe; // 最近一次生成的配方（含各项 Label）
        string title = r == null
            ? I18n.T("MyPlanCurrentDefault")
            : $"{r.MethodLabel} · {r.RoastLabel} · {r.OriginLabel} · {r.ProcessLabel}";
        string detail = r == null ? "" :
            $"· 粉量 {r.Dose:0}g / 总水 {r.TotalWater:0}g，粉水比 1:{r.Ratio:0.#}\n" +
            $"· {r.MethodLabel} · {I18n.T("Temp")} {r.Temp}℃ · {I18n.T("Bloom")} {r.BloomWait}s\n" +
            $"· {I18n.T("Roast")}：{r.RoastLabel}（Ag {r.RoastAgMin}–{r.RoastAgMax}）\n" +
            $"· {I18n.T("Origin")}：{r.OriginLabel}（{r.OriginRegion}）· {I18n.T("Process")}：{r.ProcessLabel}\n" +
            $"· {I18n.T("Dripper")}：{r.DripperLabel} · 研磨 {r.GrindLabel}（C40≈{r.GrindC40}）";

        SaveMyPlan(new MyPlan
        {
            Title = title,
            Summary = I18n.T("MyPlanCurrentSummary"),
            Detail = detail,
            Method = _method,
            Dripper = _dripper,
            // 剂量类以配方有效值为准：引擎会对粉水比做归一化（如输入 17 → 实际 1:16.7），
            // 若这里存原始输入，就会出现「正文写 1:16.7、字段写 17」的自相矛盾。
            Dose = (r?.Dose ?? _dose).ToString("0", CultureInfo.InvariantCulture),
            Ratio = (r?.Ratio ?? _ratio).ToString("0.##", CultureInfo.InvariantCulture),
            Process = _process,
            Origin = _origin,
            Roast = _roast,
            Flavor = _flavor,
        });
    }

    /// <summary>生成按钮文案（走 I18n，语言切换时刷新）。</summary>
    public string GenerateLabel => I18n.T("GeneratePlan");

    /// <summary>当前冲煮方式的科普介绍（走 I18n，语言切换时刷新），供「查看介绍」弹窗展示。</summary>
    public string MethodNote => I18n.T("MethodNote_" + _method);
    /// <summary>当前冲煮方式的显示名（走 I18n，语言切换时刷新），供「查看介绍」弹窗标题展示。</summary>
    public string MethodLabel => I18n.T("Method_" + _method);
    /// <summary>「查看介绍」弹窗正文：科普说明 + 当前参数下的实时分段步骤、水温、总水量（语言切换时刷新）。</summary>
    public string MethodIntroBody
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine(MethodNote);
            sb.AppendLine();
            var r = _recipe;
            if (r != null)
            {
                sb.AppendLine(I18n.T("MethodIntroParamHeader"));
                sb.AppendLine($"🌡 {I18n.T("Temp")}：{r.Temp}℃");
                sb.AppendLine($"💧 {I18n.T("MethodIntroWater")}：{r.TotalWater:0} g");
                sb.AppendLine($"⏱ {I18n.T("MethodIntroTime")}：{r.TotalTimeSec:0}s");
                sb.AppendLine();
                if (_isCupping)
                {
                    sb.AppendLine(I18n.T("CuppingEquipment"));
                    sb.AppendLine("⏱ 冲煮方式：注入 94℃ 热水 → 静置浸泡 4 分钟（单段、无闷蒸 / 不破壳 / 不过滤）");
                    sb.AppendLine();
                }
                int idx = 1;
                foreach (var ph in r.Phases)
                {
                    if (ph.Type == PhaseType.Pour)
                        sb.AppendLine($"{idx}. {ph.Name}：注水至 {ph.Target:0} g（累计），{ph.Tip}");
                    else
                        sb.AppendLine($"{idx}. {ph.Name}：等待 {ph.DurationSec}s，{ph.Tip}");
                    idx++;
                }
            }
            else
            {
                sb.AppendLine(I18n.T("MethodIntroNoRecipe"));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 导入咖啡大师 / 通用冲煮方案（JSON）。兼容两种格式：
    /// 1) 本程序记录格式：{ "dose":15, "ratio":15, "roast":"light", "method":"classic", "origin":"ethiopia", "variety":"heirloom", "process":"washed", "dripper":"v60", "filter":"bleached", "size":"standard" }
    /// 2) 咖啡师大赛常见字段（cupping/brew 风格）：method/coffeeWeight/waterRatio/roastLevel/origin/variety/process/dripper/filter/size
    /// 解析后映射到 VM 参数并重新生成配方。解析失败抛 FormatException（由 UI 捕获提示）。
    /// </summary>
    public void ImportPlan(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new FormatException("方案为空");
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            string Str(string key, string fallback) =>
                r.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString()! : fallback;
            double Num(string key, double fallback) =>
                r.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : fallback;

            // 兼容别名
            string method = Str("method", Str("brewMethod", _method));
            string roast = Str("roast", Str("roastLevel", _roast));
            string origin = Str("origin", Str("coffeeOrigin", _origin));
            string variety = Str("variety", Str("coffeeVariety", _variety));
            string process = Str("process", Str("processMethod", _process));
            string dripper = Str("dripper", Str("brewer", _dripper));
            string filter = Str("filter", Str("filterType", _filter));
            string size = Str("size", Str("doseTier", _size));
            double dose = Num("dose", Num("coffeeWeight", _dose));
            double ratio = Num("ratio", Num("waterRatio", _ratio));

            // 仅当字段存在于已知枚举才赋值（避免脏数据）
            if (BrewEngine.MethodProfiles.ContainsKey(method)) Method = method;
            if (BrewEngine.RoastProfiles.ContainsKey(roast)) Roast = roast;
            if (BrewEngine.OriginProfiles.ContainsKey(origin)) Origin = origin;
            if (BrewEngine.VarietyProfiles.ContainsKey(variety)) Variety = variety;
            if (BrewEngine.ProcessProfiles.ContainsKey(process)) Process = process;
            if (BrewEngine.DripperProfiles.ContainsKey(dripper)) Dripper = dripper;
            if (BrewEngine.FilterProfiles.ContainsKey(filter)) Filter = filter;
            if (BrewEngine.SizeProfiles.ContainsKey(size)) Size = size;
            Dose = dose;
            Ratio = ratio;
            Generate();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new FormatException("方案 JSON 格式错误：" + ex.Message);
        }
    }

    // ---------- INotifyPropertyChanged ----------
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
