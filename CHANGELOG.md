# 更新日志（CHANGELOG）— AlphaSunCoffeeScale

> 手冲咖啡智能冲煮称 · 所有显著变更记录于此。
> 格式参照 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循语义化（当前为 1.x 阶段，次版本号随每次发布递增）。
> 软件名：**AlphaSunCoffeeScale**（界面显示名：AlphaSun 手冲咖啡计算器）｜作者：**阳光 net2net2net（Vx: net2net）**

## [Unreleased]

- 计划中：iOS / MacCatalyst 头工程接入 `UseIconFontFallbacks()`（本机无法构建，代码已预留同一行调用）。

## [1.3] — 2026-09-13

### Added（新增）
- **界面全面升级为「INS × Apple」视觉语言**（共享 UI 层，四端一致生效）：
  - 新增设计令牌：`Palette.Shadow()` / `ShadowLifted()` 柔和投影、`Palette.Hairline()` 渐隐细线、`IconInk`（暖白图标字形色）、`Ink`（深墨）。
  - 新增 `IconText()` 图标控件：图标不再当作普通文本渲染，显式绑定内嵌图标字族 + 显式前景色 + 光学居中。
- 应用显式声明 `RequestedThemeVariant = Dark`：修复"深色界面下仍按 Light 主题解析"的割裂（未显式设色的子文本、ComboBox/DatePicker 飞出、ScrollBar 等现在与整体调性一致）。

### Fixed（修复）
- **图标瓷砖字形对比度缺陷**：瓷砖内字形此前继承 Button 的主题前景（深色），在模块色瓷砖上几乎不可见；现统一为暖白 `#FFF6EA`，形成"彩色瓷砖 + 暖白字形"的标准 App 图标质感。
- **英文界面残留中文**：`Author` 词条的 en-US 字典此前仍为中文（`作者：阳光…`），切英文后顶栏与新手模式页脚出现中文，触发"整层无中文"语言一致性守卫失败；英文署名改为 `Author: Sunlight · net2net2net (Vx: net2net)`。
- **禁用按钮不可辨**：禁用态透明度 0.4 → 0.55，深色底上「停止 / 下一段」等按钮不再"凭空消失"。

### Changed（调整）
- 主界面（Home）：内容纵向居中（消除底部大片死白）、Hero 卡改竖向渐变 + 24 大圆角 + 投影、品牌字加字距。
- 模块卡：圆角 16→20、柔和投影 + 悬停"抬升"反馈、状态徽章改胶囊形（可进入模块用焦糖渐变）、图标瓷砖加顶部高光描边 + 轻投影（iOS App 图标质感）。
- 按钮：主操作按钮渐变提亮 + 顶部高光描边 + 悬停增亮；圆角 10→12；页头返回键改胶囊形。
- 分隔线改"左实右虚"的渐隐 hairline；数字输入框（粉量/粉水比/水量）加 12 圆角与 SemiBold 字重。
- 卡片结构：卡面（背景/描边/投影）由内层 Border 承担（Avalonia 11 的 Button 无 `BoxShadow`，且需保持"卡片容器下直接是 Button"的既有结构断言），对应 UI 回归测试同步适配。

### Tests
- UI headless 测试 39/39、ViewModel 51/51、Core 72/72，共 **162 项全绿**。

## [1.2] — 2026-09-13

### Fixed（修复）
- **Android 端图标整片缺失**（用户报告：主界面、新手模式等处 emoji 图标在 APK 上不显示）：
  - 根因：根字体栈 `Segoe UI, Microsoft YaHei, PingFang SC, sans-serif` 不含 emoji 字形，且全项目未配置字体回退；Windows 靠系统 Segoe UI Emoji 兜底，Android 回退链无法渲染彩色 emoji（CBDT 位图字体）。
  - 修复：内嵌开源字体 **Noto Emoji**（单色 emoji，覆盖界面全部 emoji 码位）与 **Noto Sans Symbols 2**（○●☆✓✕▶◀ 等符号）到 `CoffeeScale.UI/Assets/Fonts/`，新增 `FontConfig.UseIconFontFallbacks()` 注册全局字体回退；桌面 `Program.cs` 与 Android `MainActivity.CustomizeAppBuilder` 均已调用。中文不受影响（两款字体均无 CJK 字形，继续回退平台 Noto Sans CJK）。
  - 验证：headless 断言两款 `avares://` 字族解析成功且 `TryMatchCharacter` 全码位命中；APK 内 `lib/<abi>/lib_CoffeeScale.UI.dll.so` 确认含字体；`apksigner verify` 通过。

### Changed（调整）
- 版本号 1.1 → 1.2（`AppVer` 与 Android `ApplicationDisplayVersion` 对齐）。

## [1.1] — 2026-09-11

### Fixed（修复）
- **杯测法闷蒸时长错误**：闷蒸段误用普通冲煮的 `BloomWait`（约 30–47s），与 SCA 杯测「4:00 闷蒸」不符，导致预计总时长偏离 12–15 分钟；现固定 240s。
- **杯测法注水双重计时**：杯测注水段既按 `DurationSec` 计时又按"水量/流速"叠加计时，夸大总时长；现仅"按重量推进"的非杯测段计入注水时间。

### Changed（调整）
- **命名统一**：「Origami 树脂锥形滤杯」→「**Origami折纸滤杯**」（新手/专业两模式共用同一 i18n 词源，中英文同步）。
- 新手模式底部署名条（作者 + 版本）；`AppVer` 与 Android `ApplicationDisplayVersion` 对齐为 1.1。
- 新手模式视觉增强：模块色渐变分隔条、建议卡/模拟卡模块色描边、参数数据卡升级为「色条 + 图标瓷砖 + 大数值」。

### Tests
- Core 引擎单测 72/72 通过（逻辑修复无回归）。

## [1.0] — 2026-09-03

### Added（新增）
- **首个 .NET 原生跨端版本**（.NET 9 + Avalonia 11，四层解耦：Core → ViewModels → UI → 各平台 head）。
- 冲煮引擎（Web 原型 `brew-logic.js` 的 C# 移植）：三维闷蒸（烘焙度 × 风味 × 养豆期）、8 级烘焙度（Agtron 粉样标尺）、8 种处理法、滤杯/滤纸/研磨多维关联、聪明杯浸泡分支、水质推荐。
- 多种冲煮法（经典分段 / 粕谷 46 / 浅烘加强 / 逆向注水 / 瑞士搅拌 / 杯测）+ 科普介绍。
- 模拟智能称：实时重量/计时/流速/粉水比、阶段推进、流量偏快与停滞警告；称控制键（运输键循环 + 清零 + 上一/下一阶段）。
- 冲煮日期与记录持久化（`brews.json`）、产地（18 大区）与豆种（14 种）风味元数据、养豆期推荐与满日期推算。
- 四模块入口主界面：新手模式 / 专业模式 / 简易计算器（粉水比换算 + 分段计时）/ 手冲大师方案清单。
- 中英双语（`I18n.cs`，切换即时重建文本）。

---

## 发布清单（Release Checklist）

发布新版本时按顺序执行：

1. `src/CoffeeScale.UI/MainWindow.cs` → `AppVer` 常量改新版本号；
2. `src/CoffeeScale.Android/CoffeeScale.Android.csproj` → `ApplicationDisplayVersion` 改同一版本号、`ApplicationVersion`（整数）递增；
3. 在本文件新增对应版本条目；
4. 运行 `powershell ./release.ps1 -Version <版本号>`（自动打包并按 `AlphaSunCoffeeScale-<版本>` 命名）；
5. 核对 `dist/` 下两个单文件产物与版本号一致。

[Unreleased]: https://example.invalid/compare/1.3...HEAD
[1.3]: https://example.invalid/compare/1.2...1.3
[1.2]: https://example.invalid/compare/1.1...1.2
[1.1]: https://example.invalid/compare/1.0...1.1
[1.0]: https://example.invalid/tag/1.0
