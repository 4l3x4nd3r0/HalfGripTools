# Writing a mod

A mod is a folder in the `Mods` folder next to `HalfGrip.exe` (HalfGripTools 1.x used `BepInEx\HalfGripMods`, which is still read). At game start HalfGripTools compiles its C# (if any), reads its JSON items and runs it. You do not build anything and you ship no DLL. What you publish is what people read.

```
MyMod\
  mod.json          required
  items\*.json      optional, items without code
  src\*.cs          optional, code
  sprites\*.png     optional, pictures
```

`.\tools\new-mod.ps1 MyMod` creates the folder from a template, in `examples\`. Examples to copy from:

- [`examples/HelloItems`](../examples/HelloItems): JSON only.
- [`examples/HelloMod`](../examples/HelloMod): smallest code mod.
- Real mods in the [HalfGripMods repo](https://github.com/4l3x4nd3r0/HalfGripMods): [SpeedCharms](https://github.com/4l3x4nd3r0/HalfGripMods/tree/main/mods/SpeedCharms) (JSON, with art), [InfinitePlayground](https://github.com/4l3x4nd3r0/HalfGripMods/tree/main/mods/InfinitePlayground) (small code mod), [DaciaBody](https://github.com/4l3x4nd3r0/HalfGripMods/tree/main/mods/DaciaBody) (item with art and a 3D model). The other mods named below (BombRace, RPG, SniperRifle, M1911) are there too.

## mod.json

```json
{
  "id": "mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "author": "you",
  "description": "One sentence.",
  "icon": "icon.png",
  "requires": [],
  "gameVersion": "0.418",
  "apiVersion": "1.1.0",
  "kind": "item",
  "trust": "full",
  "tags": ["gun"]
}
```

| Field | Meaning |
|---|---|
| `id` | Required. Lower case letters, digits, `-`, `_`. Must be unique. Names the config file (`mymod.cfg`) and the compile cache. |
| `name`, `version`, `author`, `description` | Shown in the log and in the mod manager. Keep `name` under about 28 characters, the list cuts it. |
| `requires` | Ids of mods that must load first. If one is missing or off, your mod is skipped. |
| `gameVersion` | Game version you made it for. `"0.418"` is exactly 0.418. `"0.x"` is any 0.something. `x` must be a whole part, `0.4x` does not work. |
| `icon` | Optional. A picture of the mod: a `.png` inside the mod's folder, for example `"icon.png"`. The mod manager shows it next to the name (1.0.5 and newer) and installers show it on the mod's card. Use a square image, 128x128 is plenty; pixel art stays sharp. It is only a picture: the sprites of your items in `sprites\` are separate. A missing file, or a path that leaves the folder, is ignored. |
| `tools` | Optional. The oldest HalfGripTools version the mod runs on, for example `"1.0.2"`. A loader older than that which knows this field skips the mod and logs "needs HalfGripTools 1.0.2 or newer". (1.0.1 and older do not know the field: a mod that uses `ctx.Keys` should check for it itself, see below.) |

| `apiVersion` | The same as `tools`, under the name the official API will use (1.1.0 and newer). Give either one. If a mod gives both, the newer counts. |
| `kind` | Optional. `item`, `map`, `mode` or `library`. For lists and, later, Steam Workshop. An unknown word is logged and ignored. |
| `trust` | `full` (the default) or `restricted`. **Only recorded for now: a `restricted` mod still runs with full access.** It is there so the label is in the mod from the start. When the restricted compile mode exists, `restricted` will mean: the public API only (no `Assembly-CSharp`, Harmony, BepInEx), no files or network except through `ctx.Storage`, checked against an allow-list before the mod runs. An unknown word is logged and counted as `full`. |
| `tags` | Optional list of words for finding the mod (`"gun"`, `"map"`). Lower case; spaces become `-`. |

The log shows `id version kind trust api tags` for every mod at start.

To read the list of running mods from code (1.1.0 and newer) use `LoadedMods.All`: for each mod its `Id`, `Version`, `Hash` (SHA-256 of the mod's folder, the same on every PC with the same files), `Folder`, `Kind`, `Trust`, and `Line` (`id@version:hash8`, what the run stats and rooms use). The hash ignores a `lang\` folder and editor files (`*.csproj`, `bin\`, `obj\`, `.gitignore`). Do not reach into the loader's internal classes for this.

A mod whose `gameVersion` does not match the running game still loads. The mod manager shows a warning sign next to it and the line "might not work correctly". Empty or missing `gameVersion` never warns.

## An item without code

`items\lucky_charm.json`:

```json
{
  "id": "mymod.lucky_charm",
  "name": "Lucky Charm",
  "flavour": "Found it in the glovebox",
  "tier": "Modified",
  "cost": 60,
  "icon": "lucky_charm_icon.png",
  "charm": "lucky_charm.png",
  "statBoosts": [
    { "stat": "MoneyPerKill", "amount": 0.25 },
    { "stat": "MaxHp", "amount": 10, "addsToBase": true }
  ]
}
```

| Field | Meaning |
|---|---|
| `id` | Required. Start it with your mod id (`mymod.lucky_charm`) so it cannot clash with the game or other mods. Saves store items by this id. Never change it after release. |
| `name` | Required. Card title. |
| `flavour` | Grey line under the title. |
| `tier` | `Stock`, `Modified`, `RaceSpec`, `Legendary`, `BlackMarket`. Controls how often shops and rewards offer it. Default `Stock`. |
| `cost` | Shop price. Default 20. |
| `icon` | PNG in `sprites\`, the shop card picture. Drawn like the game sprite it replaces (same pixel density and pivot), so use the same size. The log warns on a size mismatch. |
| `charm` | PNG in `sprites\`, the charm that hangs in the car. Passives only. |
| `copyOf` | Id of a game item to copy. Default: a plain passive. |
| `statBoosts` | List of `{ "stat", "amount" }`. The game applies them and writes the card's stat lines. |

`amount` is a percentage by default: `0.06` is +6% of whatever part is fitted. Flags on a boost row:

- `"addsToBase": true`: whole units on the stat's base (+10 max HP).
- `"multiplicative": true`: multiplies. `1.5` is half as much again.
- `"hideLine": true`: applies the row but keeps it off the card.

Stats: `NitroPower`, `NitroCharge`, `NitroDuration`, `SlowMoStrength`, `SlowMoDuration`, `EnginePower`, `TopSpeed`, `BrakePower`, `TireGrip`, `FrontTireGrip`, `RearTireGrip`, `DownForce`, `RamDamage`, `MaxHp`, `HpRegen`, `ExtraMagazines`, `AmmoPickup`, `SlowMoCharges`, `HealthPickup`, `WeaponDamage`, `BulletsPerShot`, `MoneyPerKill`, `ObjectiveMoney`, `RaceMoney`, `Interest`, `UncappedAmmo`, `SteeringViscosity`, `SteeringFeedbackStrength`, `ComOffset`, `Mass`, `GuaranteedAmmo`, `GuaranteedMoney`, `GuaranteedRefills`, `MagazinesPerKill`, `HealPerKill`, `HpPerShot`, `Lifesteal`, `EffectStrength`, `EffectDuration`, `EffectCapacity`, `EffectRefill`.

A mistake in a file (bad JSON, unknown stat or tier, no id) is logged with the file name and position. Only that item is skipped.

## A mod with code

Put one class that derives from `HalfGripMod` in `src\`. Other `.cs` files can sit next to it. Language: C# 10.

```csharp
using HalfGripTools;
using HarmonyLib;

namespace MyMod
{
    public class Mod : HalfGripMod
    {
        public override void Load(ModContext ctx)
        {
            var greeting = ctx.Config.Bind("General", "Greeting", "hello", "Written once to the log.");
            ctx.Log.LogInfo(greeting.Value);
            ctx.PatchAll();
        }
    }

    // Every [HarmonyPatch] class in the mod is applied by ctx.PatchAll().
    [HarmonyPatch(typeof(Playground), "Awake")]
    internal static class PlaygroundHello
    {
        private static void Postfix() => UnityEngine.Debug.Log("playground!");
    }
}
```

Compile it without starting the game: `.\tools\check-mods.ps1 -ModsPath .\examples -Mod MyMod`. To run it, copy the folder into the `Mods` folder in the game folder, start the game and read the log (`HalfGripTools.log` in the data folder, which is `%USERPROFILE%\AppData\LocalLow\Half Games\HalfGrip\Mods`; with the BepInEx host `BepInEx\LogOutput.log`).

### ModContext

| Member | Meaning |
|---|---|
| `Log` | A `ModLog` named after your mod: `LogInfo`, `LogWarning`, `LogError`, `LogDebug` (2.0; before, BepInEx's `ManualLogSource`, which has the same method names). |
| `Config` | A `ModConfig`, saved to `config\<id>.cfg` in the data folder, in the format BepInEx used. `Config.Bind(section, key, default, description)` returns a `ModSetting<T>` (`.Value`, `.DefaultValue`, `SettingChanged`). Works for numbers, `bool`, `string` and enums. `Config.Save()` writes at once; new entries are written when `Load` returns. Section and key names cannot contain `=`, quotes or brackets. |
| `Folder` | Your mod's folder. Read from it, **never write to it**: if the mod comes from Steam's Workshop, Steam owns that folder and replaces it on an update. |
| `DataFolder` | A folder your mod may write to (`storage\<id>` in the data folder), made the first time you ask for it. Use it for anything the mod has to remember. |
| `HarmonyId` | The id to give a Harmony instance of your own (`new Harmony(ctx.HarmonyId + ".something")`), for patches `PatchAll` cannot make (a patch on another mod's method, found at run time). Call `UnpatchSelf` from `ctx.OnUnload`. |
| `PatchAll()` | Applies each `[HarmonyPatch]` class on its own. A hook that no longer fits the game is logged and skipped, the rest of the mod keeps working. Needs a host that can patch (the standalone and BepInEx hosts can; a host built into the game cannot, see [HOSTING.md](HOSTING.md)): there it logs an error and a mod that needs it cannot run. |
| `OnSceneLoaded(callback)` | Runs after every scene load. |
| `OnUnload(callback)` | Runs when mods switch off (speedrun mode). |
| `Sprites` | `LoadSprite(file, like, ppuOverride)` makes a sprite from `sprites\<file>` with the pivot and pixel density of the game sprite `like`. `LoadTexture(file)` returns a point-filtered texture. `Reskin(map, ppu)` swaps sprites by name in the scene. |
| `Items` | See below. |
| `Keys` | Adds keys to Settings > Controls (since 1.0.2). See below. |
| `Net` | Messages to the other players of a Multiplayer room, and game modes (since 1.0.6). See below. |
| `Maps` | Race maps built from code (2.0). See "Maps" below. |

Also public: `VoxelMesh.Build` and `VoxelMesh.MakeObject` (turn a sprite into the thin 3D mesh the game uses for a held gun), and `GunConversion.CopyFields` (copy serialized fields between components). The RPG and SniperRifle mods use both.

### Items from code

```csharp
ctx.Items.Add("mymod.thing", catalog =>
{
    PassiveUpgrade basis = ctx.Items.FirstOf<PassiveUpgrade>(catalog);
    PassiveUpgrade made = ctx.Items.Clone(basis, "mymod.thing");
    made.description = "Thing\n<color=#7e7e7e>Does things</color>";
    made.cost = 40;
    return made;     // return null (after logging why) to give up
});
```

- `Add(id, create)`: `create` runs once, as soon as the item catalog exists. The result goes into the catalog before the game builds its id lookup (it does that once, in `UpgradeMapper.Awake`; an item added later is not found when a save loads). An id already in the catalog is skipped with a warning.
- `OnInject(catalog => ...)`: runs at every injection point. It must be safe to run many times. Use it for side effects such as adding an item to a loadout.
- `Clone(basis, id)`: copy of an existing item. `FirstOf<T>(catalog, where)`: first catalog item of exactly that type. `Get(id)`: your item once made.

Every item here starts as a copy of a game item with new numbers. That gives you the game's card, stat lines and save handling for free. Item fields are in [GAME_NOTES.md](GAME_NOTES.md).

### Keys in Settings > Controls

```csharp
ModKey scope = ctx.Keys.Add(KeyCode.Z, "Scope in / out", Controls.Section.Looking);
...
if (scope != null && scope.Down) { ... }     // follows the player's rebind
string hint = "Press " + scope.Name;          // "Z", or whatever they bound
```

`Add(key, label, section = Controls.Section.Race, shown = null)` puts a row in the game's own Controls list, with its rebind button, its "Default" button and its swap-with-whatever-holds-that-key behaviour. The player's choice is saved by the game. It returns null (with a warning in the log) if the default key already belongs to the game or another mod. Avoid `Controls.Section.Cheats`: those rows only show with cheats on.

- `Down` and `Held` read the key through the game's input, so they are false while the game has input muted (pause menu, popups).
- `DownRaw` is true on the frame the key goes down even then, for a mod that mutes input itself.
- Item text goes through the game's `Controls.Keys`, so `{Z}` in a description prints the key the player bound to Z.
- Keys are taken out of the list when mods switch off.

`ctx.Keys` does not exist before 1.0.2, so a mod that must still compile on an older loader reads it by reflection (`ctx.GetType().GetProperty("Keys")`) and says what is wrong when it is missing. The SniperRifle and Multiplayer mods do this (`ModKeyShim.cs`).

### Talking to other players, and game modes (since 1.0.6)

`ctx.Net` carries bytes between your mod on one player's machine and your mod on the others', through the Multiplayer mod's room. It does not need Multiplayer to compile: without it `Net.Available` is false, `Net.InRoom` stays false and every send returns false, so a mod can use it without checking. Everybody in a room has the same mods (Multiplayer checks), so a message only reaches your own mod.

```csharp
public override void Load(ModContext ctx)
{
    ctx.Net.OnReceive((from, data) =>           // main thread
    {
        if (data.Length > 0 && data[0] == 1) { /* a gate was passed: data[1..] */ }
    });
    Net.PhaseChanged += phase => { if (phase == NetPhase.Racing) { /* the race started */ } };
    Net.PlayerLeft += player => ctx.Log.LogInfo(player.Name + " left");
}

// anywhere later
ctx.Net.Send(new byte[] { 1, 3 });                       // to every other player, reliable
ctx.Net.Send(position, Net.Everyone, reliable: false);   // for things sent often
ctx.Net.SendToHost(bytes);
```

| Member | Meaning |
|---|---|
| `ctx.Net.Send(data, to = Net.Everyone, reliable = true)` | Returns false when it was not sent: no room, or too big (`Net.MaxReliable` 3800 bytes, `Net.MaxUnreliable` 1100). Send big things in parts. |
| `ctx.Net.SendToHost(data, reliable = true)` | Does nothing on the host (the host runs its own code). |
| `ctx.Net.OnReceive((from, data) => ...)` | `from` is the sender's player id. Called on the main thread. |
| `ctx.Net.RegisterMode(new NetMode { ... })` | Adds a game mode to the MODE row of the race settings. See below. |
| `Net.Available`, `InRoom`, `IsHost`, `LocalId`, `HostId`, `Players`, `Phase`, `ModeId` | The room as it is now. `ModeId` is `"race"`, `"demolition"`, `"tournament"`, `"custom"` or the id of a registered mode. |
| `Net.PlayerJoined`, `PlayerLeft`, `PhaseChanged`, `ModesChanged` | Events. `PlayerJoined` also fires for each player already there when you join. |
| `Net.EndRace()` | The host's call: ends the race for everybody and shows the results. |

**Game modes.** Multiplayer has a CUSTOM mode of its own: up to 20 racers and up to 100 laps. A mod that wants other numbers, or its own grid, registers a mode:

```csharp
ctx.Net.RegisterMode(new NetMode
{
    Id = "mymod.hotlap",                 // unique; the same on every machine
    Name = "HOT LAP",                    // shown in the MODE row
    MaxPlayers = 30,                     // 2 to 64 (Multiplayer takes at most 64); the default is 20
    MaxLaps = 5,                         // 1 to 100; the default is 100
    SpawnPoint = i => i < 30 ? new Pose(MyGridPosition(i), MyGridRotation) : (Pose?)null   // optional
});
```

The host picks it like any other mode. Every mode is run as a race (laps, finish line, results); the mod adds its own rules over `ctx.Net`, and `Net.EndRace()` ends the race early. `SpawnPoint(i)` is asked on every machine for car number `i` (0 is the first on the grid) and must give the same answer everywhere; return null to use the track's own grid, which continues behind the start line, along the racing line, past its six spots. More than about 20 players is slow: every car is sent to every player.

If the host changes to a mode that takes fewer players than the room has, the players who joined last start the race as spectators.

## Maps

A map has no scene of its own (a mod cannot add one). It is built inside the Stadium scene when its race loads. HalfGripTools does the plumbing, so a map needs no patches:

* it makes the map's entry in the game's run config (so it is dealt in runs and a saved run finds it by name),
* gives it a **track id** of its own and remembers it (`maps	rackids.json` in the data folder; ids 20 to 31 first, then 5 to 19). A map that already has records under an id can ask for it with `PreferredTrackId` (Highway asks for 20),
* sends the game to the Stadium scene for it, and
* calls your `OnRace` once that scene has loaded and before it starts, so the start grid can still be moved.

```csharp
ctx.Maps.Register(new MapInfo
{
    Id = "mymap",                 // unique, lower case
    DisplayName = "My Map",
    PreferredTrackId = -1,        // 5..31 to keep an id you shipped before
    LapLength = 3000f,            // game units: time objectives are Stadium's (1457) scaled by this
    BenchmarkFactor = 0.9f,       // lower is harder
    Weather = w => w.fogActive = true,   // adjust the map's copy of the weather
    Adjust = def => def.icon = ctx.Sprites.LoadSprite("icon.png", def.icon),   // anything else on the MapDefinition
    OnRace = (scene, tm) => MyScene.Build(scene, tm)
});
```

`info.Definition` is the map's `MapDefinition` once the game has a run config (not yet at `Load`; the loader tries again at every scene load). Highway and Temple of Drift are the two big examples.

### A map from a file, with no code

A mod folder with only a `mod.json` and a `maps` folder is a map mod: every `maps\*.json` in it is a map. `examples\SampleMap` is one. The file is what the Map Maker writes.

```json
{
  "format": 1,
  "id": "mymap",
  "name": "My Map",
  "weather": "clear",
  "icon": "icon.png",
  "music": "race.wav",
  "track": [
    { "x": 0, "y": 0, "z": -380, "width": 40, "surface": "asphalt", "railRight": true, "wallLeft": true },
    ...
  ],
  "props": [ { "type": "tree", "x": 200, "y": 0, "z": 600, "rot": 90, "scale": 1.2 } ],
  "terrain": { "size": 1600, "cells": 160, "heights": "<base64 of little-endian int16, 0.05 units each>" }
}
```

| Field | Meaning |
|---|---|
| `track` | The track line: a closed loop through the points (a uniform Catmull-Rom spline), 4 to 400 points. Point 0 is the start line; the cars line up behind it. `width` is the whole road, 12 to 120 (a car is about 14 long and 5 wide). `surface` is `asphalt`, `dirt`, `ice` or `wet` from this point to the next. `railLeft`, `railRight` add guardrail, `wallLeft`, `wallRight` an invisible frictionless wall at the road's edge (a car slides along it). Left and right are as you drive. The track must not cross itself. |
| `props` | `tree`, `cone`, `barrel`, `block`, `ramp` or `arch`, up to 3000. `rot` is degrees about the up axis. |
| `terrain` | The ground: a square of `size` units, `cells` squares to a side (16 to 256), centred on the origin, one height per grid point (`(cells+1)` squared, row by row from the corner at -x, -z). It is pressed down under the road automatically, so a hill never covers it. Leave it out for flat ground. |
| `weather` | `clear`, `rain`, `fog`, `snow`, `ash`, `night`. |
| `icon`, `music` | Files next to the map file (no folders): a PNG for the run map, a 16-bit PCM wav for the race. |

The start line is turned to point along +X for you (the game's start grid faces +X), and the map is lowered so its start is at height 0. The file is checked when it is read: a damaged file is skipped with a line in the log, coordinates are limited to 4000 units, and nothing in it can name a file outside its own folder.

### The Map Maker

Switch it on in `config\HalfGripTools.cfg` in the data folder:

```
[MapMaker]
Enabled = true
AlwaysOpen = true
```

`Enabled` adds a scratch map, **Map Maker**, an oval, and the editor. `AlwaysOpen` makes every race you start that scratch map, so starting a run (use a throwaway one: leaving a race costs a life) takes you straight to the editor. The editor is also available in a race on any map made from a file: press **F10** (`Key`) and the race is loaded again with the editor open.

The editor holds time and gives you a free camera over the map as it stands, and edits it live, so what you see is what you race on:

* **Camera**: right mouse look, `WASD` fly, `Q`/`E` down and up, shift faster, scroll changes the speed, `T` a top view (scroll changes the height).
* **1 TRACK**: click empty ground to add a point (on the road, between its neighbours), drag a point to move it, `Delete` removes it, `[` `]` or PgUp/PgDn change its height. The panel sets the width, surface, guardrail and walls of the selected point, or of all points.
* **2 PROPS**: pick a kind and click the ground; click a prop to drag it; `R`/`F` turn it.
* **3 TERRAIN**: a brush (raise, lower, smooth, flatten) with a size and strength.
* **4 MAP**: name, weather, **SAVE**, **SAVE AS**, **EXPORT AS A MOD FOLDER**, **TEST DRIVE** and **CLOSE EDITOR**.

SAVE writes `maps\<id>.json` in the data folder; maps there join your runs the next time the game starts (`MyMapsInRuns`). EXPORT writes `export\<id>` with a `mod.json` and the map file, a mod folder ready for the game's `Mods` folder or the Workshop. TEST DRIVE saves, then loads the race again so the game lays out its start grid and AI on the changed map; press F10 to go back. Close the editor without a test drive and the race starts on the old grid.

The guardrail of these maps holds like a wall below 90 on the dash (speed into the rail), bends from 90 to 120 and breaks from 120 (`[Guardrail]` settings in the same file).

## Mod manager

Your mod appears in the main menu mod manager with its `name`, `version` and `author`. Players can switch it off there, and then `Load` does not run at the next start. If it does not compile, the manager shows `ERROR` and the log has the details. Do not rely on being loaded. If you need another mod, put it in `requires`.

## Porting to 2.0

HalfGripTools 2.0 does not need BepInEx, so a mod can no longer use BepInEx's types. A mod made for 1.x does not compile and the log says so (`uses BepInEx types and was made for HalfGripTools 1.x`). The change is small and mechanical; all thirteen mods in [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods) were ported this way.

| 1.x (BepInEx) | 2.0 |
|---|---|
| `using BepInEx.Logging;` | remove it (`using HalfGripTools;` is enough) |
| `using BepInEx.Configuration;` | remove it |
| `ManualLogSource` | `ModLog` |
| `ConfigFile` | `ModConfig` |
| `ConfigEntry<T>` | `ModSetting<T>` |
| `ConfigEntryBase` | `ModSetting` |
| `ctx.Harmony` | `new Harmony(ctx.HarmonyId)`, and `ctx.OnUnload(() => harmony.UnpatchSelf())` |
| `BepInEx.Paths.ConfigPath` and files next to the config | `ctx.DataFolder` |
| `"tools": "1.0.6"` in `mod.json` | `"apiVersion": "2.0.0"` (`tools` still works) |

Your settings carry over: the first time 2.0 runs it copies `BepInEx\config\halfgrip.<id>.cfg` to `config\<id>.cfg` in the data folder (the old files are left where they are), and a file a mod kept next to them (`halfgrip.<id>.<name>`) to that mod's `storage\<id>\<name>`. A mod that wrote its own file to the config folder should now write to `ctx.DataFolder`.

Everything else is as before: `HalfGripMod`, `ModContext`, `Items`, `Keys`, `Net`, `Sprites`, `[HarmonyPatch]` classes and `PatchAll()`.
## Speedrun mode

You do nothing. With speedrun mode on, your mod is not loaded. If the mode is turned on while the game runs, your patches, items and scene callbacks are removed for you. Only objects your mod put into a scene need cleanup: override `Unload()` (or call `ctx.OnUnload`) and destroy them, as BombRace does.

Do not change the speedrun setting yourself. Do not keep references to objects the loader removed.

## Habits

- Read the log (`HalfGripTools.log` in the data folder, or `BepInEx\LogOutput.log`) after every change. Log what you created and what you found: `Created X from 'Y'`.
- Wrap risky work in try/catch and log. One broken feature should not stop the mod.
- Check for null on game objects. The game updates and things move.
- Textures must match the size of the sprite they replace. Pixel art uses point filtering.
- The config file keeps an old value when you change a default. After retuning, edit or delete the `.cfg`, or add a version entry and reset once (SniperRifle does).
- People read mods before installing. Keep code in plain files, comment why, and avoid network, file and save access. If you write to the save, say so in your README. Files your mod needs to keep go in `ctx.DataFolder`, nowhere else.
