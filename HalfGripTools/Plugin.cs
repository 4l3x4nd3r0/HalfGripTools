using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripTools
{
	/// <summary>
	/// The only compiled part of the mod system. It finds the mods in <c>BepInEx\HalfGripMods</c> (plain C# and JSON, readable by anyone),
	/// compiles them on this PC and runs them, unless the game's speedrun mode is on.
	/// </summary>
	[BepInPlugin(Guid, "HalfGripTools", Version)]
	public class Plugin : BaseUnityPlugin
	{
		public const string Guid = "halfgrip.tools";

		public const string Version = "1.1.0";

		public const string Author = "4l3x4nd3r";

		internal static ManualLogSource Log;

		private Harmony harmony;

		private bool speedrunAtStart;

		private float nextCheck;

		private void Awake()
		{
			Log = base.Logger;
			DontDestroyOnLoad(gameObject);

			harmony = new Harmony(Guid);
			// These two only react to speedrun mode; they change nothing else in the game.
			PatchSafely(typeof(SpeedrunModePatch));
			PatchSafely(typeof(SpeedrunBeginRunPatch));
			// Adds the mod list to the anonymous run stats; before the speedrun check so a speedrun run says "mods":[].
			PatchSafely(typeof(RunStatsPatch));

			speedrunAtStart = SpeedrunGuard.IsOnOrUnknown(Log);
			if (speedrunAtStart)
			{
				Log.LogWarning("Speedrun mode is on: no mods are loaded. Turn it off and restart the game to use mods.");
				return;
			}

			PatchSafely(typeof(ControlsListPatch));
			PatchSafely(typeof(UpgradeMapperAwakePatch));
			PatchSafely(typeof(LoadoutApplyPatch));
			SceneManager.sceneLoaded += OnSceneLoaded;
			ModLoader.ExtraFolders = Config.Bind("General", "ExtraModFolders", "", "More folders to load mods from, separated by ';'. The normal BepInEx\\HalfGripMods folder is always read first; if the same mod id is in two folders the first one wins.").Value;
			ModLoader.LoadAll(Log);
			Log.LogInfo("Loaded HalfGripTools " + Version + " by " + Author + " with " + ModLoader.Mods.Count + " mod(s)");
		}

		private void PatchSafely(Type type)
		{
			try
			{
				harmony.CreateClassProcessor(type).Patch();
			}
			catch (Exception e)
			{
				Log.LogError("Patch " + type.Name + " failed and is off: " + e.Message);
			}
		}

		private bool failuresShown;

		/// <summary>Once, when the main menu is up: a few lines naming the mods that did not load, so a broken mod is not found out from a log file.</summary>
		private void ReportFailures()
		{
			if (failuresShown)
			{
				return;
			}
			failuresShown = true;
			string[] bad = ModLoader.Entries.Where(e => e.Status == ModStatus.Failed || e.Status == ModStatus.NeedsOther).Select(e => e.Manifest.DisplayName).ToArray();
			if (bad.Length > 0)
			{
				ToastBehaviour.Show($"{bad.Length} mod(s) did not load: {string.Join(", ", bad.Take(4))}{(bad.Length > 4 ? ", ..." : "")}. Open Mods to see why.");
			}
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (!ModLoader.Running)
			{
				return;
			}
			ItemInjector.InjectAll();
			MenuButton.TryAttach();
			if (MenuButton.Attached)
			{
				ReportFailures();
			}
			foreach (ModContext mod in ModLoader.Mods.ToArray())
			{
				foreach (Action<Scene> callback in mod.SceneCallbacks.ToArray())
				{
					try
					{
						callback(scene);
					}
					catch (Exception e)
					{
						Log.LogError($"{mod.Manifest.DisplayName}: scene callback failed: {e}");
					}
				}
			}
		}

		// The setter patch catches the settings button. This catches any other way the mode changes (a restored save, say).
		private void Update()
		{
			if (!ModLoader.Running || Time.unscaledTime < nextCheck)
			{
				return;
			}
			nextCheck = Time.unscaledTime + 0.5f;
			if (SpeedrunGuard.IsOnOrUnknown(Log))
			{
				ModLoader.Shutdown(Log);
				SpeedrunGuard.Notify("Speedrun mode is on: all mods are switched off. Restart the game before a clean speedrun.");
			}
		}
	}
}
