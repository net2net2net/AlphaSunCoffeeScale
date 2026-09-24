#!/usr/bin/env bash
# AlphaSunCoffeeScale · Linux 一键安装（当前用户，无需 root）
#
# 用法：解压 AlphaSunCoffeeScale-1.5-linux-x64.tar.gz 后，在解压目录执行：
#         chmod +x install-linux.sh && ./install-linux.sh
#   卸载：./install-linux.sh --uninstall
#
# 做了三件事：
#   1. 程序装到 ~/.local/share/AlphaSunCoffeeScale/
#   2. 图标装到 ~/.local/share/icons/hicolor/512x512/apps/
#   3. 生成 ~/.local/share/applications/AlphaSunCoffeeScale.desktop（应用菜单可见）
set -euo pipefail

APP_ID="alphasuncoffeescale"
APP_DIR="$HOME/.local/share/AlphaSunCoffeeScale"
ICON_DIR="$HOME/.local/share/icons/hicolor/512x512/apps"
DESKTOP_DIR="$HOME/.local/share/applications"
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "${1:-}" = "--uninstall" ]; then
  rm -rf "$APP_DIR" "$DESKTOP_DIR/AlphaSunCoffeeScale.desktop" "$ICON_DIR/$APP_ID.png"
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" || true
  echo "已卸载 AlphaSunCoffeeScale"
  exit 0
fi

mkdir -p "$APP_DIR" "$ICON_DIR" "$DESKTOP_DIR"

install -m 0755 "$SRC_DIR/CoffeeScale" "$APP_DIR/CoffeeScale"
[ -f "$SRC_DIR/icon-512.png" ] && install -m 0644 "$SRC_DIR/icon-512.png" "$ICON_DIR/$APP_ID.png"

cat > "$DESKTOP_DIR/AlphaSunCoffeeScale.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=AlphaSun 手冲咖啡
GenericName=Coffee Scale
Comment=没有感情的手冲咖啡智能计算器（AlphaSunCoffeeScale）
Exec=$APP_DIR/CoffeeScale
Icon=$APP_ID
Terminal=false
Categories=Utility;Education;
Keywords=coffee;pourover;brew;scale;
StartupWMClass=CoffeeScale
EOF

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" || true

echo "安装完成："
echo "  程序目录：$APP_DIR"
echo "  应用菜单入口：$DESKTOP_DIR/AlphaSunCoffeeScale.desktop"
echo "  也可直接运行：$APP_DIR/CoffeeScale"
echo
echo "若启动报缺库（libX11 / fontconfig 等），请先装运行时依赖："
echo "  Debian/Ubuntu: sudo apt install -y libx11-6 libice6 libsm6 fontconfig libfontconfig1 openssl libssl3 zlib1g"
echo "  Fedora/RHEL  : sudo dnf install -y libX11 libICE libSM fontconfig openssl zlib"
echo "  Arch         : sudo pacman -S --needed libx11 libice libsm fontconfig openssl zlib"
