# Makes the two release zips and their checksums in release\:
#   HalfGripTools-v<version>.zip              the BepInEx host: BepInEx\plugins\HalfGripTools\ (unzips into a game that has BepInEx;
#                                              the name and layout are the ones installers of 1.x already look for)
#   HalfGripToolsStandalone-v<version>.zip    the standalone host: winhttp.dll (Unity Doorstop 4.5.0), doorstop_config.ini and HalfGripTools\
#                                              (unzips into the game folder; no BepInEx needed, and it must not be put in a game that has BepInEx)
#   SHA256SUMS.txt
#   .\package.ps1 -Version 2.0.0
# Doorstop is downloaded from its GitHub release and checked against the hash below (it is the same winhttp.dll the game has with BepInEx). Harmony, MonoMod and Cecil
# for the standalone zip come from the BepInEx 5.4.23.5 release (also checked against its hash): the builds of them that run on this game's Mono. Nothing in the zips
# depends on what happens to be installed on the PC that builds them.
param([Parameter(Mandatory = $true)][string]$Version, [string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot\build.ps1" -GameDir $GameDir
if ($LASTEXITCODE -ne 0) { throw "build failed" }
if (-not (Test-Path "$root\dist\BepInEx\plugins\HalfGripTools\HalfGripTools.BepInEx.dll")) { throw "the BepInEx host was not built (BepInEx must be in $GameDir)" }

$doorstopUrl = "https://github.com/NeighTools/UnityDoorstop/releases/download/v4.5.0/doorstop_win_release_4.5.0.zip"
$doorstopSha = "7bb953e8d883c8bde76ced96f6d0e45660ad6e0151880d8ab5856bf4f532b147"
$licenseUrl = "https://raw.githubusercontent.com/NeighTools/UnityDoorstop/v4.5.0/LICENSE"
$bepinexUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip"
$bepinexSha = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4"
$harmonyFiles = "0Harmony.dll", "MonoMod.Utils.dll", "MonoMod.RuntimeDetour.dll", "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll"

# The zip format wants "/" in entry names; Compress-Archive and ZipFile.CreateFromDirectory in Windows PowerShell 5.1 write "\".
function New-Zip($dir, $zipPath) {
	Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
	$base = (Resolve-Path $dir).Path.TrimEnd("\") + "\"
	$file = [IO.File]::Create($zipPath)
	$zip = New-Object IO.Compression.ZipArchive($file, [IO.Compression.ZipArchiveMode]::Create)
	try {
		foreach ($item in Get-ChildItem $dir -Recurse -File -Force) {
			$name = $item.FullName.Substring($base.Length).Replace("\", "/")
			[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $item.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
		}
	} finally {
		$zip.Dispose()
		$file.Dispose()
	}
}

function Get-Pinned($url, $sha, $name) {
	$cache = Join-Path $env:TEMP ("halfgrip-pinned-" + $name + ".zip")
	if (-not (Test-Path $cache)) { Invoke-WebRequest -Uri $url -OutFile $cache -UseBasicParsing }
	$hash = (Get-FileHash $cache -Algorithm SHA256).Hash.ToLower()
	if ($hash -ne $sha) { Remove-Item $cache -Force; throw "$name does not match its expected hash ($hash)" }
	$cache
}

$out = "$root\release"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
$stage = Join-Path $env:TEMP ("halfgrip-stage-" + [guid]::NewGuid().ToString("N"))
try {
	# 1. BepInEx host
	$a = "$stage\bepinex"
	New-Item -ItemType Directory -Force "$a\BepInEx\plugins" | Out-Null
	Copy-Item "$root\dist\BepInEx\plugins\HalfGripTools" "$a\BepInEx\plugins\HalfGripTools" -Recurse
	Copy-Item "$root\LICENSE" "$a\BepInEx\plugins\HalfGripTools\LICENSE.txt"
	Copy-Item "$root\THIRD_PARTY.txt" "$a\BepInEx\plugins\HalfGripTools\THIRD_PARTY.txt"
	New-Zip $a "$out\HalfGripTools-v$Version.zip"

	# 2. standalone host
	$b = "$stage\standalone"
	New-Item -ItemType Directory -Force $b | Out-Null
	Copy-Item "$root\dist\standalone\HalfGripTools" "$b\HalfGripTools" -Recurse
	Copy-Item "$root\HalfGripTools.Standalone\doorstop_config.ini" "$b\doorstop_config.ini"
	Copy-Item "$root\LICENSE" "$b\HalfGripTools\LICENSE.txt"
	Copy-Item "$root\THIRD_PARTY.txt" "$b\HalfGripTools\THIRD_PARTY.txt"
	New-Item -ItemType Directory -Force "$b\HalfGripTools\LICENSES" | Out-Null
	Invoke-WebRequest -Uri $licenseUrl -OutFile "$b\HalfGripTools\LICENSES\Doorstop-LGPL-2.1.txt" -UseBasicParsing
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$door = [IO.Compression.ZipFile]::OpenRead((Get-Pinned $doorstopUrl $doorstopSha "doorstop-4.5.0"))
	try {
		foreach ($name in "x64/winhttp.dll", "x64/.doorstop_version") {
			$entry = $door.GetEntry($name)
			if ($null -eq $entry) { throw "$name is not in the Doorstop zip" }
			[IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$b\" + (Split-Path $name -Leaf), $true)
		}
	} finally { $door.Dispose() }
	# Harmony, MonoMod and Cecil from the pinned BepInEx release (they replace whatever the build left in HalfGripTools\)
	$bep = [IO.Compression.ZipFile]::OpenRead((Get-Pinned $bepinexUrl $bepinexSha "bepinex-5.4.23.5"))
	try {
		foreach ($name in $harmonyFiles) {
			$entry = $bep.GetEntry("BepInEx/core/$name")
			if ($null -eq $entry) { throw "BepInEx/core/$name is not in the BepInEx zip" }
			[IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$b\HalfGripTools\$name", $true)
		}
	} finally { $bep.Dispose() }
	New-Zip $b "$out\HalfGripToolsStandalone-v$Version.zip"
} finally {
	Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem "$out\*.zip" | ForEach-Object {
	"$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
} | Set-Content "$out\SHA256SUMS.txt" -Encoding ascii
Get-Content "$out\SHA256SUMS.txt"