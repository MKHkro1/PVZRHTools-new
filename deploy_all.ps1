# PVZRHTools 部署脚本（三端差异同步 + 全目录复查）
#   背景（2026-09-26 两次踩坑）：
#     ① 改了三端共享的数据契约 ToolModData 后，只同步了 exe+PVZRHTools.dll，
#        漏掉桌面端目录里的 ToolModData.dll ⇒ 运行时 MissingMethodException
#        （新 WPF 调新 setter、加载的却是旧程序集）。
#     ② 更早一次：plugins 里的插件 DLL 没替换成功，导致"修了但看起来没修"。
#   ⇒ 定式：**整目录差异同步（MD5 比对）+ 同步后再复查一遍全目录**，绝不手工挑文件。
#
# 用法: pwsh -File deploy_all.ps1 [-WhatIfOnly]
param([switch]$WhatIfOnly)

$ErrorActionPreference = 'Stop'
$rel  = "D:\Web\二创\插件项目Code\PVZRHTools\.release"
$game = "D:\GAME\PVZ\融合\4.0\B版\植物大战僵尸融合版4.0雪夜上半"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# 三端：(名字, 源目录, 目标目录)
$targets = @(
    @{ Name = '桌面端'; Src = "$rel\PVZRHTools";        Dst = "$game\PVZRHTools" },
    @{ Name = '插件端'; Src = "$rel\BepInEx\plugins";   Dst = "$game\BepInEx\plugins" }
)

# 游戏运行时 plugins/exe 被占用，覆盖会失败 ⇒ 先拦一道
$p = Get-Process PlantsVsZombiesRH -ErrorAction SilentlyContinue
if ($p) { throw "游戏正在运行（PlantsVsZombiesRH）——请先关闭游戏再部署（插件 DLL 被占用）。" }

function Sync-One([string]$name, [string]$src, [string]$dst) {
    Write-Host "== $name ==" -ForegroundColor Cyan
    Write-Host "   源: $src"
    Write-Host "   目标: $dst"
    if (-not (Test-Path $src)) { Write-Host "   [SKIP] 源目录不存在" -ForegroundColor Yellow; return 0 }
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }

    $n = 0
    foreach ($f in Get-ChildItem $src -File) {
        $g = Join-Path $dst $f.Name
        $need = $true
        if (Test-Path $g) {
            $a = (Get-FileHash $f.FullName -Algorithm MD5).Hash
            $b = (Get-FileHash $g -Algorithm MD5).Hash
            if ($a -eq $b) { $need = $false }
        }
        if ($need) {
            if ($WhatIfOnly) { Write-Host "   [预演] 需更新 $($f.Name)" -ForegroundColor DarkGray; $n++; continue }
            if (Test-Path $g) { Copy-Item $g "$g.bak-$stamp" -Force -ErrorAction SilentlyContinue }
            Copy-Item $f.FullName $g -Force
            Write-Host "   已更新 $($f.Name)" -ForegroundColor Green
            $n++
        }
    }
    if ($n -eq 0) { Write-Host "   已是最新（0 个差异）" -ForegroundColor Green }
    return $n
}

$total = 0
foreach ($t in $targets) { $total += Sync-One $t.Name $t.Src $t.Dst }

# ★ 关键：同步后**再复查一遍全目录**——这是"部署未生效"的唯一可靠判据
Write-Host ""
Write-Host "== 复查（应全为零差异）==" -ForegroundColor Cyan
$bad = 0
foreach ($t in $targets) {
    $diff = @()
    foreach ($f in Get-ChildItem $t.Src -File) {
        $g = Join-Path $t.Dst $f.Name
        if (-not (Test-Path $g)) { $diff += "$($f.Name)(缺失)"; continue }
        if ((Get-FileHash $f.FullName -Algorithm MD5).Hash -ne (Get-FileHash $g -Algorithm MD5).Hash) { $diff += $f.Name }
    }
    if ($diff.Count -gt 0) { Write-Host "   [$($t.Name)] 仍不一致: $($diff -join ', ')" -ForegroundColor Red; $bad += $diff.Count }
    else { Write-Host "   [$($t.Name)] ✓ 全目录一致" -ForegroundColor Green }
}

Write-Host ""
if ($bad -eq 0) { Write-Host "部署有效 ✓（本次更新 $total 个文件，备份后缀 bak-$stamp）" -ForegroundColor Green; exit 0 }
else { Write-Host "部署无效 ✗（$bad 个文件仍不一致）" -ForegroundColor Red; exit 1 }
