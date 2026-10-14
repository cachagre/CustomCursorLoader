$ErrorActionPreference = 'Stop'

function Find-DotNet {
    $found = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    throw '需要 .NET 9 SDK。请安装 Microsoft 官方 SDK 后重试。'
}

function Find-Game([string]$GameDir) {
    if ($GameDir) {
        $resolved = (Resolve-Path -LiteralPath $GameDir).Path
        if (!(Test-Path -LiteralPath (Join-Path $resolved 'data_sts2_windows_x86_64\sts2.dll'))) {
            throw 'GameDir 应为包含 SlayTheSpire2.exe 的游戏安装目录。'
        }
        return $resolved
    }
    if ($env:STS2_GAME_DIR) { return Find-Game $env:STS2_GAME_DIR }
    $steamRoots = @(
        (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
        (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath
        (Join-Path ${env:ProgramFiles(x86)} 'Steam')
    )
    foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
        foreach ($name in @('Steam', 'SteamLibrary')) { $steamRoots += Join-Path $drive.Root $name }
    }
    $libraries = @($steamRoots | Where-Object { $_ } | Select-Object -Unique)
    foreach ($steam in $libraries) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $steamRoots += $match.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    foreach ($steam in ($steamRoots | Where-Object { $_ } | Select-Object -Unique)) {
        $candidate = Join-Path $steam 'steamapps\common\Slay the Spire 2'
        if (Test-Path -LiteralPath (Join-Path $candidate 'data_sts2_windows_x86_64\sts2.dll')) { return $candidate }
    }
    throw '没有找到游戏。请传入 -GameDir "游戏安装目录"。'
}
