using Avalonia;
using Avalonia.Media;

namespace CoffeeScale.UI;

/// <summary>全局字体回退配置：随包内嵌 emoji / 符号字体，确保界面图标在所有平台稳定显示。
///
/// 【背景】界面用 emoji（🌱 ⚖ 🌡 ⚙ 💧 🫗 ⏱ ⏳ ☕ 🧮 📋 📤 📥 📊 ✅ 🏆 ⏭ …）作图标。
/// Windows 依赖系统 Segoe UI Emoji 可以显示，但 Android 的字体回退链无法渲染彩色 emoji
/// （CBDT 位图字体），导致移动端图标整片缺失。
///
/// 【方案】内嵌两款开源字体并注册为全局回退。Avalonia 的字形回退顺序为：
///   应用回退（本配置） → 复合字族名 → 平台系统字体
///   ① Noto Emoji        —— 单色轮廓 emoji，覆盖界面用到的全部 emoji 码位
///   ② Noto Sans Symbols 2 —— 几何/杂项符号（— – • … − ○ ● ☆ ✓ ✕ ▶ ◀ 等）
/// 单色字体与界面既有「按模块色着色图标」的设计一致，四端渲染统一；
/// 中文不受影响——两款字体均无 CJK 字形，会继续回退到平台 Noto Sans CJK。</summary>
public static class FontConfig
{
    // avares:// 资源族名（# 后）须与字体内部 family name 完全一致
    private const string EmojiFamily =
        "avares://CoffeeScale.UI/Assets/Fonts/NotoEmoji-Regular.ttf#Noto Emoji";
    private const string SymbolsFamily =
        "avares://CoffeeScale.UI/Assets/Fonts/NotoSansSymbols2-Regular.ttf#Noto Sans Symbols 2";

    /// <summary>图标专用复合字族：显式按「Noto Emoji → Noto Sans Symbols 2」顺序解析。
    ///
    /// 【为什么显式指定】回退链（<see cref="UseIconFontFallbacks"/>）解决的是"主字族缺字形"，
    /// 但它依赖运行时的字形回退逻辑；而图标是界面骨架，必须**可预期**。给图标 TextBlock 直接
    /// 挂上本字族后，解析路径不再经过任何平台字体——四端渲染完全一致，也与回退链互为兜底。
    /// 两款字体均无 CJK 字形，故不会误吞中文（中文由普通文本控件走平台字体）。</summary>
    public static FontFamily IconFontFamily { get; } =
        new FontFamily($"{EmojiFamily}, {SymbolsFamily}");

    /// <summary>为 AppBuilder 注册 emoji / 符号字体回退（各平台入口均调用，保证图标一致）。</summary>
    public static AppBuilder UseIconFontFallbacks(this AppBuilder builder) =>
        builder.With(new FontManagerOptions
        {
            FontFallbacks = new[]
            {
                new FontFallback { FontFamily = new FontFamily(EmojiFamily) },
                new FontFallback { FontFamily = new FontFamily(SymbolsFamily) },
            },
        });
}
