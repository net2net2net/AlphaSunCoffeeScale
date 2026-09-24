using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoffeeScale.Core;

/// <summary>单次冲煮记录（含方案参数与实测结果），持久化为 JSON。</summary>
public sealed class BrewRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime BrewDate { get; set; }       // 冲煮日期
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Method { get; set; } = "";
    public string MethodLabel { get; set; } = "";
    public double Dose { get; set; }             // 粉量 g
    public double Ratio { get; set; }            // 粉水比 1:N
    public double TotalWater { get; set; }       // 计划总水量 g
    public string RoastLabel { get; set; } = "";
    public int RoastAg { get; set; }            // 具体烘焙度 Agtron 读数
    public int RoastAgMin { get; set; }         // 烘焙度 Agtron 范围下限
    public int RoastAgMax { get; set; }         // 烘焙度 Agtron 范围上限
    public string ProcessLabel { get; set; } = "";
    public string DripperLabel { get; set; } = "";
    public string FilterLabel { get; set; } = "";
    public string GrindLabel { get; set; } = ""; // 推荐研磨度
    public int GrindC40 { get; set; }           // C40 刻度
    public int GrindEK { get; set; }            // EK43 刻度
    public string WaterLabel { get; set; } = ""; // 水质推荐
    public string OriginLabel { get; set; } = "";
    public string OriginRegion { get; set; } = ""; // 产地大区
    public string VarietyLabel { get; set; } = "";
    public string OriginFlavor { get; set; } = ""; // 产地常规风味走向
    public string OriginGrade { get; set; } = "";  // 产地等级分类
    public int BloomWait { get; set; }
    public int Temp { get; set; }
    public string Summary { get; set; } = "";
    public double? FinalWeight { get; set; }     // 实测末重 g
    public double? RatioAchieved { get; set; }   // 实测实际粉水比

    // —— Phase 11 结构化引擎键（可空，旧记录反序列化为 null，向后兼容）——
    // 作用：① 「☆ 存为我的方案」复刻时可直取引擎键，无需从展示标签反查；
    //       ② 与 MyPlan/MasterPlanItem 同一套键，三条数据通路（大师清单 / 我的方案 / 历史记录）打通。
    public string? Roast { get; set; }     // 烘焙度引擎键（blonde…extreme_dark）
    public string? Process { get; set; }   // 处理方式引擎键（washed / natural / …）
    public string? Origin { get; set; }    // 产地引擎键（ethiopia / kenya / …）
    public string? Dripper { get; set; }   // 滤杯引擎键（v60 / wave / smart）
    public string? Flavor { get; set; }    // 风味走向引擎键（balanced / sweet / acidic）
    public string? PlanTitle { get; set; } // 本次冲煮所应用的「我的方案 / 大师方案」名（溯源；未用方案为 null）
}

/// <summary>冲煮记录文件仓储（框架无关，路径可注入）。</summary>
public static class RecordsStore
{
    public static List<BrewRecord> Load(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<BrewRecord>>(json);
            return list ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void SaveAll(string path, IReadOnlyList<BrewRecord> records)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static void Add(string path, BrewRecord rec)
    {
        var list = Load(path);
        list.Add(rec);
        SaveAll(path, list);
    }

    public static void Delete(string path, Guid id)
    {
        var list = Load(path);
        list.RemoveAll(r => r.Id == id);
        SaveAll(path, list);
    }
}
