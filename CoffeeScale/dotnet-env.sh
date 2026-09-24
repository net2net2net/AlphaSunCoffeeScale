#!/usr/bin/env bash
# dotnet 构建环境修复包装脚本（Windows / Git Bash）
#
# 【问题现象】
#   在本会话的 bash 环境下执行 `dotnet build` / `dotnet test` / `dotnet publish` 会失败：
#     NuGet.targets(781,5): error : Value cannot be null. (Parameter 'path1')
#     NETSDK1060: 读取资产文件时出错 … project.assets.json … Value cannot be null. (Parameter 'path1')
#   连 `dotnet nuget locals global-packages --list` 也一样报错（与项目无关，是全局问题）。
#
# 【根因】
#   该 shell 是「最小环境」：SystemRoot / SystemDrive / ProgramData / ALLUSERSPROFILE /
#   APPDATA / ProgramFiles / CommonProgramFiles / PUBLIC 全部缺失。
#   NuGet 解析机器级配置目录时走
#     NuGet.Common.NuGetEnvironment.GetFolderPath(MachineWideSettingsBaseDirectory)
#       -> Path.Combine(Environment.GetFolderPath(SpecialFolder.CommonApplicationData), "NuGet")
#   CommonApplicationData 取不到值返回 null，Path.Combine(null, …) 即抛 'path1' 为 null。
#   （开启 NUGET_SHOW_STACK=true 可看到上述调用栈，定位关键就在 XPlatMachineWideSetting..ctor）
#
# 【注意】这不是沙箱拦截，也不是项目问题：
#   - 禁用沙箱后变量依然缺失；
#   - 换任意工作目录（C:/ D:/ E:/）都同样失败；
#   - 项目 obj/project.assets.json 与 ~/.nuget/packages 均完好。
#   NuGet.Config 本身也是干净的（默认 nuget.org 源），无需改动。
#
# 【用法】
#   ./dotnet-env.sh build CoffeeScale.sln -c Debug -warnaserror
#   ./dotnet-env.sh test  CoffeeScale.sln -c Debug
#   ./dotnet-env.sh publish src/CoffeeScale.Avalonia/CoffeeScale.Avalonia.csproj -c Release ...
# 已存在同名变量时不覆盖（用 ${VAR:-默认} 语义），便于外部显式指定。

export SystemRoot="${SystemRoot:-C:\\Windows}"
export SystemDrive="${SystemDrive:-C:}"
export ProgramData="${ProgramData:-C:\\ProgramData}"
export ALLUSERSPROFILE="${ALLUSERSPROFILE:-C:\\ProgramData}"
export APPDATA="${APPDATA:-C:\\Users\\net2n\\AppData\\Roaming}"
export LOCALAPPDATA="${LOCALAPPDATA:-C:\\Users\\net2n\\AppData\\Local}"
export ProgramFiles="${ProgramFiles:-C:\\Program Files}"
export CommonProgramFiles="${CommonProgramFiles:-C:\\Program Files\\Common Files}"
export PUBLIC="${PUBLIC:-C:\\Users\\Public}"

exec "C:/Program Files/dotnet/dotnet" "$@"
