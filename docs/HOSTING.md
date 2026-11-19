# Hosting HalfGripTools

HalfGripTools is one loader (`HalfGripTools.dll`) and a small piece of code, the **host**, that starts it. The loader itself needs nothing from outside the game: it does not reference BepInEx, Harmony or Doorstop. This page says what the loader needs from the game, how the three hosts that exist provide it, and what the game has to do to run the loader with no host at all (no Harmony, no Doorstop).

```
HalfGripTools.dll             the loader: finds mods, compiles them, runs them, mod manager, items, keys, settings, net
   ^  calls                      knows nothing of BepInEx, Harmony or Doorstop
   |
   +-- GameHooks                 six plain static methods: what the loader needs the game to tell it
   +-- IModHost                  how a host gives it a log (and says whether it can patch)
   +-- IPatchProvider            only for hosts that patch the game from outside

HalfGripTools.Harmony.dll     IPatchProvider on HarmonyX: patches the game and calls GameHooks
HalfGripTools.Standalone.dll  host 1: Doorstop starts it, no BepInEx
HalfGripTools.BepInEx.dll     host 2: a BepInEx plugin (for games that already use other BepInEx plugins)
HalfGripTools.Unity (source)  host 3: built into the game's Unity project; no Harmony, no Doorstop
```

## What the loader needs from the game: `GameHooks`

Each is one call. From outside, `HalfGripTools.Harmony` patches the place with Harmony and makes the call; built into the game, the game makes it directly.

| Call | Where it goes in the game | What it does |
|---|---|---|
| `GameHooks.SpeedrunModeChanged()` | end of the `Speedrun.Mode` setter | Switches every mod off when speedrun mode turns on (and says to restart when it turns off). |
| `GameHooks.SpeedrunRunBeginning()` | start of `Speedrun.BeginRun`, before it fingerprints the catalog | Same, for a run that starts with mods still on. |
| `json = GameHooks.RunStatsFit(json)` | end of `RunStats.Fit`, on its result | Adds the list of running mods (`id@version:hash8`) to the anonymous run stats, so runs with mods can be counted. `"mods":[]` in a speedrun. |
| `GameHooks.CatalogAboutToBeRead()` | start of `UpgradeMapper.Awake` and of `LoadoutProgress.ApplyToNewRun` | Puts the mods' items in the item catalog before the game builds its lookup from it. Safe to call again. |
| `GameHooks.ControlRows()` | in `Controls.Holder` and `ControlsSettingsUI.Fill`, wherever `Controls.All` is read | The game's controls plus the keys mods added, so they show up in Settings > Controls with the game's own rebinding. Then set `GameHooks.ControlsRedirected = true`. |

All of them do nothing while no mods are running, so they can sit in the game's code for good.

The loader also reads these game members directly (they must stay reachable): `Speedrun.On`, `ItemCatalog.Instance.items`, `UpgradeMapper.Instance` (`allUpgrades`, the private `lookup`), `Controls.All`, `Controls.Action`, `GameInput`, `SaveData` (only through `Speedrun`), and the main menu's `LanguageButton` row (to place the mod manager button). The census (`tools\hook-census.ps1`, `HOOK_CENSUS.md`) lists what the mods use.

## The three hosts

A host calls `ModRuntime.Start(IModHost)` once, after the engine is up and before the game's own scenes wake. `IModHost` has a name, a `Write(source, level, text)` for the log and `Patches` (an `IPatchProvider`, or null).

### Standalone (Doorstop, no BepInEx)

`winhttp.dll` (Unity Doorstop 4.5.0, the file BepInEx also uses) sits next to `HalfGrip.exe`. `doorstop_config.ini` names `HalfGripTools\HalfGripTools.Standalone.dll` as `target_assembly`. Doorstop calls `Doorstop.Entrypoint.Start()` about 40 ms after the process starts, before Unity has loaded any module, so `Start` only does two things: it installs an `AssemblyResolve` handler that looks in its own folder (for HalfGripTools, Roslyn, Harmony), and it waits for Unity's core module. When `UnityEngine.CoreModule` loads, it patches `SceneManager.Internal_ActiveSceneChanged` with Harmony. That callback fires for the very first scene, before the game's own `Awake` methods (`SceneLoader.Awake`, `SaveData.Load` come after it), and its prefix calls `ModRuntime.Start`. Found by trying: a patch on `Application`'s static constructor never fires, and the game's own classes are not in the first scenes (the first real scene is the main menu).

Harmony, MonoMod and Cecil in that folder are the builds BepInEx 5.4.23.5 ships. HarmonyX's own NuGet build (netstandard2.0) fails to load on this game's Mono with a `TypeLoadException`.

### BepInEx

`HalfGripTools.BepInEx.dll` is a BepInEx plugin of about forty lines. Its `Awake` calls `ModRuntime.Start(this)` and `Write` forwards to BepInEx's log. BepInEx's own Harmony does the patching. Use it when other BepInEx plugins are in the game: Doorstop can only start one of them, and BepInEx is the one that starts the others.

### Built into the game (no host to install)

Copy `HalfGripTools.Unity\UnityHost.cs` into the Unity project, with `HalfGripTools.dll` and the Roslyn DLLs. Its `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` method calls `ModRuntime.Start(new UnityHost())`. It has no patch provider, so the game must make the five calls in the table above. There is no Harmony and no Doorstop anywhere.

What changes for mods in that host: data-only mods and mods written against the API (items, keys, sprites, settings, `ctx.Net`, the mod manager) run as before. A mod with `[HarmonyPatch]` classes or `using HarmonyLib` cannot run, and the log and the mod manager say so ("This host cannot patch the game, and this mod uses Harmony"). That is the point of the next API steps (game events, a gun base class): so fewer mods need to patch.

`UnityHost.cs` is built here only to prove it compiles against the loader alone (`dist\unity\`). It cannot be started from outside the game, because Unity only runs `RuntimeInitializeOnLoadMethod` for assemblies it compiled itself.

## Where files go

Mods are read from, in this order: `<game>\Mods`, the 1.x folder `<game>\BepInEx\HalfGripMods` (if it exists), the folders in the `ExtraModFolders` setting, and Steam's Workshop folder (`steamapps\workshop\content\<app id>`, found through the `appmanifest_<id>.acf` next to the game's `steamapps\common`). The first copy of an id wins.

Nothing the loader or a mod writes goes into any of those folders (Steam replaces a Workshop folder on every update). Everything it writes is in the **data folder**, `<Application.persistentDataPath>\Mods` (`%USERPROFILE%\AppData\LocalLow\Half Games\HalfGrip\Mods`), the same for every host:

```
config\HalfGripTools.cfg   the loader's own settings
config\<mod id>.cfg        a mod's settings
storage\<mod id>\          a mod's own files (ctx.DataFolder)
cache\                     compiled mods
mods.json                  the mod manager's choices (what is off, playlists, Workshop consent)
HalfGripTools.log          the log (standalone and Unity hosts; BepInEx has its own)
```

Environment variables, for tests:

- `HALFGRIPTOOLS_DATA=<folder>` moves the data folder.
- `HALFGRIPTOOLS_PATCHES=none` starts the standalone host without Harmony, to see what the loader does in a host that cannot patch.

## Tried, in game copies

The standalone host (a game copy with no BepInEx at all, only the zip's files), the BepInEx host and the loader with no patch provider all start and load mods; Multiplayer's two-process tests (a race, a refused mismatch, the mod network channel) pass on the standalone and on the BepInEx host. The Unity host was only compiled, because it needs the game's own Unity project.
