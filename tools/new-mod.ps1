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
  "gameVersion": "0.418",
  "apiVersion": "1.1.0",
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
			// ctx.Config.Bind(...)   settings, saved to BepInEx\config\halfgrip.$id.cfg
			// ctx.PatchAll();        apply every [HarmonyPatch] class in this mod
			// ctx.Items.Add(...)     put an item in the game's catalog
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
    <Reference Include="$GameDir\BepInEx\core\BepInEx.dll" />
    <Reference Include="$GameDir\BepInEx\core\0Harmony.dll" />
    <Reference Include="$GameDir\BepInEx\plugins\HalfGripTools\HalfGripTools.dll" />
  </ItemGroup>
</Project>
"@ | Set-Content "$dir\$Name.csproj" -Encoding utf8
"bin/`r`nobj/`r`n.vs/" | Set-Content "$dir\.gitignore" -Encoding ascii
Write-Host "Created $dir. Copy it to BepInEx\HalfGripMods in the game folder and start the game."
