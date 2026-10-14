#requires -Version 7.0
param([string]$GameDir, [string]$WorkDir = (Join-Path (Get-Location) 'work\cursor-runtime-tests'), [switch]$LateInitialization, [switch]$Visual)
. (Join-Path $PSScriptRoot 'Common.ps1')
$project = Split-Path $PSScriptRoot -Parent
$game = Find-Game $GameDir
$testRoot = [IO.Path]::GetFullPath((Join-Path $WorkDir ([Guid]::NewGuid().ToString('N'))))
if ($testRoot.StartsWith($game + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw '隔离测试目录必须在游戏目录之外。'
}
$runtime = Join-Path $testRoot 'runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$env:MSBuildEnableWorkloadResolver = 'false'
& (Find-DotNet) build (Join-Path $project 'tests\RuntimeTests\RuntimeTests.csproj') -c Release "-p:GameDir=$game" --ignore-failed-sources
if ($LASTEXITCODE) { throw 'RuntimeTests 编译失败。' }
Get-ChildItem -LiteralPath $game -File | Where-Object { $_.Extension -in '.exe','.dll','.json' } | Copy-Item -Destination $runtime
Copy-Item -LiteralPath (Join-Path $game 'data_sts2_windows_x86_64'),(Join-Path $game 'SlayTheSpire2.pck') -Destination $runtime -Recurse
$production = Join-Path $runtime 'mods\CustomCursorLoader'
$driver = Join-Path $runtime 'mods\RuntimeTests'
New-Item -ItemType Directory -Path $production,$driver -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'bin\Release\net9.0\CustomCursorLoader.dll'),(Join-Path $project 'CustomCursorLoader.json') -Destination $production
Copy-Item -LiteralPath (Join-Path $project 'assets\gallery') -Destination $production -Recurse
Copy-Item -LiteralPath (Join-Path $project 'tests\RuntimeTests\bin\Release\net9.0\RuntimeTests.dll'),(Join-Path $project 'tests\RuntimeTests\RuntimeTests.json') -Destination $driver
$noop = Join-Path $testRoot 'noop.gd'
Set-Content -LiteralPath $noop -Value 'extends Node'
$noopPath = $noop.Replace('\','/')
Set-Content -LiteralPath (Join-Path $runtime 'override.cfg') -Value (@"
[application]
config/custom_user_dir_name="CursorHarness"
[autoload]
SentryBootstrap="*$noopPath"
"@)
$isolationCheck = Join-Path $testRoot 'check.gd'
Set-Content -LiteralPath $isolationCheck -Value @'
extends SceneTree
func _initialize():
    if OS.get_user_data_dir().simplify_path() != OS.get_environment("CCL_ISOLATED_USER_ROOT").simplify_path():
        push_error("User data isolation failed")
        quit(1)
        return
    print("ISOLATION VERIFIED")
    quit()
'@
$originalAppData = $env:APPDATA
$originalLocalAppData = $env:LOCALAPPDATA
$originalTestRoot = $env:CCL_ISOLATED_USER_ROOT
$originalFixtures = $env:CCL_ANIMATION_FIXTURES
$originalCapture = $env:CCL_CAPTURE_PATH
try {
    $env:APPDATA = Join-Path $testRoot 'appdata'
    $env:LOCALAPPDATA = Join-Path $testRoot 'localappdata'
    $env:CCL_ISOLATED_USER_ROOT = (Join-Path $env:APPDATA 'CursorHarness').Replace('\','/')
    $env:CCL_ANIMATION_FIXTURES = Join-Path $project 'tests\fixtures'
    $env:CCL_CAPTURE_PATH = Join-Path $testRoot 'gallery.png'
    New-Item -ItemType Directory -Path $env:APPDATA,$env:LOCALAPPDATA -Force | Out-Null
    $exe = Join-Path $runtime 'SlayTheSpire2.exe'
    $checkLog = Join-Path $testRoot 'isolation.log'
    $process = Start-Process -FilePath $exe -ArgumentList @('--headless','--script',('"' + $isolationCheck + '"'),'--log-file',('"' + $checkLog + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $checkLog -Pattern 'ISOLATION VERIFIED' -Quiet)) { throw '未确认用户数据隔离，停止。' }
    $bootstrapLog = Join-Path $testRoot 'bootstrap.log'
    $process = Start-Process -FilePath $exe -ArgumentList @('--headless','--force-steam=off','--quit-after','180','--max-fps','60','--log-file',('"' + $bootstrapLog + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw '隔离游戏首次启动失败。' }
    # 只修改刚创建的测试设置；不访问原游戏用户数据或云存档。
    $settingsPath = Join-Path $env:CCL_ISOLATED_USER_ROOT 'default\1\settings.save'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    $settings['mod_settings'] = @{mods_enabled=$true;mod_list=@()}
    $settings['skip_intro_logo']=$true
    $settings['seen_ea_disclaimer']=$true
    $settings | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $settingsPath
    $log = Join-Path $testRoot 'runtime-tests.log'
    $runtimeArgs = @('--force-steam=off','--quit-after','2400','--max-fps','60','--log-file',('"' + $log + '"'))
    if ($Visual) { $runtimeArgs += @('--rendering-method','gl_compatibility') } else { $runtimeArgs += '--headless' }
    $runtimeArgs += @('--','--cursor-isolated-test')
    if ($LateInitialization) { $runtimeArgs += '--cursor-late-init-test' }
    $process = Start-Process -FilePath $exe -ArgumentList $runtimeArgs -WindowStyle Hidden -Wait -PassThru
    Select-String -LiteralPath $log -Pattern '^RUNTIME ' | ForEach-Object { Write-Host $_.Line }
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern '^RUNTIME COMPLETE' -Quiet)) { throw "运行测试失败，日志：$log" }
    $cursorConfigPath = Join-Path $env:CCL_ISOLATED_USER_ROOT 'CustomCursorLoader\config.json'
    $savedCursorConfig = [IO.File]::ReadAllBytes($cursorConfigPath)
    try {
        $frameConfig = Get-Content -LiteralPath $cursorConfigPath -Raw | ConvertFrom-Json -AsHashtable
        $frameConfig['cursorImage'] = 'cursors/animation.png'
        $frameConfig['cursorIdleFrame'] = 1
        $frameConfig['scale'] = 2
        $frameConfig | ConvertTo-Json | Set-Content -LiteralPath $cursorConfigPath
        $frameRestartLog = Join-Path $testRoot 'frame-restart.log'
        $process = Start-Process -FilePath $exe -ArgumentList @('--headless','--force-steam=off','--quit-after','2400','--max-fps','60','--log-file',('"' + $frameRestartLog + '"'),'--','--cursor-isolated-test','--cursor-frame-restart-test') -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $frameRestartLog -Pattern '^RUNTIME FRAME RESTART PASS' -Quiet)) { throw '跨进程默认帧验证失败。' }
        Write-Host 'RUNTIME FRAME RESTART PASS 默认帧选择跨进程保留'
    }
    finally { [IO.File]::WriteAllBytes($cursorConfigPath, $savedCursorConfig) }
    $completedDriver = [IO.Path]::GetFullPath((Join-Path $testRoot 'driver-completed'))
    if ([IO.Path]::GetFullPath($driver) -ne [IO.Path]::GetFullPath((Join-Path $runtime 'mods\RuntimeTests')) -or
        !$completedDriver.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw '测试驱动移动目录越界，停止。'
    }
    Move-Item -LiteralPath $driver -Destination $completedDriver
    $restartLog = Join-Path $testRoot 'restart.log'
    $process = Start-Process -FilePath $exe -ArgumentList @('--headless','--force-steam=off','--quit-after','600','--max-fps','60','--log-file',('"' + $restartLog + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $restartLog -SimpleMatch '[CustomCursorLoader] 已加载 cursors/中文 空格.png；64×32 → 72×36' -Quiet)) { throw '跨进程快捷键大小验证失败。' }
    Write-Host 'RUNTIME RESTART PASS 配置跨进程持久化'
    Write-Host "测试副本与日志位于：$testRoot（约占用 3 GB，确认后可自行删除）。"
}
finally {
    $env:APPDATA = $originalAppData
    $env:LOCALAPPDATA = $originalLocalAppData
    $env:CCL_ISOLATED_USER_ROOT = $originalTestRoot
    $env:CCL_ANIMATION_FIXTURES = $originalFixtures
    $env:CCL_CAPTURE_PATH = $originalCapture
}
