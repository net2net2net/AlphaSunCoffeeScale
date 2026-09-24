AlphaSunCoffeeScale 1.5 · macOS 版（自包含单文件，无需安装 .NET）

【芯片对应】
  AlphaSunCoffeeScale-1.5-macos-arm64  → Apple Silicon（M1 / M2 / M3 …）
  AlphaSunCoffeeScale-1.5-macos-x64    → Intel Mac

【安装】
  1. 解压得到 AlphaSunCoffeeScale.app，拖到「应用程序」文件夹
  2. 首次打开因无签名会被 Gatekeeper 拦住，两种方式任一：
     · 右键（或 Control + 点按）该 App → 打开 → 确认「打开」
     · 终端执行：xattr -cr /Applications/AlphaSunCoffeeScale.app
  3. 之后双击即可正常启动

【说明】本包未做 Apple 开发者签名与公证（CI 无证书），功能与签名版完全一致。
【数据位置】配置与冲煮记录（settings.json / brews.json / myplans.json）
  存于 ~/Library/Application Support/AlphaSunCoffeeScale/（Finder：前往文件夹粘贴此路径），
  不写进 .app 包内——这样更新 App 不会丢数据，也不会破坏包内容。
【排错】右键 → 显示包内容 → Contents/MacOS/CoffeeScale 可直接在终端运行看输出；
       崩溃日志在数据目录 CoffeeScale.crash.log。
