# HalfGripTools

A mod loader for Half Grip Demo (Steam, Windows). Made by 4l3x4nd3r.

Mods are plain text: C# files and JSON files in a folder. You can read what a mod does before it runs. The loader compiles them when the game starts, lists them in a mod manager in the main menu, and turns them all off when the game's speedrun mode is on.

HalfGripTools 2.0 needs nothing else installed: no BepInEx. Mods are source.

Mods made by 4l3x4nd3r (guns, a race mode, a car body, charms) are in a separate repo: [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods). This repo is only the loader.

## Terms

- **Roslyn**: Microsoft's C# compiler, as a library. HalfGripTools uses it to compile mod source on your PC.
- **Harmony**: a library that changes a game method while the game runs ("patching"). Mods use it to hook into the game.
- **Doorstop**: a small program (`winhttp.dll` next to the game) that starts code before a Unity game does. HalfGripTools uses it to start without BepInEx.
- **Host**: the way HalfGripTools is started. Standalone (Doorstop), BepInEx (a plugin, for players who already use other BepInEx plugins), or built into the game itself. The loader (`HalfGripTools.dll`) is the same in all of them. See [docs/HOSTING.md](docs/HOSTING.md).
- **Game folder**: the folder with `HalfGrip.exe`. In Steam: right click the game, Manage, Browse local files.
- **Data folder**: where settings, the compile cache, a mod's own files and the log go: `%USERPROFILE%\AppData\LocalLow\Half Games\HalfGrip\Mods`. The same for every host.

## Install

You need Windows, the game on Steam and 5 minutes. The easiest way is the [installer](https://github.com/4l3x4nd3r0/HalfGripMods/tree/main/installer), which does all of this and keeps it up to date. By hand:

### Without BepInEx (most players)

Download `HalfGripToolsStandalone-v2.0.0.zip` from the [releases page](../../releases/latest) and unzip it into the game folder. You get:

```
Half Grip Demo\
  HalfGrip.exe
  winhttp.dll            <- Unity Doorstop 4.5.0, the file BepInEx also uses
  doorstop_config.ini    <- points Doorstop at HalfGripTools
  .doorstop_version
  HalfGripTools\
    HalfGripTools.dll             <- ours: the loader
    HalfGripTools.Harmony.dll     <- ours: patches the game for the loader
    HalfGripTools.Standalone.dll  <- ours: the Doorstop entry
    0Harmony.dll, MonoMod.*, Mono.Cecil*   <- Harmony and what it needs (MIT)
    Microsoft.CodeAnalysis*.dll, System.*.dll   <- Roslyn and what it needs, Microsoft's
```

**Do not unzip it into a game that already has BepInEx and other BepInEx plugins:** both use `winhttp.dll` and `doorstop_config.ini`, and the other plugins would stop loading. Use the next section.

### Inside BepInEx (if you already use other BepInEx plugins)

1. Install BepInEx 5.4.23.5, x64, if it is not there: <https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5>. Copy everything inside `BepInEx_win_x64_5.4.23.5.zip` into the game folder. Start the game once and quit.
2. Download `HalfGripTools-v2.0.0.zip` and unzip it into the game folder, merge when asked about `BepInEx`. You get `BepInEx\plugins\HalfGripTools\`.

Optional check of a zip, in PowerShell:

```powershell
Get-FileHash HalfGripToolsStandalone-v2.0.0.zip -Algorithm SHA256
```

Compare with `SHA256SUMS.txt` on the release page. More checks are under [Trust](#trust).

### Mods

Mods live in a `Mods` folder next to `HalfGrip.exe`, one folder each (make it if it is not there):

```
Half Grip Demo\Mods\
  HelloMod\
    mod.json
    src\*.cs
    sprites\*.png
```

To try it, copy [`examples/HelloMod`](examples/HelloMod) there. For real mods, see [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods).

HalfGripTools 1.x kept mods in `BepInEx\HalfGripMods`. That folder is still read, so an old install keeps working.

Mods you subscribe to on **Steam Workshop** are read from Steam's own folder (`steamapps\workshop\content\<game id>`). They are never written to. Because Steam can change a Workshop mod's code at any time, a Workshop mod starts **off**: you switch it on in the mod manager, and it stays on only for the files it had then. If an update changes them, it is off again until you switch it on again. (Steam Workshop for Half Grip depends on the game's release; the loader already knows the folder.)

### Check

Start the game, quit, open the log. With the standalone host it is `HalfGripTools.log` in the data folder (the game's own `Player.log` has the same lines); with the BepInEx host it is `BepInEx\LogOutput.log`:

```
[Info   :HalfGripTools] HalfGripTools 2.0.0 by 4l3x4nd3r, Standalone host, patching: HarmonyX 2.9.0. Data folder ...\LocalLow\Half Games\HalfGrip\Mods
[Info   :HalfGripTools] Found 1 mod(s) in ...\Half Grip Demo\Mods. Game version 0.419
[Info   :HalfGripTools] Hello Mod: compiled 1 files in 847 ms
[Info   :Hello Mod] hello
[Info   :HalfGripTools] Loaded Hello Mod 1.0.0
[Info   :HalfGripTools] Loaded HalfGripTools 2.0.0 with 1 mod(s)
```

The first start compiles every mod and takes a few seconds. The result is cached in the data folder (`cache`), so later starts are fast. The cache is rebuilt when the mod, the game or HalfGripTools changes.

If something is wrong, the log says which mod and why:

| Log line | Meaning |
|---|---|
| `X does not compile:` then `file(line,col): error ...` | Broken source, or the game changed. That mod is skipped, the rest load. |
| `X uses BepInEx types and was made for HalfGripTools 1.x` | A 1.x mod. It needs a small change: see [Porting to 2.0](docs/WRITING_MODS.md#porting-to-20). |
| `Patch Y failed and is off` | One hook no longer fits the game. The rest of that mod works. |
| `X: Made for game 0.417, this is 0.418...` | Version warning, see [Game version](#game-version). |
| `X is switched off in the mod manager` | You turned it off. |
| `X comes from Steam's Workshop and stays off until you switch it on` | See above. Switch it on in the mod manager. |
| `...mod.json: same files as <folder> (id 'x'). Ignored.` | The same mod is in two folders. The first one found is loaded, the copy is ignored. |
| `...id 'x' is already used by <folder> with different files. Loaded as 'x-1a2b3c'` | Two different mods picked the same id. The first one found keeps it. The other gets the id plus the start of its own hash, so its settings stay with it between starts. |
| `Load order: a, b, c` | The order the mods load in: `Mods`, the old `BepInEx\HalfGripMods`, your extra folders as written, then Steam's Workshop, each by folder name. A mod that `requires` another always comes after it. |
| `...mod.json: 'trust' is 'y', not one of ...` | An unknown word in `trust` or `kind`. The default is used and the mod still loads. |
| `Speedrun mode is on: no mods are loaded` | See [Speedrun mode](#speedrun-mode). |

## Mod manager

In the main menu there is a puzzle piece button, one slot above the language flag. It opens the mod manager. Esc or a click outside closes it.

Each installed mod shows its version, author and state: `RUNNING`, `OFF`, `ERROR: ...` (did not compile; the file, line and message are under its name) or `NEEDS <mod>`. The ON/OFF button switches it. A mod from Steam's Workshop is marked `[Steam Workshop]`.

A playlist is a named set of mods.

- ALL MODS: everything on (a Workshop mod is not switched on by this).
- SAVE AS NEW: saves what is on now under a name.
- Click a playlist to switch to it. `Guns *` means you changed something since you picked it.
- UPDATE: overwrite the selected playlist with what is on now.
- DELETE: remove the selected playlist.

A mod that is not in a playlist is off when you pick that playlist. A mod you install later stays off until you add it.

Changes are saved at once and apply the next time the game starts. A running game cannot take new items safely. Until you restart, the list shows `ON AT RESTART` or `OFF AT RESTART`. QUIT GAME is there for that.

Your choices are in `mods.json` in the data folder:

```json
{
  "disabled": ["bombrace"],
  "workshop": { "somemod": "<hash of the files it was switched on for>" },
  "active": "Guns",
  "playlists": [
    { "name": "Guns", "mods": ["m1911", "rpg", "sniperrifle"] }
  ]
}
```

Mouse only. With speedrun mode on, the button is not there.

## Game version

A mod says which game version it was made for in `mod.json`:

```json
"gameVersion": "0.419"    only 0.419
"gameVersion": "0.x"      any 0.something: 0.1, 0.419, 0.999
```

`x` stands for a whole part of the version. `0.4x` does not work.

If the game version does not match, the mod still loads. The mod manager puts a warning sign in front of its name with the line "Made for game 0.417, this is 0.419. It might not work correctly." The log has the same line. A mod without `gameVersion` never warns.

## Speedrun mode

The game has a speedrun mode (Settings: Race, Items, Achievements). A speedrun is seeded: the game builds the run from a seed and the list of unlocked items is part of it. Extra items would change it. So mods stay out.

- On at game start: no mod is compiled or loaded, no item is added, no button in the menu.
- Turned on while the game runs: every mod is switched off right away. Items leave the catalog, patches are removed, mod objects leave the scene, the button disappears. A message asks you to restart before a clean speedrun.
- Turned off: mods come back at the next start.
- If HalfGripTools cannot read the setting, it loads nothing.

Not undone by a switch-off: anything a mod wrote to your save, and items already inside a saved run.

## Settings, update, remove

- Settings: each code mod writes `config\<mod id>.cfg` in the data folder on first run, for example `hellomod.cfg`. Every entry has a comment. Edit with the game closed. (1.x wrote `BepInEx\config\halfgrip.<id>.cfg`; those files are copied over once, the old ones are left where they are.)
- HalfGripTools has one setting of its own, in `config\HalfGripTools.cfg`: `ExtraModFolders`, more folders to read mods from, separated by `;`. If the same mod id is in two folders, the first one is used and the other is reported in the log.
- To keep the data folder somewhere else (a test copy of the game, a game on a stick), set the environment variable `HALFGRIPTOOLS_DATA` to a folder before starting the game.
- When a mod does not compile, the mod manager shows the file, line and message under its name, and the main menu shows a short note naming the mods that did not load.
- Update a mod or HalfGripTools: replace the folder. A changed mod recompiles at the next start.
- Remove a mod: delete its folder, or switch it off in the manager.
- Remove HalfGripTools (standalone): delete `HalfGripTools`, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` from the game folder, and the data folder if you want the settings gone. With BepInEx: delete `BepInEx\plugins\HalfGripTools`.
- Saves: items are saved by id. Without their mod, a saved run loses those items. Nothing else in the save changes.
- The game marks runs with a `cheats` flag. For a vanilla run, switch the mods off and cheats off.

## Trust

You run code from the internet. This is what runs and how to check it.

- Compiled code of ours: `HalfGripTools.dll` (the loader, about 100 KB), `HalfGripTools.Harmony.dll`, and the host (`HalfGripTools.Standalone.dll` or `HalfGripTools.BepInEx.dll`). Source in this repo, one folder each.
- Next to them, DLLs that are not ours: Roslyn and its `System.*` helpers (published and signed by Microsoft), and with the standalone host HarmonyX, MonoMod and Cecil (MIT, the builds BepInEx 5.4.23.5 ships) and Doorstop (`winhttp.dll`, LGPL, unmodified). All are listed in [`THIRD_PARTY.txt`](THIRD_PARTY.txt), which is in both zips. The release script checks the Doorstop and BepInEx downloads against fixed hashes.
- Mods are real C# and run with the game's permissions. Read a mod before you install it. HalfGripTools does not sandbox mods (a `restricted` mode is planned, see the roadmap).
- The compile cache is made on your PC from the source you can see. It is never downloaded.

Check the signatures. Everything except our own DLLs and the Harmony, MonoMod, Cecil and Doorstop files must say `Valid`:

```powershell
Get-ChildItem "HalfGripTools\*.dll" | Get-AuthenticodeSignature | Select-Object Path, Status
```

Check the loader's code for network, process and file access:

```powershell
git grep -nE "HttpClient|WebRequest|WebClient|Net\.Sockets|Process\.Start|File\.(Write|Delete|Create|Copy|Move|Append)|SaveData\.Set|PlayerPrefs|DllImport|Application\.Quit" -- HalfGripTools HalfGripTools.Harmony HalfGripTools.Standalone HalfGripTools.BepInEx
```

Expected hits:

- `HalfGripTools/Compiler.cs`: writes and deletes DLLs in the data folder's `cache`.
- `HalfGripTools/ModStore.cs`: writes `mods.json`; `ModConfig.cs`: writes a mod's `.cfg`; `ModPaths.cs`: copies the 1.x settings once.
- `HalfGripTools/ModManagerUI.cs`: the QUIT GAME button.
- `HalfGripTools.Standalone/FileLogHost.cs`: the log file in the data folder.

Run the same search over any mod you install. Mod authors should say in their README if a mod writes your save.

`HalfGripTools.dll` is built deterministically: the same commit and the same game version give the same bytes. The commit is in the DLL's version (`2.0.0+<commit>`, see the file's Details tab). Check out the release's tag, build it yourself and compare hashes with the release. A hosted build service cannot do this for you, because the build needs the game's own DLL, which is not in the repo.

## Build

You need the game and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). To build the BepInEx host, BepInEx must be in the game folder too (it is compiled against it); without it only the loader and the standalone host are built.

```powershell
git clone https://github.com/4l3x4nd3r0/HalfGripTools.git
cd HalfGripTools
.\tools\build.ps1 -GameDir "C:\path\to\Half Grip Demo"    # builds dist\ (add -Install to copy the BepInEx host into the game)
.\tools\check-mods.ps1 -ModsPath .\examples -GameDir "C:\path\to\Half Grip Demo"   # compile mods, no game start
.\tools\hook-census.ps1 -ModsPath .\examples -Out census.md -GameDir "C:\path\to\Half Grip Demo"   # which game members a set of mods uses, patches and reflects on
.\tools\package.ps1 -Version 2.0.0 -GameDir "C:\path\to\Half Grip Demo"            # both release zips in release\
```

## Write a mod

See [docs/WRITING_MODS.md](docs/WRITING_MODS.md). Notes on the game's classes are in [docs/GAME_NOTES.md](docs/GAME_NOTES.md). Putting HalfGripTools into the game itself, with no Harmony and no Doorstop, is in [docs/HOSTING.md](docs/HOSTING.md). Working examples: [`examples/`](examples) here, and the mods in [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods).

## Problems

Open an issue and attach the log (`HalfGripTools.log` in the data folder, or `BepInEx\LogOutput.log`).

## Credits and licence

HalfGripTools: 4l3x4nd3r, [MIT](LICENSE). Roslyn and the `System.*` DLLs are Microsoft's, unchanged. Harmony (HarmonyX), MonoMod and Cecil are MIT; Unity Doorstop is LGPL-2.1, unmodified (see [`THIRD_PARTY.txt`](THIRD_PARTY.txt)). Half Grip belongs to its developers. This project is fan-made and not affiliated with them.
