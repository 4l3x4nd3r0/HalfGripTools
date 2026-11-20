using System;
using System.Reflection;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>
	/// Applies the patches of one mod. HalfGripTools itself does not know how: a host that can patch the game (Harmony, from outside) gives it one;
	/// a host built into the game has none, and mods that need patching cannot run there.
	/// </summary>
	public interface IModPatcher
	{
		/// <summary>Applies every patch class in the mod's assembly. A patch that no longer fits the game is logged and skipped.</summary>
		void PatchAll(Assembly mod, ModLog log);

		/// <summary>Takes every patch of this mod out again (speedrun mode switches mods off while the game runs).</summary>
		void UnpatchAll();
	}

	/// <summary>
	/// What a host brings when it patches the game from outside: <c>HalfGripTools.Harmony.dll</c>, built on HarmonyX. It turns patches on the game's
	/// methods into calls to <see cref="GameHooks"/>, which is everything HalfGripTools itself needs from the game. A host built into the game does not
	/// use a provider: the game calls <see cref="GameHooks"/> itself, and HalfGripTools has no dependency on Harmony or on any injector.
	/// </summary>
	public interface IPatchProvider
	{
		/// <summary>For the log, for example "HarmonyX 2.9.0".</summary>
		string Name { get; }

		/// <summary>The assembly that holds the patching types (<c>[HarmonyPatch]</c>, <c>Traverse</c>), which mods are compiled against.</summary>
		Assembly Api { get; }

		/// <summary>The hooks that only watch for speedrun mode and the run stats. They are on even when mods are not.</summary>
		void InstallStartupHooks();

		/// <summary>The hooks mods need: the game's item catalog and the controls list. Only installed when mods are going to run.</summary>
		void InstallModHooks();

		/// <summary>The hooks of <see cref="GunBehaviour"/> on the gun classes. Installed the first time a mod registers one.</summary>
		void InstallGunHooks();

		IModPatcher CreatePatcher(string ownerId);
	}

	/// <summary>
	/// The places HalfGripTools needs the game to tell it something. Each one is a plain static method, so the game can call it directly where the
	/// comment says; from outside, <see cref="IPatchProvider"/> patches those places and calls them. Safe to call before mods are loaded: they do nothing then.
	/// </summary>
	public static class GameHooks
	{
		/// <summary>True when the game reads <see cref="ControlRows"/> where it used to read <c>Controls.All</c> (Controls.Holder, ControlsSettingsUI.Fill).</summary>
		public static bool ControlsRedirected { get; set; }

		/// <summary>
		/// Call at the start of <c>MenuManager.TrackIndex</c> (private, returns the index of the scene to load). If this returns true, return
		/// <paramref name="result"/> instead: the race is on a map a mod added, which is built inside the Stadium scene.
		/// </summary>
		public static bool TrackIndex(ref int result)
		{
			return ModLoader.Running && MapRegistry.TrackIndex(ref result);
		}

		/// <summary>Call at the start of <c>RunState.BeginRace</c>. Only does anything when the Map Maker is set to open on every race.</summary>
		public static void RaceBeginning()
		{
			if (ModLoader.Running)
			{
				MapRegistry.RaceBeginning();
			}
		}

		/// <summary>Call at the end of <c>Loc.Load(string lang)</c> (private): the game has just built its text table for that language, and the mods add their own rows.</summary>
		public static void LanguageLoaded(string language)
		{
			if (!ModLoader.Running)
			{
				return;
			}
			foreach (ModContext mod in ModLoader.Mods)
			{
				mod.Text.Merge(language);
			}
		}

		/// <summary>Call after the <c>Speedrun.Mode</c> setter ran.</summary>
		public static void SpeedrunModeChanged()
		{
			if (Speedrun.On)
			{
				if (ModLoader.Running)
				{
					ModLoader.Shutdown(ModRuntime.Log);
					SpeedrunGuard.Notify("Speedrun mode is on: all mods are switched off. Restart the game before a clean speedrun.");
				}
			}
			else if (!ModLoader.Running)
			{
				SpeedrunGuard.Notify("Speedrun mode is off. Restart the game to switch the mods back on.");
			}
		}

		/// <summary>Call at the start of <c>Speedrun.BeginRun</c>, before it fingerprints the item catalog.</summary>
		public static void SpeedrunRunBeginning()
		{
			if (Speedrun.On && ModLoader.Running)
			{
				ModLoader.Shutdown(ModRuntime.Log);
				SpeedrunGuard.Notify("Speedrun run starting: mods switched off. Restart the game if you want the catalog clean.");
			}
		}

		/// <summary>Pass the text <c>RunStats.Fit</c> made and use what comes back: it has the list of running mods added. On any problem the text is returned as it was.</summary>
		public static string RunStatsFit(string json)
		{
			return RunStatsMods.Apply(json);
		}

		/// <summary>Call before <c>UpgradeMapper.Awake</c> and before <c>LoadoutProgress.ApplyToNewRun</c>: puts the mods' items in the catalog.</summary>
		public static void CatalogAboutToBeRead()
		{
			ItemInjector.InjectAll();
		}

		// ---- Guns: where the game's gun classes tell GunBehaviour what happens. Each is called from the patch (or by the game) named in its comment. ----

		/// <summary>Call at the start of <c>NAutomaticGun.Awake</c> and <c>NPumpActionGun.Awake</c>. If this returns false, skip the game's Awake.</summary>
		public static bool GunBeforeAwake(Gun gun)
		{
			return !ModLoader.Running || GunHub.All(b => b.BeforeAwake(gun), "BeforeAwake");
		}

		/// <summary>Call at the end of <c>NAutomaticGun.Awake</c> and <c>NPumpActionGun.Awake</c>.</summary>
		public static void GunAfterAwake(Gun gun)
		{
			if (ModLoader.Running)
			{
				GunHub.Each(b => b.AfterAwake(gun), "AfterAwake");
			}
		}

		/// <summary>Call at the end of <c>NAutomaticGun.Start</c>.</summary>
		public static void GunAfterStart(Gun gun)
		{
			if (ModLoader.Running)
			{
				GunHub.Each(b => b.AfterStart(gun), "AfterStart");
			}
		}

		/// <summary>Call at the start of <c>NAutomaticGun.Shoot</c> and <c>NPumpActionGun.Shoot</c>. If this returns false, skip the game's Shoot.</summary>
		public static bool GunBeforeShoot(Gun gun)
		{
			return !ModLoader.Running || GunHub.All(b => b.BeforeShoot(gun), "BeforeShoot");
		}

		/// <summary>Call at the start of <c>NAutomaticGun.CanEnterAimMode</c>. If this returns true, return <paramref name="result"/> instead.</summary>
		public static bool GunCanAim(Gun gun, ref bool result)
		{
			bool value = result;
			bool decided = ModLoader.Running && GunHub.Decide(b => b.CanAim(gun, ref value), "CanAim");
			result = value;
			return decided;
		}

		/// <summary>Call at the start of <c>NAutomaticGun.IsFull</c>. If this returns true, return <paramref name="result"/> instead.</summary>
		public static bool GunIsFull(Gun gun, ref bool result)
		{
			bool value = result;
			bool decided = ModLoader.Running && GunHub.Decide(b => b.IsFull(gun, ref value), "IsFull");
			result = value;
			return decided;
		}

		/// <summary>Call at the start of <c>NAutomaticGun.ShotsLeft</c>. If this returns true, return <paramref name="result"/> instead.</summary>
		public static bool GunShotsLeft(Gun gun, ref int result)
		{
			int value = result;
			bool decided = ModLoader.Running && GunHub.Decide(b => b.ShotsLeft(gun, ref value), "ShotsLeft");
			result = value;
			return decided;
		}

		/// <summary>Call at the end of <c>NPumpActionGun.UpdatePump</c>.</summary>
		public static void GunAfterUpdatePump(NPumpActionGun gun)
		{
			if (ModLoader.Running)
			{
				GunHub.Each(b => b.AfterUpdatePump(gun), "AfterUpdatePump");
			}
		}

		/// <summary>Call at the end of <c>NPumpActionGun.PartsAim</c>, passing what it returned; return value goes back as the result.</summary>
		public static bool GunAfterPartsAim(NPumpActionGun gun, bool result)
		{
			bool value = result;
			if (ModLoader.Running)
			{
				GunHub.Each(b => b.AfterPartsAim(gun, ref value), "AfterPartsAim");
			}
			return value;
		}

		/// <summary>Call at the start of <c>Gun.FireRay</c>. If this returns true, skip the game's FireRay and return <paramref name="result"/>.</summary>
		public static bool GunBeforeFireRay(Gun gun, Vector3 origin, Vector3 direction, ref Vector3 result)
		{
			Vector3 end = result;
			bool replaced = ModLoader.Running && GunHub.Decide(b => !b.BeforeFireRay(gun, origin, direction, ref end), "BeforeFireRay");
			result = end;
			return replaced;
		}

		/// <summary>Call at the end of <c>Gun.FireRay</c>, also when it threw (a finalizer).</summary>
		public static void GunAfterFireRay(Gun gun)
		{
			if (ModLoader.Running)
			{
				GunHub.Each(b => b.AfterFireRay(gun), "AfterFireRay");
			}
		}

		/// <summary>Call at the start of <c>MagazineManager.Awake</c>.</summary>
		public static void RackBeforeAwake(MagazineManager rack)
		{
			if (ModLoader.Running)
			{
				GunHub.Each(b => b.BeforeRackAwake(rack), "BeforeRackAwake");
			}
		}

		/// <summary>Call at the end of <c>MagazineManager.Start</c>.</summary>
		public static void RackAfterStart(MagazineManager rack)
		{
			if (ModLoader.Running)
			{
				GunHub.Each(b => b.AfterRackStart(rack), "AfterRackStart");
			}
		}

		/// <summary>Call at the start of <c>MagazineManager.Grant</c>. If this returns true, return <paramref name="result"/> instead.</summary>
		public static bool RackGrant(MagazineManager rack, int magazines, ref int result)
		{
			int value = result;
			bool decided = ModLoader.Running && GunHub.Decide(b => b.GrantMagazines(rack, magazines, ref value), "GrantMagazines");
			result = value;
			return decided;
		}

		/// <summary>Call at the start of <c>MagazineManager.Spawn</c>. If this returns true, return <paramref name="result"/> instead.</summary>
		public static bool RackSpawn(MagazineManager rack, ref int result)
		{
			int value = result;
			bool decided = ModLoader.Running && GunHub.Decide(b => b.SpawnMagazines(rack, ref value), "SpawnMagazines");
			result = value;
			return decided;
		}

		/// <summary>Call at the start of <c>MagazineManager.SpawnEntities</c>. If this returns true, return <paramref name="result"/> instead.</summary>
		public static bool RackSpawnEntities(MagazineManager rack, ref int result)
		{
			int value = result;
			bool decided = ModLoader.Running && GunHub.Decide(b => b.SpawnMagazineObjects(rack, ref value), "SpawnMagazineObjects");
			result = value;
			return decided;
		}

		/// <summary>The game's controls plus the keys mods added. Read this wherever the game reads <c>Controls.All</c> to list or look up controls.</summary>
		public static Controls.Action[] ControlRows()
		{
			return ModKeys.Rows();
		}
	}
}
