using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CoffeeScale.Core;

/// <summary>
/// 大师清单 / 我的方案共用的「结构化方案项」：除正文（Title / Summary / Detail）外，
/// 额外携带 8 个可选的引擎联动字段（Method / Dripper / Dose / Ratio / Process / Origin / Roast / Flavor）。
/// 设计要点：处理方式 / 产地 / 烘焙度 无法从自由正文文本稳健识别（大师方案正文多为赛事参数，不写豆子信息），
/// 因此必须以结构化字段承载，应用时直接回灌引擎——这是「大师方案 → 引擎」可以完整联动的前提。
/// 字段全部可空：为空表示「该方案未指定」，应用时保留用户当前选择，不臆测覆盖。
/// </summary>
public sealed class MasterPlanItem
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Detail { get; set; } = "";
    // —— 结构化联动字段（引擎主键字符串；Dose/Ratio 为不变文化数值字符串）——
    public string? Method { get; set; }     // classic / kasuya46 / light / reverse / swiss / cupping
    public string? Dripper { get; set; }    // v60 / wave / smart
    public string? Filter { get; set; }     // bleached / unbleached / metal / cloth
    public string? Dose { get; set; }       // 粉量 g
    public string? Ratio { get; set; }      // 粉水比 N（1:N）
    public string? Process { get; set; }    // washed / natural / honey / anaerobic / wet_hulled / carbonic / barrel / k72
    public string? Origin { get; set; }     // ethiopia / kenya / …（见 BrewEngine.OriginProfiles）
    public string? Roast { get; set; }      // blonde … extreme_dark（见 BrewEngine.RoastProfiles）
    public string? Flavor { get; set; }     // balanced / sweet / acidic

    /// <summary>转为可持久化的「我的方案」（收藏大师方案时用，连带结构化字段一起落盘）。</summary>
    public MyPlan ToMyPlan() => new()
    {
        Title = Title, Summary = Summary, Detail = Detail,
        Method = Method, Dripper = Dripper, Filter = Filter, Dose = Dose, Ratio = Ratio,
        Process = Process, Origin = Origin, Roast = Roast, Flavor = Flavor,
    };
}

/// <summary>用户收藏的「我的方案」：一条从大师清单（或自定义）保存的冲煮方案，可离线复用、删除。
/// 存正文（Title/Summary/Detail）+ 可选的结构化联动字段（同 <see cref="MasterPlanItem"/>），
/// 应用时经 <see cref="BrewViewModel"/> 的 ApplyMasterPlan 回灌引擎，保持单一事实来源。
/// 新增字段均可空，旧版 myplans.json（无这些字段）反序列化后自动为 null，向后兼容。</summary>
public sealed class MyPlan
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Detail { get; set; } = ""; // 方案正文，应用时经 ApplyMasterPlan 回灌引擎
    public string? Method { get; set; }
    public string? Dripper { get; set; }
    public string? Filter { get; set; }
    public string? Dose { get; set; }
    public string? Ratio { get; set; }
    public string? Process { get; set; }
    public string? Origin { get; set; }
    public string? Roast { get; set; }
    public string? Flavor { get; set; }

    /// <summary>转为结构化方案项形状（统一走同一条回灌引擎的通路）。</summary>
    public MasterPlanItem AsItem() => new()
    {
        Title = Title, Summary = Summary, Detail = Detail,
        Method = Method, Dripper = Dripper, Filter = Filter, Dose = Dose, Ratio = Ratio,
        Process = Process, Origin = Origin, Roast = Roast, Flavor = Flavor,
    };

    /// <summary>便携分享文本的版本标记（首行）。版本号用于未来格式演进时的兼容判断。</summary>
    public const string ShareFormatVersion = "CoffeeScalePlan v1";
    private const string ShareSeparator = "---";

    /// <summary>把方案序列化为可读的便携分享文本：首行版本标记 + 「Key: value」头字段（10 个，缺失的跳过）
    /// + 分隔行「---」+ 正文（可多行）。可直接复制到剪贴板、写入 .txt 发送给他人，
    /// 或经 <see cref="FromShareText"/> 无损还原（含全部结构化联动字段）。</summary>
    public string ToShareText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(ShareFormatVersion);
        sb.AppendLine("Title: " + Title);
        if (!string.IsNullOrEmpty(Summary)) sb.AppendLine("Summary: " + Summary);
        if (!string.IsNullOrEmpty(Method)) sb.AppendLine("Method: " + Method);
        if (!string.IsNullOrEmpty(Dripper)) sb.AppendLine("Dripper: " + Dripper);
        if (!string.IsNullOrEmpty(Filter)) sb.AppendLine("Filter: " + Filter);
        if (!string.IsNullOrEmpty(Dose)) sb.AppendLine("Dose: " + Dose);
        if (!string.IsNullOrEmpty(Ratio)) sb.AppendLine("Ratio: " + Ratio);
        if (!string.IsNullOrEmpty(Process)) sb.AppendLine("Process: " + Process);
        if (!string.IsNullOrEmpty(Origin)) sb.AppendLine("Origin: " + Origin);
        if (!string.IsNullOrEmpty(Roast)) sb.AppendLine("Roast: " + Roast);
        if (!string.IsNullOrEmpty(Flavor)) sb.AppendLine("Flavor: " + Flavor);
        sb.AppendLine(ShareSeparator);
        sb.Append(Detail);
        return sb.ToString();
    }

    /// <summary>从分享文本还原方案。格式非法时抛 <see cref="FormatException"/>（中文消息）：
    /// 缺版本标记 / 空文本 / 缺标题。缺失的结构化字段置 null（应用时保留用户当前选择，不臆测覆盖）。</summary>
    public static MyPlan FromShareText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("分享文本为空");
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != ShareFormatVersion)
            throw new FormatException("不是有效的方案分享文本（缺少版本标记 " + ShareFormatVersion + "）");

        var plan = new MyPlan();
        int i = 1;
        for (; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line == ShareSeparator) { i++; break; }         // 分隔行 → 其后为正文
            var idx = line.IndexOf(':');
            if (idx <= 0) break;                                 // 非头行（空行/正文提前）→ 剩余即正文
            var key = line.Substring(0, idx).Trim();
            var val = line.Substring(idx + 1).Trim();
            switch (key)
            {
                case "Title": plan.Title = val; break;
                case "Summary": plan.Summary = val; break;
                case "Method": plan.Method = val; break;
                case "Dripper": plan.Dripper = val; break;
                case "Filter": plan.Filter = val; break;
                case "Dose": plan.Dose = val; break;
                case "Ratio": plan.Ratio = val; break;
                case "Process": plan.Process = val; break;
                case "Origin": plan.Origin = val; break;
                case "Roast": plan.Roast = val; break;
                case "Flavor": plan.Flavor = val; break;
            }
        }
        // 正文：分隔行之后的所有行（含空行）原样拼接
        plan.Detail = string.Join("\n", lines.Skip(i));
        if (string.IsNullOrWhiteSpace(plan.Title))
            throw new FormatException("分享文本缺少标题（Title 行）");
        return plan;
    }
}

/// <summary>我的方案文件仓储（框架无关，路径可注入）。按 Title 去重（同名覆盖），坏文件不崩溃。</summary>
public static class MyPlansStore
{
    public static List<MyPlan> Load(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<MyPlan>>(json);
            return list ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void SaveAll(string path, IReadOnlyList<MyPlan> plans)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(plans, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>按 Title 去重保存：已存在则覆盖（保持原顺序），否则追加到末尾。</summary>
    public static void AddOrReplace(string path, MyPlan plan)
    {
        var list = Load(path);
        var idx = list.FindIndex(p => p.Title == plan.Title);
        if (idx >= 0) list[idx] = plan; else list.Add(plan);
        SaveAll(path, list);
    }

    public static void Delete(string path, string title)
    {
        var list = Load(path);
        list.RemoveAll(p => p.Title == title);
        SaveAll(path, list);
    }
}
