using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CoffeeScale.Core;

/// <summary>
/// 参数知识库条目：设置页每个参数旁 ⓘ 图标的弹窗内容。
/// 静态知识（定位 / 机理 / 实务建议）来自：
///   [R]《手冲咖啡多维冲煮逻辑系统·技术报告》v1.1
///   [P]《智能手冲咖啡冲煮系统·项目建设方案》V1.1
/// 动态规则（EngineRules）运行时从 BrewEngine 档位表实时生成——弹窗数值与引擎实际标定永不漂移。
/// </summary>
public sealed record ParamInfoEntry(
    string Title,               // 中文标题
    string TitleEn,             // 英文标题
    string Role,                // 定位（这个参数是什么）
    string RoleEn,
    string Mechanism,           // 作用机理（为什么影响冲煮）
    string MechanismEn,
    Func<string>? EngineRules,  // 引擎规则（当前标定动态生成；null 则不显示该区块）
    string[] Tips,              // 实务建议 / 常见误区（中文）
    string[] TipsEn);           // 英文

/// <summary>参数知识库：key 与设置页参数一一对应。</summary>
public static class ParamInfo
{
    /// <summary>统一来源标注（中文）。</summary>
    public const string Source =
        "来源：[R]《手冲咖啡多维冲煮逻辑系统·技术报告》v1.1 · [P]《智能手冲咖啡冲煮系统·项目建设方案》V1.1 · 规则值实时取自引擎当前标定";

    /// <summary>统一来源标注（英文）。</summary>
    public const string SourceEn =
        "Source: [R] Multi-dimensional Pour-over Brewing Logic System v1.1 · [P] Smart Coffee Brewing System v1.1 · Rules are live-derived from engine calibration";

    /// <summary>按当前语言返回来源标注。</summary>
    public static string SourceLocalized => Zh ? Source : SourceEn;

    private static bool Zh => I18n.Current == I18n.ZhCN;

    private static string Num(double v, string fmt = "0.##") => v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

    public static readonly IReadOnlyDictionary<string, ParamInfoEntry> Entries = new Dictionary<string, ParamInfoEntry>
    {
        /* ==================== 烘焙度 ==================== */
        ["roast"] = new(
            "烘焙度", "Roast Level",
            "决定细胞壁破坏程度与可溶物总量，是水温、闷蒸与研磨的第一驱动量。",
            "Determines cell-wall breakdown and solubles yield; the primary driver of water temperature, bloom and grind.",
            "本引擎采用 Agtron 粉样(Ground)标尺的 10 级分级（色值越大烘焙越浅）。烘焙越深，细胞结构越疏松、可溶物越易析出，天然萃取率越高——因此浅烘需要高温+长闷蒸提高萃取驱动力，深烘需要低温+短闷蒸抑制苦涩析出。引擎按档位直接给出基准水温与闷蒸秒数（落点制），再由风味/处理法等维度叠加修正。",
            "Uses a 10-level Agtron ground-sample scale (higher = lighter). Deeper roasts are more porous and solubles dissolve easily, so light roasts need hotter water and longer bloom while dark roasts need cooler water and shorter bloom to avoid bitterness. Each level carries its own baseline temperature and bloom time.",
            () =>
            {
                var sb = new StringBuilder();
                foreach (var r in BrewEngine.RoastProfiles.Values)
                    sb.AppendLine(Zh
                        ? $"· {r.Label} Ag {r.AgMin}–{r.AgMax}：水温 {r.Temp}℃ · 闷蒸基准 {r.BloomBase}s · 养豆窗口 {r.RestIdeal[0]}–{r.RestIdeal[1]} 天"
                        : $"· {I18n.T("Roast_" + r.Id)} Ag {r.AgMin}–{r.AgMax}: Water temp {r.Temp}°C · Bloom base {r.BloomBase}s · Rest window {r.RestIdeal[0]}–{r.RestIdeal[1]}d");
                return sb.ToString().TrimEnd();
            },
            new[]
            {
                "常见误区：浅烘用低温「避免苦涩」——浅烘可溶物少、结构紧密，低温只会加剧萃取不足，正确做法是高温+长闷蒸",
                "总时长倾向：浅烘类接触更长（发展不足需补偿），深烘类接触更短（防过萃）",
            },
            new[]
            {
                "Myth: brewing light roasts cooler does NOT avoid astringency — it worsens under-extraction. Go hotter with a longer bloom instead.",
                "Light roasts favour longer contact; dark roasts favour shorter contact to prevent over-extraction.",
            }),

        /* ==================== 烘焙值 Ag ==================== */
        ["roastAg"] = new(
            "烘焙值 Ag（Agtron 读数）", "Ag Value (Agtron)",
            "所选烘焙度档位内的具体 Agtron 粉样读数，是档位的精确落点。",
            "The exact Agtron ground reading within the selected roast level.",
            "Ag 读数与烘焙度档位强关联：切换档位时自动取该档 Ag 区间中值，可手动微调。注意两套标尺：粉样(Ground)标尺与整豆(Bean/Roast)标尺相差约 17–20 单位（整豆偏高），本引擎统一采用粉样标尺。",
            "The value auto-fills to the midpoint of the selected level and can be fine-tuned. Ground-sample readings run about 17–20 points lower than whole-bean readings; this engine standardises on the ground scale.",
            () =>
            {
                var sb = new StringBuilder(Zh ? "档位区间（粉样标尺，越高越浅）：" : "Tier ranges (ground scale, higher = lighter):");
                foreach (var r in BrewEngine.RoastProfiles.Values)
                    sb.Append(Zh
                        ? $"\n· {r.Label}：{r.AgMin}–{r.AgMax}"
                        : $"\n· {I18n.T("Roast_" + r.Id)}: {r.AgMin}–{r.AgMax}");
                return sb.ToString();
            },
            new[]
            {
                "只有色值仪读数时：整豆读数减约 17–20 再填入，避免档位被高估（整豆偏高）",
                "Ag 读数主要影响档位归属与推荐联动，不单独参与数值修正",
            },
            new[]
            {
                "If you only have a whole-bean reading, subtract ~17–20 before entering it.",
                "The Ag value drives level selection, not a separate numeric adjustment.",
            }),

        /* ==================== 处理方式 ==================== */
        ["process"] = new(
            "处理方式", "Process",
            "决定发酵度与细胞壁破损程度，直接影响可溶物释放速度，同时修正闷蒸、水温与粉水比。",
            "Defines fermentation degree and cell-wall damage; adjusts bloom, temperature and ratio.",
            "发酵越重（日晒/厌氧/酒桶），果胶残留越多、细胞壁破损越严重，可溶物越易萃出——需要降低水温、延长闷蒸排气并收紧粉水比以防杂味与过萃；水洗类结构干净均匀，按基准处理。引擎按处理法对「闷蒸时长（BloomMul）、水温（TempAdj）、粉水比（RatioAdj）」三项做代数修正。",
            "Heavier fermentation (natural/anaerobic/barrel) leaves more pectin and damaged cell walls, so solubles extract faster: lower temperature, longer bloom, tighter ratio. Washed coffees stay at baseline. The engine applies multiplicative bloom and additive temp/ratio corrections.",
            () =>
            {
                var sb = new StringBuilder();
                foreach (var p in BrewEngine.ProcessProfiles.Values)
                {
                    var baseLabel = Zh ? "基准" : "base";
                    var mul = p.BloomMul == 1.0 ? baseLabel : "×" + Num(p.BloomMul);
                    var tmp = p.TempAdj == 0 ? baseLabel : (p.TempAdj > 0 ? "+" : "−") + Num(Math.Abs(p.TempAdj)) + "℃";
                    var rat = p.RatioAdj == 0 ? baseLabel : (p.RatioAdj > 0 ? "+" : "−") + Num(Math.Abs(p.RatioAdj));
                    sb.AppendLine(Zh
                        ? $"· {p.Label}：闷蒸 {mul} · 水温 {tmp} · 粉水比 {rat}（{p.Note}）"
                        : $"· {I18n.T("Process_" + p.Id)}: Bloom {mul} · Water temp {tmp} · Ratio {rat}（{p.Note}）");
                }
                return sb.ToString().TrimEnd();
            },
            new[]
            {
                "杂味重/发酵味过强 → 先查水温是否过高与处理法错配：厌氧/酒桶等重发酵豆建议低温慢萃并缩短总时长",
                "蜜处理介于水洗与日晒之间，黑蜜更接近日晒需注意降温",
            },
            new[]
            {
                "Harsh, fermented cups usually mean water too hot for the process — go cooler for anaerobic/barrel lots.",
                "Honey sits between washed and natural; black honey behaves closer to natural.",
            }),

        /* ==================== 烘焙日期（养豆期） ==================== */
        ["roastDate"] = new(
            "烘焙日期（养豆期）", "Roast Date (Rest)",
            "烘焙后 CO₂ 排气决定萃取阻力与通道风险，是闷蒸时长的第一决定因素。",
            "CO₂ degassing dictates extraction resistance and channeling risk; the primary input for bloom time.",
            "烘焙后豆体蓄积大量 CO₂ 并随时间排出：越新鲜排气越猛，越需要充分闷蒸帮助排气并防通道效应；超过 30 天进入老化期，闷蒸缩短。引擎用排气系数 rf（≤1 天 ×1.8 → >30 天 ×0.55）乘入闷蒸秒数，并结合所选烘焙度的理想养豆窗口给出状态判定（偏新鲜 / 最佳窗口 / 风味渐退 / 已过赏味期）。",
            "Fresh coffee degasses hard and needs a longer bloom; past 30 days it ages and bloom shortens. The engine multiplies bloom time by a rest factor (×1.8 for ≤1 day down to ×0.55 past 30 days) and reports status against the roast level's ideal rest window.",
            () =>
            {
                var sb = new StringBuilder(Zh
                    ? "排气系数（乘入闷蒸秒数）：\n"
                    : "Degassing factor (multiplied into bloom seconds):\n");
                sb.AppendLine(Zh
                    ? "· ≤1 天：×1.8（极活跃，易通道效应）"
                    : "· ≤1 d: ×1.8 (very active, prone to channeling)");
                sb.AppendLine(Zh
                    ? "· ≤3 天：×1.5 · ≤7 天：×1.2"
                    : "· ≤3 d: ×1.5 · ≤7 d: ×1.2");
                sb.AppendLine(Zh
                    ? "· ≤14 天：×1.0（通用理想点）"
                    : "· ≤14 d: ×1.0 (general ideal point)");
                sb.AppendLine(Zh
                    ? "· ≤21 天：×0.85 · ≤30 天：×0.7 · >30 天：×0.55（老化）"
                    : "· ≤21 d: ×0.85 · ≤30 d: ×0.7 · >30 d: ×0.55 (aged)");
                sb.Append(Zh
                    ? "理想养豆窗口随烘焙深度变化：浅烘更长、深烘更短（见烘焙度规则）。"
                    : "Ideal rest window varies with roast depth: longer for light, shorter for dark (see roast rules).");
                return sb.ToString();
            },
            new[]
            {
                "过度强调养豆对风味的影响而忽视闷蒸操作，是常见认知偏差——先按 rf 调闷蒸",
                "风味平淡/空洞时先确认是否已过赏味期；出现油哈味建议弃用",
            },
            new[]
            {
                "Don't over-index on rest while ignoring the bloom itself — follow the rest factor first.",
                "Flat, hollow cups past the window rarely recover; discard if rancid.",
            }),

        /* ==================== 豆种 ==================== */
        ["variety"] = new(
            "豆种（品种）", "Variety",
            "物种决定咖啡因、绿原酸与糖分结构，从根本上约束冲煮策略；品种塑造风味谱与密度。",
            "Species sets caffeine/chlorogenic-acid/sugar structure; variety shapes flavour and density.",
            "阿拉比卡低因低绿原酸、风味佳，是手冲主流；罗布斯塔高因高绿原酸、苦重体厚，手冲极易苦涩（多作拼配）；混种按通用参数兜底。品种主要影响风味谱与密度特征，冲煮参数随之微调，不以品种单独改大框架。",
            "Arabica is the pour-over mainstay; Robusta is bitter and heavy — best in blends. Variety tunes flavour and density but rarely rewrites the whole recipe.",
            () =>
            {
                var groups = BrewEngine.VarietyProfiles.Values.GroupBy(v => v.Species).OrderBy(g => g.Key).ToList();
                var sb = new StringBuilder(Zh ? "引擎内置品种（按物种分组）：" : "Built-in varieties (by species):");
                foreach (var g in groups)
                    sb.Append(Zh
                        ? $"\n· {g.Key}（{g.Count()} 款）：{string.Join("、", g.Select(v => v.Label))}"
                        : $"\n· {g.Key} ({g.Count()}): {string.Join(", ", g.Select(v => I18n.T("Variety_" + v.Id)))}");
                return sb.ToString();
            },
            new[]
            {
                "罗布斯塔若手冲：极粗研磨 + 低温 82–85℃ + 短接触，否则极易苦涩",
                "瑰夏/SL28 等名种按产区与处理法常规参数即可，无需特殊化",
            },
            new[]
            {
                "For Robusta pour-over: very coarse, 82–85°C, short contact.",
                "Geisha/SL28 follow standard regional recipes — no special handling needed.",
            }),

        /* ==================== 产地 ==================== */
        ["origin"] = new(
            "产地", "Origin",
            "产区通过海拔/气候/处理传统塑造豆子，提供风味走向与等级参考。",
            "Origin shapes the bean via altitude, climate and processing tradition; provides flavour direction and grading.",
            "引擎为每个产区内置「区域 / 风味谱 / 等级」信息卡：非洲产区多明亮花果酸，拉美偏焦糖坚果均衡，亚洲多醇厚低酸。产地在引擎中作为风味参考与背景解释，不直接修正水温/研磨——参数修正由烘焙度、处理法等维度承载。",
            "Each origin carries a region, flavour map and grade card: African lots lean bright and floral, Latin American caramel-nut balanced, Asian heavy and low-acid. Origin is explanatory context; numeric corrections come from roast and process.",
            () =>
            {
                var o = BrewEngine.OriginProfiles.Values.ToList();
                var africa = o.Count(x => x.Region.Contains("非洲"));
                var latam = o.Count(x => x.Region.Contains("拉美"));
                var asia = o.Count(x => x.Region.Contains("亚洲"));
                var oce = o.Count(x => x.Region.Contains("大洋洲"));
                return Zh
                    ? $"引擎内置 {o.Count} 个产区：非洲 {africa} · 拉美 {latam} · 亚洲 {asia} · 大洋洲 {oce} · 兜底 1。\n选择产地后，推荐方案将展示该产区的风味谱与等级信息。"
                    : $"Engine built-in {o.Count} origins: Africa {africa} · Latin America {latam} · Asia {asia} · Oceania {oce} · fallback 1.\nAfter picking an origin, the recommended plan will show its flavor profile and grade.";
            },
            new[]
            {
                "产区建议是「风味预期基线」，最终参数以实际烘焙度与处理法为准",
                "同一产区处理法不同（如埃塞水洗 vs 日晒）风味差异可能大于产地差异",
            },
            new[]
            {
                "Origin sets flavour expectations; roast and process set the numbers.",
                "Process differences within one origin can outweigh origin differences.",
            }),

        /* ==================== 咖啡豆海拔 ==================== */
        ["beanAltitudeM"] = new(
            "咖啡豆海拔", "Bean Altitude",
            "咖啡树成长的海拔高度，是咖啡豆密度的根本驱动量（海拔→慢熟→细胞壁厚度→密度）。",
            "Altitude where the coffee tree grows; the root driver of bean density (altitude → slow maturation → cell-wall thickness → density).",
            "海拔升高→气温低、成熟慢→豆体紧实、细胞壁厚→密度高（致密）；海拔低→疏松多孔→密度低。本参数作为密度的便捷预填：改海拔即刷新密度档，密度下拉仍可手选覆盖（手选后不再被海拔回写）。海拔只驱动密度/研磨，不直接影响水温。",
            "Higher altitude → slower maturation → thicker, denser beans (dense). Lower altitude → porous, less dense. This field pre-fills density: editing altitude refreshes the density tier, while the dropdown stays manually overridable. Altitude drives density/grind only, not water temperature.",
            () =>
                (Zh ? "海拔 → 密度档（预填）：\n" : "Altitude → density tier (prefill):\n")
                + (Zh
                    ? "· ≥1700m：致密 dense\n"
                    : "· ≥1700m: dense\n")
                + (Zh
                    ? "· 1200–1699m：中等 medium\n"
                    : "· 1200–1699m: medium\n")
                + (Zh
                    ? "· <1200m：疏松 light\n"
                    : "· <1200m: light\n")
                + (Zh
                    ? "密度决定研磨补偿（±1 档）；如需特例可无视海拔、直接在密度下拉框选择。"
                    : "Density steers grind compensation (±1 step); for special cases just pick the desired tier in the dropdown, ignoring altitude."),
            new[]
            {
                "与「冲煮地海拔」不同：本参数决定豆子本身密度，不限制水温沸点（沸点封顶逻辑已于 2026-09-08 移除）。",
                "海拔是因、密度是果：海拔只做预填，最终以密度下拉框所选为准。",
            },
            new[]
            {
                "Different from brewing altitude: this sets bean density, not the boil cap (removed 2026-09-08).",
                "Altitude→density is a convenience prefill; the dropdown value is authoritative.",
            }),

        /* ==================== 豆体密度 ==================== */
        ["density"] = new(
            "豆体密度", "Bean Density",
            "种植海拔的代理指标：致密度反映细胞壁厚度，决定研磨补偿方向。",
            "A proxy for growing altitude: density reflects cell-wall thickness and steers grind compensation.",
            "高海拔慢熟豆密度高、细胞壁厚、结构紧实——易萃取不足，研磨向细调一档保证萃取；低海拔/深烘豆疏松多孔——易过萃，研磨向粗调一档防过萃。引擎在「滤杯基准 + 烘焙度修正」之上再叠加密度修正。",
            "Dense high-grown beans have thick cell walls and under-extract easily: grind one step finer. Porous low-grown beans over-extract easily: one step coarser. The engine stacks this on top of dripper baseline and roast adjustment.",
            () =>
                (Zh ? "密度 → 研磨修正（档位制）：\n" : "Density → grind adjustment (tier steps):\n")
                + (Zh
                    ? "· 致密（dense，高海拔硬豆）：向细 +1 档\n"
                    : "· Dense (hard, high-grown): +1 step finer\n")
                + (Zh ? "· 中等（medium，默认）：不修正\n" : "· Medium (default): no adjustment\n")
                + (Zh
                    ? "· 疏松（light，低海拔/深烘常见）：向粗 −1 档\n"
                    : "· Light (low-grown/dark roast): −1 step coarser\n")
                + (Zh
                    ? "最终研磨 = 滤杯基准档 + 烘焙度修正（浅烘更细/深烘更粗）+ 密度修正，并给出 C40 / EK43 对照刻度。"
                    : "Final grind = dripper baseline + roast adjustment (lighter=fine / darker=coarse) + density adjustment, with C40 / EK43 reference."),
            new[]
            {
                "注意区分两个海拔：本参数指「豆子种在哪里」（密度代理），「冲煮地海拔」指「你在哪里冲」（沸点约束）",
                "豆表油脂多、颜色深的商业豆通常偏疏松；高海拔水洗 G1 级通常偏致密",
            },
            new[]
            {
                "Don't confuse growing altitude (density) with brewing altitude (boil cap).",
                "Oily dark commercial beans are usually porous; high-grown washed G1 lots dense.",
            }),

        /* ==================== 粉量档位 ==================== */
        ["size"] = new(
            "粉量档位", "Dose Class",
            "决定分段结构：闷蒸倍数、注水段数、段间隔与滴滤收尾时长。",
            "Sets the segmentation: bloom multiplier, number of pours, pour gap and draw-down.",
            "粉层厚度影响水流阻力与热质量：大粉量粉层厚、阻力大、热惯性高，需要更多段数与更长闷蒸来保证均匀萃取；小粉量粉层薄、流速快，段数减少即可。引擎按档位输出整套分段结构，段数变更自动重排注水时间轴。",
            "A thicker bed resists flow and holds heat: more pours and longer bloom for even extraction; a thin bed drains fast and needs fewer pours. Each class outputs the full pour schedule.",
            () =>
            {
                var sb = new StringBuilder();
                foreach (var s in BrewEngine.SizeProfiles.Values)
                    sb.AppendLine(Zh
                        ? $"· {s.Label} {s.MinDose}–{s.MaxDose}g（默认 {Num(s.DefaultDose, "0.#")}g）：闷蒸 ×{Num(s.BloomMul)} · {s.Segments} 段 · 段间隔 {s.SegGap}s · 滴滤收尾 {s.DrawWait}s"
                        : $"· {I18n.T("Size_" + s.Id)} {s.MinDose}–{s.MaxDose}g (default {Num(s.DefaultDose, "0.#")}g): bloom ×{Num(s.BloomMul)} · {s.Segments} segments · seg gap {s.SegGap}s · drawdown {s.DrawWait}s");
                return sb.ToString().TrimEnd();
            },
            new[]
            {
                "档位由粉量自动推断（≤12g 小 / 13–22g 标准 / ≥23g 大），也可手动指定",
                "流速过快（<2:00 滴完）可增加粉量或换平底滤杯；过慢则反向操作",
            },
            new[]
            {
                "Class is inferred from dose (≤12 g small / 13–22 g standard / ≥23 g large).",
                "Fast draw-downs (<2:00) suggest a bigger dose or flat-bottom dripper.",
            }),

        /* ==================== 粉量 ==================== */
        ["dose"] = new(
            "粉量", "Dose",
            "决定一杯的绝对产量与总注水量，是所有配方的计算基数。",
            "Determines cup volume and total water; the base of every calculation.",
            "总注水 = 粉量 × 粉水比（处理法会再做 RatioAdj 修正），出液量 ≈ 总注水 − 粉量 × 2.0（粉吸水系数 2 g/g）。粉量变化还会联动粉量档位（分段结构）与推荐研磨的档位归并。",
            "Total water = dose × ratio (adjusted by process); beverage ≈ water − dose × 2.0 (absorption 2 g/g). Dose also drives the dose class and grind recommendation.",
            () =>
            {
                var d = 15.0;
                var size = BrewEngine.DetectSize(d);
                var p = BrewEngine.SizeProfiles[size];
                return Zh
                    ? $"当前示例：{Num(d, "0.#")}g 粉 → {p.Label}（闷蒸 {Num(d * p.BloomMul, "0.#")}g = 粉量×{Num(p.BloomMul)}）。\n"
                      + "粉水比 1:15 时：总注水 225g，出液 ≈ 195g（15×2=30g 被粉吸留）。\n"
                      + "±1g 粉量的味道差异通常小于研磨 ±1 格，但会改变分段时间轴总长。"
                    : $"Current example: {Num(d, "0.#")}g dose → {I18n.T("Size_" + p.Id)} (bloom {Num(d * p.BloomMul, "0.#")}g = dose×{Num(p.BloomMul)}).\n"
                      + "At ratio 1:15: total water 225g, yield ≈ 195g (15×2=30g absorbed by grounds).\n"
                      + "±1g dose changes taste less than ±1 grind step, but shifts the segment time axis length.";
            },
            new[]
            {
                "触摸友好步进器调整，范围被钳制在所选档位内（DoseMin..DoseMax）",
                "想加浓优先调粉水比或风味目标，而不是盲目加粉",
                "粉水比不是独立旋钮而是萃取率的因变量：ratio = EY ÷ 目标TDS + 吸水系数(2.0)，萃取率越高粉水比应越稀",
            },
            new[]
            {
                "Use the stepper; the range is clamped to the selected class.",
                "To strengthen the cup, adjust ratio or flavour goal before adding dose.",
                "Ratio is a dependent of extraction, not an independent dial: ratio = EY ÷ target TDS + absorption (2.0).",
            }),

        /* ==================== 滤杯 ==================== */
        ["dripper"] = new(
            "滤杯 / 冲煮器具", "Dripper",
            "决定流态（旁路效应）与基准流速，是研磨基准与注水引导的第一依据。",
            "Sets flow geometry and bypass; the first basis for grind baseline and pouring guidance.",
            "锥形（V60）流速快、旁路少，需更细研磨延长接触；平底（蛋糕杯）萃取均匀、容错高；聪明杯为浸泡式——引擎自动切换为「闷蒸→两次注水→关阀浸泡→滴滤」阶段（浸泡时长随养豆系数伸缩），不再走常规分段。流速基准 FlowBase 还决定各注水段的引导时长。",
            "Conical (V60) flows fast with little bypass: grind finer for contact. Flat-bottom extracts evenly and is forgiving. The Clever dripper switches to immersion phases (bloom → two pours → steep → draw) scaled by rest factor. FlowBase drives per-pour timing.",
            () =>
            {
                var sb = new StringBuilder();
                foreach (var d in BrewEngine.DripperProfiles.Values)
                    sb.AppendLine(Zh
                        ? $"· {d.Label}：基准流速 {Num(d.FlowBase)} g/s{(d.Immersion ? " · 浸泡式（阶段自动切换）" : " · 滴滤式分段")}"
                        : $"· {I18n.T("Dripper_" + d.Id)}: base flow {Num(d.FlowBase)} g/s{(d.Immersion ? " · immersion (auto phase switch)" : " · drawdown segments")}");
                sb.Append(Zh
                    ? "推荐流速 = 滤杯基准 + 研磨修正 + 滤纸修正（3–14 g/s 钳制）。"
                    : "Recommended flow = dripper baseline + grind adjustment + filter adjustment (clamped to 3–14 g/s).");
                return sb.ToString();
            },
            new[]
            {
                "金属滤网无滤纸阻力：放粗研磨会数秒滤完导致萃取不足，正确策略是中细研磨+筛细粉",
                "聪明杯浸泡萃取容错最高，适合新手与多杯量场景",
                "研磨度对萃取率的影响约为水温的 1.7 倍（1 格 ≈ 1.7℃），日常微调优先动研磨、水温留给风味走向的粗调",
            },
            new[]
            {
                "Metal mesh has no paper resistance — never grind coarse; keep medium-fine and sift fines.",
                "The Clever dripper is the most forgiving; great for beginners.",
                "Grind affects extraction ~1.7× more than temperature (1 step ≈ 1.7 °C): adjust grind first for fine-tuning.",
            }),

        /* ==================== 滤纸 ==================== */
        ["filter"] = new(
            "滤纸 / 滤网", "Filter",
            "厚度与材质决定流速修正与油脂吸附量，影响干净度与 body。",
            "Thickness and material set flow adjustment and oil adsorption — clarity vs body.",
            "原色滤纸有纸浆味需热水润洗；漂白滤纸免润洗。金属滤网流速快、保留油脂（body 厚但可能有沉淀感）；绒布口感圆润介于两者之间。引擎用 FlowAdj 修正推荐流速，并用 Rinse 标记提醒润洗步骤。",
            "Unbleached papers need a rinse to remove paper taste; bleached are rinse-free. Metal keeps oils (heavier body, some sediment); cloth sits in between. The engine adjusts recommended flow and flags the rinse step.",
            () =>
            {
                var sb = new StringBuilder();
                foreach (var f in BrewEngine.FilterProfiles.Values)
                {
                    var adj = f.FlowAdj == 0 ? (Zh ? "基准" : "base") : (f.FlowAdj > 0 ? "+" : "−") + Num(Math.Abs(f.FlowAdj)) + " g/s";
                    sb.AppendLine(Zh
                        ? $"· {f.Label}：流速 {adj} · {(f.Rinse ? "需润洗" : "免润洗")}（{f.Note}）"
                        : $"· {I18n.T("Filter_" + f.Id)}: flow {adj} · {(f.Rinse ? "rinse needed" : "rinse-free")}（{f.Note}）");
                }
                return sb.ToString().TrimEnd();
            },
            new[]
            {
                "润洗三重作用：去纸浆味 + 预热滤杯 + 使滤纸贴合杯壁（防旁路），原色/金属/绒布务必执行",
                "流速过慢（>4:00 未滴完）→ 厚滤纸换标准滤纸是快速解法之一",
            },
            new[]
            {
                "Rinsing removes paper taste, preheats and seats the filter — always do it for unbleached/metal/cloth.",
                "For stalls past 4:00, switching to a faster paper is the quickest fix.",
            }),

        /* ==================== 冲煮方式 ==================== */
        ["method"] = new(
            "冲煮方式", "Brewing Method",
            "决定整壶咖啡的阶段序列生成器：同样的粉与水，不同的分段节奏就是不同的萃取曲线。",
            "Selects the phase-sequence generator: same coffee and water, but a different pour rhythm means a different extraction curve.",
            "引擎内每种方法对应独立的阶段生成器（经典三段 / 粕谷 5 段等水 / 浅烘长闷多段 / 逆向画圈 / 瑞士搅拌 / 杯测静态浸泡）。注水段数会进入萃取率估算的段数项（每多 1 段约 +0.20% EY），杯测法则切换为按时间推进的静态浸泡并把粉水比固定为 SCA 的 1:18.18（统一基准 11g 粉 / 200g 94℃ 热水）。方法与「查看介绍」键联动，可展开当前参数下的实时分段步骤。",
            "Each method has its own phase generator (classic 3-pour / Kasuya 5 equal pours / light-roast long bloom / reverse circle pour / Swiss stir / cupping immersion). Pour count feeds the extraction estimate (+~0.20% EY per extra pour); cupping switches to time-driven immersion with the SCA 1:18.18 ratio (unified base 11g coffee / 200g 94°C water). The intro button expands a live step-by-step breakdown.",
            () =>
            {
                var sb = new StringBuilder();
                foreach (var m in BrewEngine.MethodProfiles.Values)
                {
                    var tag = m.IsCupping
                        ? (Zh ? " · 粉水比固定 1:18.18 · 按时间推进" : " · ratio fixed 1:18.18 · time-driven")
                        : (m.PreferImmersion ? (Zh ? " · 浸泡式" : " · immersion") : "");
                    sb.AppendLine(Zh
                        ? $"· {m.Label}：{m.Note}{tag}"
                        : $"· {I18n.T("Method_" + m.Id)}: {m.Note}{tag}");
                }
                return sb.ToString().TrimEnd();
            },
            new[]
            {
                "浅烘豆优先试「浅烘加强法」（长闷蒸 + 多段小水补萃取），深烘豆用「逆向注水法」防过萃",
                "拿不准就选「经典分段法」：最通用稳健，其余方法都是对它的针对性变体",
            },
            new[]
            {
                "For light roasts try the light-roast method (long bloom + multiple small pours); for dark roasts use the reverse pour to avoid over-extraction.",
                "When unsure, stick with the classic segmented method — the others are targeted variants of it.",
            }),
    };
}
