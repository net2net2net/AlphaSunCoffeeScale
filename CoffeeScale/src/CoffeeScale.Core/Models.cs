namespace CoffeeScale.Core;

/// <summary>阶段类型：注水以重量达标推进，等待以时间达标推进。</summary>
public enum PhaseType
{
    Pour, // 注水
    Wait, // 等待 / 滴滤
}

/// <summary>单个冲煮阶段定义。</summary>
public sealed class Phase
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public PhaseType Type { get; init; }
    public double Target { get; init; }      // POUR：目标累计重量(g)
    public int DurationSec { get; init; }    // WAIT：等待秒数
    public string Tip { get; init; } = "";
    public double StartWeight { get; set; }  // 进入本阶段时的累计重量
}

/// <summary>养豆期推荐结果。</summary>
public sealed class RestRec
{
    public int IdealMin { get; init; }
    public int IdealMax { get; init; }
    public string Status { get; init; } = "";
    public string Tip { get; init; } = "";
}

/// <summary>完整冲煮配方。</summary>
public sealed class Recipe
{
    public string Method { get; init; } = "";
    public string MethodLabel { get; init; } = "";
    public bool IsCupping { get; init; }   // 是否杯测法（静态评估模式，阶段按时间推进，不依赖注水模拟）
    public double Dose { get; init; }
    public double Ratio { get; init; }
    public string Size { get; init; } = "";
    public string SizeLabel { get; init; } = "";
    public string Roast { get; init; } = "";
    public string RoastLabel { get; init; } = "";
    public int RoastAg { get; init; }      // 具体烘焙度 Agtron 读数（用户可输入，默认档位中值）
    public int RoastAgMin { get; init; }  // 烘焙度 Agtron 范围下限
    public int RoastAgMax { get; init; }  // 烘焙度 Agtron 范围上限
    public string Flavor { get; init; } = "";
    public string FlavorLabel { get; init; } = "";
    public int RestDays { get; init; }
    public string Process { get; init; } = "";
    public string ProcessLabel { get; init; } = "";
    public string Dripper { get; init; } = "";
    public string DripperLabel { get; init; } = "";
    public string Filter { get; init; } = "";
    public string FilterLabel { get; init; } = "";
    public string Grind { get; init; } = "";
    public string GrindLabel { get; init; } = "";
    public int GrindC40 { get; init; }   // 推荐 C40(Comandante) 刻度
    public int GrindEK { get; init; }    // 推荐 EK43 刻度
    public string Water { get; init; } = "";
    public string WaterLabel { get; init; } = "";
    public string WaterNote { get; init; } = ""; // 水质推荐说明（TDS/硬度）
    public string Origin { get; init; } = "";
    public string OriginLabel { get; init; } = "";
    public string OriginRegion { get; init; } = "";    // 产地所属大区（非洲/拉美/亚洲…）
    public string OriginFlavor { get; init; } = "";   // 产地常规风味走向
    public string OriginGrade { get; init; } = "";     // 产地等级分类
    public string Variety { get; init; } = "";
    public string VarietyLabel { get; init; } = "";
    public double RecommendedFlow { get; init; }
    public bool NeedRinse { get; init; }
    public int BloomWait { get; init; }      // 推荐闷蒸秒
    public int Temp { get; init; }           // 推荐水温 ℃
    public RestRec RestRec { get; init; } = new();
    public DateTime RestReadyMinDate { get; init; } // 养豆期满（最早可赏味）
    public DateTime RestReadyMaxDate { get; init; } // 完全养豆日期
    public double TotalWater { get; init; }
    public IReadOnlyList<Phase> Phases { get; init; } = Array.Empty<Phase>();
    public double TotalTimeSec { get; init; }
    // —— 黄金杯估算（诊断参考，不替换相位模型）——
    public ExtractionEst? Extraction { get; init; } // 黄金杯 EY/TDS 估算诊断
    public string GoldenCupTune { get; init; } = ""; // 黄金杯自动校准说明（空=无需校准已落入）
}

/// <summary>黄金杯萃取诊断（文档2 技术报告：累加式基准 + 敏感度系数 + 反推）。</summary>
public sealed class ExtractionEst
{
    public double Ey { get; init; }       // 预估萃取率 %
    public double Tds { get; init; }      // 预估浓度 %
    public bool IsGolden { get; init; }   // 是否落入 SCA 黄金杯（EY 18–22% 且 TDS 1.15–1.45%）
    public string Verdict { get; init; } = ""; // 判据文案（已本地化）
    public string Notes { get; init; } = "";    // 补充建议
}

/// <summary>引擎运行时状态。</summary>
public sealed class EngineState
{
    public Recipe Recipe { get; set; } = null!;
    public bool Running { get; set; }
    public double ElapsedMs { get; set; }
    public long StartedAt { get; set; }
    public int PhaseIndex { get; set; }
    public double PhaseEnteredAt { get; set; }
    public double PhaseEnteredWeight { get; set; }
    public double Weight { get; set; }
    public double Flow { get; set; }
    public bool Done { get; set; }
    public double LastNow { get; set; }
    public bool HasLastNow { get; set; }
    public double LastW { get; set; }
    public bool HasLastW { get; set; }
    public List<LogEntry> Log { get; set; } = new();
}

public sealed class LogEntry
{
    public string Name { get; init; } = "";
    public double AtMs { get; init; }
}

/// <summary>供 UI 渲染的当前阶段快照。</summary>
public sealed class SnapshotPhase
{
    public string Name { get; init; } = "";
    public PhaseType Type { get; init; }
    public string Tip { get; init; } = "";
    public double Target { get; init; }
    public double StartWeight { get; init; }
    public double Progress { get; init; }       // 0..1
    public double? RemainingSec { get; init; }  // 仅 WAIT 阶段
}

/// <summary>引擎每帧快照。</summary>
public sealed class Snapshot
{
    public bool Running { get; init; }
    public bool Done { get; init; }
    public double ElapsedMs { get; init; }
    public double Weight { get; init; }
    public double Flow { get; init; }
    public double RatioAchieved { get; init; }
    public double TotalWater { get; init; }
    public ExtractionEst? Extraction { get; init; }
    public double Dose { get; init; }
    public double Ratio { get; init; }
    public string Method { get; init; } = "";
    public string MethodLabel { get; init; } = "";
    public string Size { get; init; } = "";
    public string SizeLabel { get; init; } = "";
    public string Roast { get; init; } = "";
    public string RoastLabel { get; init; } = "";
    public int RoastAg { get; init; }      // 具体烘焙度 Agtron 读数（用户可输入，默认档位中值）
    public int RoastAgMin { get; init; }  // 烘焙度 Agtron 范围下限
    public int RoastAgMax { get; init; }  // 烘焙度 Agtron 范围上限
    public string Flavor { get; init; } = "";
    public string FlavorLabel { get; init; } = "";
    public int RestDays { get; init; }
    public string Process { get; init; } = "";
    public string ProcessLabel { get; init; } = "";
    public string Dripper { get; init; } = "";
    public string DripperLabel { get; init; } = "";
    public string Filter { get; init; } = "";
    public string FilterLabel { get; init; } = "";
    public string Grind { get; init; } = "";
    public string GrindLabel { get; init; } = "";
    public int GrindC40 { get; init; }   // 推荐 C40(Comandante) 刻度
    public int GrindEK { get; init; }    // 推荐 EK43 刻度
    public string Water { get; init; } = "";
    public string WaterLabel { get; init; } = "";
    public string WaterNote { get; init; } = ""; // 水质推荐说明（TDS/硬度）
    public string Origin { get; init; } = "";
    public string OriginLabel { get; init; } = "";
    public string OriginRegion { get; init; } = "";    // 产地所属大区（非洲/拉美/亚洲…）
    public string OriginFlavor { get; init; } = "";   // 产地常规风味走向
    public string OriginGrade { get; init; } = "";     // 产地等级分类
    public string Variety { get; init; } = "";
    public string VarietyLabel { get; init; } = "";
    public double RecommendedFlow { get; init; }
    public bool NeedRinse { get; init; }
    public int BloomWait { get; init; }
    public int Temp { get; init; }
    public RestRec RestRec { get; init; } = new();
    public DateTime RestReadyMinDate { get; init; } // 养豆期满（最早可赏味）
    public DateTime RestReadyMaxDate { get; init; } // 完全养豆日期
    public int PhaseIndex { get; init; }
    public int PhaseNo { get; init; }
    public int TotalPhases { get; init; }
    public SnapshotPhase? Phase { get; init; }
    public double OverallProgress { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<LogEntry> Log { get; init; } = Array.Empty<LogEntry>();
}

/// <summary>computeRecipe 入参。</summary>
public sealed class RecipeOptions
{
    public double? Dose { get; init; }
    public double? Ratio { get; init; }
    public string? Method { get; init; }
    public string? Size { get; init; }
    public string? Roast { get; init; }
    public int? RoastAg { get; init; }      // 具体烘焙度 Agtron 读数（用户可输入，默认档位中值）
    public string? Flavor { get; init; }
    public string? Process { get; init; }
    public string? Dripper { get; init; }
    public string? Filter { get; init; }
    public string? Grind { get; init; }   // 预留：显式研磨度覆盖（默认由引擎推荐）
    public double? RestDays { get; init; }
    public DateTime? RoastDate { get; init; } // 烘焙日期：优先用于推算养豆天数与养豆期满日期
    public string? Origin { get; init; }  // 产地
    public string? Variety { get; init; } // 豆种（品种）
    public string? Density { get; init; } // 咖啡豆密度：light(疏松)/medium(中等)/dense(致密)，影响研磨补偿（默认中等）
}
