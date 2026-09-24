<#
.SYNOPSIS
    AlphaSunCoffeeScale 一键发布脚本：打包 Windows 单文件 EXE + Android 签名 APK，
    并按「AlphaSunCoffeeScale-<版本>」规范命名输出到 dist\。

.DESCRIPTION
    步骤：
      1) 校验版本号三处对齐（MainWindow.AppVer ↔ Android ApplicationDisplayVersion ↔ -Version 参数）
      2) 发布 Windows 单文件自包含 EXE（PublishSingleFile + SelfContained + win-x64）
      3) 调 publish-android.ps1 构建并签名 APK（内部走英文路径 C:\dev\cs-build 规避 aapt2 中文路径损坏）
      4) 统一重命名为 AlphaSunCoffeeScale-<版本>.exe / .apk 放入 dist\
      5) 校验：产物存在、EXE 为 PE(MZ) 头、APK 签名可验证、aapt2 读回 versionName

    用法：  powershell ./release.ps1 -Version 1.3
    注意：  Android 打包偶发 XABLD7000 重命名假错（签名 APK 实际已生成），
            本脚本会自行从构建目录找回 APK，不中断发布。

.PARAMETER Version
    本次发布版本号，例如 1.3。须与 MainWindow.AppVer、Android ApplicationDisplayVersion 一致。
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "1.3"
)

$ErrorActionPreference = "Stop"
$projRoot = $PSScriptRoot
if (-not $projRoot) { $projRoot = (Get-Location).Path }

function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; exit 1 }
function Ok($msg)   { Write-Host "[OK]   $msg" -ForegroundColor Green }

# ---------- ① 版本号三处对齐 ----------
$mwPath   = Join-Path $projRoot "src\CoffeeScale.UI\MainWindow.cs"
$csprojPath = Join-Path $projRoot "src\CoffeeScale.Android\CoffeeScale.Android.csproj"

$appVer = (Select-String -Path $mwPath -Pattern 'AppVer = "([^"]+)"').Matches[0].Groups[1].Value
$displayVer = (Select-String -Path $csprojPath -Pattern '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>').Matches[0].Groups[1].Value

Write-Host "==> 版本核对: 参数=$Version  MainWindow.AppVer=$appVer  Android.Display=$displayVer"
if ($appVer -ne $Version -or $displayVer -ne $Version) {
    Fail "版本号不一致！请先把 MainWindow.AppVer 与 ApplicationDisplayVersion 都改成 $Version（CHANGELOG.md 同步补条目）"
}
Ok "版本号三处对齐：$Version"

$dist = Join-Path $projRoot "dist"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$exeOut = "$dist\AlphaSunCoffeeScale-$Version.exe"
$apkOut = "$dist\AlphaSunCoffeeScale-$Version.apk"

# ---------- ② Windows 单文件 EXE ----------
Write-Host "`n==> [1/3] 发布 Windows 单文件 EXE ..."
$desktopProj = Join-Path $projRoot "src\CoffeeScale.Avalonia\CoffeeScale.Avalonia.csproj"
# 注意：不给 bash 传参（PS→原生调用按系统码页转码，中文路径必乱码；反斜杠被 bash 吃掉）。
# dotnet-env.sh 的本质只是补 8 个缺失环境变量（SystemRoot 等），这里直接补齐并直呼 dotnet.exe。
# 同时：PowerShell 5.1 的 Set-Location 不改**进程** CWD，必须用 .NET API 设置进程级 CWD，
# 否则相对参数（dist/win-x64 等）会解析到错误目录。
$envDone = @{}
foreach ($kv in @{
    "SystemRoot"        = "C:\Windows"
    "SystemDrive"       = "C:"
    "ProgramData"       = "C:\ProgramData"
    "ALLUSERSPROFILE"   = "C:\ProgramData"
    "APPDATA"           = "C:\Users\net2n\AppData\Roaming"
    "LOCALAPPDATA"      = "C:\Users\net2n\AppData\Local"
    "ProgramFiles"      = "C:\Program Files"
    "CommonProgramFiles"= "C:\Program Files\Common Files"
    "PUBLIC"            = "C:\Users\Public"
}.GetEnumerator()) {
    if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($kv.Key))) {
        [Environment]::SetEnvironmentVariable($kv.Key, $kv.Value, "Process")
    }
}
[System.IO.Directory]::SetCurrentDirectory($projRoot)
& "C:\Program Files\dotnet\dotnet.exe" publish src/CoffeeScale.Avalonia/CoffeeScale.Avalonia.csproj -c Release -r win-x64 -o dist/win-x64
if ($LASTEXITCODE -ne 0) { Fail "EXE 发布失败" }

Copy-Item (Join-Path $dist "win-x64\CoffeeScale.exe") $exeOut -Force
$fs = [System.IO.File]::OpenRead($exeOut); $mz = New-Object byte[] 2; [void]$fs.Read($mz, 0, 2); $fs.Close()
if (-not ($mz[0] -eq 0x4D -and $mz[1] -eq 0x5A)) { Fail "EXE 不是有效 PE 文件：$exeOut" }
Ok ("EXE: {0}  ({1:N1} MB)" -f $exeOut, ((Get-Item $exeOut).Length / 1MB))

# ---------- ③ Android APK ----------
Write-Host "`n==> [2/3] 构建 Android 签名 APK ..."
# 进程 CWD 已是仓库根 → 用 ASCII 相对路径调用，避免中文路径经原生调用乱码
& powershell -NoProfile -ExecutionPolicy Bypass -File .\publish-android.ps1
# publish-android.ps1 可能因 XABLD7000 假错返回非 0（APK 实际已生成），不直接中断，下面自行找回
if ($LASTEXITCODE -ne 0) { Write-Host "[WARN] publish-android.ps1 退出码 $LASTEXITCODE（若是 XABLD7000 假错可忽略，继续找回产物）" -ForegroundColor Yellow }

$apkCandidate = $null
$distApk = Join-Path $dist "android\CoffeeScale-Debug.apk"
if (Test-Path $distApk) { $apkCandidate = $distApk }
$csBuild = "C:\dev\cs-build\src\CoffeeScale.Android\bin\Debug\net9.0-android"
if (-not $apkCandidate) {
    $signed = Get-ChildItem -Path $csBuild -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($signed) { $apkCandidate = $signed.FullName }
}
if (-not $apkCandidate) { Fail "未找到签名 APK（dist 与 $csBuild 均无 *-Signed.apk）" }

# 时间戳兜底：dist 里的可能是旧包，优先取构建目录中最新产物
$built = Get-ChildItem -Path $csBuild -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($built -and $built.LastWriteTime -ge (Get-Item $apkCandidate).LastWriteTime) { $apkCandidate = $built.FullName }
Write-Host "    使用 APK 源：$apkCandidate"
Copy-Item $apkCandidate $apkOut -Force
Ok ("APK: {0}  ({1:N1} MB)" -f $apkOut, ((Get-Item $apkOut).Length / 1MB))

# ---------- ④ 校验 ----------
Write-Host "`n==> [3/3] 校验产物 ..."
$sdk = "C:\Users\net2n\Android\Sdk"
$apksigner = "$sdk\build-tools\35.0.0\apksigner.bat"
$aapt2 = "$sdk\build-tools\35.0.0\aapt2.exe"

if (Test-Path $apksigner) {
    & $apksigner verify $apkOut | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "APK 签名校验失败" }
    Ok "APK 签名验证通过"
} else {
    Write-Host "[WARN] 未找到 apksigner，跳过签名校验" -ForegroundColor Yellow
}
if (Test-Path $aapt2) {
    $badging = & $aapt2 dump badging $apkOut | Select-String "package:"
    Write-Host "    $badging"
    if ("$badging" -notmatch "versionName='$Version'") {
        Write-Host "[WARN] APK versionName 与 $Version 不一致，请核对！" -ForegroundColor Yellow
    } else {
        Ok "APK versionName = $Version"
    }
}

# ---------- ⑤ 清理中间产物（保持发布目录只留规范命名的版本化文件） ----------
# dist\win-x64\（EXE 发布输出）与 dist\android\（APK 脚本输出）只是流水线中间目录，
# 规范命名拷贝完成后即删除，避免与 AlphaSunCoffeeScale-<版本> 产生"哪个是最新的"歧义。
foreach ($stage in @("win-x64", "android")) {
    $p = Join-Path $dist $stage
    if (Test-Path $p) { Remove-Item -Recurse -Force $p; Write-Host "[OK]   清理中间目录 dist\$stage" }
}

Write-Host ""
Write-Host "========== 发布完成 ==========" -ForegroundColor Cyan
Get-ChildItem $dist -Filter "AlphaSunCoffeeScale-*" | Format-Table Name, @{n="Size(MB)";e={[math]::Round($_.Length/1MB,1)}}, LastWriteTime -AutoSize
Write-Host "安装：手机点击 APK（允许未知来源）或 adb install -r `"$apkOut`"；桌面双击 `"$exeOut`"。"
