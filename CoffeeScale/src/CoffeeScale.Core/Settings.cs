using System;
using System.IO;
using System.Text.Json;

namespace CoffeeScale.Core;

/// <summary>可被持久化的冲煮参数与界面偏好（用户上次设置），与具体 UI 框架无关。</summary>
public sealed class BrewSettings
{
    public string Method { get; set; } = "classic";
    public string Size { get; set; } = "standard";
    public double Dose { get; set; } = 15;
    public double Ratio { get; set; } = 15;
    public string Roast { get; set; } = "light"; // 默认浅烘 Light（粉样 Ag 80–90，偏深 90）；阳光 2026-09-03 要求默认 Light Ag 90
    public int RoastAg { get; set; } = 90; // 粉样(Ground)标尺下浅烘 Light 偏深 (Ag 90)；与 BrewViewModel 字段默认值一致
    public string Flavor { get; set; } = "balanced";
    public string Process { get; set; } = "washed";
    public string Dripper { get; set; } = "v60";
    public string Filter { get; set; } = "bleached";
    public string Origin { get; set; } = "ethiopia";
    public string Variety { get; set; } = "heirloom";
    public int RestDays { get; set; } = 10;
    public string? RoastDateIso { get; set; }   // 烘焙日期（ISO），未设置则为 null
    public string? BrewDateIso { get; set; }     // 冲煮日期（ISO）
    public string Culture { get; set; } = I18n.ZhCN; // 界面语言
    public string Density { get; set; } = "medium"; // 咖啡豆密度：light/medium/dense（默认中等），影响研磨补偿
    public int BeanAltitudeM { get; set; } = 1500;   // 咖啡豆海拔(m)：密度的便捷预填来源（海拔越高→密度越高）
    /// <summary>兼容字段：旧版为「自动推导开关」。现语义 = 密度是否仍由海拔预填（true）/ 已被用户手选（false）。
    /// 保留以便旧配置与本版互读；UI 已无此开关。</summary>
    public bool DensityAuto { get; set; } = true;
}

/// <summary>冲煮参数/偏好文件仓储（框架无关，路径可注入）。与 RecordsStore 同构，便于测试与复用。</summary>
public static class SettingsStore
{
    public static BrewSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<BrewSettings>(json);
            return s ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Save(string path, BrewSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
