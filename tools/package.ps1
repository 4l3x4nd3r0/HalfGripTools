# Makes the release zip and its checksum in release\:
#   HalfGripTools-v<version>.zip   BepInEx\plugins\HalfGripTools\ (the only compiled part)
# It unzips straight into the game folder.
#   .\package.ps1 -Version 1.0.0
param([Parameter(Mandatory = $true)][string]$Version, [string]$GameDir = "F:\SteamLibrary\steamapps\common\Half Grip Demo")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot\build.ps1" -GameDir $GameDir
if ($LASTEXITCODE -ne 0) { throw "build failed" }

# The zip format wants "/" in entry names; Compress-Archive and ZipFile.CreateFromDirectory in Windows PowerShell 5.1 write "\".
function New-Zip($dir, $zipPath) {
	Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
	$base = (Resolve-Path $dir).Path.TrimEnd("\") + "\"
	$file = [IO.File]::Create($zipPath)
	$zip = New-Object IO.Compression.ZipArchive($file, [IO.Compression.ZipArchiveMode]::Create)
	try {
		foreach ($item in Get-ChildItem $dir -Recurse -File) {
			$name = $item.FullName.Substring($base.Length).Replace("\", "/")
			[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $item.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
		}
	} finally {
		$zip.Dispose()
		$file.Dispose()
	}
}

$out = "$root\release"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
$stage = Join-Path $env:TEMP ("halfgrip-stage-" + [guid]::NewGuid().ToString("N"))
try {
	New-Item -ItemType Directory -Force "$stage\BepInEx\plugins" | Out-Null
	Copy-Item "$root\dist\BepInEx\plugins\HalfGripTools" "$stage\BepInEx\plugins\HalfGripTools" -Recurse
	Copy-Item "$root\LICENSE" "$stage\BepInEx\plugins\HalfGripTools\LICENSE.txt"
	New-Zip $stage "$out\HalfGripTools-v$Version.zip"
} finally {
	Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem "$out\*.zip" | ForEach-Object {
	"$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
} | Set-Content "$out\SHA256SUMS.txt" -Encoding ascii
Get-Content "$out\SHA256SUMS.txt"
