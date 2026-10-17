<#
.SYNOPSIS
  GitHub Pages への公開（DESIGN §14-3）: Builds/WebGL の中身を gh-pages ブランチに置いて commit する。
  -Push を付けたときだけ push する（付けなければ push の直前で止まる）。

.DESCRIPTION
  - main の作業ツリーは汚さない。一時フォルダに git worktree を作り、そこで gh-pages を組み立てて commit し、最後に worktree を消す。
  - gh-pages がローカルにも リモートにも無ければ、履歴を持たない（orphan）ブランチとして作る。
  - 中身は Builds/WebGL のすべて（index.html と Build/）＋ .nojekyll。毎回まるごと置き換える。
  - ビルドが GitHub Pages 用（Decompression Fallback ON の .unityweb）であることを確かめる。unityroom 用（.gz）なら止める。
  - BGM の曲ファイル（.wav / .mp3 / .ogg など）がまぎれていたら止める（OpenTracks のライセンス上、曲ファイルそのものは公開しない。DESIGN §8-2・§14-3）。
  - push はメインセッションが magu の承認を得てから -Push を付けて実行する。

.EXAMPLE
  # 空実行（ローカルの gh-pages に commit するところまで。push はしない）
  powershell -ExecutionPolicy Bypass -File tools/deploy_pages.ps1

.EXAMPLE
  # 承認を得たあとで push する
  powershell -ExecutionPolicy Bypass -File tools/deploy_pages.ps1 -Push
#>
param(
    [string]$BuildDir = 'Builds/WebGL',
    [string]$Branch = 'gh-pages',
    [string]$Remote = 'origin',
    # commit のメッセージ（省略時は「Deploy WebGL build from main <sha>」）
    [string]$Message,
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

function Invoke-Git {
    # git を呼び、失敗したら止める（PowerShell 5.1 でも stderr を例外にしないように、終了コードで判断する）
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & git.exe @args 2>&1 } finally { $ErrorActionPreference = $prev }
    $text = @($out | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed ($LASTEXITCODE):`n$($text -join "`n")" }
    return $text
}

# 失敗しても止めない版（終了コードを $LASTEXITCODE で見る）
function Invoke-GitSoft {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & git.exe @args 2>$null } finally { $ErrorActionPreference = $prev }
    return @($out | ForEach-Object { "$_" })
}

$repo = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).ToString().Trim()
Set-Location $repo
$build = Join-Path $repo $BuildDir
Write-Output "[deploy] repo  : $repo"
Write-Output "[deploy] build : $build"

# ---- 1. ビルドの確認 ----
if (-not (Test-Path (Join-Path $build 'index.html'))) { throw "index.html が見つかりません: $build（先に BuildScript.BuildWebGL でビルドする）" }
$buildFiles = Get-ChildItem $build -Recurse -File
$loader = $buildFiles | Where-Object { $_.Name -like '*.loader.js' }
$unityweb = $buildFiles | Where-Object { $_.Extension -eq '.unityweb' }
$gz = $buildFiles | Where-Object { $_.Extension -in '.gz', '.br' }
if (-not $loader) { throw 'Build/*.loader.js が見つかりません' }
if ($gz) { throw "圧縮済みの .gz / .br があります（unityroom 用のビルド？）。GitHub Pages には Decompression Fallback ON のビルド（.unityweb）を置く: $($gz.Name -join ', ')" }
if (-not $unityweb) { throw '.unityweb がありません（Decompression Fallback ON のビルドではない）' }
$audio = $buildFiles | Where-Object { $_.Extension -in '.wav', '.mp3', '.ogg', '.flac', '.aif', '.aiff', '.m4a' }
if ($audio) { throw "曲ファイルがまぎれています（公開しない）: $($audio.FullName -join ', ')" }
$total = ($buildFiles | Measure-Object Length -Sum).Sum
Write-Output ("[deploy] {0} files, {1:N1} MB" -f $buildFiles.Count, ($total / 1MB))

# ---- 2. 元にする main のコミット ----
$sha = (Invoke-Git rev-parse --short HEAD | Select-Object -First 1).ToString().Trim()
$dirty = @(Invoke-Git status --porcelain --untracked-files=no | Where-Object { $_ }).Count -gt 0
if (-not $Message) { $Message = "Deploy WebGL build from main $sha" + $(if ($dirty) { ' (with uncommitted changes)' } else { '' }) }

# ---- 3. 一時フォルダに worktree を作る（main の作業ツリーには触れない） ----
$wt = Join-Path ([IO.Path]::GetTempPath()) ('edgewise-gh-pages-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$hasRemote = (Invoke-GitSoft remote) -contains $Remote
$localExists = $false
Invoke-GitSoft show-ref --verify --quiet "refs/heads/$Branch" | Out-Null
if ($LASTEXITCODE -eq 0) { $localExists = $true }
$remoteExists = $false
if ($hasRemote -and -not $localExists) {
    # リモートにだけ gh-pages があるか（認証の画面を出さない。リポジトリがまだ無い・非公開なら「無い」として進む）
    $env:GIT_TERMINAL_PROMPT = '0'
    $env:GCM_INTERACTIVE = 'never'
    $ls = Invoke-GitSoft ls-remote --heads $Remote $Branch
    if ($LASTEXITCODE -eq 0 -and $ls) { $remoteExists = $true }
    Write-Output "[deploy] $Remote/$Branch exists: $remoteExists"
}

try {
    if ($localExists) {
        Write-Output "[deploy] using the local branch $Branch"
        Invoke-Git worktree add $wt $Branch | Out-Null
    } elseif ($remoteExists) {
        Write-Output "[deploy] tracking $Remote/$Branch"
        Invoke-Git fetch $Remote $Branch | Out-Null
        Invoke-Git worktree add -b $Branch $wt "$Remote/$Branch" | Out-Null
    } else {
        Write-Output "[deploy] creating an orphan branch $Branch"
        Invoke-Git worktree add --detach $wt HEAD | Out-Null
        Invoke-Git -C $wt checkout --orphan $Branch | Out-Null
        Invoke-Git -C $wt rm -rf --quiet . | Out-Null
    }

    # ---- 4. 中身を入れ替える（.git 以外を消して、ビルドをまるごと置く） ----
    Get-ChildItem $wt -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $build '*') $wt -Recurse -Force
    # Jekyll に処理させない（_ で始まるファイルなどをそのまま配信する）
    New-Item -ItemType File (Join-Path $wt '.nojekyll') -Force | Out-Null

    Invoke-Git -C $wt add -A | Out-Null
    $changes = @(Invoke-Git -C $wt status --porcelain | Where-Object { $_ })
    if ($changes) {
        Invoke-Git -C $wt commit -q -m $Message | Out-Null
        Write-Output "[deploy] committed to $Branch"
    } else {
        Write-Output "[deploy] no changes since the last deploy"
    }
    Write-Output ''
    Write-Output (Invoke-Git -C $wt log -1 --format='[deploy] %h %s (%an, %ad)' --date=iso)
    Write-Output "[deploy] contents of $Branch :"
    foreach ($line in (Invoke-Git -C $wt ls-tree -r -l HEAD)) {
        # <mode> <type> <object> <size>\t<path>
        $parts = $line -split "`t"
        $size = ($parts[0] -split '\s+')[3]
        Write-Output ("  {0,12:N0}  {1}" -f [long]$size, $parts[1])
    }

    # ---- 5. push（-Push のときだけ） ----
    if ($Push) {
        if (-not $hasRemote) { throw "リモート '$Remote' がありません（git remote add $Remote <URL> を先に）" }
        Write-Output "[deploy] pushing $Branch to $Remote ..."
        Invoke-Git -C $wt push $Remote "$($Branch):$($Branch)" | ForEach-Object { Write-Output "  $_" }
        Write-Output '[deploy] pushed. GitHub の Settings → Pages で、Source を「Deploy from a branch」・Branch を gh-pages /(root) にする（初回のみ）。'
    } else {
        Write-Output ''
        Write-Output '[deploy] STOPPED before push（-Push を付けていないので push していません）。'
        Write-Output "[deploy] 中身を確かめて、magu の承認を得てから: powershell -ExecutionPolicy Bypass -File tools/deploy_pages.ps1 -Push"
        if (-not $hasRemote) { Write-Output "[deploy] （リモート '$Remote' はまだありません）" }
    }
}
finally {
    if (Test-Path $wt) {
        Invoke-GitSoft worktree remove --force $wt | Out-Null
        if (Test-Path $wt) { Remove-Item $wt -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Invoke-GitSoft worktree prune | Out-Null
}
