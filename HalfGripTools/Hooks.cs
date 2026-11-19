using System;
using System.Reflection;

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

		/// <summary>The game's controls plus the keys mods added. Read this wherever the game reads <c>Controls.All</c> to list or look up controls.</summary>
		public static Controls.Action[] ControlRows()
		{
			return ModKeys.Rows();
		}
	}
}
