<#
.SYNOPSIS
  unityroom 用のビルドを作り、アップロードする 4 ファイルを Builds/unityroom/ にまとめる（DESIGN §14-3。手順は docs/PUBLISH.md）。

.DESCRIPTION
  unityroom の公式ヘルプ「unityroomにアップロードするためのビルド方法」の設定に合わせる:
    Development Build オフ / Compression Format = Gzip / Decompression Fallback オフ
  これは BuildScript.BuildWebGLUnityroom が一時的に設定してビルドし、終わったら元に戻す（GitHub Pages 用の設定は変えない）。
  出力（Builds/unityroom-build/edgewise/Build）から次の 4 ファイルを確かめて Builds/unityroom/ にコピーする:
    edgewise.loader.js / edgewise.data.gz / edgewise.framework.js.gz / edgewise.wasm.gz
  確認すること: ちょうど 4 ファイル・.unityweb / .br が無い（圧縮形式の取り違え）・.gz が本当に gzip（先頭 1F 8B）・曲ファイルが無い。
  アップロードそのものは magu が自分のアカウントで行う（このスクリプトはファイルを用意するだけ）。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/package_unityroom.ps1             # ビルドしてまとめる
  powershell -ExecutionPolicy Bypass -File tools/package_unityroom.ps1 -SkipBuild  # 既にあるビルドをまとめ直すだけ
#>
param(
    [switch]$SkipBuild,
    [string]$Out = 'Builds/unityroom'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$proj = Split-Path -Parent $PSScriptRoot
Set-Location $proj
$name = 'edgewise'
$buildRoot = Join-Path $proj "Builds/unityroom-build/$name"
$buildDir = Join-Path $buildRoot 'Build'

if (-not $SkipBuild) {
    Write-Output '[unityroom] building (Gzip, Decompression Fallback OFF, Development Build OFF) ...'
    $log = 'unityroom_build.log'
    & (Join-Path $PSScriptRoot 'RunUnity.ps1') -LogName $log -Extra '-buildTarget', 'WebGL', '-executeMethod', 'Edgewise.EditorTools.BuildScript.BuildWebGLUnityroom'
    $logPath = Join-Path $proj "Logs/$log"
    $ok = Select-String -Path $logPath -Pattern '\[Edgewise\] WebGL build \(unityroom\) Succeeded' -Quiet
    if (-not $ok) { throw "unityroom のビルドに失敗しました（$logPath）" }
}

if (-not (Test-Path $buildDir)) { throw "ビルドが見つかりません: $buildDir" }
$files = Get-ChildItem $buildDir -File
$bad = $files | Where-Object { $_.Extension -in '.unityweb', '.br' }
if ($bad) { throw "圧縮形式が違います（unityroom は Gzip・Decompression Fallback オフ）: $($bad.Name -join ', ')" }
$audio = Get-ChildItem $buildRoot -Recurse -File | Where-Object { $_.Extension -in '.wav', '.mp3', '.ogg', '.flac', '.aif', '.aiff', '.m4a' }
if ($audio) { throw "曲ファイルがまぎれています（公開しない）: $($audio.Name -join ', ')" }

$expected = @("$name.loader.js", "$name.data.gz", "$name.framework.js.gz", "$name.wasm.gz")
foreach ($e in $expected) {
    if (-not (Test-Path (Join-Path $buildDir $e))) { throw "見つかりません: $e（Build/ の中身: $($files.Name -join ', ')）" }
}
if ($files.Count -ne 4) { throw "Build/ のファイルが 4 つではありません: $($files.Name -join ', ')" }
foreach ($e in $expected | Where-Object { $_ -like '*.gz' }) {
    $fs = [IO.File]::OpenRead((Join-Path $buildDir $e))
    try { $b0 = $fs.ReadByte(); $b1 = $fs.ReadByte() } finally { $fs.Close() }
    if ($b0 -ne 0x1f -or $b1 -ne 0x8b) { throw "$e が gzip ではありません" }
}

$outDir = Join-Path $proj $Out
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force $outDir | Out-Null
foreach ($e in $expected) { Copy-Item (Join-Path $buildDir $e) $outDir }

Write-Output "[unityroom] upload these 4 files (from $outDir):"
$sum = 0
foreach ($e in $expected) {
    $f = Get-Item (Join-Path $outDir $e)
    $sum += $f.Length
    $hash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.Substring(0, 12)
    Write-Output ("  {0,-28} {1,12:N0} bytes  sha256 {2}" -f $e, $f.Length, $hash)
}
Write-Output ("[unityroom] total {0:N1} MB" -f ($sum / 1MB))
Write-Output '[unityroom] unityroom の「WebGL設定」で Unity のバージョンを 6000.3.x にし、「WebGLアップロード」で拡張子を確かめながら 1 つずつ選ぶ（docs/PUBLISH.md）。'
