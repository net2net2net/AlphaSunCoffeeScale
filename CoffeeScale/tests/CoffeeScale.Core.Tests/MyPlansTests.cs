using System;
using CoffeeScale.Core;
using Xunit;

namespace CoffeeScale.Core.Tests;

/// <summary>Phase 13：MyPlan 便携分享文本（ToShareText / FromShareText）往返与校验。</summary>
public class MyPlansTests
{
    private static MyPlan Sample() => new()
    {
        Title = "我的肯尼亚方案",
        Summary = "复刻自历史冲煮记录（含当日实测参数）",
        Detail = "· 粉量 22g / 总水 367g，粉水比 1:16.7\n· 经典三段式 · 水温 93℃ · 闷蒸 45s\n· 烘焙度：中深烘（Ag 40–50）",
        Method = "classic",
        Dripper = "wave",
        Dose = "22",
        Ratio = "16.7",
        Process = "natural",
        Origin = "kenya",
        Roast = "medium_dark",
        Flavor = "sweet",
    };

    // 1) 完整往返：8 个结构化字段 + 标题/摘要/正文全部无损
    [Fact]
    public void ShareText_Roundtrip_PreservesAllFields()
    {
        var plan = Sample();
        var text = plan.ToShareText();
        // 文本可读：首行版本标记 + 头字段 + 分隔行 + 正文
        Assert.StartsWith(MyPlan.ShareFormatVersion + Environment.NewLine, text);
        Assert.Contains("Title: 我的肯尼亚方案", text);
        Assert.Contains("Process: natural", text);
        Assert.Contains("Roast: medium_dark", text);
        Assert.Contains("---", text);
        Assert.Contains("粉水比 1:16.7", text); // 正文原样保留

        var back = MyPlan.FromShareText(text);
        Assert.Equal(plan.Title, back.Title);
        Assert.Equal(plan.Summary, back.Summary);
        Assert.Equal(plan.Detail, back.Detail);
        Assert.Equal(plan.Method, back.Method);
        Assert.Equal(plan.Dripper, back.Dripper);
        Assert.Equal(plan.Dose, back.Dose);
        Assert.Equal(plan.Ratio, back.Ratio);
        Assert.Equal(plan.Process, back.Process);
        Assert.Equal(plan.Origin, back.Origin);
        Assert.Equal(plan.Roast, back.Roast);
        Assert.Equal(plan.Flavor, back.Flavor);
    }

    // 2) 缺失字段 → null（应用时保留用户当前选择，不臆测覆盖）
    [Fact]
    public void ShareText_MissingOptionalFields_AreNull()
    {
        var text = MyPlan.ShareFormatVersion + "\nTitle: 极简方案\n---\n只有正文";
        var back = MyPlan.FromShareText(text);
        Assert.Equal("极简方案", back.Title);
        Assert.Null(back.Process);
        Assert.Null(back.Origin);
        Assert.Null(back.Roast);
        Assert.Equal("只有正文", back.Detail);
    }

    // 3) 非法输入 → FormatException（中文消息）
    [Fact]
    public void ShareText_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => MyPlan.FromShareText(""));
        Assert.Throws<FormatException>(() => MyPlan.FromShareText("   "));
        Assert.Throws<FormatException>(() => MyPlan.FromShareText("随便一段文字"));
        Assert.Throws<FormatException>(() => MyPlan.FromShareText(MyPlan.ShareFormatVersion + "\nSummary: 没有标题\n---\n正文"));
    }

    // 4) 空字段不产生空行（分享文本紧凑）；CRLF 输入兼容
    [Fact]
    public void ShareText_EmptyFieldsSkipped_CrlfParsed()
    {
        var plan = new MyPlan { Title = "A", Detail = "正文" }; // 其余字段为空
        var text = plan.ToShareText();
        Assert.DoesNotContain("Method: ", text);
        Assert.DoesNotContain("Process: ", text);

        // 真实 CRLF 换行的分享文本（如微信/邮件里复制后经 Windows 编辑器保存的 .txt）
        var crlf = MyPlan.ShareFormatVersion + "\r\nTitle: A\r\n---\r\n正文";
        var back = MyPlan.FromShareText(crlf);
        Assert.Equal("A", back.Title);
        Assert.Equal("正文", back.Detail);
    }
}
