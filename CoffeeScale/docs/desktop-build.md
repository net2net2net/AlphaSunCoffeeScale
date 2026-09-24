# Linux / macOS 桌面版构建与打包

配套文档：[ios-build.md](ios-build.md)（iOS / MacCatalyst）。
桌面端一套代码（`CoffeeScale.Avalonia`）出四个 RID，Windows 在本机构建，Linux / macOS 走 GitHub Actions。

## 1. 四个 RID 与产物

| RID | 平台 | 构建地点 | 产物 |
|---|---|---|---|
| `win-x64` | Windows 10/11 x64 | 本机（PowerShell / bash） | `AlphaSunCoffeeScale-<版本>.exe` |
| `linux-x64` | Linux x64 | CI `ubuntu-22.04` | `AlphaSunCoffeeScale-<版本>-linux-x64.tar.gz` |
| `osx-arm64` | macOS Apple Silicon | CI `macos-15` | `AlphaSunCoffeeScale-<版本>-macos-arm64.tar.gz` |
| `osx-x64` | macOS Intel | CI `macos-15` | `AlphaSunCoffeeScale-<版本>-macos-x64.tar.gz` |

均为 **自包含单文件**（`SelfContained` + `PublishSingleFile`），目标机器无需装 .NET。

## 2. 本机命令（Windows 出 exe；Linux/macOS 出 ELF / Mach-O）

```bash
# 所有 dotnet 命令必须经 dotnet-env.sh（本 shell 缺 SystemRoot 等变量，否则 NuGet 报 path1 null）
bash dotnet-env.sh publish src/CoffeeScale.Avalonia/CoffeeScale.Avalonia.csproj \
  -c Release -r win-x64 -o dist/win-x64
```

换 RID 即可出其它平台的可执行文件；但 **Unix 产物不要在 Windows 上打包**——
可执行位、`.app` bundle 结构、`icns` 资源都需要原生文件系统语义，故 Linux/macOS 一律用 CI。

## 3. 云构建（`.github/workflows/build-desktop.yml`）

- 触发：`workflow_dispatch` 手动，或推送命中 `CoffeeScale/src/**`、`CoffeeScale/packaging/**`。
- 作业：`build-linux`（ubuntu-22.04）与 `build-macos`（macos-15，矩阵 `[osx-arm64, osx-x64]`）。
- 产物为 Actions Artifacts：`AlphaSunCoffeeScale-linux-x64-r<编号>`、`AlphaSunCoffeeScale-macos-{arm64,x64}-r<编号>`。
- 取包：`gh run download <run_id> --name <artifact>`（**大包下载常中途断流，需按 artifact 逐个带重试下**）。

## 4. 打包布局

```
packaging/linux/  AlphaSunCoffeeScale.desktop   应用菜单入口（Exec 指向 ~/.local/share 下的程序）
                  install-linux.sh              安装 / --uninstall（当前用户，无需 root）
                  README-Linux.txt              随包分发为「安装说明.txt」
packaging/macos/  Info.plist                    CFBundleExecutable=CoffeeScale / CFBundleIconFile=AppIcon
                  README-macOS.txt              随包分发为「安装说明.txt」
```

macOS `.app` 由工作流现场拼装：

```
AlphaSunCoffeeScale.app/Contents/MacOS/CoffeeScale      ← 单文件可执行文件
AlphaSunCoffeeScale.app/Contents/Resources/AppIcon.icns ← tools/gen-appicon.py 生成
AlphaSunCoffeeScale.app/Contents/Info.plist
```

未做 Apple 开发者签名与公证（CI 无证书）：用户「右键 → 打开」或 `xattr -cr` 去隔离属性即可启动。

## 5. 数据目录（AppPaths）

`CoffeeScale.Core/AppPaths.cs` 决定配置 / 记录 / 方案 / 崩溃日志落在哪：

| 平台 | 目录 |
|---|---|
| Windows / Android / iOS | 程序同目录（`AppContext.BaseDirectory`，兼容既有数据） |
| macOS | `~/Library/Application Support/AlphaSunCoffeeScale` |
| Linux | `~/.local/share/AlphaSunCoffeeScale`（尊重 `XDG_DATA_HOME`） |

macOS 上若沿用程序同目录，等于往 `.app` 包内写——更新 App 会丢数据、且破坏包内容，故必须分流。
首次启动会把程序目录里的旧 json 搬到新目录（只搬不删）。

## 6. 坑位记录

1. **NETSDK1147: workloads must be installed: android**
   共享 UI 库 `CoffeeScale.UI.csproj` 默认多目标 `net9.0-android`，Linux / macOS runner 没装 android workload。
   桌面发布必须加 `-p:CoffeeSkipAndroid=true`（UI 库内已有该开关，只留 `net9.0`）。
2. **Windows 上打包 Linux/macOS 产物**：可执行位与 `.app` 结构不对，双击无反应。只在原生 runner 上打包。
3. **`UsePlatformDetect()` 不可省**：原写死 `UseWin32()` 会让 Linux/macOS 启动即崩（找不到 Win32 后端）。
4. **Linux 缺系统库**：自包含包带了 .NET 与 Skia/HarfBuzz，但 X11 / fontconfig / openssl 需系统提供，
   Debian/Ubuntu：`sudo apt install -y libx11-6 libice6 libsm6 fontconfig libfontconfig1 libssl3 zlib1g`。
5. **仓库里有一份 `CoffeeScale/` 物理副本**：改完工作区源码要跑
   `python .workbuddy/scripts/sync_repo.py`（`--check` 干跑）单向镜像到发布仓，否则 CI 编的是旧代码。
