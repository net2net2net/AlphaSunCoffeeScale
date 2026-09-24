AlphaSunCoffeeScale 1.5 · Linux 版（x64，自包含单文件，无需安装 .NET）

【最简单】
  1. 解压后 ./CoffeeScale 直接运行（首次启动约 1-2 秒，原生库自解压）
  2. 想进应用菜单：chmod +x install-linux.sh && ./install-linux.sh
     （装到 ~/.local/share/AlphaSunCoffeeScale，卸载：./install-linux.sh --uninstall）

【系统依赖】自包含包已带 .NET 运行时与 Skia/HarfBuzz，但 X11 与字体库需系统提供：
  Debian/Ubuntu: sudo apt install -y libx11-6 libice6 libsm6 fontconfig libfontconfig1 libssl3 zlib1g
  Fedora/RHEL  : sudo dnf install -y libX11 libICE libSM fontconfig openssl zlib
  Arch         : sudo pacman -S --needed libx11 libice libsm fontconfig openssl zlib

【数据位置】配置与冲煮记录（settings.json / brews.json / myplans.json）
  存于 ~/.local/share/AlphaSunCoffeeScale/（遵循 XDG 规范，卸载 App 不丢数据；
  若设了 XDG_DATA_HOME 则在其下）。如在程序目录发现旧 json，首次启动会自动搬过去。
【排错】若启动无反应，在终端里运行 ./CoffeeScale 看输出；崩溃日志在数据目录 CoffeeScale.crash.log。
