# Compiles the C# of every mod in a folder against the game, BepInEx and HalfGripTools, without starting the game.
# It uses the same language version (C# 10) as the compiler inside HalfGripTools. Nothing is written to the repo.
# HalfGripTools is built first (dist\), and its DLL is what the mods compile against.
#   .\check-mods.ps1 -ModsPath .\examples                  every mod folder inside .\examples
#   .\check-mods.ps1 -ModsPath C:\work\mymods -Mod MyMod   one mod
param([Parameter(Mandatory = $true)][string]$ModsPath, [string]$Mod = "", [string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo")
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

$failed = @()
foreach ($dir in Get-ChildItem $ModsPath -Directory) {
	if ($Mod -ne "" -and $dir.Name -ne $Mod) { continue }
	if (-not (Test-Path "$($dir.FullName)\src")) { Write-Host "$($dir.Name): no code, nothing to compile"; continue }
	$work = Join-Path $env:TEMP ("halfgrip-check-" + $dir.Name)
	New-Item -ItemType Directory -Force $work | Out-Null
	$managed = "$GameDir\HalfGrip_Data\Managed"
	@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>HalfGripMod.$($dir.Name)</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$($dir.FullName)\src\**\*.cs" />
    <Reference Include="$managed\*.dll" Exclude="$managed\mscorlib.dll;$managed\netstandard.dll;$managed\System*.dll" />
    <Reference Include="$GameDir\BepInEx\core\BepInEx.dll" />
    <Reference Include="$GameDir\BepInEx\core\0Harmony.dll" />
    <Reference Include="$tools" />
  </ItemGroup>
</Project>
"@ | Set-Content "$work\check.csproj" -Encoding utf8
	Write-Host "== $($dir.Name)"
	dotnet build "$work\check.csproj" -c Release -v q --nologo "-p:OutDir=$work\out\"
	if ($LASTEXITCODE -ne 0) { $failed += $dir.Name }
}
if ($failed.Count -gt 0) { throw "These mods do not compile: $($failed -join ', ')" }
Write-Host "All mods compile."
