# Builds HalfGripTools (the only compiled part) into dist\, and optionally installs it into the game for testing.
#   .\build.ps1                    build only
#   .\build.ps1 -Install           build, then copy HalfGripTools into the game folder
#   .\build.ps1 -Install -GameDir "D:\Games\Half Grip Demo"
# Needs the .NET 8 SDK and the game with BepInEx installed (the build compiles against the game's own DLLs).
# Mods are not built here: they are source, and HalfGripTools compiles them when the game starts.
param([string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo", [switch]$Install)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
# A per-user SDK install (~\.dotnet) wins if there is one; otherwise whatever dotnet is on PATH.
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
	$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
	$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

dotnet build "$root\HalfGripTools\HalfGripTools.csproj" -c Release "-p:GameDir=$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { throw "build of HalfGripTools failed" }
Write-Host "Built. dist\BepInEx\plugins\HalfGripTools is ready."

if ($Install) {
	if (-not (Test-Path "$GameDir\BepInEx\core\BepInEx.dll")) { throw "BepInEx is not installed in $GameDir (see the README, step 1)" }
	robocopy "$root\dist\BepInEx\plugins\HalfGripTools" "$GameDir\BepInEx\plugins\HalfGripTools" /MIR /NFL /NDL /NJH /NJS | Out-Null
	Write-Host "Installed into $GameDir\BepInEx\plugins"
}
