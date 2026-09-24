using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CoffeeScale.Core;

/// <summary>冲煮记录导出 CSV（Excel / WPS 可直接打开：UTF-8 BOM + 全字段转义，中文不乱码）。</summary>
public static class CsvExport
{
    /// <summary>CSV 单字段转义：含逗号/引号/换行时用双引号包裹，内部引号翻倍。</summary>
    private static string Esc(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var s = v.Replace("\"", "\"\"");
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s + "\"";
        return s;
    }

    /// <summary>生成 UTF-8 BOM CSV 文本（表头随当前界面语言）。记录为空时仅含表头。</summary>
    public static string BuildRecordsCsv(IEnumerable<BrewRecord> records)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // UTF-8 BOM：Excel/WPS 直接识别 UTF-8 中文
        var headers = new[]
        {
            I18n.T("CsvDate"), I18n.T("CsvMethod"), I18n.T("CsvDose"), I18n.T("CsvRatio"),
            I18n.T("CsvWater"), I18n.T("CsvRoast"), I18n.T("CsvAg"), I18n.T("CsvProcess"),
            I18n.T("CsvDripper"), I18n.T("CsvGrind"), I18n.T("CsvWaterQuality"),
            I18n.T("CsvOrigin"), I18n.T("CsvVariety"), I18n.T("CsvTemp"), I18n.T("CsvBloom"),
            I18n.T("CsvFinalWeight"), I18n.T("CsvRatioAchieved"), I18n.T("CsvSummary"), I18n.T("CsvPlanSource"),
        };
        sb.AppendLine(string.Join(",", headers.Select(Esc)));
        var inv = CultureInfo.InvariantCulture;
        foreach (var r in records)
        {
            var fields = new[]
            {
                r.BrewDate.ToString("yyyy-MM-dd HH:mm", inv),
                r.MethodLabel,
                r.Dose.ToString("0.#", inv),
                "1:" + r.Ratio.ToString("0.#", inv),
                r.TotalWater.ToString("0.#", inv) + "g",
                r.RoastLabel,
                r.RoastAg.ToString(inv),
                r.ProcessLabel,
                r.DripperLabel,
                GrindCell(r),
                r.WaterLabel,
                r.OriginLabel,
                r.VarietyLabel,
                r.Temp.ToString(inv) + "°C",
                r.BloomWait.ToString(inv) + "s",
                r.FinalWeight?.ToString("0.#", inv) ?? "",
                r.RatioAchieved?.ToString("0.#", inv) ?? "",
                r.Summary,
                r.PlanTitle ?? "",
            };
            sb.AppendLine(string.Join(",", fields.Select(Esc)));
        }
        return sb.ToString();
    }

    /// <summary>研磨单元格：档位 + C40/EK 刻度（有刻度时拼接展示）。</summary>
    private static string GrindCell(BrewRecord r)
    {
        if (r.GrindC40 <= 0 && r.GrindEK <= 0) return r.GrindLabel;
        var parts = new List<string>();
        if (r.GrindC40 > 0) parts.Add("C40≈" + r.GrindC40);
        if (r.GrindEK > 0) parts.Add("EK≈" + r.GrindEK);
        return r.GrindLabel + (parts.Count > 0 ? " (" + string.Join(" / ", parts) + ")" : "");
    }
}
