using System;
using CoffeeScale.Core;
using Xunit;

namespace CoffeeScale.Core.Tests;

public class BrewEngineTests
{
    // ---------- 粉量档位推断 ----------
    [Theory]
    [InlineData(10, "small")]
    [InlineData(12, "small")]
    [InlineData(15, "standard")]
    [InlineData(22, "standard")]
    [InlineData(25, "large")]
    [InlineData(40, "large")]
    public void DetectSize_Boundaries(double dose, string expected)
        => Assert.Equal(expected, BrewEngine.DetectSize(dose));

    [Fact]
    public void SizeProfiles_Parameters()
    {
        Assert.Equal(2.0, BrewEngine.SizeProfiles["small"].BloomMul);
        Assert.Equal(2.0, BrewEngine.SizeProfiles["standard"].BloomMul);
        Assert.Equal(2.5, BrewEngine.SizeProfiles["large"].BloomMul);
        Assert.Equal(2, BrewEngine.SizeProfiles["standard"].Segments);
        Assert.Equal(3, BrewEngine.SizeProfiles["large"].Segments);
        Assert.Equal(40, BrewEngine.SizeProfiles["standard"].DrawWait);
        Assert.Equal(55, BrewEngine.SizeProfiles["large"].DrawWait);
    }

    [Fact]
    public void SizeProfiles_DefaultDoseValues()
    {
        // 粉量档位默认值：少粉量 8g / 标准 15g / 大粉量 25g
        Assert.Equal(8, BrewEngine.SizeProfiles["small"].DefaultDose);
        Assert.Equal(15, BrewEngine.SizeProfiles["standard"].DefaultDose);
        Assert.Equal(25, BrewEngine.SizeProfiles["large"].DefaultDose);
    }

    [Fact]
    public void ExplicitSize_BeatsAutoDetect()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 10, Ratio = 15, Size = "large" });
        Assert.Equal("large", r.Size);
        Assert.Equal("大粉量", r.SizeLabel);
    }

    // ---------- 养豆期排气系数 ----------
    [Theory]
    [InlineData(1, 1.8)]
    [InlineData(3, 1.5)]
    [InlineData(7, 1.2)]
    [InlineData(14, 1.0)]
    [InlineData(21, 0.85)]
    [InlineData(30, 0.7)]
    [InlineData(40, 0.55)]
    [InlineData(120, 0.55)]
    public void RestFactor_Values(double days, double expected)
        => Assert.Equal(expected, BrewEngine.RestFactor(days));

    // ---------- 闷蒸时长三维驱动 ----------
    [Fact]
    public void BloomWait_Medium_Standard_Rest10_Balanced_Is30()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Roast = "medium", Flavor = "balanced", RestDays = 10 });
        Assert.Equal(30, r.BloomWait);          // 30 * 1.0 * 1.0
        Assert.Equal(90, r.Temp);               // 90 + 0
    }

    [Fact]
    public void BloomWait_Light_Rest1_Sweet_Clamped75()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Roast = "light", Flavor = "sweet", RestDays = 1 });
        Assert.Equal(75, r.BloomWait);          // clamp(42*1.8*1.2=90.72 → 75)
    }

    [Fact]
    public void BloomWait_ByRoastBase()
    {
        int Light() => BrewEngine.ComputeRecipe(new() { Roast = "light", RestDays = 14 }).BloomWait;   // 42*1.0
        int Med() => BrewEngine.ComputeRecipe(new() { Roast = "medium", RestDays = 14 }).BloomWait;     // 30*1.0
        int Dark() => BrewEngine.ComputeRecipe(new() { Roast = "dark", RestDays = 14 }).BloomWait;       // 18*1.0
        Assert.Equal(40, Light());
        Assert.Equal(30, Med());
        Assert.Equal(18, Dark());
    }

    [Fact]
    public void BloomWait_ByFlavor()
    {
        int Sweet() => BrewEngine.ComputeRecipe(new() { Roast = "medium", Flavor = "sweet", RestDays = 14 }).BloomWait;   // 30*1.2=36
        int Acid() => BrewEngine.ComputeRecipe(new() { Roast = "medium", Flavor = "acidic", RestDays = 14 }).BloomWait;  // 30*0.9=27
        Assert.Equal(36, Sweet());
        Assert.Equal(27, Acid());
    }

    [Fact]
    public void Temp_ByRoastAndFlavor()
    {
        Assert.Equal(93, BrewEngine.ComputeRecipe(new() { Roast = "light", Flavor = "balanced" }).Temp);
        Assert.Equal(90, BrewEngine.ComputeRecipe(new() { Roast = "medium", Flavor = "balanced" }).Temp);
        Assert.Equal(86, BrewEngine.ComputeRecipe(new() { Roast = "dark", Flavor = "balanced" }).Temp);
        Assert.Equal(94, BrewEngine.ComputeRecipe(new() { Roast = "light", Flavor = "acidic" }).Temp);   // 93+1
        Assert.Equal(92, BrewEngine.ComputeRecipe(new() { Roast = "light", Flavor = "sweet" }).Temp);     // 93-1
    }

    // ---------- 养豆期推荐四态 ----------
    [Fact]
    public void RecommendRest_States()
    {
        Assert.Equal("偏新鲜（排气中）", BrewEngine.RecommendRest("medium", 3).Status);     // < 7
        Assert.Equal("✅ 最佳赏味窗口", BrewEngine.RecommendRest("medium", 10).Status);     // 7..14
        Assert.Equal("风味渐退", BrewEngine.RecommendRest("medium", 20).Status);            // 15..21 (hi+14=28)
        Assert.Equal("⚠ 已过赏味期", BrewEngine.RecommendRest("medium", 40).Status);        // > 28
    }

    // ---------- 配方计算 ----------
    [Fact]
    public void ComputeRecipe_Classic_Structure()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Method = "classic" });
        Assert.Equal("classic", r.Method);
        // 黄金杯自动校准：浅烘水洗 1:15 → TDS 1.47 偏高 → 粉水比自动放大到 1:15.3（总水量 229.5）
        Assert.Equal(15.3, r.Ratio, 5);
        Assert.Equal(229.5, r.TotalWater, 5);                                     // 15*15.3（校准后）
        Assert.Equal(6, r.Phases.Count);                                          // 闷蒸注水+等待 + 2段(第2段带等待) + 滴滤
        Assert.Equal(PhaseType.Pour, r.Phases[0].Type);
        Assert.Equal(PhaseType.Wait, r.Phases[1].Type);
        Assert.Equal(30, r.Phases[0].Target);                                     // 15*2.0（闷蒸不随 ratio 变）
        Assert.Equal(129.8, r.Phases[2].Target, 1);                               // 第1段终点（校准后总量 229.5）
        Assert.Equal(229.5, r.Phases[4].Target, 1);                               // 第2段终点 = 总量
    }

    [Fact]
    public void ComputeRecipe_Kasuya46_Structure()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 20, Ratio = 15, Method = "kasuya46" });
        Assert.Equal(10, r.Phases.Count);                                         // 5注水 + 4等待 + 1滴滤
        // 黄金杯自动校准：浅烘 1:15 → TDS 偏高 → 粉水比 1:15.6（总水量 312）
        Assert.Equal(15.6, r.Ratio, 5);
        Assert.Equal(312.0, r.TotalWater, 1);
        Assert.Equal(62.4, r.Phases[0].Target, 1);                                // 第1段 = step=62.4
        Assert.Equal(312.0, r.Phases[8].Target, 1);                               // 第5段注水 = 总量 312
    }

    [Fact]
    public void ComputeRecipe_Light_Structure()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Method = "light" });
        Assert.Equal(8, r.Phases.Count);
        Assert.Equal(45, r.Phases[0].Target);                                     // dose15 标准档 bloomMul2.0 → 15*(2.0+1)=45
    }

    // ---------- 状态机完整推进 ----------
    [Fact]
    public void StateMachine_Classic_FullAdvance()
    {
        var recipe = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Method = "classic", Roast = "dark" });
        // 黄金杯自动校准（深烘）：研磨 粗→中、粉水比 1:15→1:15.8 → 总水量 237
        Assert.Equal(237.0, recipe.TotalWater, 1);
        var st = BrewEngine.CreateState(recipe);
        BrewEngine.Start(st, 0);
        // 闷蒸注水：瞬时到 30g
        var snap = BrewEngine.Tick(st, 0, 30);
        Assert.Equal(PhaseType.Wait, snap.Phase!.Type);                           // 进入闷蒸等待
        // 闷蒸等待 18s（深烘），tick 30s 必然推进
        snap = BrewEngine.Tick(st, 30000, 30);
        Assert.Equal("第1段注水", snap.Phase!.Name);
        // 第1段注水到 133.5（校准后第1段终点）
        snap = BrewEngine.Tick(st, 30000, 133.5);
        Assert.Equal("等待下降", snap.Phase!.Name);
        // 等待 8s
        snap = BrewEngine.Tick(st, 38000, 133.5);
        Assert.Equal("第2段注水", snap.Phase!.Name);
        // 第2段注水到 237 → 滴滤收尾
        snap = BrewEngine.Tick(st, 38000, 237);
        Assert.Equal("滴滤收尾", snap.Phase!.Name);
        // 滴滤 40s 后完成
        snap = BrewEngine.Tick(st, 78000, 237);
        Assert.True(snap.Done);
        Assert.Equal(recipe.Phases.Count, snap.TotalPhases);
    }

    [Fact]
    public void StateMachine_Kasuya46_WaitShrinksWithRest()
    {
        int firstWaitFresh() => BrewEngine.ComputeRecipe(new() { Method = "kasuya46", RestDays = 1 }).Phases[1].DurationSec;
        int firstWaitRested() => BrewEngine.ComputeRecipe(new() { Method = "kasuya46", RestDays = 30 }).Phases[1].DurationSec;
        Assert.True(firstWaitFresh() > firstWaitRested());                       // 新鲜排气多→等待长
    }

    // ---------- 流量警告 ----------
    [Fact]
    public void Flow_Warning_WhenTooFast()
    {
        var recipe = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Method = "classic", Roast = "dark" });
        var st = BrewEngine.CreateState(recipe);
        BrewEngine.Start(st, 0);
        BrewEngine.Tick(st, 0, 30);                                              // 进入闷蒸等待
        BrewEngine.Tick(st, 30000, 30);                                          // 进入第1段注水（POUR，目标 127.5g）
        var snap = BrewEngine.Tick(st, 30100, 80);                               // 100ms 内 30→80g ≈ 500 g/s，仍在本段
        Assert.Contains(snap.Warnings, w => w.Contains("注水偏快"));
    }

    // ---------- 默认值回退 ----------
    [Fact]
    public void ComputeRecipe_Defaults()
    {
        var r = BrewEngine.ComputeRecipe(null);
        Assert.Equal(15, r.Dose);
        Assert.Equal(15.3, r.Ratio, 5);           // 黄金杯自动校准：默认浅烘 1:15 → TDS 偏高 → 自动放大到 1:15.3
        Assert.Equal("classic", r.Method);
        Assert.Equal("light", r.Roast); // 默认回退为浅烘（需求：默认浅烘）
        Assert.Equal("balanced", r.Flavor);
    }

    // ---------- 新增维度：处理方式 / 滤杯 / 滤纸 / 研磨度 ----------
    [Fact]
    public void NewDimensions_DefaultsAreNeutral()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15 });
        Assert.Equal("washed", r.Process);
        Assert.Equal("水洗", r.ProcessLabel);
        Assert.Equal("v60", r.Dripper);
        Assert.Equal("V60 锥形滤杯", r.DripperLabel);
        Assert.Equal("bleached", r.Filter);
        Assert.Equal("漂白滤纸", r.FilterLabel);
        Assert.Equal("medium", r.Grind);      // 默认浅烘 + v60 → 推荐中（V2.0 标定 v60 基准为中粗，浅烘再细一档）
        Assert.Equal("中", r.GrindLabel);
        Assert.False(r.NeedRinse);
        Assert.Equal(5.0, r.RecommendedFlow, 0.01); // v60 5 + 中(0) + 漂白 0
    }

    [Fact]
    public void Immersion_Dripper_Smart_UsesSteepPhase()
    {
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Dripper = "smart" });
        Assert.Contains(r.Phases, p => p.Name == "浸泡萃取");
        Assert.Equal("聪明杯（浸泡）", r.DripperLabel);
    }

    [Fact]
    public void Process_Natural_LowersTemp()
    {
        int nat = BrewEngine.ComputeRecipe(new() { Roast = "medium", Flavor = "balanced", RestDays = 14, Process = "natural" }).Temp;
        int wash = BrewEngine.ComputeRecipe(new() { Roast = "medium", Flavor = "balanced", RestDays = 14, Process = "washed" }).Temp;
        Assert.Equal(wash - 1, nat); // 日晒 TempAdj -1
    }

    [Fact]
    public void Grind_Fine_LowersRecommendedFlow()
    {
        double fine = BrewEngine.ComputeRecipe(new() { Grind = "fine" }).RecommendedFlow;
        double coarse = BrewEngine.ComputeRecipe(new() { Grind = "coarse" }).RecommendedFlow;
        Assert.True(fine < coarse);
        Assert.Equal(3.0, fine, 3);   // 5 + (-2.5) = 2.5 → clamp 下限 3
        Assert.Equal(7.5, coarse, 3); // 5 + 2.5 = 7.5
    }

    [Fact]
    public void Filter_Metal_RaisesFlowAndNeedsRinse()
    {
        var rM = BrewEngine.ComputeRecipe(new() { Filter = "metal" });
        var rB = BrewEngine.ComputeRecipe(new() { Filter = "bleached" });
        Assert.True(rM.NeedRinse);
        Assert.True(rM.RecommendedFlow > rB.RecommendedFlow); // 金属滤网流速高于漂白滤纸
    }

    // ---------- 烘焙度 10 级 + Ag 值 ----------
    [Fact]
    public void RoastProfiles_TenLevelsWithAg()
    {
        Assert.Equal(10, BrewEngine.RoastProfiles.Count);
        // 采用 Agtron 粉样(Ground)标尺（阳光 2026-09-01 确认）：数值越高越浅烘
        Assert.Equal(90, BrewEngine.RoastProfiles["ultra_light"].AgMin);  // 极浅烘 SCAA 粉样 90–100
        Assert.Equal(100, BrewEngine.RoastProfiles["ultra_light"].AgMax);
        Assert.Equal(80, BrewEngine.RoastProfiles["light"].AgMin);      // 浅烘（默认）粉样 80–90
        Assert.Equal(90, BrewEngine.RoastProfiles["light"].AgMax);
        Assert.Equal(20, BrewEngine.RoastProfiles["extreme_dark"].AgMin); // 极深烘 SCAA 粉样 20–30
        Assert.Equal(30, BrewEngine.RoastProfiles["extreme_dark"].AgMax);
        Assert.Equal("light", BrewEngine.ComputeRecipe(null).Roast);   // 默认回退浅烘
    }

    // ---------- 烘焙度 Ag 区间连续且不越界（粉样(Ground)标尺校验，10 级 10–120）----------
    [Fact]
    public void RoastProfiles_AgRangesMonotonicAndWithinStandard()
    {
        int prevMax = 120;
        foreach (var id in new[] { "blonde", "super_light", "ultra_light", "light", "medium_light", "medium", "medium_dark", "dark", "ultra_dark", "extreme_dark" })
        {
            var p = BrewEngine.RoastProfiles[id];
            Assert.True(p.AgMin < p.AgMax, $"{id} 区间应 AgMin<AgMax");
            Assert.True(p.AgMin >= 0 && p.AgMax <= 120, $"{id} 应在 0–120 粉样标尺内");
            Assert.True(p.AgMin <= prevMax, $"{id} 浅→深应连续递降");
            prevMax = p.AgMin;
        }
    }

    // ---------- 研磨度推荐（非选择项） ----------
    [Fact]
    public void RecommendGrind_LightFinerThanDark()
    {
        string light = BrewEngine.RecommendGrind("v60", "light", "classic");
        string dark = BrewEngine.RecommendGrind("v60", "dark", "classic");
        var order = new[] { "coarse", "medium_coarse", "medium", "medium_fine", "fine" };
        Assert.True(Array.IndexOf(order, light) > Array.IndexOf(order, dark)); // 浅烘更细、深烘更粗
        Assert.Equal("medium", light);        // V2.0：v60 基准中粗，浅烘 +1 → 中
        Assert.Equal("coarse", dark);         // V2.0：中粗 −1 → 粗
    }

    // ---------- 烘焙日期 → 养豆期满日期 ----------
    [Fact]
    public void ComputeRecipe_RoastDate_SetsReadyDates()
    {
        var roastDate = DateTime.Today.AddDays(-30);
        var r = BrewEngine.ComputeRecipe(new() { Roast = "light", RoastDate = roastDate });
        Assert.Equal(30, r.RestDays);
        Assert.Equal(roastDate.AddDays(14).Date, r.RestReadyMinDate); // 浅烘理想最小 14 天
        Assert.Equal(roastDate.AddDays(30).Date, r.RestReadyMaxDate); // 浅烘理想最大 30 天
    }

    // ---------- 产地 / 豆种 ----------
    [Fact]
    public void Origin_Selection_SetsFlavorAndGrade()
    {
        var r = BrewEngine.ComputeRecipe(new() { Origin = "ethiopia" });
        Assert.Equal("埃塞俄比亚", r.OriginLabel);
        Assert.Contains("柑橘", r.OriginFlavor);
        Assert.Contains("G1", r.OriginGrade);
        var k = BrewEngine.ComputeRecipe(new() { Origin = "kenya" });
        Assert.Contains("黑加仑", k.OriginFlavor);
        Assert.Contains("AA", k.OriginGrade);
    }

    [Fact]
    public void Variety_Selection_Recorded()
    {
        var r = BrewEngine.ComputeRecipe(new() { Variety = "geisha" });
        Assert.Equal("瑰夏", r.VarietyLabel);
        var r2 = BrewEngine.ComputeRecipe(new() { Variety = "typica" });
        Assert.Equal("铁皮卡", r2.VarietyLabel);
    }

    // ---------- 冲煮方法扩展（默认经典 + 杯测 + 逆向 + 瑞士）----------
    [Fact]
    public void Method_DefaultIsClassic()
    {
        Assert.Equal("classic", BrewEngine.ComputeRecipe(null).Method);
        Assert.Equal("经典分段法（三段式，最稳）", BrewEngine.ComputeRecipe(null).MethodLabel);
    }

    [Fact]
    public void Method_Cupping_UsesCuppingPhasesAndRatio()
    {
        // 统一基准：11g 粉 + 200g 94℃ 热水（≈1:18.18）；此处显式传参验证单段静置浸泡结构
        var r = BrewEngine.ComputeRecipe(new() { Dose = 11, Ratio = 18.18, Method = "cupping" });
        Assert.Equal("cupping", r.Method);
        Assert.True(r.IsCupping);
        Assert.Contains(r.Phases, p => p.Name == I18n.T("PhPourIn"));   // 注水浸润（30s 引导）
        Assert.Contains(r.Phases, p => p.Name == I18n.T("PhSoak"));     // 静置浸泡（240s）
        Assert.Equal(2, r.Phases.Count);                                // 单段、无闷蒸/破壳
        Assert.Equal(94, r.Temp);                                       // 杯测固定 94℃
        Assert.Equal(25, r.GrindC40);                                   // 杯测研磨 C40 25
        Assert.Equal(9, r.GrindEK);                                     // EK43 ≈ 9
        Assert.True(r.TotalWater >= 200);                               // 11*18.18 ≈ 200（Round1 → 200）
    }

    [Fact]
    public void Method_Cupping_StateMachine_WalksToCompletion()
    {
        var recipe = BrewEngine.ComputeRecipe(new() { Method = "cupping", Dose = 11, Ratio = 18.18 });
        var st = BrewEngine.CreateState(recipe);
        BrewEngine.Start(st, 0);
        var snap = BrewEngine.Tick(st, 0, recipe.TotalWater); // 注水浸润开始（杯测按时间推进，重量无关）
        long t = 1000;
        int guard = 0;
        while (!snap.Done && guard < 2000)
        {
            t += 1000;
            BrewEngine.Tick(st, t, recipe.TotalWater);
            snap = BrewEngine.SnapshotOf(st);
            guard++;
        }
        Assert.True(snap.Done); // 杯测法 2 阶段（注水浸润 30s → 静置浸泡 240s）应完整走完
    }

    [Fact]
    public void Method_Reverse_HasInnerPourPhases()
    {
        var r = BrewEngine.ComputeRecipe(new() { Method = "reverse" });
        Assert.Contains(r.Phases, p => p.Name.Contains("由外向内"));
    }

    [Fact]
    public void Method_Swiss_HasStirPhases()
    {
        var r = BrewEngine.ComputeRecipe(new() { Method = "swiss" });
        Assert.Contains(r.Phases, p => p.Name.Contains("搅拌"));
    }

    // ---------- 研磨度 C40 / EK 刻度映射 ----------
    [Fact]
    public void Grind_C40EK_Mapping()
    {
        var r = BrewEngine.ComputeRecipe(new() { Roast = "light", Dripper = "v60" }); // 推荐中（V2.0 标定）
        Assert.Equal("中", r.GrindLabel);
        Assert.Equal(20, r.GrindC40);  // 中 C40≈20
        Assert.Equal(7, r.GrindEK);    // 中 EK≈7
        var coarse = BrewEngine.ComputeRecipe(new() { Grind = "coarse" });
        Assert.Equal(28, coarse.GrindC40);
        Assert.Equal(11, coarse.GrindEK);
    }

    // ---------- 水质推荐（与烘焙度关联）----------
    [Fact]
    public void Water_Recommendation_ByRoast()
    {
        Assert.Equal("light_water", BrewEngine.RecommendWater("light"));
        Assert.Equal("balanced", BrewEngine.RecommendWater("medium"));
        Assert.Equal("dark_water", BrewEngine.RecommendWater("dark"));
        Assert.Equal("soft", BrewEngine.RecommendWater("extreme_dark"));
        var r = BrewEngine.ComputeRecipe(new() { Roast = "light" });
        Assert.Equal("浅烘专用软水", r.WaterLabel);
        Assert.Contains("TDS", r.WaterNote);
    }

    // ---------- 产地 / 豆种扩充 ----------
    [Fact]
    public void Origin_WorldMap_Expanded()
    {
        // 非洲/拉美/亚洲多产区 + 兜底
        foreach (var id in new[] { "ethiopia", "kenya", "rwanda", "burundi", "yemen", "colombia", "brazil", "guatemala", "costarica", "panama", "honduras", "mexico", "sumatra", "java", "india", "vietnam", "yunnan", "papua", "other" })
            Assert.True(BrewEngine.OriginProfiles.ContainsKey(id), $"缺失产地 {id}");
        var v = BrewEngine.ComputeRecipe(new() { Origin = "vietnam" });
        Assert.Equal("越南", v.OriginLabel);
        Assert.Equal("亚洲", v.OriginRegion);
        Assert.Contains("罗布斯塔", v.OriginFlavor);
    }

    [Fact]
    public void Variety_IncludesRobustaAndBlendsAndFallback()
    {
        Assert.True(BrewEngine.VarietyProfiles.ContainsKey("robusta"));
        Assert.Equal("Robusta", BrewEngine.VarietyProfiles["robusta"].Species);
        Assert.Equal("Arabica", BrewEngine.VarietyProfiles["catimor"].Species); // 阿拉比卡×罗布斯塔混种
        Assert.Equal("Blend", BrewEngine.VarietyProfiles["other"].Species);     // 兜底混种
        foreach (var id in new[] { "typica", "bourbon", "geisha", "caturra", "catuai", "mundo_novo", "pacamara", "maragogipe", "heirloom", "sl", "colombia", "catimor", "robusta", "other" })
            Assert.True(BrewEngine.VarietyProfiles.ContainsKey(id), $"缺失豆种 {id}");
    }

    // ---------- I18n：所有下拉选项在两种语言下均有覆盖（防止缺键渲染成 key 本身）----------
    [Fact]
    public void I18n_DropdownOptionKeys_CoveredInBothLanguages()
    {
        var groups = new (string Prefix, string[] Ids)[]
        {
            ("Roast_",      new[] { "blonde","super_light","ultra_light","light","medium_light","medium","medium_dark","dark","ultra_dark","extreme_dark" }),
            ("Process_",    new[] { "washed","natural","honey","anaerobic","wet_hulled","carbonic","barrel","k72" }),
            ("Size_",       new[] { "small","standard","large" }),
            ("Dripper_",    new[] { "v60","wave","origami","chemex","switch","gina","smart","hario" }),
            ("Filter_",     new[] { "bleached","abaca","unbleached","sisal","metal","cloth" }),
            ("Method_",     new[] { "classic","oneshot","dongdong","osl","rao","kasuya46","light","reverse","swiss","cupping" }),
            ("MethodNote_", new[] { "classic","oneshot","dongdong","osl","rao","kasuya46","light","reverse","swiss","cupping" }),
            ("Variety_",    new[] { "typica","bourbon","geisha","caturra","catuai","mundo_novo","pacamara","maragogipe","heirloom","sl","colombia","catimor","robusta","other" }),
            ("Origin_",     new[] { "ethiopia","kenya","rwanda","burundi","yemen","colombia","brazil","guatemala","costarica","panama","honduras","mexico","sumatra","java","india","vietnam","yunnan","papua","other" }),
        };
        var prev = I18n.Current;
        try
        {
            foreach (var lang in new[] { I18n.ZhCN, I18n.EnUS })
            {
                I18n.Current = lang;
                foreach (var (prefix, ids) in groups)
                    foreach (var id in ids)
                    {
                        var t = I18n.T(prefix + id);
                        Assert.False(string.IsNullOrWhiteSpace(t), $"[{lang}] 下拉键缺失：{prefix}{id}");
                        Assert.NotEqual(prefix + id, t); // 不能回退为 key 本身
                    }
                Assert.NotEqual("SpeciesRobusta", I18n.T("SpeciesRobusta"));
                Assert.NotEqual("SpeciesBlend", I18n.T("SpeciesBlend"));
            }
        }
        finally
        {
            I18n.Current = prev;
        }
    }

    // ---------- 黄金杯萃取诊断（2026-09-08：海拔沸点约束已按用户要求整体移除）----------

    [Fact]
    public void ComputeRecipe_TempNotCapped_MediumRoast90()
    {
        // 移除海拔封顶后：中烘基准水温 90℃ 不再受任何封顶约束
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Roast = "medium", Flavor = "balanced" });
        Assert.Equal(90, r.Temp);
    }

    [Fact]
    public void ComputeRecipe_HighTempRoast_NotCapped()
    {
        // 浅烘高温方案（原 3650m 海拔会被压到 85℃）：现水温仅由烘焙度/风味/处理法/研磨决定，不被封顶
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Roast = "light", Flavor = "balanced" });
        Assert.InRange(r.Temp, 80, 96);
        var rNoAlt = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Roast = "light", Flavor = "balanced" });
        Assert.Equal(r.Temp, rNoAlt.Temp); // 决定因素里没有海拔（同参重算结果确定）
    }

    [Fact]
    public void EstimateExtraction_DefaultMedium_AutoTuned_IntoGoldenCup()
    {
        I18n.Current = I18n.ZhCN;
        // 2026-09-03 需求：生成的推荐方案必须落入 SCA 黄金杯。
        // 默认中烘（中粗研磨/90℃）原诊断 EY≈17.9 欠萃取 → 引擎自动调细研磨一档 + 校正粉水比 → 落入黄金杯。
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Roast = "medium", Flavor = "balanced" });
        Assert.NotNull(r.Extraction);
        Assert.True(r.Extraction!.IsGolden);
        Assert.True(r.Extraction.Ey >= 18 && r.Extraction.Ey <= 22, $"Ey={r.Extraction.Ey}");
        Assert.True(r.Extraction.Tds >= 1.15 && r.Extraction.Tds <= 1.45, $"Tds={r.Extraction.Tds}");
        Assert.Contains("黄金杯", r.Extraction.Verdict);
        Assert.Contains("黄金杯自动校准", r.GoldenCupTune);                        // 校准说明须告知用户
    }

    // ---------- 黄金杯自动校准守卫（2026-09-03 需求⑧）----------
    // 无论用户选什么烘焙度/方法（杯测除外），生成的推荐方案都必须落在 SCA 黄金杯范围内。
    [Fact]
    public void GoldenCup_AutoCalibration_AllRoasts_LandInGoldenCup()
    {
        foreach (var roast in new[] { "light", "medium", "medium_dark", "dark" })
        foreach (var method in new[] { "classic", "kasuya46", "light" })
        {
            var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15, Roast = roast, Method = method });
            Assert.NotNull(r.Extraction);
            Assert.True(r.Extraction!.IsGolden, $"roast={roast} method={method} Ey={r.Extraction.Ey} Tds={r.Extraction.Tds}");
        }
    }

    [Fact]
    public void EstimateExtraction_HigherRatio_FallsInGoldenCup()
    {
        I18n.Current = I18n.ZhCN;
        var r = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 16, Roast = "medium", Flavor = "balanced", Grind = "medium" });
        Assert.NotNull(r.Extraction);
        Assert.True(r.Extraction!.IsGolden);     // 研磨调细一档 +加大粉水比 → EY/TDS 同时落入黄金杯
        Assert.Contains("黄金杯", r.Extraction.Verdict);
    }

    [Fact]
    public void Snapshot_CarriesExtractionAndTemp()
    {
        var recipe = BrewEngine.ComputeRecipe(new() { Dose = 15, Ratio = 15 });
        var st = BrewEngine.CreateState(recipe);
        BrewEngine.Start(st, 0);
        var snap = BrewEngine.Tick(st, 0, 30);
        Assert.NotNull(snap.Extraction);
        Assert.Equal(recipe.Temp, snap.Temp);
    }

    [Fact]
    public void I18n_GoldenCupAndTempRecKeys_Covered()
    {
        var keys = new[] { "GoldenCup", "Ey", "GCEmpty", "GCIn", "GCUnder", "GCOver", "GCTdsLow", "GCTdsHigh", "TempRec", "TempRecHint" };
        var prev = I18n.Current;
        try
        {
            foreach (var lang in new[] { I18n.ZhCN, I18n.EnUS })
            {
                I18n.Current = lang;
                foreach (var k in keys)
                {
                    var t = I18n.T(k);
                    Assert.False(string.IsNullOrWhiteSpace(t), $"[{lang}] 缺失键 {k}");
                    Assert.NotEqual(k, t); // 不能回退为 key 本身
                }
            }
        }
        finally { I18n.Current = prev; }
    }

    // 豆密度 → 研磨补偿：致密(dense)比默认细一档、疏松(light)比默认粗一档、中等(medium)不变
    [Fact]
    public void RecommendGrind_DensityAdjustsOneStep()
    {
        string medium = BrewEngine.RecommendGrind("v60", "medium", "classic", "medium");
        string dense = BrewEngine.RecommendGrind("v60", "medium", "classic", "dense");
        string porous = BrewEngine.RecommendGrind("v60", "medium", "classic", "light");

        var order = new[] { "coarse", "medium_coarse", "medium", "medium_fine", "fine" };
        int idx(string g) => Array.IndexOf(order, g);
        Assert.Equal(idx(medium) + 1, idx(dense));   // 致密硬豆 → 磨细一档
        Assert.Equal(idx(medium) - 1, idx(porous));  // 疏松豆 → 磨粗一档
    }

    // 引擎新键守卫：密度/记录查询统计/注水总量等（中英都不得缺失或回退 key 本身）
    [Fact]
    public void I18n_UiRevamp2_KeysPresent()
    {
        var keys = new[] { "Density", "DensityHint", "Density_light", "Density_medium", "Density_dense",
                           "RecordsQuery", "NotSet", "TotalWaterPlan", "PourBump1", "PourBump20" };
        var prev = I18n.Current;
        try
        {
            foreach (var lang in new[] { I18n.ZhCN, I18n.EnUS })
            {
                I18n.Current = lang;
                foreach (var k in keys)
                {
                    var t = I18n.T(k);
                    Assert.False(string.IsNullOrWhiteSpace(t), $"[{lang}] 缺失键 {k}");
                    Assert.NotEqual(k, t);
                }
            }
        }
        finally { I18n.Current = prev; }
    }

    // UI-15 改版键守卫：阶段进度条标签（注水中/等待中）与大师方案清单新标题
    [Fact]
    public void I18n_UiRevamp15_KeysPresent()
    {
        var prev = I18n.Current;
        try
        {
            foreach (var lang in new[] { I18n.ZhCN, I18n.EnUS })
            {
                I18n.Current = lang;
                foreach (var k in new[] { "PhasePourBar", "PhaseWaitBar", "MasterPlanTitle", "MasterPlanHint" })
                {
                    var t = I18n.T(k);
                    Assert.False(string.IsNullOrWhiteSpace(t), $"[{lang}] 缺失键 {k}");
                    Assert.NotEqual(k, t);
                }
            }
            // 中文标题已按用户要求修正
            I18n.Current = I18n.ZhCN;
            Assert.Equal("咖啡大师冲煮方案清单", I18n.T("MasterPlanTitle"));
        }
        finally { I18n.Current = prev; }
    }

}
