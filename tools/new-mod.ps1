# Makes a new mod folder from a template: <Path>\<Name>\mod.json and src\Mod.cs.
#   .\new-mod.ps1 MyMod                      in .\examples
#   .\new-mod.ps1 MyMod -Path C:\work\mods
param([Parameter(Mandatory = $true)][string]$Name, [string]$Path = "")
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
  "gameVersion": "0.418"
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
Write-Host "Created $dir. Copy it to BepInEx\HalfGripMods in the game folder and start the game."
