# AlphaSunCoffeeScale · 手冲咖啡智能冲煮称

> **AlphaSunCoffeeScale** —— 一套真正编译成**原生程序**的手冲咖啡冲煮方案工具：
> 模拟智能称 + 冲煮方案生成 + 简易计算器 + 大师方案清单，覆盖 **Windows / 安卓 / 苹果手机 / 平板**。
> 技术栈：**.NET 9 + Avalonia 11**（原生 UI，非网页/PWA），一套 C# 代码编译到四端。
>
> | | |
> |---|---|
> | **软件名** | AlphaSunCoffeeScale（界面显示名：AlphaSun 手冲咖啡计算器） |
> | **当前版本** | v1.5（版本变化记录见 [CHANGELOG.md](CHANGELOG.md)） |
> | **作者** | 阳光 net2net2net（Vx: net2net） |
> | **许可证** | 未另行声明（个人项目） |

---

## 1. 软件简介

打开软件即进入**四模块工作台**：

| 模块 | 说明 |
|------|------|
| 🌱 **新手模式** | 跟着引导，一步步冲出你的第一杯手冲：选滤杯 → 选烘焙度 → 选风味，即时给出粉量/水温/研磨/粉水比/总水量与阶段路线图，附冲煮模拟。 |
| ⚙️ **专业模式** | 完整参数设置（豆种/产地/处理方式/烘焙度/粉量档/滤杯/滤纸/冲煮法）→ 生成推荐方案 → 右侧**实时冲煮**（模拟称：重量/计时/流速/实际粉水比、阶段推进、流量偏快与停滞警告、保存记录与统计查询）。 |
| 🧮 **简易咖啡计算器** | 粉量 × 粉水比 = 总注水量三联换算（任一清空可由另两项反推）+ 分段计时秒表。 |
| 🏆 **手冲大师方案** | 冠军与大师配方清单（按年份降序），一键套用到专业模式。 |

冲煮引擎核心能力：**三维闷蒸**（烘焙度 × 风味走向 × 养豆期排气系数）、**8 级烘焙度**（Agtron 粉样标尺）、**8 种处理法**、滤杯/滤纸/研磨多维关联修正、**聪明杯浸泡式分支**、**水质推荐**（TDS/GH/KH）、**养豆期推荐与满日期推算**、18 大产地 + 14 种豆种风味元数据、中英双语即时切换。

---

## 2. 架构与逻辑关系

### 2.1 四层解耦（自下而上）

```text
┌──────────────────────────────────────────────────────────────────┐
│ 平台 head：CoffeeScale.Avalonia(桌面 Win32) / .Android / .iOS / .MacCatalyst │
│   只做"启动壳"：注册平台生命周期 + 字体回退（FontConfig），无业务逻辑           │
├──────────────────────────────────────────────────────────────────┤
│ CoffeeScale.UI（共享控件库，四端复用）                                      │
│   MainWindow(纯 C# 界面，无 XAML) / App / FontConfig / Converters           │
├──────────────────────────────────────────────────────────────────┤
│ CoffeeScale.ViewModels（UI 无关状态机）                                    │
│   BrewViewModel：绑定 Core、驱动模拟注水与实时快照、记录持久化               │
│   通过注入 Marshal 委托把属性回写 UI 线程 → 可被 Avalonia/MAUI/WPF 复用      │
├──────────────────────────────────────────────────────────────────┤
│ CoffeeScale.Core（纯逻辑引擎，零 UI 依赖）                                 │
│   BrewEngine（配方生成）/ I18n（双语资源表）/ 记录模型                       │
└──────────────────────────────────────────────────────────────────┘
```

> 依赖方向严格单向：head → UI → ViewModels → Core。Core 不知道 UI 存在，因此冲煮计算可以被 162 项单测完整覆盖。

### 2.2 冲煮计算的主干流程

```text
用户参数(粉量档/烘焙度/处理方式/滤杯/滤纸/冲煮法)
        │
        ▼
BrewEngine.GenerateRecipe ──► 水温 = 基准(烘焙度) + 处理法微调 + 研磨显式覆盖微调
        │                      闷蒸 = f(烘焙度, 风味走向, 养豆期排气系数)
        │                      粉水比 = 基准(烘焙度) + 处理法微调（新手模式杯测固定 18.18 兜底，统一基准 11g 粉 / 200g 94℃ 热水）
        │                      研磨 = 滤杯基准 + 烘焙度修正（浅烘+1 细 / 深烘-1 粗）
        │                      流速 = 滤杯 FlowBase + 研磨 FlowAdj + 滤纸 FlowAdj (clamp 3–14 g/s)
        │                      阶段序列 = 冲煮法模板（杯测法：注水浸润 30s → 静置浸泡 240s，无闷蒸/破壳/不过滤，静态计时）
        ▼
BrewViewModel：驱动"模拟注水" → 每帧快照(重量/计时/流速/实际粉水比/阶段进度)
        │                      流量偏快阈值 = 推荐流速 × 1.7；停滞检测
        ▼
保存记录 brews.json（配方 + 实测末重 + 实际粉水比 + 产地/豆种/Ag 等全字段）
```

界面图标全部使用 emoji，由内嵌字体（Noto Emoji / Noto Sans Symbols 2）+ 全局字体回退保证**四端一致渲染**（见 `FontConfig.cs`）。

---

## 3. 如何构建（Build）

### 3.1 环境要求

- .NET SDK **9.0** + Android workload（`dotnet workload install android`）
- Android：JDK 17 + Android SDK build-tools **35.0.0**（APK 签名校验用）
- 打包 Android 时脚本会先把仓库复制到纯英文路径 `C:\dev\cs-build` 再构建（规避 aapt2 在非 ASCII 路径下的损坏问题）

> ⚠️ 本仓库在部分受限 shell 里缺 `SystemRoot` 等环境变量，**所有 dotnet 命令须经 `dotnet-env.sh` 包装**。

### 3.2 一键发布（推荐）

```bash
powershell ./release.ps1 -Version 1.3
```

自动完成：EXE 单文件发布 → APK 构建+签名 → 按 **`AlphaSunCoffeeScale-<版本>`** 规范命名拷贝到 `dist/` → 校验签名与产物。

### 3.3 分步构建

```bash
# ① Windows 桌面单文件（无依赖，双击即运行）
bash dotnet-env.sh publish src/CoffeeScale.Avalonia/CoffeeScale.Avalonia.csproj \
     -c Release -r win-x64 -o dist/win-x64

# ② Android 单文件 APK（英文路径构建 + 签名 + 拷回）
powershell ./publish-android.ps1

# ③ iOS / MacCatalyst（*只能在 macOS + Xcode 上构建*）
./publish-ios.sh sim       # iOS 模拟器
./publish-ios.sh device    # iOS 真机 app bundle
./publish-ios.sh ipa       # iOS 可分发 IPA
./publish-ios.sh mac       # MacCatalyst 桌面 app

# ④ 全量测试（163 项）
bash dotnet-env.sh test tests/CoffeeScale.Core.Tests/CoffeeScale.Core.Tests.csproj -c Release
bash dotnet-env.sh test tests/CoffeeScale.ViewModels.Tests/CoffeeScale.ViewModels.Tests.csproj -c Release
bash dotnet-env.sh test tests/CoffeeScale.UI.Tests/CoffeeScale.UI.Tests.csproj -c Release
```

### 3.4 产物与命名规范

| 产物 | 命名 | 说明 |
|------|------|------|
| Windows 桌面 | `dist/AlphaSunCoffeeScale-<版本>.exe` | 单文件自包含（PublishSingleFile + SelfContained），无 .NET 依赖 |
| 安卓安装包 | `dist/AlphaSunCoffeeScale-<版本>.apk` | 单文件签名 APK（debug 密钥），`adb install -r` 或手机直接安装 |
| iOS / MacCatalyst | 见 `docs/ios-build.md` | 在 macOS 上构建：`.app` / `.ipa` / MacCatalyst 桌面包，需 Apple 开发者账号（模拟器可跳过） |
| 运行时配置 | `settings.json` / `brews.json` | 程序首次运行生成的**用户数据**，非分发依赖 |

> 版本号必须四处对齐：`MainWindow.AppVer` ↔ Android `ApplicationDisplayVersion`（整数 `ApplicationVersion` 递增）↔ iOS/MacCatalyst `CFBundleShortVersionString`/`CFBundleVersion` ↔ `CHANGELOG.md` 条目。

---

## 4. 版本变化记录（摘要）

| 版本 | 日期 | 要点 |
|------|------|------|
| **1.5** | 2026-09-14 | **专业模式「咖啡豆密度」精简**（海拔预填、手选优先）、杯测法基准与豆密度海拔推导正式发布，测试 163 项全绿 |
| **1.4** | 2026-09-14 | **杯测法统一基准与豆密度海拔推导**正式打包发布、SCA 基准标注统一为 1:18.18 |
| **1.3** | 2026-09-13 | **INS × Apple 界面全面升级**（投影/圆角/胶囊徽章/渐隐细线/图标瓷砖暖白字形）、深色主题变体、修复图标对比度与英文界面残留中文、禁用按钮可辨性、测试 162 项全绿 |
| **1.2** | 2026-09-13 | **修复 Android 图标整片缺失**（内嵌 Noto Emoji + Noto Sans Symbols 2 字体回退） |
| **1.1** | 2026-09-11 | 统一「Origami折纸滤杯」命名、修复杯测法闷蒸 4:00 与注水双重计时、新手模式底部署名与视觉增强 |
| **1.0** | 2026-09-03 | 首个 .NET 原生跨端版本：四层架构、冲煮引擎、模拟称、四模块工作台、双语 |

完整条目见 **[CHANGELOG.md](CHANGELOG.md)**。

---

## 5. 文档索引

- [CHANGELOG.md](CHANGELOG.md) — 版本变化记录与发布清单
- [DESIGN.md](DESIGN.md) — 设计系统「暖仪式 Warm Ritual」单一事实来源（色板/字阶/组件语言）
- [docs/01-项目建设材料总览.md](docs/01-项目建设材料总览.md)
- [docs/02-咖啡计算逻辑说明.md](docs/02-咖啡计算逻辑说明.md) — 冲煮计算逻辑详解
- [docs/03-产品结构规划-四模块模式.md](docs/03-产品结构规划-四模块模式.md)

---

*AlphaSunCoffeeScale · 作者：阳光 net2net2net（Vx: net2net） · 用一杯好咖啡开启每一天。*
