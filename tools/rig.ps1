# Starts a throwaway copy of the game with the freshly built standalone host and some mods, headless (-batchmode), and prints what the loader logged.
# The copy links to the real game's data folders (read-only), has its own data folder (settings, compile cache, log), and never touches your save:
# the save folder is hashed before and after and the result is printed.
#   .\rig.ps1 -Mods F:\Git\HalfGripMods\mods\M1911,F:\Git\HalfGripMods\mods\RPG -Seconds 40
#   .\rig.ps1 -Mods F:\Git\HalfGripMods\mods\Minigun -Seconds 40 -Pattern "Minigun|Gun hook|error"
# Build first (tools\build.ps1). Needs the .NET-built dist\standalone and release\HalfGripToolsStandalone-*.zip (for winhttp.dll and the ini).
param(
	[string[]]$Mods = @(),
	[int]$Seconds = 40,
	[string]$Name = "A",
	[string]$Pattern = "",
	[string]$Extra = "",
	[string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo",
	[string]$Work = (Join-Path $env:TEMP "halfgrip-rig")
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$rig = Join-Path $Work "game\$Name"
$data = Join-Path $Work "data\$Name"
if (-not (Test-Path (Join-Path $rig "HalfGrip.exe"))) {
	New-Item -ItemType Directory -Force $rig | Out-Null
	robocopy $GameDir $rig /E /XD HalfGrip_Data MonoBleedingEdge BepInEx Mods HalfGripTools /XF winhttp.dll doorstop_config.ini /NFL /NDL /NJH /NJS /NP | Out-Null
	cmd /c mklink /J "$rig\HalfGrip_Data" "$GameDir\HalfGrip_Data" | Out-Null
	cmd /c mklink /J "$rig\MonoBleedingEdge" "$GameDir\MonoBleedingEdge" | Out-Null
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$zip = Get-ChildItem "$root\release\HalfGripToolsStandalone-*.zip" | Select-Object -First 1
	[IO.Compression.ZipFile]::ExtractToDirectory($zip.FullName, "$Work\doorstop-$Name")
	Copy-Item "$Work\doorstop-$Name\winhttp.dll", "$Work\doorstop-$Name\doorstop_config.ini" $rig
}
# the loader and the mods are refreshed on every run
robocopy "$root\dist\standalone\HalfGripTools" "$rig\HalfGripTools" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
$modsDir = Join-Path $rig "Mods"
if (Test-Path $modsDir) { Remove-Item $modsDir -Recurse -Force }
New-Item -ItemType Directory -Force $modsDir | Out-Null
foreach ($m in $Mods) { robocopy $m (Join-Path $modsDir (Split-Path $m -Leaf)) /E /NFL /NDL /NJH /NJS /NP | Out-Null }
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force $data | Out-Null

$save = "$env:USERPROFILE\AppData\LocalLow\Half Games\HalfGrip"
function Get-SaveHash {
	$files = Get-ChildItem $save -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike "Mods*" } | Sort-Object Name
	($files | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash }) -join ""
}
$before = Get-SaveHash

$env:HALFGRIPTOOLS_DATA = $data
$log = Join-Path $rig "unity.log"
$arguments = "-batchmode -logFile `"$log`" -screen-width 1280 -screen-height 720 $Extra"
$proc = Start-Process -FilePath (Join-Path $rig "HalfGrip.exe") -ArgumentList $arguments -WorkingDirectory $rig -PassThru
Start-Sleep -Seconds $Seconds
if (-not $proc.HasExited) { $proc.Kill() }
Start-Sleep -Seconds 2
$after = Get-SaveHash
Write-Host ("save folder unchanged: " + ($before -eq $after))
$ours = Join-Path $data "HalfGripTools.log"
$lines = if (Test-Path $ours) { Get-Content $ours } else { @("(no HalfGripTools.log in $data)") }
if ($Pattern -ne "") { $lines = $lines | Where-Object { $_ -match $Pattern } }
$lines | Select-Object -First 200
