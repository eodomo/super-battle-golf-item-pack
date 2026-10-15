# Builds the release zip that players extract straight into their Super Battle Golf folder.
# It contains BepInEx (the official release, downloaded once and hash-checked) plus the Item Pack plugin.
#
#   pwsh tools\package.ps1            -> dist\SuperBattleGolfItemPack-v<version>.zip
#
# Requires the .NET SDK (uses %USERPROFILE%\.dotnet\dotnet.exe if present, else dotnet on PATH).
$ErrorActionPreference = 'Stop'

$BepInExVersion = '5.4.23.5'
$BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
$BepInExSha256 = '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'

$root = Split-Path $PSScriptRoot -Parent
$version = [regex]::Match((Get-Content "$root\src\Plugin.cs" -Raw), 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Could not read Plugin.Version from src\Plugin.cs' }

# 1. Build without installing into the local game folder.
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet build "$root\ItemPack.csproj" -c Release -p:InstallToGame=false --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$dll = "$root\bin\Release\netstandard2.1\ItemPack.dll"

# 2. Fetch BepInEx (cached in .cache\, which git ignores).
$cache = "$root\.cache"
New-Item -ItemType Directory -Force $cache | Out-Null
$bepinexZip = "$cache\BepInEx_win_x64_$BepInExVersion.zip"
if (-not (Test-Path $bepinexZip)) {
    Write-Host "Downloading BepInEx $BepInExVersion..."
    Invoke-WebRequest $BepInExUrl -OutFile $bepinexZip
}
if ((Get-FileHash $bepinexZip -Algorithm SHA256).Hash -ne $BepInExSha256) {
    Remove-Item $bepinexZip
    throw "BepInEx download does not match the expected SHA-256; deleted it. Run again to retry."
}

# 3. Stage: BepInEx files at the root, the plugin in BepInEx\plugins\ItemPack.
$stage = "$root\dist\stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
Expand-Archive $bepinexZip -DestinationPath $stage
$pluginDir = "$stage\BepInEx\plugins\ItemPack"
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item $dll $pluginDir
Copy-Item "$root\Assets\*" $pluginDir -Recurse
# Never ship audio: optional sound/music replacements are supplied by each player (and may be copyrighted).
Get-ChildItem $pluginDir -Recurse -Include *.ogg, *.wav, *.mp3, *.flac | Remove-Item
Copy-Item "$root\tools\README-players.txt" "$pluginDir\README.txt"

# 4. Zip it.
$zip = "$root\dist\SuperBattleGolfItemPack-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -Recurse -Force $stage

$entries = [IO.Compression.ZipFile]::OpenRead($zip)
try { $entries.Entries | ForEach-Object { "  $($_.FullName)" } } finally { $entries.Dispose() }
Write-Host "Packaged $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
