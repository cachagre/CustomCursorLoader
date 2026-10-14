param()
. (Join-Path $PSScriptRoot 'Common.ps1')
$project = Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver = 'false'
& (Find-DotNet) run --project (Join-Path $project 'tests\CoreTests\CoreTests.csproj') -c Release
if ($LASTEXITCODE) { throw '测试失败。' }
