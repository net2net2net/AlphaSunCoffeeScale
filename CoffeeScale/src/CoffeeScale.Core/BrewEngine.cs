using System;
using System.Collections.Generic;
using System.Linq;

namespace CoffeeScale.Core;

/// <summary>
/// 手冲咖啡智能冲煮核心逻辑引擎（纯逻辑，无 UI 依赖）。
/// 忠实移植自 Web 版 brew-logic.js，可在 .NET MAUI / WPF / Avalonia 等任意 .NET UI 复用。
/// </summary>
public static class BrewEngine
{
    // ---------- 工具 ----------
    private static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));
    private static double Round1(double v) => Math.Round(v * 10) / 10;

    // ---------- 文档2 技术报告：黄金杯萃取模型（累加式基准 + 敏感度系数） ----------
    // 基准：水温 92℃、研磨(基准 C40)=24 格、基准 EY 19.5%、基准段数 4、吸水系数 2.0 g/g。
    // 敏感度：研磨每细 1 格 +0.30%EY、水温每 +1℃ +0.18%EY、每多 1 段 +0.20%EY。
    private const double EyBase = 19.5;     // 基准萃取率 %
    private const double GrindBase = 24.0;  // 基准研磨(C40 刻度)
    private const double TempBase = 92.0;   // 基准水温 ℃
    private const double SegsBase = 4.0;    // 基准注水段数
    private const double AbsorbCoef = 2.0;  // 吸水系数 g/g（粉水比反推用）

    // Δ烘焙（浅−1.0 / 中浅−0.5 / 中0 / 中深+0.7 / 深+1.5），10 级烘焙度映射回 5 个溶解性档
    public static readonly Dictionary<string, double> RoastEyDelta = new()
    {
        ["blonde"] = -1.0, ["super_light"] = -1.0, ["ultra_light"] = -1.0,
        ["light"] = -0.5, ["medium_light"] = -0.5,
        ["medium"] = 0.0,
        ["medium_dark"] = 0.7,
        ["dark"] = 1.5, ["ultra_dark"] = 1.5, ["extreme_dark"] = 1.5,
    };
    // Δ处理（水洗−0.2 / 蜜0 / 日晒+0.3 / 厌氧+0.6 / 湿刨+0.2；碳酸/酒桶近似重发酵 +0.4/+0.6，K72 近似水洗 −0.2）
    public static readonly Dictionary<string, double> ProcessEyDelta = new()
    {
        ["washed"] = -0.2, ["k72"] = -0.2,
        ["honey"] = 0.0, ["natural"] = 0.3, ["wet_hulled"] = 0.2,
        ["carbonic"] = 0.4, ["anaerobic"] = 0.6, ["barrel"] = 0.6,
    };

    /// <summary>Δ水质（GH&lt;40 −0.8 / GH&gt;100 +0.3）。</summary>
    private static double WaterEyDelta(string water)
    {
        var wp = WaterProfiles.GetValueOrDefault(water, WaterProfiles["balanced"]);
        if (wp.GhMax < 40) return -0.8;
        if (wp.GhMin > 100) return 0.3;
        return 0.0;
    }

    /// <summary>Δ养豆（0–4 天 −0.5 / &gt;45 天 +0.3）。</summary>
    private static double RestEyDelta(int restDays)
    {
        if (restDays <= 4) return -0.5;
        if (restDays > 45) return 0.3;
        return 0.0;
    }

    /// <summary>目标 TDS（%）：醇厚 1.44 / 均衡 1.36 / 明亮 1.28（均落在黄金杯 1.15–1.45 内）。</summary>
    private static double TargetTds(string flavor)
        => flavor switch { "sweet" => 1.44, "acidic" => 1.28, _ => 1.36 };

    /// <summary>
    /// 黄金杯萃取诊断（文档2 技术报告核心）。
    /// EY = 19.5 + Δ烘焙 + Δ处理 + (24−G)×0.30 + (T−92)×0.18 + (段数−4)×0.20 + Δ水质 + Δ养豆。
    /// TDS = EY ÷ (ratio−2.0)。判据：EY 18–22% 且 TDS 1.15–1.45% 为黄金杯。
    /// </summary>
    public static ExtractionEst EstimateExtraction(int grindC40, int temp, int nPours, double ratio, string roast, string process, string water, int restDays)
    {
        double ey = EyBase
            + RoastEyDelta.GetValueOrDefault(roast, 0.0)
            + ProcessEyDelta.GetValueOrDefault(process, 0.0)
            + (GrindBase - grindC40) * 0.30
            + (temp - TempBase) * 0.18
            + (nPours - SegsBase) * 0.20
            + WaterEyDelta(water)
            + RestEyDelta(restDays);
        ey = Math.Round(ey, 2);
        // 粉水比反推核心：TDS = EY ÷ (ratio − 吸水系数2.0)
        double tds = Math.Round(ey / Math.Max(1.0, ratio - AbsorbCoef), 2);
        bool inEy = ey >= 18 && ey <= 22;
        bool inTds = tds >= 1.15 && tds <= 1.45;
        bool golden = inEy && inTds;

        var parts = new List<string>();
        if (golden) parts.Add(I18n.T("GCIn"));
        else
        {
            if (!inEy) parts.Add(ey < 18 ? I18n.T("GCUnder") : I18n.T("GCOver"));
            if (!inTds) parts.Add(tds < 1.15 ? I18n.T("GCTdsLow") : I18n.T("GCTdsHigh"));
        }
        string verdict = string.Join(I18n.T("GCSep"), parts);

        string notes = "";
        if (!inTds && tds > 1.45) notes = I18n.T("GCNoteHigh");
        else if (!inTds && tds < 1.15) notes = I18n.T("GCNoteLow");
        else if (!inEy && ey < 18) notes = I18n.T("GCNoteUnder");
        else if (!inEy && ey > 22) notes = I18n.T("GCNoteOver");

        return new ExtractionEst { Ey = ey, Tds = tds, IsGolden = golden, Verdict = verdict, Notes = notes };
    }

    // ---------- 粉量档位 ----------
    public sealed record SizeProfile(string Id, string Label, double BloomMul, int Segments, int SegGap, int DrawWait, int K46First, int K46Late, double DefaultDose, double MinDose, double MaxDose);
    public static readonly Dictionary<string, SizeProfile> SizeProfiles = new()
    {
        ["small"]    = new("small",    "少粉量", 2.0, 2, 6, 30, 40, 25, 8, 5, 12),
        ["standard"] = new("standard", "标准粉量", 2.0, 2, 8, 40, 45, 30, 15, 12, 22),
        ["large"]    = new("large",    "大粉量", 2.5, 3, 10, 55, 50, 35, 25, 22, 30),
    };

    public static string DetectSize(double dose) => dose <= 12 ? "small" : dose <= 22 ? "standard" : "large";

    // ---------- 引擎键反查（Phase 11：历史记录 → 我的方案 复刻用） ----------
    // 旧版冲煮记录只存了展示标签（MethodLabel/RoastAg 等），复刻成方案时需要反查回引擎键。

    /// <summary>按展示标签反查引擎键（如「日晒」→ natural）。查不到返回 null（= 未指定，调用方应保留空）。</summary>
    public static string? KeyByLabel<T>(IReadOnlyDictionary<string, T> profiles, Func<T, string> labelOf, string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        foreach (var kv in profiles)
            if (labelOf(kv.Value) == label) return kv.Key;
        return null;
    }

    /// <summary>按 Agtron 粉样读数反查烘焙度引擎键（档位 10 分带连续不重叠；恰落边界时归更高一档即更浅）。查不到返回 null。</summary>
    public static string? RoastKeyByAg(int ag)
    {
        foreach (var kv in RoastProfiles)
            if (ag >= kv.Value.AgMin && ag <= kv.Value.AgMax) return kv.Key;
        return null;
    }

    // ---------- 冲煮方法 ----------
    // 每种方法定义：关联的段数倍率、注水节奏、是否强依赖浸泡、杯测标志、默认粉水比覆盖、说明。
    public sealed record MethodProfile(string Id, string Label, double RatioBias, bool IsCupping, bool PreferImmersion, string Note);
    public static readonly Dictionary<string, MethodProfile> MethodProfiles = new()
    {
        ["classic"]   = new("classic",   "经典分段法",   0.0,  false, false, "三段式分段注水，最通用稳健，默认方法"),
        ["oneshot"]   = new("oneshot",   "一刀流",       0.0,  false, false, "单次注水，日式深烘常见，水流稳定一次注完"),
        ["dongdong"]  = new("dongdong",  "点点注水法",   0.0,  false, false, "小批量多次点滴式注水，提升萃取均匀度"),
        ["osl"]       = new("osl",       "OSL 大流量",   0.0,  false, false, "单次大流量注水，连续大水流贯穿粉层"),
        ["rao"]       = new("rao",       "Rao 一刀法",   0.0,  false, false, "Scott Rao 大粉量快冲，高粉水比快流速"),
        ["kasuya46"]  = new("kasuya46",  "粕谷式 46 法", 0.0,  false, false, "粕谷哲也 5 段等水，先定酸甜后定浓度"),
        ["light"]     = new("light",     "浅烘加强法",   0.0,  false, false, "长闷蒸 + 多段小水，专为浅烘高萃取设计"),
        ["reverse"]   = new("reverse",   "逆向注水法",   0.0,  false, false, "中心细流、由外向内画圈，针对深烘防过萃"),
        ["swiss"]     = new("swiss",     "瑞士搅拌法",   0.0,  false, false, "注水后匙搅破粉墙，提升萃取均匀度（Swiss Water 思路）"),
        ["cupping"]   = new("cupping",   "杯测法 (SCA)", 0.0,  true,  true,  "SCA 杯测协议：统一基准 11g 粉 + 200g 94℃ 热水（≈1:18.18），注入后静置浸泡 4min（单段、无闷蒸/破壳），滤杯/滤纸取空，建议器具：杯测碗、杯测勺"),
    };

    /// <summary>杯测法统一基准粉水比（用户 2026-09-13 最终确认：11g 粉 / 200g 94℃ 热水 = 200/11 ≈ 1:18.18，常记作 1:18）。</summary>
    public const double CuppingRatio = 18.18;

    // ---------- 烘培度 / 风味走向 / 养豆期 ----------
    // Ag = Agtron 读数（咖啡色值仪）。采用 Agtron **粉样(Ground)标尺**（与 SCAA / Agtron Inc. 公开的粉样校准区间对齐，数值越高越浅烘）。
    // 烘焙度档位：在 SCAA Agtron Roast Color 粉样(Ground)标尺基础上扩展为 10 级（浅→深逐一递降，10 分带、连续不重叠），
    // 并在原「极浅烘」之上新增两档更浅的「极浅烘 / 超浅烘」（用户 2026-09-01 要求：浅烘→肉桂烘、极浅烘→浅烘、新增极浅烘+超浅烘）：
    //   超浅烘 Blonde 110–120 / 极浅烘 Very Light 100–110 / 浅烘 Light 90–100 / 肉桂烘 Cinnamon 80–90 / 中浅烘 70–80
    //   / 浅中烘 60–70 / 中烘 50–60 / 中深烘 40–50 / 深烘 30–40 / 极深烘 20–30。
    // Ag 数值越高越浅烘（粉样标尺）；最浅档超浅烘 Blonde 已逼近 Agtron 粉样标尺上限 ~120。
    // 阳光于 2026-09-01 明确确认采用「粉样(Ground)标尺」（非整豆 Roast 标尺）；整豆比粉样高约 17–20 单位，此处统一用粉样。
    // BloomBase：闷蒸基准秒；Temp：基准水温℃（浅烘高、深烘低，与 Ag 负相关）。
    public sealed record RoastProfile(string Id, string Label, int BloomBase, int Temp, int[] RestIdeal, int AgMin, int AgMax, string Note);
    public static readonly Dictionary<string, RoastProfile> RoastProfiles = new()
    {
        ["blonde"]       = new("blonde",       "超浅烘", 50, 96, new[] { 20, 42 }, 110, 120, "超浅烘 Blonde（白烘/White Roast，极浅近乎未发展）排气极猛、密度最高，闷蒸最长、养豆最久；粉样 Ag 110–120"),
        ["super_light"]  = new("super_light",  "极浅烘", 47, 95, new[] { 19, 40 }, 100, 110, "极浅烘 Very Light（北欧式极浅，Almost City）排气极猛，闷蒸很长；粉样 Ag 100–110"),
        ["ultra_light"]  = new("ultra_light",  "浅烘",   45, 94, new[] { 18, 38 }, 90, 100,  "浅烘 Light 密度高、CO₂ 多、排气慢，需较长闷蒸；粉样 Ag 90–100（默认）"),
        ["light"]        = new("light",        "肉桂烘", 40, 93, new[] { 14, 30 }, 80, 90,  "肉桂烘 Cinnamon Roast（极浅的传统肉桂色，豆表仅起皱未爆裂）密度高、排气慢，需较长闷蒸；粉样 Ag 80–90"),
        ["medium_light"] = new("medium_light", "中浅烘", 35, 92, new[] { 10, 22 }, 70, 80,  "中浅烘 Moderately Light 兼顾果酸与甜感；粉样 Ag 70–80"),
        ["medium"]       = new("medium",       "浅中烘", 30, 90, new[] { 7, 14 },  60, 70,  "浅中烘 Light Medium 酸质转柔、甜感增强；粉样 Ag 60–70"),
        ["medium_dark"]  = new("medium_dark",  "中烘",   25, 88, new[] { 5, 10 },  50, 60,  "中烘 Medium 平衡；粉样 Ag 50–60"),
        ["dark"]         = new("dark",         "中深烘", 18, 86, new[] { 3, 7 },   40, 50,  "中深烘 Moderately Dark 出现可可/坚果；粉样 Ag 40–50"),
        ["ultra_dark"]   = new("ultra_dark",   "深烘",   14, 84, new[] { 2, 4 },   30, 40,  "深烘 Dark 排气快、易过萃，闷蒸宜短；粉样 Ag 30–40"),
        ["extreme_dark"] = new("extreme_dark", "极深烘", 10, 82, new[] { 1, 3 },   20, 30,  "极深烘 Very Dark（法式/二爆尾）几乎无酸；粉样 Ag 20–30"),
    };

    public sealed record FlavorProfile(string Id, string Label, double BloomMul, int TempAdj);
    public static readonly Dictionary<string, FlavorProfile> FlavorProfiles = new()
    {
        ["balanced"] = new("balanced", "平衡",           1.0, 0),
        ["sweet"]    = new("sweet",    "突出甜感/醇厚",   1.2, -1),
        ["acidic"]   = new("acidic",   "突出酸质/明亮",   0.9, 1),
    };

    // ---------- 咖啡豆处理方式（流行处理法） ----------
    public sealed record ProcessProfile(string Id, string Label, double BloomMul, double TempAdj, double RatioAdj, string Note);
    public static readonly Dictionary<string, ProcessProfile> ProcessProfiles = new()
    {
        ["washed"]      = new("washed",      "水洗",         1.00, 0,   0.0,  "水洗豆干净明亮、结构均匀，易稳定萃取"),
        ["natural"]     = new("natural",     "日晒",         1.10, -1, -0.5, "日晒豆甜感厚重，略降水温防过萃"),
        ["honey"]       = new("honey",       "蜜处理",       1.05, -0.5, -0.3, "蜜处理甜润滑顺，水温略降"),
        ["anaerobic"]   = new("anaerobic",   "厌氧发酵",     1.15, -1.5, -1.0, "厌氧发酵感强，宜低温慢萃"),
        ["wet_hulled"]  = new("wet_hulled",  "湿刨（曼特宁）", 1.08, -0.5, -0.2, "印尼湿刨低酸厚实，口感醇重"),
        ["carbonic"]    = new("carbonic",    "碳酸浸渍",     1.12, -1, -0.6, "碳酸浸渍果香爆破，宜低温"),
        ["barrel"]      = new("barrel",      "酒桶发酵",     1.18, -1.5, -0.9, "酒桶发酵桶香突出，低温慢萃"),
        ["k72"]         = new("k72",         "肯尼亚双重水洗", 1.00, 0,   0.0,  "K72 双重水洗干净明亮，结构似水洗"),
    };

    // ---------- 豆种（品种）----------
    // 含阿拉比卡名种、罗布斯塔(Robusta)、有名混种，最后以「其他」兜底。
    public sealed record VarietyProfile(string Id, string Label, string Species, string Note);
    public static readonly Dictionary<string, VarietyProfile> VarietyProfiles = new()
    {
        ["typica"]   = new("typica",   "铁皮卡",       "Arabica", "经典古老阿拉比卡，干净优雅、甜感细腻"),
        ["bourbon"]  = new("bourbon",  "波旁",         "Arabica", "甜感突出、风味丰富饱满，阿拉比卡优良种"),
        ["geisha"]   = new("geisha",   "瑰夏",         "Arabica", "茉莉花香、茶感、风味极致（巴拿马名种）"),
        ["caturra"]  = new("caturra",  "卡杜拉",       "Arabica", "波旁变种，明亮干净、酸质清爽"),
        ["catuai"]   = new("catuai",   "卡杜艾",       "Arabica", "波旁×蒙多诺沃混种，抗风高产、平衡"),
        ["mundo_novo"] = new("mundo_novo", "蒙多诺沃", "Arabica", "波旁×铁皮卡混种，巴西主力商业种"),
        ["pacamara"] = new("pacamara", "帕卡马拉",     "Arabica", "象豆粒大、风味奔放（萨尔瓦多名种）"),
        ["maragogipe"] = new("maragogipe", "马拉戈吉佩", "Arabica", "象豆种，低酸柔和、口感圆润"),
        ["heirloom"] = new("heirloom", "埃塞原生种",  "Arabica", "埃塞俄比亚原生混合种，风味多样复杂"),
        ["sl"]       = new("sl",       "肯尼亚 SL28/SL34", "Arabica", "肯尼亚标志品种，黑加仑般浓郁酸质"),
        ["colombia"] = new("colombia", "哥伦比亚变种", "Arabica", "抗病、平衡，常用于商业豆"),
        ["catimor"]  = new("catimor",  "卡蒂姆",       "Arabica", "阿拉比卡×罗布斯塔混种，抗病高产能，风味较平（常用于拼配）"),
        ["robusta"]  = new("robusta",  "罗布斯塔",     "Robusta", "高咖啡因、低酸、醇厚苦甜，多用于意式拼配与速溶"),
        ["other"]    = new("other",    "其他 / 混种",  "Blend",  "未列明品种或拼配豆，按通用参数处理（兜底）"),
    };

    // ---------- 产地（常规风味走向 + 等级分类）----------
    // 参考咖啡世界地图，覆盖非洲 / 拉美 / 亚洲主要产区；「其他」兜底未列明产区。
    public sealed record OriginProfile(string Id, string Label, string Region, string Flavor, string Grade);
    public static readonly Dictionary<string, OriginProfile> OriginProfiles = new()
    {
        // 非洲
        ["ethiopia"]   = new("ethiopia",   "埃塞俄比亚", "非洲", "柑橘、花香、莓果、明亮上扬酸质", "G1 / G2（Grade 1–2）"),
        ["kenya"]      = new("kenya",      "肯尼亚",     "非洲", "黑加仑、番茄、乌梅、浓郁明亮酸", "AA / AB"),
        ["rwanda"]     = new("rwanda",     "卢旺达",     "非洲", "柑橘、花香、红茶感、干净明亮", "A / B（水洗）"),
        ["burundi"]    = new("burundi",    "布隆迪",     "非洲", "莓果、红糖、柔和酸质", "A / B"),
        ["yemen"]      = new("yemen",      "也门",       "非洲/阿拉伯", "野莓、酒香、巧克力、醇厚发酵感", "Mocha Grade（麻袋分级）"),
        // 拉美
        ["colombia"]   = new("colombia",   "哥伦比亚",   "拉美", "焦糖、红苹果、坚果、平衡甜感", "Supremo / Excelso"),
        ["brazil"]     = new("brazil",     "巴西",       "拉美", "坚果、黑巧、低酸、醇厚", "NY 2 / NY 3（Santos）"),
        ["guatemala"]  = new("guatemala",  "危地马拉",   "拉美", "可可、香料、柑橘、均衡", "SHB / EP"),
        ["costarica"]  = new("costarica",  "哥斯达黎加", "拉美", "蜜桃、蜂蜜、柑橘、干净", "SHB（Strictly Hard Bean）"),
        ["panama"]     = new("panama",     "巴拿马",     "拉美", "茉莉、蜜桃、柑橘（瑰夏名产区）", "Geisha / Special"),
        ["honduras"]   = new("honduras",   "洪都拉斯",   "拉美", "焦糖、红苹果、柔顺", "HG / SHG"),
        ["mexico"]     = new("mexico",     "墨西哥",     "拉美", "坚果、巧克力、温和低酸", "Altura / HG"),
        // 亚洲 / 大洋洲
        ["sumatra"]    = new("sumatra",    "印尼·曼特宁", "亚洲", "草本、木质、醇厚低酸（湿刨）", "G1 / G2（Mandheling）"),
        ["java"]       = new("java",      "印尼·爪哇",   "亚洲", "雪松、香料、醇厚低酸", "Grade I / II"),
        ["india"]      = new("india",     "印度",       "亚洲", "香料、坚果、低酸醇厚（季风马拉巴）", "Plantation A / B"),
        ["vietnam"]    = new("vietnam",   "越南",       "亚洲", "罗布斯塔为主，醇厚苦甜、低酸", "Grade 1–5（Robusta 主）"),
        ["yunnan"]     = new("yunnan",    "中国·云南",  "亚洲", "红糖、坚果、柔和顺口", "一级 / 二级"),
        ["papua"]      = new("papua",     "巴布亚新几内亚", "大洋洲", "柑橘、焦糖、明亮均衡", "A / AA"),
        // 兜底
        ["other"]      = new("other",     "其他 / 未知产区", "—", "风味以豆种与处理法为主，无特定产区走向", "按豆种通用分级"),
    };

    // ---------- 滤杯 ----------
    public sealed record DripperProfile(string Id, string Label, double FlowBase, bool Immersion, string Note);
    public static readonly Dictionary<string, DripperProfile> DripperProfiles = new()
    {
        ["v60"]     = new("v60",     "V60 锥形滤杯",        5.0, false, "锥形流速快，细水流由内向外画圈"),
        ["wave"]    = new("wave",    "蛋糕滤杯",            6.5, false, "平底流速适中，注水均匀"),
        ["origami"] = new("origami", "Origami折纸滤杯", 5.5, false, "树脂平底可配平底滤纸，螺旋肋骨导流"),
        ["chemex"]  = new("chemex",  "Chemex 玻璃滤杯",     3.5, false, "一体式玻璃滤杯，厚滤纸流速偏慢，干净透亮"),
        ["switch"]  = new("switch",  "Switch 聪明杯",       4.5, true,  "阀门切换可浸泡可滴滤，兼具浸泡与滴滤"),
        ["gina"]    = new("gina",    "Gina 滤杯",           4.0, true,  "带称重和阀门，可浸泡可滴滤，精准控流"),
        ["smart"]   = new("smart",   "聪明杯（浸泡）",      4.0, true,  "阀门控水、浸泡式萃取，粉水接触久"),
        ["hario"]   = new("hario",   "布里斯塔平底滤杯",    7.5, false, "平底快流，注水顺畅，适合稳定大水流"),
    };

    // ---------- 滤纸 / 滤网 ----------
    public sealed record FilterProfile(string Id, string Label, bool Rinse, double FlowAdj, string Note);
    public static readonly Dictionary<string, FilterProfile> FilterProfiles = new()
    {
        ["bleached"]   = new("bleached",   "漂白滤纸",     false, 0.0,  "无味，无需润湿"),
        ["abaca"]      = new("abaca",      "大麻纤维滤纸", false, 1.5,  "流速快无需润湿，纸味低，适合快冲"),
        ["unbleached"] = new("unbleached", "原色滤纸",     true, -0.5, "需热水润湿去纸味"),
        ["sisal"]      = new("sisal",      "剑麻滤布",     true,  0.8, "纤维滤布，口感细腻，需润湿"),
        ["metal"]      = new("metal",      "金属滤网",     true,  2.0,  "流速快、保留油脂，研磨调粗"),
        ["cloth"]      = new("cloth",      "绒布滤布",     true,  0.5,  "需润湿，口感圆润"),
    };

    // ---------- 研磨度 ----------
    // C40 = Comandante C40（手磨常见刻度，数值越大越细，约 8–30 为手冲区间）；
    // EK = Mahlkönig EK43（意式/单品商用，刻度 1–14 左右，数值越小越细）。
    // 二者为常用磨的近似映射，供用户照刻度调磨。
    public sealed record GrindProfile(string Id, string Label, double BloomMul, double TempAdj, double FlowAdj, double RecFlow, int C40, int EK, string Note);
    public static readonly Dictionary<string, GrindProfile> GrindProfiles = new()
    {
        ["coarse"]       = new("coarse",       "粗",     1.05, 1,   2.5, 9.0, 28, 11, "流速快、萃取慢，水温略升（C40≈28 / EK≈11）"),
        ["medium_coarse"]= new("medium_coarse","中粗",   1.00, 0.5, 1.2, 7.0, 24, 9,  "通用偏粗（C40≈24 / EK≈9）"),
        ["medium"]       = new("medium",       "中",     1.00, 0,   0.0, 6.0, 20, 7,  "中研磨通用（C40≈20 / EK≈7）"),
        ["medium_fine"]  = new("medium_fine",  "中细",   0.98, -0.5, -1.2, 5.0, 16, 5, "通用偏细（C40≈16 / EK≈5）"),
        ["fine"]         = new("fine",         "细",     0.95, -1, -2.5, 4.0, 12, 3,  "易过萃、流速慢，水温降、水流细（C40≈12 / EK≈3）"),
    };

    // 研磨度排序（粗→细，索引越大越细）。用于按「滤杯 + 烘焙度」推荐研磨度。
    private static readonly string[] GrindOrder = { "coarse", "medium_coarse", "medium", "medium_fine", "fine" };

    /// <summary>
    /// 推荐研磨度（非用户选择项）：以滤杯为基准，烘焙度越浅越细、越深越粗。
    /// 浅烘排气强、密度高，需更细以延长接触；深烘易过萃，需更粗。
    /// </summary>
    /// <summary>研磨推荐：滤杯定基准档，烘焙度修正（浅烘更细 / 深烘更粗），豆密度再修正
    ///（致密的高海拔硬豆结构紧实、易萃取不足 → 再磨细一档；疏松的豆（多见低海拔/深烘）孔隙多 → 再磨粗一档防过萃）。</summary>
    public static string RecommendGrind(string dripper, string roast, string method, string? density = null)
    {
        int baseIdx = dripper switch
        {
            "wave" => Array.IndexOf(GrindOrder, "medium_coarse"),      // 波浪滤杯：medium → medium_coarse（偏粗）
            "smart" => Array.IndexOf(GrindOrder, "coarse"), // 聪明杯：medium_coarse → coarse（更粗）
            _ => Array.IndexOf(GrindOrder, "medium_coarse"),    // v60 及其他：medium_coarse（偏粗）
        };
        int adj = roast switch
        {
            "blonde" or "super_light" or "ultra_light" or "light" or "medium_light" => 1,   // 更细
            "medium_dark" or "dark" or "ultra_dark" or "extreme_dark" => -1, // 更粗
            _ => 0,
        };
        adj += density switch
        {
            "dense" => 1,    // 致密硬豆：磨细一档保证萃取
            "light" => -1,   // 疏松豆：磨粗一档防过萃
            _ => 0,          // 中等（默认）不修正
        };
        int idx = Math.Clamp(baseIdx + adj, 0, GrindOrder.Length - 1);
        return GrindOrder[idx];
    }

    /// <summary>由种植海拔推导咖啡豆密度档（海拔越高→慢熟→细胞壁厚→密度越高）。
    /// ≥1700m 致密(dense) / 1200–1699m 中等(medium) / &lt;1200m 疏松(light)。与网页端 brew-engine.js 的分档保持一致。</summary>
    public static string DensityFromAltitude(int altitudeM)
    {
        if (altitudeM >= 1700) return "dense";
        if (altitudeM >= 1200) return "medium";
        return "light";
    }

    // ---------- 水质推荐 ----------
    // 与烘焙度关联：浅烘（高酸高香）宜用稍高 TDS（~150mg/L）衬风味；深烘宜用低 TDS（~75–100）避免放大苦涩。
    // 碳酸氢根(HCO₃⁻)决定缓冲能力，过高会中和酸质；镁/钙影响甜感萃取。
    public sealed record WaterProfile(string Id, string Label, int TdsMin, int TdsMax, int GhMin, int GhMax, int KhMin, int KhMax, string Note);
    public static readonly Dictionary<string, WaterProfile> WaterProfiles = new()
    {
        ["light_water"] = new("light_water", "浅烘专用软水", 130, 160, 3, 5, 2, 4, "TDS 130–160 / 总硬度(GH)3–5 / 碳酸盐(KH)2–4：衬浅烘花果酸香，不过度稀释"),
        ["balanced"]    = new("balanced",    "均衡水质",      100, 140, 2, 4, 1, 3, "TDS 100–140 / GH2–4 / KH1–3：通用手冲，酸甜平衡"),
        ["dark_water"]  = new("dark_water",  "深烘专用低矿",  70, 100, 1, 3, 1, 2, "TDS 70–100 / GH1–3 / KH1–2：降矿质避免深烘苦涩放大"),
        ["soft"]        = new("soft",        "极软水",        40, 70, 0, 2, 0, 1, "TDS 40–70：极低矿，突出干净，但易萃取不足（慎用于深烘）"),
    };

    /// <summary>依据烘焙度推荐水质（浅烘偏高矿、深烘偏低矿）。</summary>
    public static string RecommendWater(string roast)
    {
        return roast switch
        {
            "blonde" or "super_light" or "ultra_light" or "light" or "medium_light" => "light_water",
            "medium" => "balanced",
            "medium_dark" or "dark" => "dark_water",
            "ultra_dark" or "extreme_dark" => "soft",
            _ => "balanced",
        };
    }

    private static List<Phase> Immersion(double dose, double total, string size, Ctx ctx)
    {
        var p = SizeProfiles[size];
        double bloom = Round1(dose * p.BloomMul);
        double p1 = Round1(total * 0.5);
        int steep = (int)Clamp(Math.Round(120 * ctx.Rf), 90, 200);
        return new List<Phase>
        {
            new() { Name = I18n.T("PhBloomPour"), Type = PhaseType.Pour, Target = bloom, Tip = string.Format(I18n.T("TipBloomSmart"), bloom) },
            new() { Name = I18n.T("PhBloomWait"), Type = PhaseType.Wait, DurationSec = ctx.BloomWait, Tip = string.Format(I18n.T("TipBloomWaitSimple"), ctx.BloomWait) },
            new() { Name = I18n.T("PhPour1"), Type = PhaseType.Pour, Target = p1, Tip = string.Format(I18n.T("TipPourTo"), p1) },
            new() { Name = I18n.T("PhPour2"), Type = PhaseType.Pour, Target = total, Tip = string.Format(I18n.T("TipPourToTotalSteep"), total) },
            new() { Name = I18n.T("PhSteep"), Type = PhaseType.Wait, DurationSec = steep, Tip = string.Format(I18n.T("TipSteepClose"), steep) },
            new() { Name = I18n.T("PhDrawEnd"), Type = PhaseType.Wait, DurationSec = p.DrawWait, Tip = string.Format(I18n.T("TipSteepDraw"), p.DrawWait) },
        };
    }

    /// <summary>养豆期排气系数：越新鲜排气越猛 → 闷蒸越长。</summary>
    public static double RestFactor(double days)
    {
        days = Clamp(days, 0, 120);
        if (days <= 1) return 1.8;
        if (days <= 3) return 1.5;
        if (days <= 7) return 1.2;
        if (days <= 14) return 1.0;   // 理想窗口
        if (days <= 21) return 0.85;
        if (days <= 30) return 0.7;
        return 0.55;                  // 超过 30 天老化
    }

    /// <summary>养豆期推荐：结合烘培度给出状态与建议。</summary>
    public static RestRec RecommendRest(string roast, double days)
    {
        var prof = RoastProfiles.GetValueOrDefault(roast, RoastProfiles["medium"]);
        int lo = prof.RestIdeal[0], hi = prof.RestIdeal[1];
        string status, tip;
        if (days < lo)
        {
            var wait = lo - (int)days;
            status = I18n.T("RestFresh");
            tip = string.Format(I18n.T("RestTipFresh"), wait, lo, hi);
        }
        else if (days <= hi)
        {
            status = I18n.T("RestBest");
            tip = string.Format(I18n.T("RestTipBest"), lo, hi);
        }
        else if (days <= hi + 14)
        {
            status = I18n.T("RestFading");
            tip = string.Format(I18n.T("RestTipFading"), hi);
        }
        else
        {
            status = I18n.T("RestPast");
            tip = string.Format(I18n.T("RestTipPast"), hi + 14);
        }
        return new RestRec { IdealMin = lo, IdealMax = hi, Status = status, Tip = tip };
    }

    // ---------- 配方构建器上下文 ----------
    private sealed class Ctx
    {
        public string Roast = "";
        public RoastProfile RoastProf = default!;
        public string Flavor = "";
        public FlavorProfile FlavorProf = default!;
        public double RestDays;
        public int BloomWait;
        public int Temp;
        public double Rf;
    }

    private static List<Phase> Classic(double dose, double total, string size, Ctx ctx)
    {
        var p = SizeProfiles[size];
        double bloom = Round1(dose * p.BloomMul);
        double remaining = total - bloom;
        int segs = p.Segments;
        var phases = new List<Phase>
        {
            new() { Name = I18n.T("PhBloomPour"), Type = PhaseType.Pour, Target = bloom, Tip = string.Format(I18n.T("TipBloomClassic"), bloom, I18n.T("Size_" + p.Id), p.BloomMul) },
            new() { Name = I18n.T("PhBloomWait"), Type = PhaseType.Wait, DurationSec = ctx.BloomWait, Tip = string.Format(I18n.T("TipBloomWaitCtx"), ctx.BloomWait, I18n.T("Roast_" + ctx.Roast), (int)ctx.RestDays, I18n.T("Flavor_" + ctx.Flavor)) },
        };
        for (int i = 1; i <= segs; i++)
        {
            double target = Round1(bloom + remaining * (i / (double)segs));
            phases.Add(new() { Name = string.Format(I18n.T("PhPourN"), i), Type = PhaseType.Pour, Target = target, Tip = string.Format(I18n.T("TipPourCircle"), target) });
            if (i < segs) phases.Add(new() { Name = I18n.T("PhWaitDown"), Type = PhaseType.Wait, DurationSec = p.SegGap, Tip = string.Format(I18n.T("TipWaitGap"), p.SegGap) });
        }
        phases.Add(new() { Name = I18n.T("PhDrawEnd"), Type = PhaseType.Wait, DurationSec = p.DrawWait, Tip = string.Format(I18n.T("TipDrawEnd"), p.DrawWait) });
        return phases;
    }

    /// <summary>一刀流/单次大流量注水法（oneshot/osl/rao）：闷蒸后一次性注水到总量，再滴滤收尾。</summary>
    private static List<Phase> Oneshot(double dose, double total, string size, Ctx ctx)
    {
        var p = SizeProfiles[size];
        double bloom = Round1(dose * p.BloomMul);
        return new List<Phase>
        {
            new() { Name = I18n.T("PhBloomPour"), Type = PhaseType.Pour, Target = bloom, Tip = string.Format(I18n.T("TipBloomClassic"), bloom, I18n.T("Size_" + p.Id), p.BloomMul) },
            new() { Name = I18n.T("PhBloomWait"), Type = PhaseType.Wait, DurationSec = ctx.BloomWait, Tip = string.Format(I18n.T("TipBloomWaitCtx"), ctx.BloomWait, I18n.T("Roast_" + ctx.Roast), (int)ctx.RestDays, I18n.T("Flavor_" + ctx.Flavor)) },
            new() { Name = I18n.T("PhPour1"), Type = PhaseType.Pour, Target = total, Tip = string.Format(I18n.T("TipPourToTotal"), total) },
            new() { Name = I18n.T("PhDrawEnd"), Type = PhaseType.Wait, DurationSec = p.DrawWait, Tip = string.Format(I18n.T("TipDrawEnd"), p.DrawWait) },
        };
    }

    private static List<Phase> Kasuya46(double dose, double total, string size, Ctx ctx)
    {
        var p = SizeProfiles[size];
        double step = Round1(total * 0.2); // 五段等水量，每段 20%
        int firstWait = (int)Clamp(Math.Round(p.K46First * ctx.Rf * ctx.FlavorProf.BloomMul), 10, 75);
        int lateWait = (int)Clamp(Math.Round(p.K46Late * ctx.Rf), 10, 60);
        var phases = new List<Phase>();
        for (int i = 0; i < 5; i++)
        {
            double cum = Round1(step * (i + 1));
            phases.Add(new() { Name = string.Format(I18n.T("PhPourN"), i + 1), Type = PhaseType.Pour, Target = cum, Tip = string.Format(I18n.T("TipPourTo"), cum) });
            if (i < 4)
            {
                int wait = i < 2 ? firstWait : lateWait;
                phases.Add(new() { Name = string.Format("{0} {1}", I18n.T("PhWaitDown"), i + 1), Type = PhaseType.Wait, DurationSec = wait, Tip = string.Format(I18n.T("TipKasuyaWait"), wait, i < 2 ? I18n.T("TipKasuyaSweet") : I18n.T("TipKasuyaStrength")) });
            }
        }
        phases.Add(new() { Name = I18n.T("PhDrawDone"), Type = PhaseType.Wait, DurationSec = p.DrawWait, Tip = string.Format(I18n.T("TipDrawDone"), p.DrawWait) });
        return phases;
    }

    private static List<Phase> Light(double dose, double total, string size, Ctx ctx)
    {
        var p = SizeProfiles[size];
        double bloom = Round1(dose * (p.BloomMul + 1.0));
        double p1 = Round1(total * 0.55), p2 = Round1(total * 0.85);
        return new List<Phase>
        {
            new() { Name = I18n.T("PhBloomPour"), Type = PhaseType.Pour, Target = bloom, Tip = string.Format(I18n.T("TipBloomLight"), bloom, I18n.T("Size_" + p.Id)) },
            new() { Name = I18n.T("PhBloomWait"), Type = PhaseType.Wait, DurationSec = ctx.BloomWait, Tip = string.Format(I18n.T("TipBloomLightWait"), ctx.BloomWait) },
            new() { Name = I18n.T("PhPour1"), Type = PhaseType.Pour, Target = p1, Tip = string.Format(I18n.T("TipPourTo"), p1) },
            new() { Name = I18n.T("PhWaitDown"), Type = PhaseType.Wait, DurationSec = p.SegGap, Tip = string.Format(I18n.T("TipWaitSimple"), p.SegGap) },
            new() { Name = I18n.T("PhPour2"), Type = PhaseType.Pour, Target = p2, Tip = string.Format(I18n.T("TipPourTo"), p2) },
            new() { Name = I18n.T("PhWaitDown"), Type = PhaseType.Wait, DurationSec = p.SegGap, Tip = string.Format(I18n.T("TipWaitSimple"), p.SegGap) },
            new() { Name = I18n.T("PhPour3"), Type = PhaseType.Pour, Target = total, Tip = string.Format(I18n.T("TipPourToTotal"), total) },
            new() { Name = I18n.T("PhDrawEnd"), Type = PhaseType.Wait, DurationSec = p.DrawWait, Tip = string.Format(I18n.T("TipDrawSimple"), p.DrawWait) },
        };
    }

    /// <summary>逆向注水法：中心细流、由外向内画圈，针对深烘防过萃、提升均匀度。</summary>
    private static List<Phase> Reverse(double dose, double total, string size, Ctx ctx)
    {
        var p = SizeProfiles[size];
        double bloom = Round1(dose * p.BloomMul);
        double remaining = total - bloom;
        int segs = p.Segments;
        var phases = new List<Phase>
        {
            new() { Name = I18n.T("PhBloomPour"), Type = PhaseType.Pour, Target = bloom, Tip = string.Format(I18n.T("TipBloomRev"), bloom) },
            new() { Name = I18n.T("PhBloomWait"), Type = PhaseType.Wait, DurationSec = ctx.BloomWait, Tip = string.Format(I18n.T("TipBloomWaitSimple"), ctx.BloomWait) },
        };
        for (int i = 1; i <= segs; i++)
        {
            double target = Round1(bloom + remaining * (i / (double)segs));
            phases.Add(new() { Name = string.Format(I18n.T("PhPourNRev"), i), Type = PhaseType.Pour, Target = target, Tip = string.Format(I18n.T("TipPourRev"), target) });
            if (i < segs) phases.Add(new() { Name = I18n.T("PhWaitDown"), Type = PhaseType.Wait, DurationSec = p.SegGap, Tip = string.Format(I18n.T("TipWaitSimple"), p.SegGap) });
        }
        phases.Add(new() { Name = I18n.T("PhDrawEnd"), Type = PhaseType.Wait, DurationSec = p.DrawWait, Tip = string.Format(I18n.T("TipDrawEnd"), p.DrawWait) });
        return phases;
    }

    /// <summary>瑞士搅拌法：每段注水后用匙搅破粉墙，提升萃取均匀度（Swiss Water 思路的家用化）。</summary>
    private static List<Phase> Swiss(double dose, double total, string size, Ctx ctx)
    {
        var p = SizeProfiles[size];
        double bloom = Round1(dose * p.BloomMul);
        double remaining = total - bloom;
        int segs = Math.Max(2, p.Segments - 1);
        var phases = new List<Phase>
        {
            new() { Name = I18n.T("PhBloomPour"), Type = PhaseType.Pour, Target = bloom, Tip = string.Format(I18n.T("TipBloomSwiss"), bloom) },
            new() { Name = I18n.T("PhBloomWait"), Type = PhaseType.Wait, DurationSec = ctx.BloomWait, Tip = string.Format(I18n.T("TipBloomWaitSimple"), ctx.BloomWait) },
            new() { Name = I18n.T("PhStir"), Type = PhaseType.Wait, DurationSec = 8, Tip = I18n.T("TipStirBloom") },
        };
        for (int i = 1; i <= segs; i++)
        {
            double target = Round1(bloom + remaining * (i / (double)segs));
            phases.Add(new() { Name = string.Format(I18n.T("PhPourN"), i), Type = PhaseType.Pour, Target = target, Tip = string.Format(I18n.T("TipPourTo"), target) });
            phases.Add(new() { Name = string.Format(I18n.T("PhStirN"), i), Type = PhaseType.Wait, DurationSec = 10, Tip = string.Format(I18n.T("TipStirN"), i) });
        }
        phases.Add(new() { Name = I18n.T("PhDrawEnd"), Type = PhaseType.Wait, DurationSec = p.DrawWait, Tip = string.Format(I18n.T("TipDrawEnd"), p.DrawWait) });
        return phases;
    }

    /// <summary>SCA 杯测法（用户 2026-09-13 最终确认）：注入 94℃ 热水至总量 → 静置浸泡 4 分钟（240s），单段、无闷蒸、不破壳、不过滤。
    /// 杯测为静态评估模式：所有阶段按「时间」推进，不依赖注水模拟（IsCupping=true）。
    /// 第 1 阶段（PourIn）只作「注水浸润引导」，给 DurationSec 让状态机按时推进；第 2 阶段（Soak）为静置浸泡。</summary>
    private static List<Phase> Cupping(double dose, double total, string size, Ctx ctx)
    {
        return new List<Phase>
        {
            new() { Name = I18n.T("PhPourIn"), Type = PhaseType.Pour, Target = total, DurationSec = 30, Tip = string.Format(I18n.T("TipCuppingPour"), total) },
            new() { Name = I18n.T("PhSoak"), Type = PhaseType.Wait, DurationSec = 240, Tip = I18n.T("TipCuppingSoak") },
        };
    }

    /// <summary>计算冲煮配方。size 不传则按 dose 自动推断。</summary>
    public static Recipe ComputeRecipe(RecipeOptions? opts)
    {
        opts ??= new();
        double dose = Clamp(opts.Dose ?? 15, 5, 60);
        double ratio = Clamp(opts.Ratio ?? 15, 8, 20);
        string method = MethodProfiles.ContainsKey(opts.Method ?? "") ? opts.Method! : "classic"; // 默认经典分段法
        string size = SizeProfiles.ContainsKey(opts.Size ?? "") ? opts.Size! : DetectSize(dose);
        string roast = RoastProfiles.ContainsKey(opts.Roast ?? "") ? opts.Roast! : "light";
        // 具体烘焙度 Ag 读数：用户可输入，默认取所选档位区间中值
        int roastAg = opts.RoastAg ?? ((RoastProfiles[roast].AgMin + RoastProfiles[roast].AgMax) / 2);
        string flavor = FlavorProfiles.ContainsKey(opts.Flavor ?? "") ? opts.Flavor! : "balanced";
        string process = ProcessProfiles.ContainsKey(opts.Process ?? "") ? opts.Process! : "washed";
        string dripper = DripperProfiles.ContainsKey(opts.Dripper ?? "") ? opts.Dripper! : "v60";
        string filter = FilterProfiles.ContainsKey(opts.Filter ?? "") ? opts.Filter! : "bleached";
        string origin = OriginProfiles.ContainsKey(opts.Origin ?? "") ? opts.Origin! : "ethiopia";
        string variety = VarietyProfiles.ContainsKey(opts.Variety ?? "") ? opts.Variety! : "heirloom";
        // 研磨度由引擎按「滤杯 + 烘焙度 + 豆密度」推荐（非用户选择项），允许 opts.Grind 显式覆盖（测试/高级用）
        string grind = (opts.Grind != null && GrindProfiles.ContainsKey(opts.Grind)) ? opts.Grind! : RecommendGrind(dripper, roast, method, opts.Density);
        // 水质推荐（与烘焙度关联：浅烘偏高矿、深烘偏低矿）
        string water = RecommendWater(roast);
        // 养豆天数：优先由烘焙日期推算（与界面一致），否则用显式值，默认 10 天
        double restDays = Clamp(opts.RestDays ?? 10, 0, 120);
        if (opts.RoastDate.HasValue)
            restDays = Clamp((DateTime.Today - opts.RoastDate.Value.Date).Days, 0, 120);

        var pp = ProcessProfiles[process];
        var dp = DripperProfiles[dripper];
        var fp = FilterProfiles[filter];
        var gp = GrindProfiles[grind];

        // 研磨度的 BloomMul / TempAdj 仅在「用户显式覆盖」时参与配方：
        // 推荐研磨度由烘焙度推导，其粗细已体现在 RecommendedFlow；若再叠加 TempAdj/BloomMul 会与烘焙度修正重复。
        bool grindOverridden = opts.Grind != null && GrindProfiles.ContainsKey(opts.Grind);
        double grindBloomMul = grindOverridden ? gp.BloomMul : 1.0;
        double grindTempAdj = grindOverridden ? gp.TempAdj : 0.0;

        double rf = RestFactor(restDays);
        int bloomWait = (int)Clamp(Math.Round(RoastProfiles[roast].BloomBase * rf * FlavorProfiles[flavor].BloomMul * pp.BloomMul * grindBloomMul), 10, 75);
        int tempRaw = (int)Clamp(RoastProfiles[roast].Temp + FlavorProfiles[flavor].TempAdj + pp.TempAdj + grindTempAdj, 80, 96);
        int temp = tempRaw; // 推荐水温（2026-09-08 用户要求：移除海拔沸点封顶逻辑，水温仅由烘焙度/风味/处理法/研磨决定）
        if (method == "cupping") temp = 94; // 杯测法固定 94℃（用户 2026-09-13 确认），不套用烘焙度/密度/海拔修正链
        var restRec = RecommendRest(roast, restDays);
        // 养豆期满日期：烘焙日期 + 理想养豆期（最早可赏味 / 完全养豆）
        DateTime restReadyMin = opts.RoastDate.HasValue ? opts.RoastDate.Value.Date.AddDays(restRec.IdealMin) : default;
        DateTime restReadyMax = opts.RoastDate.HasValue ? opts.RoastDate.Value.Date.AddDays(restRec.IdealMax) : default;

        double ratioEff = Clamp(ratio + pp.RatioAdj, 8, 20);
        double recommendedFlow = Clamp(dp.FlowBase + gp.FlowAdj + fp.FlowAdj, 3, 14);
        bool needRinse = fp.Rinse;

        var ctx = new Ctx
        {
            Roast = roast, RoastProf = RoastProfiles[roast],
            Flavor = flavor, FlavorProf = FlavorProfiles[flavor],
            RestDays = restDays, BloomWait = bloomWait, Temp = temp, Rf = rf,
        };

        // 阶段构建局部函数：给定有效粉水比与流速 → (阶段序列, 注水段数, 总时长, 总水量)。
        // 抽成局部函数是为了黄金杯自动校准：调 ratio 后需按新总量重建阶段。
        (List<Phase> Phases, int NPours, double TotalTimeSec, double Total) BuildPhases(double ratioLocal, double flow)
        {
            double totalLocal = Round1(dose * ratioLocal);
            List<Phase> raw = method == "cupping" ? Cupping(dose, totalLocal, size, ctx)
                : dp.Immersion ? Immersion(dose, totalLocal, size, ctx)
                : method switch
                {
                    "kasuya46" => Kasuya46(dose, totalLocal, size, ctx),
                    "light" => Light(dose, totalLocal, size, ctx),
                    "reverse" => Reverse(dose, totalLocal, size, ctx),
                    "swiss" => Swiss(dose, totalLocal, size, ctx),
                    "oneshot" or "osl" or "rao" => Oneshot(dose, totalLocal, size, ctx),
                    _ => Classic(dose, totalLocal, size, ctx),
                };

            double acc = 0;
            var phs = new List<Phase>();
            int i = 0;
            foreach (var p in raw)
            {
                var ph = new Phase { Id = i, Name = p.Name, Type = p.Type, Target = p.Target, DurationSec = p.DurationSec, Tip = p.Tip, StartWeight = acc };
                if (ph.Type == PhaseType.Pour) acc = ph.Target;
                phs.Add(ph);
                i++;
            }
            double tt = phs.Sum(p => p.DurationSec);
            // 修正：Pour 阶段实际按重量推进，需计入注水时间 = 注水量 / 推荐流速
            // 注水时间仅计入「按重量推进」的非杯测段；杯测法注水段已由 DurationSec 计时驱动，避免双重计时夸大总时长
            foreach (var ph in phs)
            {
                if (ph.Type == PhaseType.Pour && ph.DurationSec <= 0)
                {
                    double pourAmount = ph.Target - ph.StartWeight;
                    tt += pourAmount / flow;
                }
            }
            return (phs, phs.Count(p => p.Type == PhaseType.Pour), tt, totalLocal);
        }

        var (phases, nPours, totalTimeSec, total) = BuildPhases(ratioEff, recommendedFlow);
        var extraction = EstimateExtraction(gp.C40, temp, nPours, ratioEff, roast, process, water, (int)restDays);

        // —— 黄金杯自动校准（2026-09-03 用户需求：生成的推荐方案必须落入 SCA 黄金杯）——
        // EY 出界→研磨微调（仅引擎推荐研磨，用户显式指定的研磨不覆盖；最多 2 格，0.30%/格）；
        // TDS 出界→粉水比最小步校正（上界 1.44 留 0.01 余量 / 下界 1.16），取黄金杯边界内最近的 0.1 步值。
        // 杯测为静态评估标准（1:18.18，统一基准 11g 粉 / 200g 94℃ 热水），不参与校准。
        string goldenTune = "";
        if (!extraction.IsGolden && method != "cupping")
        {
            string oldGrindLabel = I18n.T("Grind_" + gp.Id);
            bool grindMoved = false;
            if (!grindOverridden)
            {
                int gIdx = Array.IndexOf(GrindOrder, grind);
                int guard = 2;
                while (extraction.Ey < 18 && gIdx < GrindOrder.Length - 1 && guard-- > 0) { gIdx++; grindMoved = true; }
                guard = 2;
                while (extraction.Ey > 22 && gIdx > 0 && guard-- > 0) { gIdx--; grindMoved = true; }
                if (grindMoved)
                {
                    grind = GrindOrder[gIdx];
                    gp = GrindProfiles[grind];
                    recommendedFlow = Clamp(dp.FlowBase + gp.FlowAdj + fp.FlowAdj, 3, 14);
                    extraction = EstimateExtraction(gp.C40, temp, nPours, ratioEff, roast, process, water, (int)restDays);
                }
            }
            double oldRatio = ratioEff;
            if (extraction.Tds > 1.45)
            {
                double r = Math.Ceiling((extraction.Ey / 1.45 + AbsorbCoef) * 10) / 10;
                while (r < 20 && Math.Round(extraction.Ey / Math.Max(1.0, r - AbsorbCoef), 2) > 1.44) r += 0.1;
                ratioEff = Clamp(r, 8, 20);
            }
            else if (extraction.Tds < 1.15)
            {
                double r = Math.Floor((extraction.Ey / 1.15 + AbsorbCoef) * 10) / 10;
                while (r > 8 && Math.Round(extraction.Ey / Math.Max(1.0, r - AbsorbCoef), 2) < 1.16) r -= 0.1;
                ratioEff = Clamp(r, 8, 20);
            }
            bool ratioMoved = Math.Abs(ratioEff - oldRatio) >= 0.05;
            if (grindMoved || ratioMoved)
            {
                var tuneParts = new List<string>();
                if (grindMoved) tuneParts.Add(string.Format(I18n.T("GCTuneGrind"), oldGrindLabel, I18n.T("Grind_" + gp.Id)));
                if (ratioMoved) tuneParts.Add(string.Format(I18n.T("GCTuneRatio"), oldRatio.ToString("0.#"), ratioEff.ToString("0.#")));
                goldenTune = string.Format(I18n.T("GCTuneHead"), string.Join("；", tuneParts));
                // 按最终参数重建阶段并出具最终诊断（nPours 不随 ratio 变化，EY 稳定 → 校准结果确定）
                (phases, nPours, totalTimeSec, total) = BuildPhases(ratioEff, recommendedFlow);
                extraction = EstimateExtraction(gp.C40, temp, nPours, ratioEff, roast, process, water, (int)restDays);
            }
        }

        return new Recipe
        {
            Method = method, MethodLabel = I18n.T("Method_" + method), IsCupping = method == "cupping", Dose = dose, Ratio = ratioEff, Size = size, SizeLabel = I18n.T("Size_" + size),
            Roast = roast, RoastLabel = I18n.T("Roast_" + roast), RoastAg = roastAg, RoastAgMin = RoastProfiles[roast].AgMin, RoastAgMax = RoastProfiles[roast].AgMax,
            Flavor = flavor, FlavorLabel = I18n.T("Flavor_" + flavor),
            Process = process, ProcessLabel = I18n.T("Process_" + process),
            Dripper = method == "cupping" ? "" : dripper,
            DripperLabel = method == "cupping" ? "—" : I18n.T("Dripper_" + dripper),
            Filter = method == "cupping" ? "" : filter,
            FilterLabel = method == "cupping" ? "—" : I18n.T("Filter_" + filter),
            Grind = method == "cupping" ? "cupping" : grind,
            GrindLabel = method == "cupping" ? I18n.T("Grind_cupping") : I18n.T("Grind_" + grind),
            GrindC40 = method == "cupping" ? 25 : gp.C40,
            GrindEK = method == "cupping" ? 9 : gp.EK,
            Water = water, WaterLabel = I18n.T("Water_" + water), WaterNote = WaterProfiles[water].Note,
            Origin = origin, OriginLabel = I18n.T("Origin_" + origin), OriginRegion = OriginProfiles[origin].Region, OriginFlavor = OriginProfiles[origin].Flavor, OriginGrade = OriginProfiles[origin].Grade,
            Variety = variety, VarietyLabel = I18n.T("Variety_" + variety),
            RecommendedFlow = recommendedFlow, NeedRinse = needRinse,
            RestDays = (int)restDays, BloomWait = bloomWait, Temp = temp, RestRec = restRec,
            RestReadyMinDate = restReadyMin, RestReadyMaxDate = restReadyMax,
            Extraction = extraction,
            GoldenCupTune = goldenTune,
            TotalWater = total, Phases = phases, TotalTimeSec = totalTimeSec,
        };
    }

    // ---------- 引擎状态机 ----------
    public static EngineState CreateState(Recipe recipe) => new()
    {
        Recipe = recipe, Running = false, ElapsedMs = 0, StartedAt = 0, PhaseIndex = 0,
        PhaseEnteredAt = 0, PhaseEnteredWeight = 0, Weight = 0, Flow = 0, Done = false,
        HasLastNow = false, HasLastW = false, Log = new(),
    };

    public static void Start(EngineState s, double nowMs)
    {
        if (s.Done) return;
        s.Running = true;
        s.StartedAt = (long)(nowMs - s.ElapsedMs);
        s.LastNow = nowMs; s.HasLastNow = true;
        s.LastW = s.Weight; s.HasLastW = true;
        PushLog(s, "开始冲煮");
    }

    public static void Pause(EngineState s, double nowMs)
    {
        if (!s.Running) return;
        s.ElapsedMs = nowMs - s.StartedAt;
        s.Running = false;
        PushLog(s, "暂停");
    }

    public static void Reset(EngineState s)
    {
        s.Running = false; s.ElapsedMs = 0; s.StartedAt = 0; s.PhaseIndex = 0;
        s.PhaseEnteredAt = 0; s.PhaseEnteredWeight = 0; s.Weight = 0; s.Flow = 0; s.Done = false;
        s.HasLastNow = false; s.LastNow = 0; s.HasLastW = false; s.LastW = 0; s.Log = new();
    }

    private static void PushLog(EngineState s, string name) => s.Log.Add(new LogEntry { Name = name, AtMs = s.ElapsedMs });

    private static void EnterNext(EngineState s)
    {
        var prev = s.PhaseIndex < s.Recipe.Phases.Count ? s.Recipe.Phases[s.PhaseIndex] : null;
        if (prev != null) PushLog(s, "完成：" + prev.Name);
        s.PhaseIndex++;
        s.PhaseEnteredAt = s.ElapsedMs;
        s.PhaseEnteredWeight = s.Weight;
        var cur = s.PhaseIndex < s.Recipe.Phases.Count ? s.Recipe.Phases[s.PhaseIndex] : null;
        if (cur != null) PushLog(s, "进入：" + cur.Name);
        if (s.PhaseIndex >= s.Recipe.Phases.Count)
        {
            s.Done = true;
            s.PhaseIndex = s.Recipe.Phases.Count;
            PushLog(s, "冲煮完成 🎉");
        }
    }

    /// <summary>手动进入下一阶段（用户可强制跳过）。</summary>
    public static void NextPhase(EngineState s, double nowMs)
    {
        if (s.Done) return;
        EnterNext(s);
    }

    /// <summary>手动回到上一阶段（用户可强制回退）。已在首阶段或首阶段之前则不动作，仅重置本阶段进入计时。</summary>
    public static void PrevPhase(EngineState s, double nowMs)
    {
        if (s.PhaseIndex <= 0)
        {
            // 已在首阶段：重置进入计时，重头开始本阶段（便于重新计时闷蒸等）
            s.PhaseEnteredAt = s.ElapsedMs;
            s.PhaseEnteredWeight = s.Weight;
            s.Done = false;
            return;
        }
        s.Done = false;
        s.PhaseIndex--;
        s.PhaseEnteredAt = s.ElapsedMs;
        s.PhaseEnteredWeight = s.Weight;
        if (s.PhaseIndex < s.Recipe.Phases.Count)
            PushLog(s, "回到：" + s.Recipe.Phases[s.PhaseIndex].Name);
    }

    private static void AdvancePhases(EngineState s)
    {
        int guard = 0;
        while (!s.Done && guard < 64)
        {
            guard++;
            var ph = s.PhaseIndex < s.Recipe.Phases.Count ? s.Recipe.Phases[s.PhaseIndex] : null;
            if (ph == null) { s.Done = true; break; }
            // 杯测法（静态评估模式）：所有阶段按时间推进，不依赖注水重量（注水模拟在 UI 已隐藏）
            if (ph.Type == PhaseType.Pour && !s.Recipe.IsCupping)
            {
                if (s.Weight >= ph.Target - 0.4) EnterNext(s);
                else break;
            }
            else
            {
                double pe = (s.ElapsedMs - s.PhaseEnteredAt) / 1000.0;
                if (pe >= ph.DurationSec) EnterNext(s);
                else break;
            }
        }
    }

    /// <summary>推进引擎，每帧调用。返回供 UI 渲染的快照。</summary>
    public static Snapshot Tick(EngineState s, double nowMs, double weight)
    {
        double dt = s.HasLastNow ? (nowMs - s.LastNow) / 1000.0 : 0;
        s.Weight = weight;
        if (s.Running)
        {
            s.ElapsedMs = nowMs - s.StartedAt;
            if (dt > 0 && s.HasLastW) s.Flow = (weight - s.LastW) / dt;
            AdvancePhases(s);
        }
        s.LastNow = nowMs; s.HasLastNow = true;
        s.LastW = weight; s.HasLastW = true;
        return SnapshotOf(s);
    }

    public static Snapshot SnapshotOf(EngineState s)
    {
        var r = s.Recipe;
        int idx = Math.Min(s.PhaseIndex, r.Phases.Count - 1);
        // 完成后不再返回最后一段阶段名，让 UI 显示「完成」而非残留的滴滤收尾
        var ph = (!s.Done && idx >= 0 && idx < r.Phases.Count) ? r.Phases[idx] : null;
        var warnings = new List<string>();
        double phaseProgress = 0;
        double? remainingSec = null;
        double target = 0;
        if (ph != null)
        {
            if (ph.Type == PhaseType.Pour)
            {
                double span = Math.Max(0.1, ph.Target - ph.StartWeight);
                target = ph.Target;
                phaseProgress = Clamp((s.Weight - ph.StartWeight) / span, 0, 1);
                // 杯测法 Pour 阶段（注水浸润引导）按时间计剩余；非杯测 Pour 无倒计时（依赖注水重量）
                if (s.Recipe.IsCupping && ph.DurationSec > 0)
                {
                    double pe = (s.ElapsedMs - s.PhaseEnteredAt) / 1000.0;
                    phaseProgress = Clamp(pe / ph.DurationSec, 0, 1);
                    remainingSec = Math.Max(0, ph.DurationSec - pe);
                }
            }
            else
            {
                target = ph.StartWeight;
                double pe = (s.ElapsedMs - s.PhaseEnteredAt) / 1000.0;
                phaseProgress = Clamp(pe / (ph.DurationSec == 0 ? 1 : ph.DurationSec), 0, 1);
                remainingSec = Math.Max(0, ph.DurationSec - pe);
            }
            if (ph.Type == PhaseType.Pour && s.Running)
            {
                double fastTh = Math.Max(10, r.RecommendedFlow * 1.7);
                if (s.Flow > fastTh) warnings.Add($"注水偏快（>{s.Flow:F1} g/s），建议 {r.RecommendedFlow:F0}–{r.RecommendedFlow * 1.4:F0} g/s");
                else if (s.Flow < 0.4 && (s.ElapsedMs - s.PhaseEnteredAt) > 1500) warnings.Add("注水停滞，请保持水流");
            }
        }
        double overallProgress = Clamp(s.Weight / r.TotalWater, 0, 1);
        int totalPhases = r.Phases.Count;
        int phaseNo = s.Done ? totalPhases : s.PhaseIndex + 1;
        SnapshotPhase? sp = ph == null ? null : new SnapshotPhase
        {
            Name = ph.Name, Type = ph.Type, Tip = ph.Tip, Target = target, StartWeight = ph.StartWeight,
            Progress = phaseProgress, RemainingSec = remainingSec,
        };
        return new Snapshot
        {
            Running = s.Running, Done = s.Done, ElapsedMs = s.ElapsedMs, Weight = s.Weight, Flow = s.Flow,
            RatioAchieved = s.Weight / r.Dose, TotalWater = r.TotalWater, Dose = r.Dose, Ratio = r.Ratio,
            Method = r.Method, MethodLabel = r.MethodLabel, Size = r.Size, SizeLabel = r.SizeLabel,
            Roast = r.Roast, RoastLabel = r.RoastLabel, RoastAgMin = r.RoastAgMin, RoastAgMax = r.RoastAgMax, Flavor = r.Flavor, FlavorLabel = r.FlavorLabel,
            RestDays = r.RestDays, BloomWait = r.BloomWait, Temp = r.Temp, RestRec = r.RestRec,
            RestReadyMinDate = r.RestReadyMinDate, RestReadyMaxDate = r.RestReadyMaxDate,
            Process = r.Process, ProcessLabel = r.ProcessLabel,
            Dripper = r.Dripper, DripperLabel = r.DripperLabel,
            Filter = r.Filter, FilterLabel = r.FilterLabel,
            Grind = r.Grind, GrindLabel = r.GrindLabel, GrindC40 = r.GrindC40, GrindEK = r.GrindEK,
            Water = r.Water, WaterLabel = r.WaterLabel, WaterNote = r.WaterNote,
            RecommendedFlow = r.RecommendedFlow, NeedRinse = r.NeedRinse,
            Origin = r.Origin, OriginLabel = r.OriginLabel, OriginFlavor = r.OriginFlavor, OriginGrade = r.OriginGrade,
            Variety = r.Variety, VarietyLabel = r.VarietyLabel,
            Extraction = r.Extraction,
            PhaseIndex = s.PhaseIndex, PhaseNo = phaseNo, TotalPhases = totalPhases,
            Phase = sp, OverallProgress = overallProgress, Warnings = warnings, Log = s.Log,
        };
    }
}
