param([string]$GameDir)
. (Join-Path $PSScriptRoot 'Common.ps1')
$project = Split-Path $PSScriptRoot -Parent
$game = Find-Game $GameDir
$dotnet = Find-DotNet
$env:MSBuildEnableWorkloadResolver = 'false'
& $dotnet build (Join-Path $project 'CustomCursorLoader.csproj') -c Release "-p:GameDir=$game" --ignore-failed-sources
if ($LASTEXITCODE) { throw '构建失败。' }
$stage = Join-Path $project 'dist\mods\CustomCursorLoader'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'bin\Release\net9.0\CustomCursorLoader.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $project 'CustomCursorLoader.json') -Destination $stage
Copy-Item -LiteralPath (Join-Path $project 'LICENSE'),(Join-Path $project 'THIRD_PARTY_NOTICES.md') -Destination $stage
$gallery = Join-Path $stage 'gallery'
New-Item -ItemType Directory -Path $gallery -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $project 'assets\gallery') -File | Copy-Item -Destination $gallery -Force
Copy-Item -LiteralPath (Join-Path $project 'docs\INSTALL.md') -Destination (Join-Path $project 'dist\安装说明.md')
Copy-Item -LiteralPath (Join-Path $project 'docs\CONFIG.md') -Destination (Join-Path $project 'dist\自定义图片教程.md')
Copy-Item -LiteralPath (Join-Path $project 'docs\TESTING.md') -Destination (Join-Path $project 'dist\测试清单.md')
$payload = Get-ChildItem -LiteralPath $stage -Filter '*.dll'
if ($payload.Count -ne 1 -or $payload[0].Name -ne 'CustomCursorLoader.dll') { throw '发布目录含有意外 DLL，请检查。' }
Write-Host "发布目录：$stage"
