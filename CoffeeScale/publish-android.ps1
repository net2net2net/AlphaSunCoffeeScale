# 手冲咖啡计算器 — Android APK 发布脚本
# 用法（PowerShell）：
#   ./publish-android.ps1            # Debug 签名 APK（可直接安装，日志可调）
#   ./publish-android.ps1 -Release   # Release（需自备签名密钥）
#
# 环境要求（已在本机就绪）：
#   - dotnet android workload（dotnet workload install android）
#   - JDK 17（JAVA_HOME 指向；默认 C:\Users\net2n\android-dev\jdk\jdk-17.0.20.1+1）
#   - Android SDK（ANDROID_SDK_ROOT；默认 C:\Users\net2n\Android\Sdk，需 cmdline-tools + platforms;android-35 + build-tools;35.0.0）
#
# 重要：.NET Android 的 aapt2 在非 ASCII（中文）工程路径下会产出损坏的中间 APK
#       （XABLD7024 ZipIOException）。本脚本把 src 同步到纯英文构建目录 C:\dev\cs-build 再编译，产物拷回 dist\android。
param(
    [switch]$Release
)
$ErrorActionPreference = "Stop"

$projRoot = $PSScriptRoot
$buildRoot = "C:\dev\cs-build"
$javaHome = $env:JAVA_HOME; if (-not $javaHome) { $javaHome = "C:\Users\net2n\android-dev\jdk\jdk-17.0.20.1+1" }
$sdkRoot  = $env:ANDROID_SDK_ROOT; if (-not $sdkRoot) { $sdkRoot = "C:\Users\net2n\Android\Sdk" }
$config = if ($Release) { "Release" } else { "Debug" }

Write-Host "==> 同步源码到英文构建目录 $buildRoot"
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
# /MIR 镜像：清理构建目录中已删除的旧文件；bin/obj 用 /XD 排除（构建目录自带 bin/obj 保留以加速增量）
robocopy "$projRoot\src" "$buildRoot\src" /MIR /XD bin obj /XF *.user /NJH /NJS /NFL /NDL | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy 失败: $LASTEXITCODE" }

Write-Host "==> 构建 $config APK（JDK=$javaHome  SDK=$sdkRoot）"
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$args = @(
    "build", "$buildRoot\src\CoffeeScale.Android\CoffeeScale.Android.csproj",
    "-c", $config, "-t:SignAndroidPackage",
    "-p:JavaSdkDirectory=$javaHome",
    "-p:AndroidSdkDirectory=$sdkRoot",
    "-p:nodeReuse=false", "/nr:false"
)
& $dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败: $LASTEXITCODE" }

$apkDir = "$buildRoot\src\CoffeeScale.Android\bin\$config\net9.0-android"
$apk = Get-ChildItem "$apkDir\*-Signed.apk" | Select-Object -First 1
if (-not $apk) { throw "未找到签名 APK：$apkDir" }

$dest = "$projRoot\dist\android"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$destFile = "$dest\CoffeeScale-$config.apk"
Copy-Item $apk.FullName $destFile -Force

# 校验签名（debug 密钥也可通过验证，确认 APK 未损坏）
$apksigner = "$sdkRoot\build-tools\35.0.0\apksigner.bat"
if (Test-Path $apksigner) {
    Write-Host "==> 验证 APK 签名"
    & $apksigner verify --print-certs $destFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "APK 签名验证失败" }
}

Write-Host ""
Write-Host "完成：$destFile ($([math]::Round((Get-Item $destFile).Length/1MB,2)) MB)"
Write-Host "安装：adb install -r `"$destFile`"  （或把 APK 传到手机点击安装，需允许未知来源）"
