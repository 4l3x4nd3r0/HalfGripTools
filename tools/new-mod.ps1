# Makes a new mod folder from a template: <Path>\<Name>\mod.json, src\Mod.cs and <Name>.csproj (for editor completion only).
#   .\new-mod.ps1 MyMod                      in .\examples
#   .\new-mod.ps1 MyMod -Path C:\work\mods -GameDir "D:\Games\Half Grip Demo"   (GameDir is where the .csproj looks for the game DLLs)
param([Parameter(Mandatory = $true)][string]$Name, [string]$Path = "", [string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo")
$ErrorActionPreference = "Stop"
if ($Path -eq "") { $Path = Join-Path (Split-Path -Parent $PSScriptRoot) "examples" }
$dir = Join-Path $Path $Name
if (Test-Path $dir) { throw "$dir already exists" }
New-Item -ItemType Directory -Force "$dir\src", "$dir\sprites" | Out-Null
$id = $Name.ToLowerInvariant() -replace "[^a-z0-9_-]", ""
@"
{
  "id": "$id",
  "name": "$Name",
  "version": "0.1.0",
  "author": "",
  "description": "",
  "gameVersion": "0.419",
  "apiVersion": "2.0.0",
  "kind": "item",
  "trust": "full",
  "tags": []
}
"@ | Set-Content "$dir\mod.json" -Encoding utf8
@"
using HalfGripTools;

namespace $Name
{
	public class Mod : HalfGripMod
	{
		public override void Load(ModContext ctx)
		{
			ctx.Log.LogInfo("$Name says hello");
			// ctx.Config.Bind(...)   settings, saved to config\$id.cfg in the data folder
			// ctx.PatchAll();        apply every [HarmonyPatch] class in this mod
			// ctx.Items.Add(...)     put an item in the game's catalog
			// ctx.DataFolder        a folder this mod may write to (never write into the mod's own folder)
		}
	}
}
"@ | Set-Content "$dir\src\Mod.cs" -Encoding utf8

# A project file for the editor only: the game never reads it. It gives completion and error squiggles in VS Code / Visual Studio
# (open the folder, or run "dotnet build" here). It points at the game's own DLLs and at the installed HalfGripTools.
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
    <AssemblyName>HalfGripMod.$id</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src\**\*.cs" />
    <Reference Include="$managed\*.dll" Exclude="$managed\mscorlib.dll;$managed\netstandard.dll;$managed\System*.dll" />
    <!-- The standalone install keeps HalfGripTools next to the game, the BepInEx one inside BepInEx. Use whichever is there. -->
    <Reference Include="$GameDir\HalfGripTools\HalfGripTools.dll" Condition="Exists('$GameDir\HalfGripTools\HalfGripTools.dll')" />
    <Reference Include="$GameDir\HalfGripTools\0Harmony.dll" Condition="Exists('$GameDir\HalfGripTools\0Harmony.dll')" />
    <Reference Include="$GameDir\BepInEx\plugins\HalfGripTools\HalfGripTools.dll" Condition="!Exists('$GameDir\HalfGripTools\HalfGripTools.dll') And Exists('$GameDir\BepInEx\plugins\HalfGripTools\HalfGripTools.dll')" />
    <Reference Include="$GameDir\BepInEx\core\0Harmony.dll" Condition="!Exists('$GameDir\HalfGripTools\0Harmony.dll') And Exists('$GameDir\BepInEx\core\0Harmony.dll')" />
  </ItemGroup>
</Project>
"@ | Set-Content "$dir\$Name.csproj" -Encoding utf8
"bin/`r`nobj/`r`n.vs/" | Set-Content "$dir\.gitignore" -Encoding ascii
Write-Host "Created $dir. Copy it to the Mods folder in the game folder (make it if it is not there) and start the game."
