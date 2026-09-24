using System;
using System.IO;

namespace CoffeeScale.Core;

/// <summary>
/// 用户数据落盘位置（配置 settings.json / 记录 brews.json / 我的方案 myplans.json / 崩溃日志）。
///
/// 背景：原先一律写 AppContext.BaseDirectory（程序同目录）。这在 Windows 单文件 exe 与安卓上都合适，
/// 但 macOS 的 .app 是一个「包」（BaseDirectory = AlphaSunCoffeeScale.app/Contents/MacOS），
/// 往包里写数据会：① 更新 App 时数据被覆盖丢失；② 破坏包内容（未签名包虽不触发 Gatekeeper 校验，
/// 但用户一旦自行签名，写入即让签名失效）。Linux 同理，装到 ~/.local/share 后程序目录可能只读。
///
/// 策略（对外行为保持一致的「同目录」直觉，仅在 Unix 桌面改为标准用户数据目录）：
///   Windows / Android / iOS  → AppContext.BaseDirectory（与既有用户数据完全兼容，不做迁移）
///   macOS                    → ~/Library/Application Support/AlphaSunCoffeeScale
///   Linux                    → ~/.local/share/AlphaSunCoffeeScale（XDG_DATA_HOME 优先）
/// 首次启动时若新目录为空、而程序目录里存在旧数据文件，自动搬一次（Migrate），老用户无感升级。
/// </summary>
public static class AppPaths
{
    private const string AppName = "AlphaSunCoffeeScale";

    /// <summary>数据目录（已确保存在；创建失败时回退到程序同目录）。</summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    /// <summary>崩溃日志目录：与数据目录相同（便于用户回传）。</summary>
    public static string LogDirectory => DataDirectory;

    /// <summary>取数据目录下的文件名全路径。</summary>
    public static string File(string fileName) => Path.Combine(DataDirectory, fileName);

    /// <summary>true 表示数据目录就是程序所在目录（Windows / 移动端），用于文档与提示文案。</summary>
    public static bool IsPortableLayout => DataDirectory == AppContext.BaseDirectory;

    private static string ResolveDataDirectory()
    {
        string dir;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // .NET 在 macOS 上 SpecialFolder.ApplicationData 指向 ~/.config，不是 macOS 惯例，
                // 这里显式用 ~/Library/Application Support（Finder 可见，符合 Apple 规范）。
                dir = Path.Combine(HomeDir(), "Library", "Application Support", AppName);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                dir = string.IsNullOrWhiteSpace(xdg)
                    ? Path.Combine(HomeDir(), ".local", "share", AppName)
                    : Path.Combine(xdg, AppName);
            }
            else
            {
                // Windows / Android / iOS：保持程序同目录，兼容既有数据
                return AppContext.BaseDirectory;
            }

            Directory.CreateDirectory(dir);
            MigrateLegacyFiles(dir);
            return dir;
        }
        catch
        {
            // 目录创建失败（只读 HOME 等）时退回程序同目录，保证功能可用
            return AppContext.BaseDirectory;
        }
    }

    private static string HomeDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        return home;
    }

    /// <summary>把早期版本写在程序目录里的 json 搬到新数据目录（只搬、不删，失败静默）。</summary>
    private static void MigrateLegacyFiles(string dir)
    {
        foreach (var name in new[] { "settings.json", "brews.json", "myplans.json" })
        {
            try
            {
                var oldPath = Path.Combine(AppContext.BaseDirectory, name);
                var newPath = Path.Combine(dir, name);
                if (System.IO.File.Exists(oldPath) && !System.IO.File.Exists(newPath))
                    System.IO.File.Copy(oldPath, newPath, false);
            }
            catch { }
        }
    }
}
