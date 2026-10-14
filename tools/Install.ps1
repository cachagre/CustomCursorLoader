param([string]$GameDir)
. (Join-Path $PSScriptRoot 'Common.ps1')
$project = Split-Path $PSScriptRoot -Parent
$game = Find-Game $GameDir
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) { throw '请先关闭游戏。' }
$stage = Join-Path $project 'dist\mods\CustomCursorLoader'
if (!(Test-Path -LiteralPath (Join-Path $stage 'CustomCursorLoader.dll'))) { throw '请先运行 Build.ps1。' }
$mods = Join-Path $game 'mods'
$target = Join-Path $mods 'CustomCursorLoader'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
if (Test-Path -LiteralPath $target) {
    # 移到 mods 扫描范围之外；备份里的旧 Manifest 不会被重复加载。
    $backupRoot = Join-Path $project 'install-backups'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backup = Join-Path $backupRoot ('CustomCursorLoader-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $resolvedTarget = (Resolve-Path -LiteralPath $target).Path
    $expectedTarget = [IO.Path]::GetFullPath((Join-Path $game 'mods\CustomCursorLoader'))
    $resolvedBackup = [IO.Path]::GetFullPath($backup)
    if (!$resolvedBackup.StartsWith([IO.Path]::GetFullPath($backupRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw '备份路径越界，已停止。'
    }
    $item = Get-Item -LiteralPath $target -Force
    if ($resolvedTarget -ne $expectedTarget -or !$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw '安装目标不是预期的普通 Mod 目录，已停止。'
    }
    Move-Item -LiteralPath $resolvedTarget -Destination $backup
    Write-Host "旧版本备份：$backup"
}
try { Copy-Item -LiteralPath $stage -Destination $target -Recurse }
catch {
    if ($backup -and !(Test-Path -LiteralPath $target)) { Move-Item -LiteralPath $backup -Destination $target }
    throw
}
Write-Host "已安装：$target"
Write-Host '首次启动请在游戏的 Mods 提示中同意加载；F8 重载，F9 恢复。'
