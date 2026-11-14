# Lists, for every mod in a folder, the game members it uses, the Harmony targets, the reflection strings, the loader API it calls and
# what would stop it from running restricted. Offline: Roslyn reads the source against the game's own DLLs. Writes one Markdown report.
# HalfGripTools is built first (dist\), like check-mods.ps1, and its DLL is what the mods are read against.
#   .\hook-census.ps1                                     the mods in F:\Git\HalfGripMods\mods, report F:\Git\HalfGrip\HOOK_CENSUS.md
#   .\hook-census.ps1 -ModsPath C:\work\mods -Out C:\work\census.md
param([string]$ModsPath = "F:\Git\HalfGripMods\mods", [string]$Out = "F:\Git\HalfGrip\HOOK_CENSUS.md", [string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
	$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
	$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

dotnet build "$root\HalfGripTools\HalfGripTools.csproj" -c Release "-p:GameDir=$GameDir" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "HalfGripTools does not build" }
$tools = "$root\dist\BepInEx\plugins\HalfGripTools\HalfGripTools.dll"

# bin\ and obj\ next to the project are git-ignored.
dotnet run --project "$PSScriptRoot\hook-census\HookCensus.csproj" -c Release -v q --nologo -- --mods $ModsPath --game $GameDir --tools $tools --out $Out
if ($LASTEXITCODE -ne 0) { throw "hook census failed" }
