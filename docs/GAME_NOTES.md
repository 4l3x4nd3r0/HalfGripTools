# Game notes

What is known about Half Grip's code. Use it as a starting point, not as fact: the game changes between versions, so check anything you rely on against a fresh decompile.

To decompile: `ilspycmd -p -o <dir> "<game folder>\HalfGrip_Data\Managed\Assembly-CSharp.dll"` gives about 640 classes. An older ilspycmd needs the environment variable `DOTNET_ROLL_FORWARD=Major` on a newer .NET.

Checked against game version 0.418 (Steam build 26182106). Only the speedrun, `StatBoost`, `UpgradeComponent`, `UpgradeMapper`, `ItemTier`, `Stat` and `UnlockKind` classes were re-read for that version. The gun, camera, car and race objective sections below were read from the build before it. The mods that use them still load and register their items on 0.418, which shows the hooks exist, not that the behaviour is unchanged.

The running game reports its version as `Application.version` (`"0.418"`). Mods compare it with `gameVersion` in `mod.json`.

## Speedrun mode

- `Speedrun.Mode` is a static property. It reads `SaveData.GetInt("speedrun.mode")` once and caches the value. Values: `SpeedrunMode.Off`, `Race`, `Items`, `Achievements`. `Speedrun.On` is `Mode != Off`. The setter writes the save. The settings button calls it (`SpeedrunSettingsUI.Press`). `Speedrun.ForgetCache()` clears the cache.
- `SaveData` loads its files on the first read. Reading `Speedrun.On` in a BepInEx plugin's `Awake` is safe and has no side effects.
- `Speedrun.BeginRun()` (called from `RunState`) stores `run.seed` and `run.unlocks`. `UnlockedIds()` is every catalog item that is not `disabled` and is unlocked, sorted. Items added by a mod would change that list. This is why HalfGripTools keeps mods out of speedruns.
- `Speedrun.Seeded(...)` seeds the random draws for the map, rerolls, races and payouts. A mod that changes one of those rolls (BombRace changes the objective roll) would change a seeded run.
- The game does not detect BepInEx or Harmony.

### Items
- Every shop item, reward and starting piece is a `ScriptableObject` deriving from `UpgradeComponent`. Subclasses seen: `GenericUpgrade`/`PassiveUpgrade` (PASSIVE), `GunUpgrade` (GUN), `TireUpgrade`, `EngineUpgrade`, `TransmissionUpgrade`, `ChassisUpgrade` (bodies), `ServoUpgrade`, plus behaviour items `NO2`, `SlowMotion`, `HandBrake`, `BeerUpgrade`, `RerollMapUpgrade`, `RunBoonUpgrade` *(unverified list)*.
- Base fields used by the mods: `id`, `description`, `secondaryDescription`, `interaction`, `image`, `charmSprite`, `charmVariant`, `cost`, `tier` (`ItemTier`: Stock, Modified, RaceSpec, Legendary, BlackMarket), `disabled`, `startsLocked`, `unlockKind` (`UnlockKind.Discovery` etc.), `hideStatLines`, `forceShow`/`forceHide` (lists of stat-line names, e.g. `"fire rate"`), `customSprites`, `mountSlot`, `interiorPrefab`, `exteriorPrefab`, and `statBoosts` (`List<StatBoost>` with `stat`, `amount`).
- **`statBoosts` alone does the work for passives.** `Stat` has about 40 entries (TopSpeed, EnginePower, BrakePower, TireGrip, MaxHp, WeaponDamage, MoneyPerKill, ...). Amounts are fractions (0.06 = +6%) and scale the fitted part. The game also writes the card's stat lines itself.
- **Description format:** first line is the name; the grey flavour line is `"\n<color=#7e7e7e>text</color>"`.
- **Registration:** `ItemCatalog.Instance.items` is the list. `UpgradeMapper.Awake` copies it and builds the id lookup, so an item must be in the list **before** that runs. Saves store items by id and find them via the mapper. Hook: Harmony **prefix on `UpgradeMapper.Awake`** calling an idempotent `Inject()`; also call it on `SceneManager.sceneLoaded` and, for loadout items, on `LoadoutProgress.ApplyToNewRun`.
- **Making an item:** `UnityEngine.Object.Instantiate(existingItem)`, set `hideFlags = HideAndDontSave`, overwrite the fields. Bases used: `PassiveUpgrade` id `treeFreshener` (fallback: first plain passive), `ChassisUpgrade` id `aero_1`, the SMG `GunUpgrade` (`GunType.SMG_RIFLE`; revolver = `GunType.REVOLVER`).
- Shop inclusion is by tier and `disabled`. `disabled = true` keeps an item out of shops and race rewards. Normal items use `disabled = false`.
- Other item ids seen: `tire_1` Street Tires, `engine_ev` Electric Motor, `shifter_cvt` CVT, `aero_tough` Aero Tough, `shield_pistons` Extra Pistons, `aero_1` Eagle Wing *(name unverified)*.
- Loadouts: `LoadoutCatalog.Instance.loadouts` (`LoadoutDefinition`: `Id`, `loadoutName`, `startingItems`). `LoadoutProgress.IsUnlocked(loadout)`; the unlock flag in the save is `loadout.unlocked.<Id>` = 1. Loadouts: Rookie ($25), Nitro Head ($10, NO2, -20% HP), Gunslinger ($0, Slug Shotgun) *(unverified)*.

### Guns
- A gun item is a `GunUpgrade`; in the car, a `Gun` component does the shooting. `NAutomaticGun` is the SMG/rifle one (`fullAuto`, `RequiredGunType`, `aimModel`). `Gun.Fitted` is the equipped item. `Gun.FireRay(origin, direction)` is the single-shot step. Gun sound/tracer fields: `shootSounds`, `shoootSoundVolume` (sic, three o's), `tracerMaterial`, `tracerWidthMultiplier`, `tracerLifetime`, `bulletHitEffect`, `bulletCollisionSounds`.
- Magazines: `NMagazine` (`gun`, `insertPoint`, `alignPoint`, `loadedSprite`, `emptySprite`, `insertSounds`), held by a `MagazineManager` rack that counts and copies them in its `Awake`. Base classes: `Grabbable`, `FallingObject`, `NAmmo`.
- The SMG's flat sprites are named `baza smg_0`, `smg2_0`, `smg3 backside_0`, `smg4_0`, `fullbullet_0`, `amunitie smg_0`, `amunitie smg full_0`. Replacements must match the original pixel size to line up.
- The held 3D gun (`aimModel`) is three extruded meshes, one per texture (`baza smg`, `smg2`, `amunitie smg`). `SpriteMesh.Build(texture, Frame, name)` rebuilds a mesh from a PNG using frame constants read off the game's own meshes. The `smg2` texture is **shared with small sight blocks** that would float over the new slide, so meshes under 100 wide are disabled.
- Sprites: `Sprites.LoadSprite(file, like)` copies pixels-per-unit and pivot from the sprite it replaces; textures use `FilterMode.Point`. Files load from `<plugin dir>\sprites`.

### Gun internals
- `Gun` (abstract): `FireRay(origin, direction)` is **protected**, not public (Harmony still patches it by name). A shot's damage is the property `Gun.Damage` = `Stats.Get(WeaponDamage, Fitted.damage / projectiles)`, so changing `Fitted.damage` for the length of a `FireRay` call changes the shot (passives still apply on top). Tire hits go through `CarTireDamage.ShootTire` and ignore it. `LapShield.Scale(enemy, damage)` then scales damage by how far ahead the target is.
- `Fitted` = `PlayerStats.gunUpgrade` if its `gunType == RequiredGunType`. `FireShot()` fires `FireRay` once per projectile and then `FireEffects`. `Update` handles aim (`UpdateAimModel`, release on `HandUp(0)`, `Shoot()` on `HandDown(1)`) and calls `LoadFully()` when `Endless` (cheat infinite ammo, `godGun`).
- Aim mode: `OnBodyGrabbed` -> `CanEnterAimMode()` -> `EnterAimMode()`. `AimParts()` lists extra grabbables (the SMG's slide) whose `grabRedirect` is `AimRedirect`: grabbing one raises the gun **if `PartsAim() && CanEnterAimMode()`**, otherwise it is an ordinary grab. Overridable abstracts a new gun must give: `RequiredGunType`, `ShotsLeft`, `CanEnterAimMode`, `IsFull`, `LoadFully`.
- Aim ray is `fpvcamera.ScreenPointToRay(AimScreenPoint)` (protected property). `fpvcamera` is the **world** camera; the gun and cockpit are drawn by a separate `InteriorCamera`.
- `NAutomaticGun`: `slide` is a `LinearObject` (value 0 closed .. 1 back; `targetValue` is the spring target; `IsGrabbed`; `onGrabbed`, `onRightClicked`). `slideBackThreshold` 0.9 pulled, `slideForwardThreshold` 0.1 returned; its own `UpdateSlide` locks the slide open on an empty magazine. Fields reused by the rifle: `slidePullSounds`, `slideReleaseSounds`, `magLockSounds`, `emptyClickSounds`, `bulletEjectEffect` (spent brass, played on slide pull), `OutsideGunShellEffect`. Chambering is `TryChamber()` from the magazine, which a replaced gun must not rely on.
- Ammo: `NAmmo : FallingObject` has `loadZone`, `busy`, `spent`, `FellOutOfTheWorld()`. `NMagazine` seats via `insertPoint`/`alignPoint`. `MagazineManager.Awake` adopts every `NAmmo` under it; `FitAuthoredCount` makes the count equal the fitted item's `magazines` (it finds the gun with `GetComponentInParent<Gun>`); `Take(item)` removes and destroys an item for good.
- Camera: `CarControllerNew.Update` calls private `UpdateCameraEffects(float, Vector3)`, which sets `fpvCamera.fieldOfView = Lerp(current, target, 0.1)` **every frame**. Setting the FOV yourself from a `LateUpdate` is fought by the lerp; the zoom here puts the game's value back in a Harmony prefix and applies the zoom in the postfix (`Scope.cs`).
- Enemies: `EnemyController.health` starts at `carController.carStats.health` (default 100; levelled drivers differ, not measured); `TakeDamage(amount, ...)`.

### Cars and bodies
- `ChassisUpgrade`: `rollingFriction`, `airDrag` (stock 1), `downForce` (stock 0), `isFwd`, `isAwd`, `carSkin`. `CarSkin`: `color1/2/3`, `skinIndex`, `baseTexture`, `colorMask`.
- `CarSkinController.EquipCurrentSkin` is where `Awake`, `ApplySkin` (shop preview, refit) and `RandomizeSkin` all end: the place to follow a skin change.
- Game cars are real cars scaled 0.83 in length and width and 0.64 in height. A car is about 13 game units long. **Speed: 1 unit = 1 m**; the game's own km/h is `rb.linearVelocity.magnitude * 3.6` (`ClapperFrame`). An older note here said "100 km/h is roughly 80 units/s"; that disagrees with the game's own conversion (100 km/h = 27.8 units/s) and was not measured, so it was dropped. The speedometer needle is `CarLink.dialspeed.value = cc.forwardSpeed / speedMultiplier` (a share of top speed, not km/h). **The number on the dash is `SpeedDashboard`: `cc.forwardSpeed * SpeedDashboard.SpeedToDisplay` (1.8)**, shown as "---" before the start; that is the speed a player reads, so use it, not the 3.6 above (about half).
- Player health/wreck: `CarLink.hp` / `MaxHp`; `CarLink.TakeCrashDamage(damage, shake = true, kind)` lowers `hp`, emits `WreckImminent` (an item can save the car) and does nothing if `RaceTracker.Ended` or `CarLink.godMode`. A wreck is noticed by `TrackManager.OnPlayerDestroyed` (`hp <= 0`) -> `RaceTracker.ReportWrecked` -> `EndAfterWreck` (the game's explosion and "WRECKED" text). `DevCommands.Kill` just sets `playerCarlink.hp = 0`. `CarControllerNew` is the car (`isPlayer`; `CastGround` casts against all layers except Ignore Raycast).
- The Dacia model is `art\dacia_model.txt` (6 MB, made once by `art\import_ref_logan.py` from a reference glb; needs numpy, Pillow, trimesh, scipy, fast-simplification). `art\preview_dacia_model.py` draws what the plugin will load. It replaces the second car model's body, hood, glass and light meshes; interior untouched.

### Race objectives and the run map
- A race "mode" is a `RaceObjective` struct (`kind`, `amount`, `target`, `moneyBonus`) on each `RunNode`. `RunMap.Generate`: column 0 gets `None`, the last column `RunConfig.FinaleObjective()` (Boss or Place 1), every middle column `RunConfig.RollObjective(column)` (weighted `objectives` list). `RunMap.KeepAKillFreePath` calls `RollObjective` again to replace kill-heavy nodes, so one postfix on `RollObjective` covers both. `RunState.BeginRace` copies the node's objective into `RunState.PendingObjective`.
- `ObjectiveKind` = None 0, Place 1, KillEnemies 2, Assassinate 3, Time 4, NotLast 5, Boss 8. `RaceObjective.IsMet(RaceResult)` decides success; with `RunConfig.surviveIsImplicit` (true) a wrecked player fails any objective. `NotLast` is met if `place >= 1 && place < max(2, field)`.
- **Save pitfall:** `RunState.Valid` throws the whole saved run away (`SaveData.DeleteKey("run")`) if a node's `objectiveKind` is not `Enum.IsDefined`. So a mod must not invent an `ObjectiveKind`. `RunSave` stores `objectiveKind/Amount/Target/Money` per node and `pending*` for the current race, so **`target` survives a save**. BombRace tags a `NotLast` objective with `target = "bombrace.bomb"`; without the mod it reads as plain "Don't be last".
- Display: `RaceObjective.Describe(bool coloured)` (also used by the pause screen, `SelectedObjectiveText`, `RaceObjectiveAnnouncer` which calls it out 4 s into the race, and `RunMapView.Describe` for the map tooltip). The icon above a map node: `RunMapView.BuildNode` -> `RunMapSkin.ObjectiveSprite(objective)` (searches `objectiveIcons` for a matching `kind`/`amount`), placed at `skin.objectivePosition`. Map icons are 12x12 at ppu 100 (`FactoryIcon_0`, `StadiumIcon_0`, ...); the actual objective icons' size was **not** found (the `Slice_*_0` sprites 12x12/15x15 are candidates), so `Sprites.LoadSprite` warns if the bomb art is a different size.
- In a race: `TrackManager.raceStarted`, `TimeSinceRaceStart` (`Time.time - raceStartTime`), `playerCarlink`, `lapManager` (`HasPlayerFinished()`, `GetPlayerTotalTime()`); `RaceTracker.Ended`, `GameManager.raceOver` (static); `TextController.EnqueueText(string, seconds)` is the on-screen call-out. `RaceCutoff` and `RaceObjectiveAnnouncer` are components added to the scene (`RaceCutoff` via `RuntimeInitializeOnLoadMethod` + `sceneLoaded`), the pattern BombRace copies.

### Runs, map, shop
- `RunMap.Generate(RunConfig)` builds 7 columns (1 node at each end, 2-3 in between); `RunMap.Deal(config, node)` rolls a node's map when entered if `unknownMap`. `RunNode`: `index, column, row, rowCount, map, variant, unknownMap, shortRace, objective{kind, amount}`. `ObjectiveKind`: None, Place, NotLast, Time, Kill..., Assassinate. `MapDefinition.RollVariant()`, `RunConfig.FindMap(name)`.
- `RunMapView.Describe` and the private `RefreshNode` (its argument is a private nested type; read its `node` field with `Traverse`) draw the map icons and tooltips.
- `UpgradeUIManager.Awake` fills a shop panel; static `restore`/`avoid` are non-null on a save-restore or reroll; `FillFrom(List<string> ids)` is private (call with `AccessTools.Method`); `ItemIds()`; `upgradeSlots[].upgradeTransform`; `Reroller`; `UpgradeMapper.Resolve().Get(id)`; `GameManager.Owns(item)`; `RunState.Active/Races`.
- Timer: `RaceStats.RunSeconds` uses `unscaledDeltaTime`, so it runs on the map, in shops and during loads; stops only when `timeScale == 0`. Race ends after the finish line plus a fixed 6 s hold.
- Playground (`playground` console command): class `Playground` (`Awake`, `OnDisable`), `PlayerResetLogic.noHints` (static; stops "Hold R to reset").

---

## Pitfalls

- BepInEx rejects `'` and `"` in config section names. `Winner's Grip` has to become `Winners Grip`.
- `giveitem` matches the item's card name in snake_case, not its id.
- `SpriteLoader.LoadSprite` warns when a PNG is not the size of the sprite it replaces. `dacia_icon.png` is 32x18, the game's body icon is 43x13. That warning is known.
- BepInEx keeps an existing config value when a mod's default changes. After retuning a default, edit or delete the installed `.cfg`.
- The car eases the camera field of view every frame. Set it only inside the `UpdateCameraEffects` prefix and postfix.
- A removed mod makes a saved run lose its items. Nothing else in the save changes.
- `RunState.Valid` deletes a saved run that has a node with an undefined `ObjectiveKind`. Do not invent one.
- An item must be in `ItemCatalog.Instance.items` before `UpgradeMapper.Awake`, which builds the id lookup once. HalfGripTools adds items there, before a new run takes its loadout, and after each scene load.
- Do not anchor things inside the cockpit to the screen. The cockpit moves against the screen when the aspect ratio changes. Use the cockpit's own geometry (see `DashBomb.cs` in the BombRace mod, in the HalfGripMods repo).
- Main menu: the language flag, Discord and Settings buttons sit in `SettingsButtonContainer`, stacked by hand 76 units apart. `ButtonDepth` remembers its starting Y in `Awake` and eases back to it, so a copied button needs that field set (`baseY`) after you move it.
- A TMP label with `overflowMode = Ellipsis` shows nothing if its rect is shorter than the font size.
