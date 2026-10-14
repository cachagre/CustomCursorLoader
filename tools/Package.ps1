param()
. (Join-Path $PSScriptRoot 'Common.ps1')
$project = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $project 'dist'
$dll = Join-Path $stage 'mods\CustomCursorLoader\CustomCursorLoader.dll'
if (!(Test-Path -LiteralPath $dll)) { throw '请先运行 Build.ps1。' }
$output = Split-Path $project -Parent
$manifest = Get-Content -LiteralPath (Join-Path $stage 'mods\CustomCursorLoader\CustomCursorLoader.json') -Raw | ConvertFrom-Json
$version = $manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '无效的发布版本号。' }
$zip = Join-Path $output ("CustomCursorLoader-v$version.zip")
Compress-Archive -LiteralPath (Join-Path $stage 'mods'),(Join-Path $stage '安装说明.md'),(Join-Path $stage '自定义图片教程.md'),(Join-Path $stage '测试清单.md') -DestinationPath $zip -Force
Write-Host "安装包：$zip"
