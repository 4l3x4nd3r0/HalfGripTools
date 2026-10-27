# HalfGripTools

A mod loader for Half Grip Demo (Steam, Windows). Made by 4l3x4nd3r.

Mods are plain text: C# files and JSON files in a folder. You can read what a mod does before it runs. The loader compiles them when the game starts, lists them in a mod manager in the main menu, and turns them all off when the game's speedrun mode is on.

The loader is the only DLL you install. Mods are source.

Mods made by 4l3x4nd3r (guns, a race mode, a car body, charms) are in a separate repo: [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods). This repo is only the loader.

## Terms

- **BepInEx**: the standard mod loader for Unity games. It runs plugins when the game starts. HalfGripTools is one such plugin.
- **Roslyn**: Microsoft's C# compiler, as a library. HalfGripTools uses it to compile mod source on your PC.
- **Harmony**: a library that changes a game method while the game runs ("patching"). Mods use it to hook into the game.
- **Game folder**: the folder with `HalfGrip.exe`. In Steam: right click the game, Manage, Browse local files.

## Install

You need Windows, the game on Steam and 5 minutes.

### 1. BepInEx (once)

Get BepInEx 5.4.23.5, x64: <https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5>. Take `BepInEx_win_x64_5.4.23.5.zip`.

Copy everything inside the zip into the game folder:

```
Half Grip Demo\
  HalfGrip.exe
  winhttp.dll          <- must sit next to HalfGrip.exe
  doorstop_config.ini
  .doorstop_version
  BepInEx\
```

Start the game once and quit. If `BepInEx\LogOutput.log` exists, it works.

### 2. HalfGripTools (once)

Download `HalfGripTools-v1.0.3.zip` from the [releases page](../../releases/latest). Unzip it into the game folder, merge when asked about `BepInEx`. You get:

```
BepInEx\plugins\HalfGripTools\
  HalfGripTools.dll                 <- ours
  Microsoft.CodeAnalysis*.dll       <- Roslyn
  System.*.dll                      <- 8 files Roslyn needs, also Microsoft's
```

Optional check of the zip, in PowerShell:

```powershell
Get-FileHash HalfGripTools-v1.0.3.zip -Algorithm SHA256
```

Compare with `SHA256SUMS.txt` on the release page. More checks are under [Trust](#trust).

### 3. Mods

Mods live in `BepInEx\HalfGripMods\`, one folder each:

```
BepInEx\HalfGripMods\
  HelloMod\
    mod.json
    src\*.cs
    sprites\*.png
```

To try it, copy [`examples/HelloMod`](examples/HelloMod) there. For real mods, see [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods).

### 4. Check

Start the game, quit, open `BepInEx\LogOutput.log`:

```
[Info   :HalfGripTools] Found 1 mod(s) in ...\BepInEx\HalfGripMods. Game version 0.418
[Info   :HalfGripTools] Hello Mod: compiled 1 files in 847 ms
[Info   :Hello Mod] hello
[Info   :HalfGripTools] Loaded Hello Mod 1.0.0
[Info   :HalfGripTools] Loaded HalfGripTools 1.0.3 by 4l3x4nd3r with 1 mod(s)
```

The first start compiles every mod and takes a few seconds. The result is cached in `BepInEx\cache\HalfGripMods`, so later starts are fast. The cache is rebuilt when the mod, the game or HalfGripTools changes.

If something is wrong, the log says which mod and why:

| Log line | Meaning |
|---|---|
| `X does not compile:` then `file(line,col): error ...` | Broken source, or the game changed. That mod is skipped, the rest load. |
| `Patch Y failed and is off` | One hook no longer fits the game. The rest of that mod works. |
| `X: Made for game 0.417, this is 0.418...` | Version warning, see [Game version](#game-version). |
| `X is switched off in the mod manager` | You turned it off. |
| `Speedrun mode is on: no mods are loaded` | See [Speedrun mode](#speedrun-mode). |

## Mod manager

In the main menu there is a puzzle piece button, one slot above the language flag. It opens the mod manager. Esc or a click outside closes it.

Each installed mod shows its version, author and state: `RUNNING`, `OFF`, `ERROR: ...` (did not compile, see the log) or `NEEDS <mod>`. The ON/OFF button switches it.

A playlist is a named set of mods.

- ALL MODS: everything on.
- SAVE AS NEW: saves what is on now under a name.
- Click a playlist to switch to it. `Guns *` means you changed something since you picked it.
- UPDATE: overwrite the selected playlist with what is on now.
- DELETE: remove the selected playlist.

A mod that is not in a playlist is off when you pick that playlist. A mod you install later stays off until you add it.

Changes are saved at once and apply the next time the game starts. A running game cannot take new items safely. Until you restart, the list shows `ON AT RESTART` or `OFF AT RESTART`. QUIT GAME is there for that.

Your choices are in `BepInEx\config\HalfGripTools.mods.json`:

```json
{
  "disabled": ["bombrace"],
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
"gameVersion": "0.418"    only 0.418
"gameVersion": "0.x"      any 0.something: 0.1, 0.418, 0.999
```

`x` stands for a whole part of the version. `0.4x` does not work.

If the game version does not match, the mod still loads. The mod manager puts a warning sign in front of its name with the line "Made for game 0.417, this is 0.418. It might not work correctly." The log has the same line. A mod without `gameVersion` never warns.

## Speedrun mode

The game has a speedrun mode (Settings: Race, Items, Achievements). A speedrun is seeded: the game builds the run from a seed and the list of unlocked items is part of it. Extra items would change it. So mods stay out.

- On at game start: no mod is compiled or loaded, no item is added, no button in the menu.
- Turned on while the game runs: every mod is switched off right away. Items leave the catalog, patches are removed, mod objects leave the scene, the button disappears. A message asks you to restart before a clean speedrun.
- Turned off: mods come back at the next start.
- If HalfGripTools cannot read the setting, it loads nothing.

Not undone by a switch-off: anything a mod wrote to your save, and items already inside a saved run.

## Settings, update, remove

- Settings: each code mod writes `BepInEx\config\halfgrip.<mod id>.cfg` on first run, for example `halfgrip.hellomod.cfg`. Every entry has a comment. Edit with the game closed.
- Update a mod or HalfGripTools: replace the folder. A changed mod recompiles at the next start.
- Remove a mod: delete its folder, or switch it off in the manager.
- Remove all mods: delete `BepInEx\plugins\HalfGripTools`. To remove BepInEx too, delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `BepInEx`.
- Saves: items are saved by id. Without their mod, a saved run loses those items. Nothing else in the save changes.
- The game marks runs with a `cheats` flag. For a vanilla run, switch the mods off and cheats off.

## Trust

You run code from the internet. This is what runs and how to check it.

- Compiled code of ours: `HalfGripTools.dll`, about 60 KB, source in [`HalfGripTools/`](HalfGripTools).
- Next to it, 10 DLLs published and signed by Microsoft (Roslyn and its `System.*` dependencies).
- Mods are real C# and run with the game's permissions. Read a mod before you install it. HalfGripTools does not sandbox mods.
- The compile cache is made on your PC from the source you can see. It is never downloaded.

Check the signatures. Everything except `HalfGripTools.dll` must say `Valid`:

```powershell
Get-ChildItem "BepInEx\plugins\HalfGripTools\*.dll" | Get-AuthenticodeSignature | Select-Object Path, Status
```

Check the loader's code for network, process and file access:

```powershell
git grep -nE "HttpClient|WebRequest|WebClient|Net\.Sockets|Process\.Start|File\.(Write|Delete|Create|Copy|Move|Append)|SaveData\.Set|PlayerPrefs|DllImport|Application\.Quit" -- HalfGripTools
```

Expected hits:

- `HalfGripTools/Compiler.cs`: writes and deletes DLLs in `BepInEx\cache\HalfGripMods`.
- `HalfGripTools/ModStore.cs`: writes `HalfGripTools.mods.json`.
- `HalfGripTools/ModManagerUI.cs`: the QUIT GAME button.

Run the same search over any mod you install. Mod authors should say in their README if a mod writes your save.

`HalfGripTools.dll` is built deterministically: the same commit and the same game version give the same bytes. The commit is in the DLL's version (`1.0.3+<commit>`, see the file's Details tab). Check out the release's tag, build it yourself and compare hashes with the release. A hosted build service cannot do this for you, because the build needs the game's own DLL, which is not in the repo.

## Build

You need the game with BepInEx installed and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/4l3x4nd3r0/HalfGripTools.git
cd HalfGripTools
.\tools\build.ps1 -GameDir "C:\path\to\Half Grip Demo" -Install   # build, copy the loader into the game
.\tools\check-mods.ps1 -ModsPath .\examples -GameDir "C:\path\to\Half Grip Demo"   # compile mods, no game start
.\tools\package.ps1 -Version 1.0.3 -GameDir "C:\path\to\Half Grip Demo"            # release zip in release\
```

## Write a mod

See [docs/WRITING_MODS.md](docs/WRITING_MODS.md). Notes on the game's classes are in [docs/GAME_NOTES.md](docs/GAME_NOTES.md). Working examples: [`examples/`](examples) here, and the mods in [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods).

## Problems

Open an issue and attach `BepInEx\LogOutput.log`.

## Credits and licence

HalfGripTools: 4l3x4nd3r, [MIT](LICENSE). Roslyn and the `System.*` DLLs are Microsoft's, unchanged. Half Grip belongs to its developers. This project is fan-made and not affiliated with them.
