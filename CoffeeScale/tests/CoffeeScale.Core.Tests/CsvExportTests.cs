using System;
using System.IO;
using System.Linq;
using Xunit;

namespace CoffeeScale.Core.Tests;

public class CsvExportTests
{
    // 1) 基本导出：UTF-8 BOM + 中文表头 + 记录行关键字段
    [Fact]
    public void BuildRecordsCsv_BomHeaderAndRow()
    {
        var prev = I18n.Current;
        try
        {
            I18n.Current = I18n.ZhCN;
            var rec = new BrewRecord
            {
                BrewDate = new DateTime(2026, 9, 2, 8, 30, 0),
                MethodLabel = "粕谷式",
                Dose = 18, Ratio = 15, TotalWater = 270,
                RoastLabel = "浅烘", ProcessLabel = "日晒",
                OriginLabel = "肯尼亚", PlanTitle = "我的肯尼亚方案",
            };
            var csv = CsvExport.BuildRecordsCsv(new[] { rec });

            Assert.StartsWith("\uFEFF", csv); // BOM：Excel 直接识别 UTF-8 中文
            var lines = csv.TrimEnd('\r', '\n').Split('\n');
            Assert.Contains("冲煮日期", lines[0]);
            Assert.Contains("粉量(g)", lines[0]);
            Assert.Contains("来源方案", lines[0]);
            Assert.Contains("2026-09-02 08:30", lines[1]);
            Assert.Contains("1:15", lines[1]);
            Assert.Contains("我的肯尼亚方案", lines[1]);
        }
        finally { I18n.Current = prev; }
    }

    // 2) 字段转义：含逗号 / 引号 / 换行的字段被引号包裹、内部引号翻倍；行结构不被打断
    [Fact]
    public void BuildRecordsCsv_EscapesCommaQuoteNewline()
    {
        var prev = I18n.Current;
        try
        {
            I18n.Current = I18n.ZhCN;
            var rec = new BrewRecord
            {
                MethodLabel = "粕谷, 4:6 \"法\"",
                ProcessLabel = "日晒,处理",
                Summary = "第一行\n第二行",
                PlanTitle = "A,B",
            };
            var csv = CsvExport.BuildRecordsCsv(new[] { rec });
            // 表头仍为首行（换行只出现在被转义的字段内）
            Assert.StartsWith("\uFEFF" + I18n.T("CsvDate"), csv);
            // 方法字段：逗号+引号 → "粕谷, 4:6 ""法"""
            Assert.Contains("\"粕谷, 4:6 \"\"法\"\"\"", csv);
            // 含换行字段整体被引号包裹，原换行保留
            Assert.Contains("\"第一行\n第二行\"", csv);
            Assert.Contains("\"A,B\"", csv);
        }
        finally { I18n.Current = prev; }
    }

    // 3) 空记录：仅表头；表头键中英双语言齐全（守卫）
    [Fact]
    public void BuildRecordsCsv_EmptyOnlyHeader_I18nKeysComplete()
    {
        var prev = I18n.Current;
        try
        {
            foreach (var lang in new[] { I18n.ZhCN, I18n.EnUS })
            {
                I18n.Current = lang;
                var csv = CsvExport.BuildRecordsCsv(Array.Empty<BrewRecord>());
                var lines = csv.TrimEnd('\r', '\n').Split('\n');
                Assert.Single(lines);
                var headers = lines[0].Split(',');
                var headerKeys = new[] { "CsvDate", "CsvMethod", "CsvDose", "CsvRatio", "CsvWater", "CsvRoast",
                    "CsvAg", "CsvProcess", "CsvDripper", "CsvGrind", "CsvWaterQuality", "CsvOrigin",
                    "CsvVariety", "CsvTemp", "CsvBloom", "CsvFinalWeight", "CsvRatioAchieved", "CsvSummary", "CsvPlanSource" };
                Assert.Equal(headerKeys.Length, headers.Length);
                foreach (var k in headerKeys)
                {
                    var t = I18n.T(k);
                    Assert.False(string.IsNullOrWhiteSpace(t), $"[{lang}] 缺失键 {k}");
                    Assert.NotEqual(k, t);
                }
                // 动作键（弹窗按钮/状态文案）同样不得缺失或回退
                foreach (var k in new[] { "ExportCsv", "ExportCsvOk", "ExportCsvFailed", "ExportCsvEmpty" })
                {
                    var t = I18n.T(k);
                    Assert.False(string.IsNullOrWhiteSpace(t), $"[{lang}] 缺失键 {k}");
                    Assert.NotEqual(k, t);
                }
            }
        }
        finally { I18n.Current = prev; }
    }

    // 4) 实测结果列：末重 / 实际粉水比 / C40/EK 刻度拼接
    [Fact]
    public void BuildRecordsCsv_MeasuredColumnsAndGrind()
    {
        var prev = I18n.Current;
        try
        {
            I18n.Current = I18n.ZhCN;
            var rec = new BrewRecord
            {
                GrindLabel = "中细研磨", GrindC40 = 18, GrindEK = 0,
                FinalWeight = 245.5, RatioAchieved = 16.3,
            };
            var csv = CsvExport.BuildRecordsCsv(new[] { rec });
            Assert.Contains("中细研磨 (C40≈18)", csv);
            Assert.Contains("245.5", csv);
            Assert.Contains("16.3", csv);
        }
        finally { I18n.Current = prev; }
    }

    // 5) 真实 CRLF 源数据：字段内含 \r\n 也不破坏 CSV 结构
    [Fact]
    public void BuildRecordsCsv_CrlfInFieldStillValid()
    {
        var prev = I18n.Current;
        try
        {
            I18n.Current = I18n.ZhCN;
            var rec = new BrewRecord { Summary = "注水偏快\r\n下次修正" };
            var csv = CsvExport.BuildRecordsCsv(new[] { rec });
            // 表头仍为首行；含 CRLF 的字段整体被引号包裹、原样保留
            Assert.StartsWith("\uFEFF" + I18n.T("CsvDate"), csv);
            Assert.Contains("\"注水偏快\r\n下次修正\"", csv);
        }
        finally { I18n.Current = prev; }
    }

    // 6) 中文内容写入文件后重读：BOM 存在、无乱码（UTF-8 往返验证）
    [Fact]
    public void BuildRecordsCsv_WrittenFileRoundTripsUtf8()
    {
        var prev = I18n.Current;
        var tmp = Path.Combine(Path.GetTempPath(), "csv_test_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            I18n.Current = I18n.ZhCN;
            var rec = new BrewRecord { MethodLabel = "瑞士搅拌", OriginLabel = "哥伦比亚", PlanTitle = "2026 决赛" };
            var csv = CsvExport.BuildRecordsCsv(new[] { rec });
            File.WriteAllText(tmp, csv, new System.Text.UTF8Encoding(true));

            var raw = File.ReadAllText(tmp); // 同编码读回
            Assert.StartsWith("\uFEFF", raw);
            Assert.Contains("瑞士搅拌", raw);
            Assert.Contains("哥伦比亚", raw);
            Assert.Contains("2026 决赛", raw);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            I18n.Current = prev;
        }
    }
}
