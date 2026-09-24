#!/usr/bin/env bash
# publish-ios.sh — AlphaSunCoffeeScale iOS / MacCatalyst 构建脚本
#
# 运行环境：macOS + Xcode + .NET 9 SDK + `dotnet workload install ios maccatalyst`
# Windows 无法安装 iOS workload / Xcode，故此脚本只能在 Mac 上执行。
#
# 用法示例：
#   # 1) 构建到 iOS 模拟器（无需付费开发者账号，本地直接跑）
#   ./publish-ios.sh sim
#
#   # 2) 构建真机 app bundle（需要 Apple Developer + 签名）
#   CODESIGN_KEY="Apple Development: net2n (XXXXXXXXXX)" \
#   CODESIGN_PROVISION="AlphaSunCoffeeScale Development" \
#   ./publish-ios.sh device
#
#   # 3) 打包成可分发 IPA（Release + ArchiveOnBuild，适合 TestFlight / Ad-Hoc）
#   CODESIGN_KEY="Apple Distribution: net2n (XXXXXXXXXX)" \
#   CODESIGN_PROVISION="AlphaSunCoffeeScale AdHoc" \
#   ./publish-ios.sh ipa
#
#   # 4) 构建 MacCatalyst 桌面 app
#   CODESIGN_KEY="Developer ID Application: net2n (XXXXXXXXXX)" \
#   ./publish-ios.sh mac
#
# 注意：
#   - 环境变量 CODESIGN_KEY / CODESIGN_PROVISION 仅在 device / ipa / mac 目标需要。
#   - 模拟器目标使用 `-r iossimulator-x64`；Apple Silicon 上也可用 iossimulator-arm64。
#   - 首次运行建议在 Xcode 中打开 build 产物检查签名 / 权限。
#   - Entitlements.plist 中 `get-task-allow` 为 true（开发调试），正式分发前请改为 false。

set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
DIST="$ROOT/dist/ios"
mkdir -p "$DIST"

IOS_PROJ="$ROOT/src/CoffeeScale.iOS/CoffeeScale.iOS.csproj"
MAC_PROJ="$ROOT/src/CoffeeScale.MacCatalyst/CoffeeScale.MacCatalyst.csproj"
VERSION="1.5"

usage() {
    echo "用法: $0 {sim|device|ipa|mac}"
    echo "  sim     - 构建到 iOS 模拟器"
    echo "  device  - 构建 iOS 真机 app bundle"
    echo "  ipa     - 打包 Release IPA"
    echo "  mac     - 构建 MacCatalyst app"
    exit 1
}

build_sim() {
    echo "==> [iOS 模拟器] 构建 AlphaSunCoffeeScale $VERSION ..."
    dotnet build "$IOS_PROJ" -c Debug -r iossimulator-x64
    echo "==> 构建完成。可用 Xcode → Open Developer Tool → Simulator 运行 app bundle。"
}

build_device() {
    echo "==> [iOS 真机] 构建 AlphaSunCoffeeScale $VERSION ..."
    [[ -n "${CODESIGN_KEY:-}" ]] || { echo "错误：device 构建需要 CODESIGN_KEY 环境变量"; exit 1; }
    dotnet build "$IOS_PROJ" -c Release -r ios-arm64 \
        -p:CodesignKey="$CODESIGN_KEY" \
        ${CODESIGN_PROVISION:+-p:CodesignProvision="$CODESIGN_PROVISION"}
    echo "==> 构建完成。可在连接的真机上部署。"
}

build_ipa() {
    echo "==> [iOS IPA] 打包 AlphaSunCoffeeScale $VERSION ..."
    [[ -n "${CODESIGN_KEY:-}" ]] || { echo "错误：ipa 打包需要 CODESIGN_KEY 环境变量"; exit 1; }
    rm -rf "$DIST"
    mkdir -p "$DIST"
    dotnet publish "$IOS_PROJ" -c Release -r ios-arm64 \
        -p:ArchiveOnBuild=true \
        -p:CodesignKey="$CODESIGN_KEY" \
        ${CODESIGN_PROVISION:+-p:CodesignProvision="$CODESIGN_PROVISION"} \
        -o "$DIST"
    echo "==> IPA 输出："
    find "$DIST" -name "*.ipa" -print
}

build_mac() {
    echo "==> [MacCatalyst] 构建 AlphaSunCoffeeScale $VERSION ..."
    [[ -n "${CODESIGN_KEY:-}" ]] || { echo "错误：mac 构建需要 CODESIGN_KEY 环境变量"; exit 1; }
    dotnet build "$MAC_PROJ" -c Release -r maccatalyst-arm64 \
        -p:CodesignKey="$CODESIGN_KEY" \
        ${CODESIGN_PROVISION:+-p:CodesignProvision="$CODESIGN_PROVISION"}
    echo "==> 构建完成。产物在 bin/Release/net9.0-maccatalyst/。"
}

case "${1:-}" in
    sim) build_sim ;;
    device) build_device ;;
    ipa) build_ipa ;;
    mac) build_mac ;;
    *) usage ;;
esac
