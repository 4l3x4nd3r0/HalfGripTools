# Writing a mod

A mod is a folder in `BepInEx\HalfGripMods\`. At game start HalfGripTools compiles its C# (if any), reads its JSON items and runs it. You do not build anything and you ship no DLL. What you publish is what people read.

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
  "requires": [],
  "gameVersion": "0.418",
  "tools": "1.0.2"
}
```

| Field | Meaning |
|---|---|
| `id` | Required. Lower case letters, digits, `-`, `_`. Must be unique. Names the config file (`halfgrip.mymod.cfg`) and the compile cache. |
| `name`, `version`, `author`, `description` | Shown in the log and in the mod manager. Keep `name` under about 28 characters, the list cuts it. |
| `requires` | Ids of mods that must load first. If one is missing or off, your mod is skipped. |
| `gameVersion` | Game version you made it for. `"0.418"` is exactly 0.418. `"0.x"` is any 0.something. `x` must be a whole part, `0.4x` does not work. |

| `tools` | Optional. The oldest HalfGripTools version the mod runs on, for example `"1.0.2"`. A loader older than that which knows this field skips the mod and logs "needs HalfGripTools 1.0.2 or newer". (1.0.1 and older do not know the field: a mod that uses `ctx.Keys` should check for it itself, see below.) |

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

Compile it without starting the game: `.\tools\check-mods.ps1 -ModsPath .\examples -Mod MyMod`. To run it, copy the folder into `BepInEx\HalfGripMods\` in the game folder, start the game and read `BepInEx\LogOutput.log`.

### ModContext

| Member | Meaning |
|---|---|
| `Log` | Log source named after your mod. |
| `Config` | BepInEx config file `BepInEx\config\halfgrip.<id>.cfg`. `Config.Bind(section, key, default, description)`. Section names cannot contain `'` or `"`. |
| `Folder` | Your mod's folder. |
| `Harmony` | A Harmony instance owned by your mod. What it patches is undone when mods switch off. |
| `PatchAll()` | Applies each `[HarmonyPatch]` class on its own. A hook that no longer fits the game is logged and skipped, the rest of the mod keeps working. |
| `OnSceneLoaded(callback)` | Runs after every scene load. |
| `OnUnload(callback)` | Runs when mods switch off (speedrun mode). |
| `Sprites` | `LoadSprite(file, like, ppuOverride)` makes a sprite from `sprites\<file>` with the pivot and pixel density of the game sprite `like`. `LoadTexture(file)` returns a point-filtered texture. `Reskin(map, ppu)` swaps sprites by name in the scene. |
| `Items` | See below. |
| `Keys` | Adds keys to Settings > Controls (since 1.0.2). See below. |

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

## Mod manager

Your mod appears in the main menu mod manager with its `name`, `version` and `author`. Players can switch it off there, and then `Load` does not run at the next start. If it does not compile, the manager shows `ERROR` and the log has the details. Do not rely on being loaded. If you need another mod, put it in `requires`.

## Speedrun mode

You do nothing. With speedrun mode on, your mod is not loaded. If the mode is turned on while the game runs, your patches, items and scene callbacks are removed for you. Only objects your mod put into a scene need cleanup: override `Unload()` (or call `ctx.OnUnload`) and destroy them, as BombRace does.

Do not change the speedrun setting yourself. Do not keep references to objects the loader removed.

## Habits

- Read `BepInEx\LogOutput.log` after every change. Log what you created and what you found: `Created X from 'Y'`.
- Wrap risky work in try/catch and log. One broken feature should not stop the mod.
- Check for null on game objects. The game updates and things move.
- Textures must match the size of the sprite they replace. Pixel art uses point filtering.
- BepInEx keeps an old config value when you change a default. After retuning, edit or delete the `.cfg`, or add a version entry and reset once (SniperRifle does).
- People read mods before installing. Keep code in plain files, comment why, and avoid network, file and save access. If you write to the save, say so in your README.
