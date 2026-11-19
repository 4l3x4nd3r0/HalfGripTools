# Builds HalfGripTools into dist\ and optionally installs it into the game for testing.
#   .\build.ps1                    build everything
#   .\build.ps1 -Install           also copy the BepInEx host into the game folder (BepInEx\plugins\HalfGripTools)
#   .\build.ps1 -Install -GameDir "D:\Games\Half Grip Demo"
# The core is HalfGripTools.dll: it needs no BepInEx, no Harmony and no Doorstop. Around it, one project per way of starting it:
#   HalfGripTools.Harmony     patches the game with Harmony and turns the patches into calls to GameHooks (hosts that patch from outside use it)
#   HalfGripTools.BepInEx     the BepInEx plugin
#   HalfGripTools.Standalone   the Doorstop host, no BepInEx
#   HalfGripTools.Unity       the host for a build inside the game's Unity project (built here only to prove it compiles; dist\unity\)
#   dist\BepInEx\plugins\HalfGripTools\   the BepInEx host, unzips into a game that has BepInEx
#   dist\standalone\HalfGripTools\        the standalone host, needs Doorstop's winhttp.dll next to the game (package.ps1 adds it)
# Needs the .NET 8 SDK and the game (the build compiles against the game's own DLLs). The BepInEx host also needs BepInEx in the game folder;
# without it only the core and the standalone host are built.
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

$hasBepInEx = Test-Path "$GameDir\BepInEx\core\BepInEx.dll"
$projects = @("HalfGripTools.Standalone\HalfGripTools.Standalone.csproj", "HalfGripTools.Unity\HalfGripTools.Unity.csproj")
if ($hasBepInEx) { $projects += "HalfGripTools.BepInEx\HalfGripTools.BepInEx.csproj" } else { Write-Host "BepInEx is not in ${GameDir}: the BepInEx host is not built." }
foreach ($p in $projects) {
	dotnet build "$root\$p" -c Release "-p:GameDir=$GameDir" --nologo
	if ($LASTEXITCODE -ne 0) { throw "build of $p failed" }
}
Write-Host "Built. dist\ is ready."

if ($Install) {
	if (-not $hasBepInEx) { throw "BepInEx is not installed in $GameDir (the standalone host is installed from the zip that package.ps1 makes)" }
	robocopy "$root\dist\BepInEx\plugins\HalfGripTools" "$GameDir\BepInEx\plugins\HalfGripTools" /MIR /NFL /NDL /NJH /NJS | Out-Null
	Write-Host "Installed into $GameDir\BepInEx\plugins"
}