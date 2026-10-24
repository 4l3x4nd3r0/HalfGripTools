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

		public const string Version = "1.0.1";

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

			speedrunAtStart = SpeedrunGuard.IsOnOrUnknown(Log);
			if (speedrunAtStart)
			{
				Log.LogWarning("Speedrun mode is on: no mods are loaded. Turn it off and restart the game to use mods.");
				return;
			}

			PatchSafely(typeof(UpgradeMapperAwakePatch));
			PatchSafely(typeof(LoadoutApplyPatch));
			SceneManager.sceneLoaded += OnSceneLoaded;
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

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (!ModLoader.Running)
			{
				return;
			}
			ItemInjector.InjectAll();
			MenuButton.TryAttach();
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
