# iOS / MacCatalyst 构建指南

> 本工程为 .NET 9 + Avalonia 11 原生跨端项目，iOS / MacCatalyst head 工程只能在 **macOS + Xcode** 上构建。Windows 侧无法安装 `ios` / `maccatalyst` workload，也不具备 Xcode 工具链，因此 Windows 只维护源码与资源，真正的编译、签名、打包必须在 Mac 上完成。

## 目录结构

```
src/
├── CoffeeScale.iOS          # iOS 头工程
│   ├── AppDelegate.cs        # AvaloniaAppDelegate<App>，已注册 UseIconFontFallbacks()
│   ├── Info.plist            # 应用元数据：版本 1.5、图标 AppIcon、设备族 iPhone/iPad
│   ├── Entitlements.plist    # 开发授权（get-task-allow=true）
│   ├── Main.storyboard       # 启动屏（暖咖啡色 + 标题）
│   └── Assets.xcassets/
│       └── AppIcon.appiconset/  # 自动生成的 App 图标（14 个尺寸）
├── CoffeeScale.MacCatalyst   # MacCatalyst 头工程（复用同一套 UI）
│   └── Assets.xcassets/
│       └── AppIcon.appiconset/  # Mac 图标（10 个尺寸）
```

## 前置条件

1. macOS 12+（推荐 macOS 14+）
2. Xcode 15+（命令行工具 `xcode-select --install`）
3. .NET 9 SDK
4. 安装 workload：
   ```bash
   dotnet workload install ios maccatalyst
   ```
5. Apple Developer 账号（真机 / TestFlight / App Store 必需；模拟器可跳过）

## 快速构建

提供了 `publish-ios.sh`，常见命令：

```bash
# 1. iOS 模拟器（最简单，无需签名）
./publish-ios.sh sim

# 2. iOS 真机 app bundle（开发签名）
CODESIGN_KEY="Apple Development: net2n (XXXXXXXXXX)" \
  CODESIGN_PROVISION="AlphaSunCoffeeScale Development" \
  ./publish-ios.sh device

# 3. 打包可分发 IPA（Release + ArchiveOnBuild）
CODESIGN_KEY="Apple Distribution: net2n (XXXXXXXXXX)" \
  CODESIGN_PROVISION="AlphaSunCoffeeScale AdHoc" \
  ./publish-ios.sh ipa

# 4. MacCatalyst 桌面 app
CODESIGN_KEY="Developer ID Application: net2n (XXXXXXXXXX)" \
  ./publish-ios.sh mac
```

## 手动构建（如需自定义）

### iOS 模拟器

```bash
dotnet build src/CoffeeScale.iOS/CoffeeScale.iOS.csproj -c Debug -r iossimulator-x64
```

产物：`bin/Debug/net9.0-ios/iossimulator-x64/CoffeeScale.iOS.app`

### iOS 真机

```bash
dotnet build src/CoffeeScale.iOS/CoffeeScale.iOS.csproj -c Release -r ios-arm64 \
  -p:CodesignKey="Apple Development: net2n (XXXXXXXXXX)" \
  -p:CodesignProvision="AlphaSunCoffeeScale Development"
```

### 导出 IPA

```bash
rm -rf dist/ios
mkdir -p dist/ios
dotnet publish src/CoffeeScale.iOS/CoffeeScale.iOS.csproj -c Release -r ios-arm64 \
  -p:ArchiveOnBuild=true \
  -p:CodesignKey="Apple Distribution: net2n (XXXXXXXXXX)" \
  -p:CodesignProvision="AlphaSunCoffeeScale AdHoc" \
  -o dist/ios
```

## 版本号

iOS / MacCatalyst 版本必须与全平台保持一致：

- `Info.plist` 中的 `CFBundleShortVersionString`：用户可见版本（当前 `1.5`）
- `Info.plist` 中的 `CFBundleVersion`：构建号（当前 `6`，与 Android `ApplicationVersion` 对齐）
- 桌面端 `src/CoffeeScale.UI/MainWindow.cs` 的 `AppVer`
- Android `src/CoffeeScale.Android/CoffeeScale.Android.csproj` 的 `ApplicationDisplayVersion`

发布新版本时，四处需同步修改，并在 `CHANGELOG.md` 新增条目。

## 图标说明

图标由 `tools/gen-appicon.py` 程序化生成，主题色与 App 界面一致（暖焦糖 → 深咖啡渐变 + 奶霜咖啡杯）。重新生成：

```bash
python3 tools/gen-appicon.py
```

需要 Pillow。

## 已知限制

- 当前 `Entitlements.plist` 中 `get-task-allow` 为 `true`，仅用于开发与调试。提交 App Store / TestFlight 前请改为 `false`。
- iOS / MacCatalyst 未加入 `CoffeeScale.sln`：因为 Windows 环境未安装 Apple workload，加入会导致 `dotnet restore` 失败。各 head 工程均通过单独 `dotnet build / publish` 构建（与 Android 的 `publish-android.ps1` 保持一致）。
