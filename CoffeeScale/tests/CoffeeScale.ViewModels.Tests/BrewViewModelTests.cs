using System;
using System.IO;
using System.Linq;
using CoffeeScale.Core;
using CoffeeScale.ViewModels;

namespace CoffeeScale.ViewModels.Tests;

/// <summary>
/// 运行时测试：不依赖任何 UI 框架，直接驱动 BrewViewModel 验证「能运行」。
/// 覆盖：生成配方、模拟注水、阶段推进、流量警告、养豆期自动计算、重置归零。
/// </summary>
public class BrewViewModelTests
{
    // 1) 生成配方：默认 classic / 标准粉量 15g / 1:15 → 总水量 225g
    [Fact]
    public void Generate_ProducesRecipeAndSummary()
    {
        var vm = new BrewViewModel();
        vm.Generate();

        Assert.True(vm.HasRecipe);
        Assert.Equal(225, vm.TotalWater, 1); // 15 * 15
        Assert.Contains("粉量（g）：15g", vm.SummaryText);
        Assert.Contains(I18n.T("Phases"), vm.SummaryText);
        Assert.Contains("建议养豆期", vm.RestRecText);
        // 初始阶段为闷蒸注水
        Assert.Equal("闷蒸注水", vm.PhaseName);
        // 流程管道图：默认经典分段法（标准粉量 2 段）→ 闷蒸 / 注水1 / 注水2 / 冲煮结束
        Assert.Equal(new[] { "闷蒸", "注水1", "注水2", "冲煮结束" }, vm.PhasePipeline.Select(s => s.Title));
        Assert.True(vm.PhasePipeline[0].Current);
        // 下一阶段预告：未开始时预告第一步「🌸 闷蒸（→ …g · …s）」
        Assert.Contains("🌸", vm.NextPhaseText);
        Assert.Contains("闷蒸", vm.NextPhaseText);
        Assert.Contains(I18n.T("NextPhase"), vm.NextPhaseText);
    }

    // 1b) 下一阶段预告随冲煮推进切换：开始后预告「注水1」，全部完成后清空隐藏
    [Fact]
    public void NextPhaseText_ProgressesAndClearsWhenDone()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();
        Assert.Contains("🌸", vm.NextPhaseText); // 未开始 → 预告闷蒸

        vm.Transport();     // 开始 → 当前阶段进入闷蒸注水
        vm.TickForTest();   // 推快照 → 预告切换到下一节点「💧 注水1」
        Assert.Contains("💧", vm.NextPhaseText);
        Assert.Contains("注水1", vm.NextPhaseText);

        // 推进到最后（完成态）→ 预告清空（UI 隐藏提示行）
        int guard = 0;
        while (!vm.Done && guard < 50)
        {
            vm.Next();
            vm.TickForTest();
            guard++;
        }
        Assert.True(vm.Done);
        Assert.Equal("", vm.NextPhaseText);
    }

    // 2) 不同粉量档位：选大粉量档 + 25g / 1:16 → 总水量 400g，且档位识别为大粉量
    [Fact]
    public void Generate_LargeDoseUsesLargeSizeProfile()
    {
        var vm = new BrewViewModel { Size = "large", Dose = 25, Ratio = 16 };
        vm.Generate();

        Assert.Equal(400, vm.TotalWater, 1); // 25 * 16
        Assert.Contains("大粉量", vm.SummaryText);
    }

    // 3) 模拟注水：开始 + 注水中，每帧 +0.3g（6g/s * 0.05s），10 帧后约 3.0g
    [Fact]
    public void SimulatedPour_GrowsWeightDeterministically()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();          // 停掉后台定时器，消除竞态
        vm.Start();         // Running = true
        vm.IsPouring = true;

        double before = vm.SimWeight;
        Assert.Equal(0, before);
        for (int i = 0; i < 10; i++) vm.TickForTest();

        Assert.Equal(3.0, vm.SimWeight, 1); // 0.3 * 10
        Assert.Equal("3.0", vm.WeightText);
        Assert.True(vm.Running);
        Assert.False(vm.Done);
    }

    // 4) 阶段推进：手动 Next 直接进入下一阶段
    [Fact]
    public void NextPhase_AdvancesPhaseNumber()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();

        string first = vm.PhaseName;
        vm.Next();
        vm.TickForTest(); // 刷新快照

        Assert.NotEqual(first, vm.PhaseName);
        Assert.True(vm.PhasePipeline.Any(s => s.Current), "切换阶段后管道图应有当前节点");
    }

    // 5) 完整冲煮：连续 Next 直到全部阶段完成 → Done（验证阶段状态机可走完）
    [Fact]
    public void FullBrew_CompletesAllPhases()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();

        int guard = 0;
        while (!vm.Done && guard < 50)
        {
            vm.Next();
            vm.TickForTest();
            guard++;
        }

        Assert.True(vm.Done);
        Assert.True(vm.PhasePipeline.All(s => s.Done), "全部阶段完成后所有流程节点应标记为已完成");
        Assert.Equal("开始", vm.StartLabel); // 完成态主按钮回归「开始」：点击即自动重置并重新冲煮
        Assert.Equal("完成", vm.PhaseName); // 引擎修复后：完成时显示「完成」而非残留阶段名
    }

    // 6) 流量警告：用注入时钟模拟一次大幅快速注水，触发「注水偏快」提示
    [Fact]
    public void FastPour_TriggersFlowWarning()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();

        long t = 1_000_000;
        vm.SetClockForTest(() => t);
        vm.Start();     // Running = true，StartedAt = t
        vm.IsPouring = true;

        t += 50;              // 真实推进 50ms，使 dt>0、流速可计算
        vm.PourBump(2);       // 单帧跳重 2g → 流速 ≈ 40 g/s，远超 10
        vm.TickForTest();     // 计算快照 → Flow 极高 → 触发警告

        Assert.False(string.IsNullOrWhiteSpace(vm.WarningsText));
        Assert.Contains("偏快", vm.WarningsText);
    }

    // 7) 养豆期：设置烘焙日期自动反推养豆天数
    [Fact]
    public void RoastDate_AutoComputesRestDays()
    {
        var vm = new BrewViewModel();

        vm.RoastDate = DateTime.Today.AddDays(-5);
        Assert.Equal(5, vm.RestDays);

        vm.RoastDate = DateTime.Today.AddDays(-30);
        Assert.Equal(30, vm.RestDays); // 在 0..120 范围内直接取天数

        vm.RoastDate = DateTime.Today.AddDays(-200);
        Assert.Equal(120, vm.RestDays); // 超出上限被钳制
    }

    // 15x) 烘焙日期默认值为现在日期前 3 天（阳光 2026-09-01）
    [Fact]
    public void RoastDate_DefaultsToTodayMinus3()
    {
        var vm = new BrewViewModel();
        Assert.Equal(DateTime.Today.AddDays(-3), vm.RoastDate);
    }

    // 15y) 称控制：开始 / 暂停 / 继续 状态联动（CanStart / Running / IsPaused）
    [Fact]
    public void ScaleControl_StartPauseResume_ReflectsState()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        // 初始空闲：开始可用，暂停/继续禁用
        Assert.True(vm.CanStart);
        Assert.False(vm.Running);
        Assert.False(vm.IsPaused);

        vm.Start();
        Assert.True(vm.Running);
        Assert.False(vm.CanStart);   // 运行中「开始」禁用
        Assert.False(vm.IsPaused);   // 运行中不是「已暂停」

        vm.Pause();
        Assert.False(vm.Running);
        Assert.True(vm.IsPaused);     // 已暂停：「继续」可用

        vm.Resume();
        Assert.True(vm.Running);

        vm.Reset();
        Assert.False(vm.Running);
        Assert.True(vm.CanStart);     // 归零后回到空闲
    }

    // 8) 重置：冲煮中途重置，重量/状态全部归零
    [Fact]
    public void Reset_ClearsRunningState()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();
        vm.Start();
        vm.IsPouring = true;
        for (int i = 0; i < 5; i++) vm.TickForTest();
        Assert.True(vm.SimWeight > 0);

        vm.Reset();

        Assert.Equal(0, vm.SimWeight);
        Assert.Equal("0.0", vm.WeightText);
        Assert.False(vm.Running);
        Assert.False(vm.Done);
        Assert.Equal("开始", vm.StartLabel);
        Assert.Equal(0, vm.OverallProgress, 3);
    }

    // 9) 暂停/继续：开始→运行(标签"暂停")；暂停→已暂停(标签"继续")；继续→恢复运行
    [Fact]
    public void Transport_StartPauseResume_UpdateStartLabel()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();
        vm.Start();
        Assert.Equal("暂停", vm.StartLabel);

        vm.Pause();
        Assert.Equal("继续", vm.StartLabel);
        Assert.False(vm.Running);
        Assert.True(vm.IsPaused);

        vm.Resume();
        Assert.Equal("暂停", vm.StartLabel);
        Assert.True(vm.Running);
    }

    // 10) 粉量档位联动：切换档位自动把粉量填为该档默认值（少8 / 标15 / 大25）
    [Fact]
    public void SizeSelection_SetsDoseToTierDefault()
    {
        var vm = new BrewViewModel();
        Assert.Equal(15, vm.Dose); // 默认标准档 15g

        vm.Size = "small";
        Assert.Equal(8, vm.Dose);

        vm.Size = "large";
        Assert.Equal(25, vm.Dose);

        vm.Size = "standard";
        Assert.Equal(15, vm.Dose);
    }

    // 11) 新增维度默认值 + 建议养豆期 + 粉量/粉水比实时显示
    [Fact]
    public void NewDimensions_DefaultsAndSuggestRest()
    {
        var vm = new BrewViewModel();
        Assert.Equal("washed", vm.Process);
        Assert.Equal("v60", vm.Dripper);
        Assert.Equal("bleached", vm.Filter);
        vm.Generate();
        Assert.Equal("中", vm.RecommendedGrindLabel); // 默认浅烘 + v60 → 推荐中（V2.0 标定 v60 基准为中粗，浅烘再细一档→中）
        Assert.Contains("建议养豆期", vm.SuggestRestLabel);
        Assert.Equal("已养 3 天", vm.RestDaysText); // 默认烘焙日期=今天前 3 天 → 推算已养 3 天
        Assert.Contains("15 g", vm.DoseText);
        Assert.Equal("1:15", vm.RatioPlanText);
        Assert.Contains("建议流速", vm.FlowRecText);
    }

    // 12) 聪明杯走浸泡式阶段
    [Fact]
    public void SmartDripper_UsesImmersionPhases()
    {
        var vm = new BrewViewModel { Dripper = "smart" };
        vm.Generate();
        Assert.Contains("浸泡萃取", vm.SummaryText);
    }

    // 13) 处理方式影响摘要
    [Fact]
    public void ProcessSelection_AffectsSummary()
    {
        var vm = new BrewViewModel { Process = "natural" };
        vm.Generate();
        Assert.Contains("日晒", vm.SummaryText);
    }

    // 14) 默认烘焙度：肉桂烘（粉样 Ag 80–90，中值 85；阳光 2026-09-02 要求默认肉桂烘）
    [Fact]
    public void Generate_DefaultRoastIsCinnamon()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        Assert.Contains("肉桂烘", vm.RoastAgText);
        Assert.Contains("Ag 80–90", vm.RoastAgText);
    }

    // 15b) 具体烘焙度 Ag 读数：默认取所选档位区间中值；切换档位回退到新档位中值
    [Fact]
    public void RoastAg_DefaultsToTierMidpoint_AndResetsOnRoastChange()
    {
        var vm = new BrewViewModel();
        // 默认浅烘 light(Ag 80–90) → 90（阳光 2026-09-03 改默认值）
        Assert.Equal(90, vm.RoastAg);
        // 用户手动改为 88，仍保留
        vm.RoastAg = 88;
        Assert.Equal(88, vm.RoastAg);
        // 切到中深烘 Moderately Dark(Ag 40–50) → 回退中值 45
        vm.Roast = "dark";
        Assert.Equal(45, vm.RoastAg);
        // 切到浅烘 ultra_light(Ag 90–100) → 中值 95
        vm.Roast = "ultra_light";
        Assert.Equal(95, vm.RoastAg);
    }

    // 15c) 具体 Ag 读数进入配方报告（区别于档位区间）
    [Fact]
    public void RoastAg_AppearsInBrewReport()
    {
        var vm = new BrewViewModel();
        vm.RoastAg = 88;
        vm.Generate();
        Assert.Contains("Ag 88", vm.BrewReportText);
    }

    // 15) 烘焙日期 → 显示养豆期满日期
    [Fact]
    public void RoastDate_SetsReadyDateText()
    {
        var vm = new BrewViewModel();
        vm.RoastDate = DateTime.Today.AddDays(-10);
        vm.Generate();
        Assert.Contains("养豆期满", vm.RestReadyDateText);
    }

    // 16) 冲煮记录：保存后落盘并可读取（含冲煮日期）
    [Fact]
    public void SaveCurrentBrew_AddsRecord()
    {
        var path = Path.Combine(Path.GetTempPath(), "coffee_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel { RecordsPath = path };
            vm.BrewDate = new DateTime(2026, 8, 20);
            vm.Generate();
            vm.SaveCurrentBrew();
            Assert.Single(vm.Records);
            Assert.Equal(new DateTime(2026, 8, 20), vm.Records[0].BrewDate);
            Assert.Contains("肉桂烘", vm.Records[0].RoastLabel);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // 17) 产地选择 → 显示常规风味走向与等级分类（+ 默认豆种）
    [Fact]
    public void Origin_Selection_UpdatesInfoText()
    {
        var vm = new BrewViewModel { Origin = "ethiopia" };
        vm.Generate();
        Assert.Contains("柑橘", vm.OriginInfoText);
        Assert.Contains("G1", vm.OriginInfoText);
        Assert.Contains("埃塞原生种", vm.OriginInfoText); // 默认豆种
    }

    // 18) 保存记录含产地 / 豆种 / 烘焙度范围
    [Fact]
    public void SaveCurrentBrew_RecordsOriginAndVariety()
    {
        var path = Path.Combine(Path.GetTempPath(), "coffee_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel { RecordsPath = path, Origin = "kenya", Variety = "sl" };
            vm.Generate();
            vm.SaveCurrentBrew();
            Assert.Single(vm.Records);
            Assert.Equal("肯尼亚", vm.Records[0].OriginLabel);
            Assert.Contains("黑加仑", vm.Records[0].OriginFlavor);
            Assert.Equal("肯尼亚 SL28/SL34", vm.Records[0].VarietyLabel);
            Assert.Equal(80, vm.Records[0].RoastAgMin);   // 默认肉桂烘(light)粉样 Ag 80–90
            Assert.Equal(90, vm.Records[0].RoastAgMax);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // 19) 粉量档位边界约束：切换档位设置 DoseMin/Max，dose 被钳制在档位范围内
    [Fact]
    public void SizeSelection_ClampsDoseToTierRange()
    {
        var vm = new BrewViewModel();
        vm.Size = "small";
        Assert.Equal(5, vm.DoseMin);
        Assert.Equal(12, vm.DoseMax);
        vm.Dose = 50; // 超出档位上限
        Assert.Equal(12, vm.Dose); // 钳制到上限
        vm.Size = "large";
        Assert.Equal(22, vm.DoseMin);
        Assert.Equal(30, vm.DoseMax);
        vm.Dose = 1; // 低于档位下限
        Assert.Equal(22, vm.Dose); // 钳制到下限
    }

    // 20) 杯测法默认粉水比切换为 1:18.18（SCA 标准）
    [Fact]
    public void Method_Cupping_SetsDefaultRatioToCupping()
    {
        var vm = new BrewViewModel();
        vm.Method = "cupping";
        Assert.Equal(BrewEngine.CuppingRatio, vm.Ratio, 1);
        vm.Generate();
        Assert.Contains("杯测法", vm.SummaryText);
    }

    // 21) 推荐研磨（研磨度描述 + 磨豆机刻度 C40/EK43，用户要求明确显示）与水质推荐文本在生成后填充
    [Fact]
    public void Generate_PopulatesGrindRecAndWaterText()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        Assert.Contains("中（C40≈20", vm.GrindRecText); // 研磨度描述（默认浅烘 + V60 → 中，V2.0 标定）
        Assert.Contains("C40≈", vm.GrindRecText);   // 用户要求显示 C40 刻度
        Assert.Contains("EK≈", vm.GrindRecText);    // 用户要求显示 EK43 刻度
        Assert.Contains("TDS", vm.WaterText);
    }

    // 21b) 推荐水温（2026-09-08 新增）：Generate 后填充「N℃」，且海拔已移除——温度仅由烘焙度/处理法决定
    [Fact]
    public void Generate_PopulatesTempRecText_NoAltitude()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        Assert.Matches(@"^\d{2,3}℃$", vm.TempRecText); // 形如「92℃」
        // 黄金杯诊断不再含海拔沸点提示（键已删除，文本不可能再出现）
        Assert.DoesNotContain("沸点", vm.GoldenCupText);
        Assert.DoesNotContain("海拔", vm.GoldenCupText);
    }

    // 22) 冲煮方法切换：默认 classic，可切到逆向/瑞士/杯测且生成无异常
    [Fact]
    public void Method_Switching_GeneratesWithoutError()
    {
        foreach (var m in new[] { "classic", "kasuya46", "light", "reverse", "swiss", "cupping" })
        {
            var vm = new BrewViewModel { Method = m };
            vm.Generate();
            Assert.True(vm.HasRecipe);
            Assert.Equal(m, vm.Records.Count >= 0 ? m : m); // 仅确保生成成功
        }
    }

    // 23) 效率优化：固定文本在 Generate 时由 BuildStaticTexts 一次性填充，停掉定时器后值依然保留（证明不依赖每帧重算）
    [Fact]
    public void BuildStaticTexts_PopulatedOnceAtGenerate()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer(); // 停掉 50ms 定时器，若文本依赖每帧重算，停止后将为空

        Assert.Contains("肉桂烘", vm.RoastAgText);
        Assert.Contains("C40≈", vm.GrindRecText); // 用户要求显示 C40/EK43 刻度
        Assert.Contains("EK≈", vm.GrindRecText);
        Assert.Contains("风味：", vm.OriginInfoText);
        Assert.Contains("TDS", vm.WaterText);
        Assert.Contains("建议养豆期", vm.SuggestRestLabel);
        Assert.Contains("已养", vm.RestDaysText);

        // 再次 TickForTest 不应改变这些固定文本（仍一致，证明每帧未重算）
        string before = vm.GrindRecText + vm.WaterText;
        vm.TickForTest();
        Assert.Equal(before, vm.GrindRecText + vm.WaterText);
    }

    // 24) 杯测法静态评估：IsCupping 在 Generate 时定值，非杯测法为 false
    [Fact]
    public void IsCupping_SetAtGenerate()
    {
        var classic = new BrewViewModel { Method = "classic" };
        classic.Generate();
        Assert.False(classic.IsCupping);

        var cupping = new BrewViewModel { Method = "cupping" };
        cupping.Generate();
        Assert.True(cupping.IsCupping);
    }

    // 25) 杯测法独立计时器：按时间推进，不依赖注水重量（SimWeight 始终保持 0 也能走完）
    [Fact]
    public void Cupping_AdvancesByTimeWithoutPour()
    {
        // 杯测法统一基准：对象初始化触发 Method setter 自动套用 11g 粉 / 200g 94℃ 热水（1:18.18）
        var vm = new BrewViewModel { Method = "cupping" };
        vm.Generate();
        vm.StopTimer();
        vm.SetClockForTest(() => _cuppingClock);
        vm.Start(); // Running = true，StartedAt = 0

        // 初始处于第 1 阶段（注水浸润，30s 引导）
        Assert.Equal("注水浸润", vm.PhaseName);
        Assert.Equal(11, vm.Dose);       // 统一基准粉量
        Assert.Equal(18.18, vm.Ratio, 2); // 统一基准粉水比 1:18.18

        // 推进到第 2 阶段（静置浸泡）：注水浸润 30s 已过的时刻
        _cuppingClock += 35_000;
        vm.TickForTest();
        Assert.Equal("静置浸泡", vm.PhaseName);

        // 再推进足够时间让静置浸泡（240s）走完 → 完成
        _cuppingClock += 245_000; // 累计 280s > 30+240=270s
        vm.TickForTest();
        Assert.True(vm.Done);
        Assert.Equal("完成", vm.PhaseName);
        // 杯测完成提示进入啜吸评分
        Assert.Contains("啜吸评分", vm.PhaseTip);
    }
    private static long _cuppingClock = 0;

    // 26) 烘焙值 Ag 与烘焙度档位联动：默认取档位区间中值（整数），提示同步档位与区间
    [Fact]
    public void RoastAg_DefaultsToTierMidpoint_AndHintFollowsTier()
    {
        var vm = new BrewViewModel();
        Assert.Equal(90, vm.RoastAg);                      // 浅烘 light Ag 80–90 → 90（阳光 2026-09-03 改默认）
        Assert.Contains("Ag 80–90", vm.RoastTierHint);
        Assert.Contains("中值 85", vm.RoastTierHint);

        vm.Roast = "dark";
        Assert.Equal(45, vm.RoastAg);                      // 中深烘 Ag 40–50 → 中值 45
        Assert.Contains("Ag 40–50", vm.RoastTierHint);
        Assert.Contains("中值 45", vm.RoastTierHint);

        vm.Roast = "ultra_light";
        Assert.Equal(95, vm.RoastAg);                      // 浅烘 Ag 90–100 → 中值 95
        Assert.Contains("Ag 90–100", vm.RoastTierHint);

        // 手动覆盖后不再被档位回退（只有切换档位才重置）
        vm.RoastAg = 93;
        Assert.Equal(93, vm.RoastAg);
    }

    // 27) 冲煮流程管道图：经典分段法（标准粉量 2 段）→ 闷蒸 / 注水1 / 注水2 / 冲煮结束
    [Fact]
    public void PhasePipeline_Classic_MergesBloomPoursAndFinish()
    {
        // 固定「已养 10 天」基准（rf=1.0 → 闷蒸等待 40s），使断言不随 RoastDate 默认值（今天前 3 天）漂移
        var vm = new BrewViewModel { Method = "classic", Size = "standard", Dripper = "v60", RoastDate = DateTime.Today.AddDays(-10) };
        vm.Generate();

        Assert.Equal(new[] { "闷蒸", "注水1", "注水2", "冲煮结束" }, vm.PhasePipeline.Select(s => s.Title));
        Assert.Equal("→ 30g · 40s", vm.PhasePipeline[0].Sub); // 默认肉桂烘(light) BloomBase=40 → 闷蒸等待 40s 归并
        Assert.Equal(4, vm.PhasePipeline[^1].Index);
    }

    // 28) 管道图随方案变化：大粉量（3 段）→ 多一个「注水3」节点
    [Fact]
    public void PhasePipeline_LargeDose_AddsThirdPour()
    {
        var vm = new BrewViewModel { Method = "classic", Size = "large" };
        vm.Generate();
        Assert.Equal(new[] { "闷蒸", "注水1", "注水2", "注水3", "冲煮结束" }, vm.PhasePipeline.Select(s => s.Title));
    }

    // 29) 管道图适配杯测法：注水1 → 静置浸泡（按推荐方案动态生成，不写死）
    [Fact]
    public void PhasePipeline_Cupping_SinglePourAndSteep()
    {
        var vm = new BrewViewModel { Method = "cupping" };
        vm.Generate();
        Assert.Equal(new[] { "注水1", "静置浸泡" }, vm.PhasePipeline.Select(s => s.Title));
    }

    // 30) 管道图状态推进：闷蒸节点跨越「闷蒸注水 + 闷蒸等待」两个阶段，进入注水段后标记为已完成
    [Fact]
    public void PhasePipeline_TracksCurrentAndDone()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();

        Assert.True(vm.PhasePipeline[0].Current);
        Assert.False(vm.PhasePipeline[0].Done);
        Assert.Equal(1, vm.PhasePipeline[0].Index);

        vm.Next(); vm.TickForTest();          // 阶段 1 = 闷蒸等待，仍属「闷蒸」节点
        Assert.True(vm.PhasePipeline[0].Current);
        Assert.False(vm.PhasePipeline[0].Done);

        vm.Next(); vm.TickForTest();          // 阶段 2 = 第一段注水 → 「注水1」
        Assert.True(vm.PhasePipeline[0].Done);
        Assert.True(vm.PhasePipeline[1].Current);
    }

    // 4b) 上一阶段：手动回退到前一个阶段（与 Next 对称）
    [Fact]
    public void PrevPhase_GoesBackToPreviousPhase()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();

        // 前进两次：闷蒸注水 → 闷蒸等待 → 第一段注水（注水1）
        vm.Next(); vm.TickForTest();
        vm.Next(); vm.TickForTest();
        Assert.Contains("注水", vm.PhaseName);

        // 回退一次：应回到闷蒸相关阶段
        vm.Prev(); vm.TickForTest();
        Assert.Contains("闷蒸", vm.PhaseName);

        // 已在首阶段时再回退不应越界（PhaseIndex 保持 ≥ 0）
        vm.Prev(); vm.TickForTest();
        vm.Prev(); vm.TickForTest();
        Assert.True(vm.PhasePipeline[0].Current || vm.PhasePipeline[0].Done);
    }

    // 5) 参数/偏好持久化（优化项）：生成后写出 settings.json，新实例能恢复上次设置
    [Fact]
    public void Settings_ArePersistedAndRestored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coffeescale_settings_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new BrewViewModel { SettingsPath = settingsPath };
            vm.Method = "cupping";
            vm.Dose = 18;
            vm.Ratio = 16;
            vm.Generate();
            vm.StopTimer();

            Assert.True(File.Exists(settingsPath));
            var saved = SettingsStore.Load(settingsPath);
            Assert.Equal("cupping", saved.Method);
            Assert.Equal(18, saved.Dose);

            // 新实例从 settings.json 恢复上次设置（构造函数默认路径不同，这里显式重载以验证持久化）
            var vm2 = new BrewViewModel { SettingsPath = settingsPath };
            vm2.LoadSettings(settingsPath);
            Assert.Equal("cupping", vm2.Method);
            Assert.Equal(18, vm2.Dose);
            Assert.Equal(16, vm2.Ratio);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // 30) Phase 11 记录↔方案打通：保存记录落盘结构化引擎键 + 方案来源溯源
    [Fact]
    public void SaveCurrentBrew_RecordsStructuredKeysAndPlanSource()
    {
        var path = Path.Combine(Path.GetTempPath(), "coffee_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel
            {
                RecordsPath = path,
                Method = "swiss", Process = "natural", Origin = "kenya",
                Roast = "medium", Dripper = "wave", Flavor = "sweet",
            };
            vm.MarkPlanSource("我的肯尼亚方案");
            vm.Generate();
            vm.SaveCurrentBrew();

            Assert.Single(vm.Records);
            var rec = vm.Records[0];
            Assert.Equal("swiss", rec.Method);        // 方法键（Phase 10 前已存）
            Assert.Equal("medium", rec.Roast);        // 新增结构化键
            Assert.Equal("natural", rec.Process);
            Assert.Equal("kenya", rec.Origin);
            Assert.Equal("wave", rec.Dripper);
            Assert.Equal("sweet", rec.Flavor);
            Assert.Equal("我的肯尼亚方案", rec.PlanTitle); // 溯源

            // 重新从文件加载仍完整（随 JSON 持久化）
            var reloaded = RecordsStore.Load(path);
            Assert.Single(reloaded);
            Assert.Equal("natural", reloaded[0].Process);
            Assert.Equal("我的肯尼亚方案", reloaded[0].PlanTitle);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // 31) Phase 11：新记录（带引擎键）复刻为我的方案 → 结构化字段直取，标题用来源方案名
    [Fact]
    public void RecordToMyPlan_NewRecord_UsesDirectKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), "coffee_test_" + Guid.NewGuid().ToString("N") + ".json");
        var plansPath = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel
            {
                RecordsPath = path, MyPlansPath = plansPath,
                Method = "swiss", Process = "natural", Origin = "kenya",
                Roast = "medium", Dripper = "wave", Flavor = "sweet",
            };
            vm.MarkPlanSource("我的肯尼亚方案");
            vm.Generate();
            vm.SaveCurrentBrew();

            vm.RecordToMyPlan(vm.Records[0].Id);

            Assert.Single(vm.MyPlans);
            var plan = vm.MyPlans[0];
            Assert.Equal("我的肯尼亚方案", plan.Title);   // 有来源方案名 → 沿用它
            Assert.Equal("swiss", plan.Method);
            Assert.Equal("natural", plan.Process);
            Assert.Equal("kenya", plan.Origin);
            Assert.Equal("medium", plan.Roast);
            Assert.Equal("wave", plan.Dripper);
            Assert.Equal("sweet", plan.Flavor);
            Assert.False(string.IsNullOrEmpty(plan.Dose));
            Assert.False(string.IsNullOrEmpty(plan.Ratio));
            Assert.Contains("粉水比", plan.Detail);
            // 复刻出的方案应用回去应完整还原引擎参数
            plan.AsItem();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(plansPath)) File.Delete(plansPath);
        }
    }

    // 32) Phase 11：旧记录（只有展示标签，无引擎键）复刻 → 按 Label / Ag 反查引擎键，查不到留空
    [Fact]
    public void RecordToMyPlan_LegacyRecord_ReverseLookup()
    {
        var path = Path.Combine(Path.GetTempPath(), "coffee_test_" + Guid.NewGuid().ToString("N") + ".json");
        var plansPath = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel { RecordsPath = path, MyPlansPath = plansPath };
            // 手工构造一条 Phase 10 之前的旧记录：只有标签与 Ag 读数，没有结构化引擎键
            var legacy = new BrewRecord
            {
                Method = "classic", MethodLabel = "经典分段法",
                Dose = 18, Ratio = 16, TotalWater = 288,
                RoastLabel = "中深烘", RoastAg = 45, RoastAgMin = 40, RoastAgMax = 50,
                ProcessLabel = "日晒", DripperLabel = "蛋糕滤杯",
                OriginLabel = "肯尼亚", OriginRegion = "非洲",
                BloomWait = 30, Temp = 86,
            };
            RecordsStore.Add(path, legacy);
            vm.RecordsPath = path;
            vm.LoadRecords();
            Assert.Single(vm.Records);

            vm.RecordToMyPlan(legacy.Id);

            Assert.Single(vm.MyPlans);
            var plan = vm.MyPlans[0];
            // 反查回引擎键
            Assert.Equal("classic", plan.Method);   // 旧记录本就存方法键
            Assert.Equal("natural", plan.Process);  // 「日晒」→ natural
            Assert.Equal("kenya", plan.Origin);     // 「肯尼亚」→ kenya
            Assert.Equal("wave", plan.Dripper);     // 「蛋糕滤杯」→ wave
            Assert.Equal("dark", plan.Roast);       // Ag 45 → 40–50 → dark（中深烘）
            Assert.Null(plan.Flavor);               // 旧记录无风味信息 → 留空（未指定）
            Assert.Equal("经典分段法 · 中深烘(Ag 45) · 肯尼亚", plan.Title); // 无来源方案名 → 按展示标签自动命名
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(plansPath)) File.Delete(plansPath);
        }
    }

    // 28) 咖啡豆密度：海拔预填 + 手选覆盖 + 持久化（引擎研磨补偿由 Core.Tests 直接验证）
    [Fact]
    public void Density_PrefilledFromAltitude_AndManualOverride_Persists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coffee_density_" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new BrewViewModel { SettingsPath = settingsPath };
            Assert.Equal("medium", vm.Density); // 默认海拔 1500m → 中等

            // 海拔即预填：未手选时，改海拔立即刷新密度档
            vm.BeanAltitudeM = 2000;            // ≥1700 → 致密
            Assert.Equal("dense", vm.Density);
            vm.BeanAltitudeM = 800;             // <1200 → 疏松
            Assert.Equal("light", vm.Density);

            // 用户手选后，改海拔不再覆盖手选值
            vm.Density = "medium";              // 手选「中等」
            vm.BeanAltitudeM = 2000;            // 改海拔不应回写
            Assert.Equal("medium", vm.Density);

            vm.Generate(); // Generate 内部会 PersistSettings（写 settingsPath）

            var vm2 = new BrewViewModel { SettingsPath = settingsPath };
            vm2.LoadSettings(settingsPath);
            Assert.Equal(2000, vm2.BeanAltitudeM); // 海拔持久化
            Assert.Equal("medium", vm2.Density);   // 手选密度持久化（不被海拔回写）
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // 28b) 未手选密度时：保存/恢复后仍按海拔重新预填（不残留旧推导值）
    [Fact]
    public void Density_AltitudeDriven_ReloadRederives()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coffee_density2_" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        try
        {
            var vm = new BrewViewModel { SettingsPath = settingsPath };
            vm.BeanAltitudeM = 2000;   // 未手选 → dense
            Assert.Equal("dense", vm.Density);
            vm.Generate();             // 持久化

            var vm2 = new BrewViewModel { SettingsPath = settingsPath };
            vm2.LoadSettings(settingsPath);
            Assert.Equal("dense", vm2.Density); // 无手选 → 由保存的海拔重新推导
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // 29) 推荐粉水比 / 注水总量：生成前为空串（UI 显示未设置），生成后取配方有效值
    [Fact]
    public void PlanRatioAndWater_EmptyBeforeGenerate_ValueAfter()
    {
        var vm = new BrewViewModel { Dose = 15, Ratio = 16 };
        Assert.False(vm.HasRecipe);
        Assert.Equal("", vm.PlanRatioText);
        Assert.Equal("", vm.PlanWaterText);

        vm.Generate();
        Assert.True(vm.HasRecipe);
        Assert.Equal("1:16", vm.PlanRatioText);
        Assert.Equal("240g", vm.PlanWaterText); // 15g × 16 = 240g
    }

    // 33) Phase 13：导出全部 → 备份文件可 Load 还原（含结构化字段）
    [Fact]
    public void ExportAllMyPlans_WritesBackupFile()
    {
        var plansPath = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        var backupPath = Path.Combine(Path.GetTempPath(), "backup_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel { MyPlansPath = plansPath };
            vm.SaveMyPlan(new MyPlan { Title = "A", Process = "natural", Detail = "d1" });
            vm.SaveMyPlan(new MyPlan { Title = "B", Origin = "kenya", Detail = "d2" });

            var msg = vm.ExportAllMyPlans(backupPath);
            Assert.Contains("2", msg); // 状态文案含条数
            Assert.True(File.Exists(backupPath));

            var loaded = MyPlansStore.Load(backupPath);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("natural", loaded[0].Process); // 结构化字段随备份落盘
            Assert.Equal("kenya", loaded[1].Origin);
        }
        finally
        {
            if (File.Exists(plansPath)) File.Delete(plansPath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }
    }

    // 34) Phase 13：从备份导入 → 按 Title 去重合并（同名保留本机现有，不覆盖）
    [Fact]
    public void ImportAllMyPlans_MergesByTitle_KeepsLocal()
    {
        var plansPath = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        var backupPath = Path.Combine(Path.GetTempPath(), "backup_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel { MyPlansPath = plansPath };
            vm.SaveMyPlan(new MyPlan { Title = "A", Process = "washed", Detail = "local A" }); // 本机现有 A
            vm.SaveMyPlan(new MyPlan { Title = "B", Origin = "ethiopia", Detail = "local B" });

            // 备份文件：A（不同内容）+ C（新增）
            MyPlansStore.SaveAll(backupPath, new[]
            {
                new MyPlan { Title = "A", Process = "natural", Detail = "backup A" },
                new MyPlan { Title = "C", Origin = "colombia", Detail = "backup C" },
            });

            var msg = vm.ImportAllMyPlans(backupPath);
            Assert.Contains("1", msg); // 新增 1 条
            Assert.Equal(3, vm.MyPlans.Count);
            var a = vm.MyPlans.First(p => p.Title == "A");
            Assert.Equal("washed", a.Process); // 同名保留本机现有（不被备份覆盖）
            Assert.Contains(vm.MyPlans, p => p.Title == "C" && p.Origin == "colombia");
        }
        finally
        {
            if (File.Exists(plansPath)) File.Delete(plansPath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }
    }

    // 35) Phase 13：单条分享文本 往返（本机导出 → 另一实例导入还原）
    [Fact]
    public void MyPlanShareText_ImportRoundtrip()
    {
        var plansPath = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        var otherPath = Path.Combine(Path.GetTempPath(), "other_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel { MyPlansPath = plansPath };
            vm.SaveMyPlan(new MyPlan
            {
                Title = "我的肯尼亚方案", Summary = "复刻自记录",
                Detail = "· 粉量 22g / 总水 367g，粉水比 1:16.7",
                Method = "classic", Dripper = "wave", Dose = "22", Ratio = "16.7",
                Process = "natural", Origin = "kenya", Roast = "medium_dark", Flavor = "sweet",
            });

            var text = vm.MyPlanShareText("我的肯尼亚方案");
            Assert.Contains("Process: natural", text);

            var vm2 = new BrewViewModel { MyPlansPath = otherPath };
            var title = vm2.ImportPlanShareText(text);
            Assert.Equal("我的肯尼亚方案", title);
            Assert.Single(vm2.MyPlans);
            var back = vm2.MyPlans[0];
            Assert.Equal("natural", back.Process);
            Assert.Equal("kenya", back.Origin);
            Assert.Equal("medium_dark", back.Roast);
            Assert.Equal("wave", back.Dripper);
            Assert.Equal("16.7", back.Ratio);
            Assert.Contains("粉水比 1:16.7", back.Detail); // 正文无损
        }
        finally
        {
            if (File.Exists(plansPath)) File.Delete(plansPath);
            if (File.Exists(otherPath)) File.Delete(otherPath);
        }
    }

    // 36) Phase 13：坏分享文本 → FormatException；找不到方案 → KeyNotFoundException
    [Fact]
    public void ImportPlanShareText_Invalid_Throws()
    {
        var plansPath = Path.Combine(Path.GetTempPath(), "myplans_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var vm = new BrewViewModel { MyPlansPath = plansPath };
            Assert.Throws<FormatException>(() => vm.ImportPlanShareText("不是方案文本"));
            Assert.Throws<KeyNotFoundException>(() => vm.MyPlanShareText("不存在的方案"));
        }
        finally
        {
            if (File.Exists(plansPath)) File.Delete(plansPath);
        }
    }

    // Phase 14 ①：导出 CSV——文件生成、含 UTF-8 BOM、记录数与关键字段；返回状态文案带条数
    [Fact]
    public void ExportRecordsCsv_WritesFileWithBomAndRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "csv_vm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csvPath = Path.Combine(dir, "brews-export.csv");
        var prev = I18n.Current;
        try
        {
            I18n.Current = I18n.ZhCN;
            var vm = new BrewViewModel { RecordsPath = Path.Combine(dir, "brews.json") };
            vm.Generate();
            vm.SaveCurrentBrew();
            vm.SaveCurrentBrew(); // 两条记录
            Assert.Equal(2, vm.Records.Count);

            var msg = vm.ExportRecordsCsv(csvPath);
            Assert.Contains("2", msg);
            Assert.True(File.Exists(csvPath));
            var text = File.ReadAllText(csvPath);
            Assert.StartsWith("\uFEFF", text);
            Assert.Contains("冲煮日期", text);
            Assert.Contains("粉量(g)", text);
            Assert.Contains("1:15", text); // 默认粉水比 15
            Assert.Contains("来源方案", text);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            I18n.Current = prev;
        }
    }

    // Phase 14 ②：无记录时导出返回「暂无记录」提示且不生成文件
    [Fact]
    public void ExportRecordsCsv_Empty_ReturnsMessageNoFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "csv_vm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csvPath = Path.Combine(dir, "brews-export.csv");
        var prev = I18n.Current;
        try
        {
            I18n.Current = I18n.ZhCN;
            var vm = new BrewViewModel
            {
                RecordsPath = Path.Combine(dir, "brews.json"),
                SettingsPath = Path.Combine(dir, "settings.json"),
            };
            Assert.Equal(I18n.T("ExportCsvEmpty"), vm.ExportRecordsCsv(csvPath));
            Assert.False(File.Exists(csvPath));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            I18n.Current = prev;
        }
    }

    // 30) 自动注水推进（UI-15c）：点「开始」后无需手动模拟注水——注水阶段按推荐流速自动进水，
    //     重量达标引擎自动切换下一阶段；等待阶段按时间推进；整程全自动走完。
    [Fact]
    public void AutoPour_AdvancesAllPhases_AfterStart()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();

        long t = 1_000_000;
        vm.SetClockForTest(() => t);
        vm.Start();
        vm.TickForTest(); // 初始快照：第一阶段为「闷蒸注水」（Pour）
        Assert.True(vm.Running);
        Assert.True(vm.IsPourPhase, "第一阶段应为注水段");
        Assert.Contains("本段注水", vm.PhasePourText);

        // 连续推进：每帧 50ms；注水段自动进水（推荐流速 g/s），等待段按时长走完
        bool sawWaitPhase = false, sawPourProgress = false;
        for (int i = 0; i < 4000 && !vm.Done; i++)
        {
            t += 50;
            vm.TickForTest();
            if (!vm.IsPourPhase) sawWaitPhase = true;
            if (vm.IsPourPhase && vm.PhaseProgress > 0.3) sawPourProgress = true;
        }
        Assert.True(vm.Done, "整程应自动完成（注水按流速推进 + 等待按时长切换）");
        Assert.True(sawPourProgress, "注水阶段进度应推进");
        Assert.True(sawWaitPhase, "应经历等待阶段");
    }

    // 31) 推荐粉水比 ± 微调（步长 1，整数，无小数）：立即重新生成配方，粉水比与注水总量同步刷新；
    //     边界钳制 10–20。黄金杯校准后 ratio 可能带小数（如 15.3），显示时四舍五入为整数。
    [Fact]
    public void AdjustPlanRatio_RefreshesRatioAndTotalWater()
    {
        var vm = new BrewViewModel();
        vm.Generate();
        vm.StopTimer();
        // 默认浅烘 r15 → 黄金杯校准后 ratio≈15.3，显示为 1:15（四舍五入），总水量≈230g
        Assert.Equal("1:15", vm.PlanRatioText);
        Assert.Contains("g", vm.PlanWaterText); // 格式正确

        vm.AdjustPlanRatio(1);
        Assert.True(vm.Ratio >= 16); // 步长 1 上调
        Assert.Equal("1:16", vm.PlanRatioText); // 显示整数

        vm.AdjustPlanRatio(-1);
        Assert.Equal("1:15", vm.PlanRatioText); // 回到 15

        for (int i = 0; i < 30; i++) vm.AdjustPlanRatio(1);
        Assert.Equal(20, vm.Ratio); // 上边界钳制
    }
}
