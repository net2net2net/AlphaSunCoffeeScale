# 更新日志（CHANGELOG）— AlphaSunCoffeeScale

> 手冲咖啡智能冲煮称 · 所有显著变更记录于此。
> 格式参照 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循语义化（当前为 1.x 阶段，次版本号随每次发布递增）。
> 软件名：**AlphaSunCoffeeScale**（界面显示名：AlphaSun 手冲咖啡计算器）｜作者：**阳光 net2net2net（Vx: net2net）**

## [Unreleased]

### Added
- **🎉 iOS 安装包（未签名 IPA）首次出包**：`dist/AlphaSunCoffeeScale-1.5-ios-unsigned.ipa`（19.4 MB，arm64 真机 AOT 包，
  iOS 14+ / iPhone & iPad 通用）。由 GitHub Actions（`build-ios.yml`）在 macOS runner 上云构建产出，
  可用 Sideloadly / 爱思助手 / AltStore 自签安装（免费 ID 7 天），或巨魔 TrollStore 免签直装。
- **iOS / MacCatalyst 头工程完成脚手架**：AppDelegate 接入 `UseIconFontFallbacks()`，与 Android / 桌面共享同一套字体回退配置，图标四端一致（关闭 [Unreleased] 计划项）。
- iOS / MacCatalyst `Info.plist` 版本对齐到 `1.5`，新增 `CFBundleIconName=AppIcon` 与设备族声明；`CFBundleVersion` 对齐 Android `ApplicationVersion=6`。
- 新增 `Assets.xcassets/AppIcon.appiconset` 图标资源目录：程序化生成暖色咖啡主题图标（iOS 14 个尺寸 + MacCatalyst 10 个尺寸）。
- 新增 `publish-ios.sh` 与 `docs/ios-build.md`：提供 iOS 模拟器、真机、IPA、MacCatalyst 的完整 Mac 构建/签名流程。

### Changed
- iOS / MacCatalyst `.csproj`：移除硬编码 `RuntimeIdentifier`，改为默认 `ios-arm64` / `maccatalyst-arm64`，可用 `-r` 自由覆盖为模拟器或 Intel。
- 删除 `MainWindow` 中从未使用的字段 `_homeMasterPlanWindow`，消除 `CS0169` 编译警告。
- 修复 `MainWindowHeadlessTests` 中 `waterBox.Text` 可空转换警告（CS8600）。

### Fixed
- **iOS / MacCatalyst 头工程补上 `<OutputType>Exe</OutputType>` + 入口点 `Main.cs`（关键修复）**：
  此前两个 head 沿用 SDK 默认的 `Library`，macios 因 `_CanOutputAppBundle=false` **静默跳过 `.app` 打包**——
  `dotnet publish` 退出码 0、日志只打印一行空的 `Created the package: `，产物目录空无一物（CI run#13/#14 连续踩坑）。
  改为 `Exe` 后正常产出 `CoffeeScale.iOS.app`；配套新增 `Main.cs`（`UIApplication.Main(args, null, typeof(AppDelegate))`），
  否则会报 `CS5001 无入口点`。
- 新增 GitHub Actions 云构建工作流 `.github/workflows/build-ios.yml`：macOS runner 上编译 arm64 并产出未签名 IPA。
  内置三处镜像坑的规避：SDK 版本 `global.json` 钉 9.x、硬编码选 `Xcode_16.4.app`（16.4.0 是残缺壳）、重建 SDK 无版本别名符号链接。
- iOS 图标改用经典 `iphone` / `ipad` / `ios-marketing` 三档 idiom（`idiom: universal` 是 Xcode 14+ 新格式，
  actool 在部署目标 14.0 下不认，不产出 `Assets.car`）；最终 IPA 以 `CFBundleIconFiles` + PNG 直接入包的方式兜底注入图标。

### Added（Linux / macOS 桌面版）
- **🎉 补齐 Linux 与 macOS 桌面版安装包**（同一套桌面代码，仅换 RID 发布）：
  - `AlphaSunCoffeeScale-1.5-linux-x64.tar.gz`（38.7 MB）：自包含单文件 ELF + `install-linux.sh` + `.desktop` 应用菜单入口 + 256/512 PNG 图标。
  - `AlphaSunCoffeeScale-1.5-macos-arm64.tar.gz`（32.8 MB，Apple Silicon）与 `AlphaSunCoffeeScale-1.5-macos-x64.tar.gz`（34.4 MB，Intel）：
    内含标准 `AlphaSunCoffeeScale.app` bundle（`Contents/MacOS` + `Resources/AppIcon.icns` + `Info.plist`）。
  - 两者均由 GitHub Actions（`build-desktop.yml`）在 ubuntu-22.04 / macOS runner 上云构建，Windows 本机出不了 Unix 可执行位与 `.app` 结构。
- 新增打包资源：`packaging/linux/`（desktop 文件、安装/卸载脚本、安装说明）与 `packaging/macos/`（`Info.plist`、安装说明）；
  新增 `Assets/AppIcon.icns` 与 `icon-256.png` / `icon-512.png`（`tools/gen-appicon.py` 一次生成桌面三规格）。

### Changed（跨平台化）
- **桌面工程不再写死 Windows**：`Program.cs` 的 `AppBuilder` 由 `UseWin32()` 改为 **`UsePlatformDetect()`**
  （Windows→Win32 / Linux→X11 / macOS→Avalonia.Native 自动选择；原写法在 Linux/macOS 上启动即崩，找不到 Win32 后端）。
  崩溃弹窗改为分平台：Windows 仍走 `user32.MessageBoxW`，Linux/macOS 写日志 + stderr。
- **数据目录按平台规范化**（新增 `CoffeeScale.Core/AppPaths`）：配置 / 记录 / 我的方案 / 崩溃日志原先一律写 `AppContext.BaseDirectory`，
  在 macOS 上等于往 `.app` 包内写（更新 App 会丢数据、破坏包内容）。现在：
  - Windows / Android / iOS：**保持程序同目录不变**（既有用户数据零影响）；
  - macOS → `~/Library/Application Support/AlphaSunCoffeeScale`；
  - Linux → `~/.local/share/AlphaSunCoffeeScale`（尊重 `XDG_DATA_HOME`）。
  首次启动若发现程序目录里有旧 json 会自动搬到新目录（`MigrateLegacyFiles`，只搬不删）。
- `CoffeeScale.Avalonia.csproj`：`RuntimeIdentifier` 改为可覆盖（Release 默认 `win-x64`），
  单文件 / 自包含 / `app.manifest` / `app.ico` / `SupportedOSPlatformVersion=11.0` 等属性按 RID 条件生效，
  一套工程即可出 `win-x64` / `linux-x64` / `osx-arm64` / `osx-x64` 四个产物。

### Fixed（CI 坑）
- `build-desktop.yml` 首跑报 `NETSDK1147: workloads must be installed: android`——共享 UI 库默认多目标 `net9.0-android`，
  而 Linux/macOS runner 没装 android workload。桌面发布统一加 `-p:CoffeeSkipAndroid=true`（UI 库已有该开关，只留 `net9.0`）。

## [1.5] — 2026-09-14

### Changed（调整）
- **专业模式「咖啡豆密度」交互精简（去掉自动推导开关）**：
  - 移除「自动（由海拔推导）」勾选框，界面只剩 **咖啡豆海拔输入框 + 密度下拉框** 并排一行，更简洁。
  - **海拔改为密度的「便捷预填」**：未手选密度时，改海拔即自动刷新密度档（≥1700 致密 / 1200–1699 中等 / <1200 疏松）；在下拉框手选一次后，海拔不再覆盖手选值（应对低海拔异常硬豆等特例）。
  - 两个控件始终可用、无禁用态；视觉升级为 12px 圆角 + 暖深底 + 细描边 + 44pt 最小高度，与全应用输入控件风格统一（INS × Apple）。

### Changed（代码优化）
- `BrewViewModel`：删除 `DensityAuto` 公开属性与 `SyncDensityFromAltitude()`，改以内部标记 `_densityUserSet` 区分「手选」与「预填」，新增 `ApplyDensityFromAltitude(altitudeDriven)`；`Reset`/`LoadSettings`/`CaptureSettings` 同步改写。
- `BrewSettings.DensityAuto` 降级为**兼容字段**（保留旧配置互读），写盘语义为 `DensityAuto = !_densityUserSet`；读取时旧版 `false`→恢复手选密度、`true`→由海拔重推。
- `I18n`：删除已不再使用的 `DensityAuto` / `DensityManual` 词条；`BeanAltitudeHint` 改写为「预填 + 可手选覆盖」表述。
- `ParamInfo`：`beanAltitudeM` 条目改写为预填语义，并清除其中早已过时的「海拔 → 水温 +1℃」描述（水温补偿已于 2026-09-08 移除）。

### Tests
- `CoffeeScale.Core.Tests` 72/72、`CoffeeScale.ViewModels.Tests` 52/52（新增密度「预填 / 手选覆盖 / 重载重推」用例）、`CoffeeScale.UI.Tests` 39/39，共 **163 项全绿**。

## [1.4] — 2026-09-14

### Added（新增）
- **杯测法统一基准与豆密度海拔推导正式打包发布**（规格于 [1.3] 续录确定，本版本首次出包）：
  - **杯测法（cupping）**：11g 咖啡粉 + 200g 94℃ 热水（≈1:18.18，`CuppingRatio=18.18`），全程静态计时（`IsCupping=true`）、不依赖注水模拟；阶段序列 **注水浸润 PourIn(30s) → 静置浸泡 Soak(240s)**，无闷蒸/破壳/不过滤；滤杯/滤纸取空。VM 选杯测且粉量/粉水比仍为默认时自动置 `Dose=11 / Ratio=18.18`（用户可微调）。
  - **豆密度海拔自动推导**：新增 `BeanAltitudeM`（默认 1500m）与 `DensityAuto`（默认 true）。`DensityAuto=true` 时密度由 `DensityFromAltitude(alt)` 实时推导（≥1700m→dense / 1200–1699m→medium / <1200m→light），改海拔即 `SyncDensityFromAltitude()` 重算；`false` 时由用户手选密度、海拔仅作参考。海拔不再参与水温（沸点封顶已于 2026-09-08 移除）。
- **源注释与网页同步**：`ParamInfo.cs` 与网页 `pour-over-lab.html` KPI 的 SCA 基准标注由 `1:18.2` 统一修订为 `1:18.18`（统一基准 11g 粉 / 200g 94℃ 热水）。

### Tests
- UI headless 39/39、ViewModel 51/51、Core 72/72，共 **162 项全绿**（含 ParamInfo 文案变更回归）。

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

### Added（新增 · 2026-09-13 续）
- **杯测法（cupping）统一基准重订**：11g 咖啡粉 + 200g 94℃ 热水（≈1:18.18，`CuppingRatio=18.18`），全程静态计时（`IsCupping=true`）、不依赖注水模拟；阶段序列由旧「闷蒸固定 240s」修订为 **注水浸润 PourIn(30s) → 静置浸泡 Soak(240s)**，无闷蒸/破壳/不过滤；滤杯/滤纸取空（建议器具：杯测碗、杯测勺、电子秤、计时器、研磨机、热水壶、温度计）。VM 选杯测且粉量/粉水比仍为默认时自动置 `Dose=11 / Ratio=18.18`（用户可微调）。
- **豆密度海拔自动推导**：新增 `BeanAltitudeM`（默认 1500m）与 `DensityAuto`（默认 true）。`DensityAuto=true` 时密度由 `DensityFromAltitude(alt)` 实时推导（≥1700m→dense / 1200–1699m→medium / <1200m→light），改海拔即 `SyncDensityFromAltitude()` 重算；`false` 时由用户手选密度、海拔仅作参考。海拔不再参与水温（2026-09-08 已移除沸点封顶）。

> 注：上述杯测法修订**取代** `[1.1]` 中「闷蒸固定 240s / 注水双重计时」的描述——杯测法现已无闷蒸段。

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
