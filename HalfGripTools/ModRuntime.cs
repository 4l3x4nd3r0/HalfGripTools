using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripTools
{
	/// <summary>
	/// The way in. A host (BepInEx, the standalone loader, or the game itself one day) calls <see cref="Start"/> once, after the engine is up and before the
	/// game's own scenes wake. This finds the mods in the mod folders, compiles them on this PC and runs them, unless the game's speedrun mode is on.
	/// </summary>
	public static class ModRuntime
	{
		public const string Version = "2.0.0";

		public const string Author = "4l3x4nd3r";

		internal static ModLog Log;

		private static IModHost host;

		/// <summary>How the host patches the game, or null.</summary>
		internal static IPatchProvider PatchProvider => host?.Patches;

		private static bool started;

		/// <summary>The folder for settings, cache, mod data and the log. Set before the first line is written.</summary>
		public static string DataFolder => ModPaths.DataDir;

		public static string HostName => host == null ? "" : host.Name;

		internal static ModLog CreateLog(string source)
		{
			return new ModLog(source, host.Write);
		}

		/// <summary>Starts HalfGripTools. Does nothing the second time, so two hosts cannot start it twice.</summary>
		public static void Start(IModHost modHost)
		{
			if (started)
			{
				return;
			}
			started = true;
			host = modHost;
			ModPaths.Init();
			Log = CreateLog("HalfGripTools");
			Log.LogInfo($"HalfGripTools {Version} by {Author}, {host.Name} host, patching: {(PatchProvider == null ? "none" : PatchProvider.Name)}. Data folder {ModPaths.DataDir}");
			try
			{
				StartInner();
			}
			catch (Exception e)
			{
				Log.LogError("HalfGripTools failed to start: " + e);
			}
		}

		private static void StartInner()
		{
			LegacyMigration.Run();
			ModConfig settings = new ModConfig(System.IO.Path.Combine(ModPaths.ConfigDir, "HalfGripTools.cfg"));
			ModLoader.ExtraFolders = settings.Bind("General", "ExtraModFolders", "", "More folders to load mods from, separated by ';'. The Mods folder next to the game is always read first; if the same mod id is in two folders the first one is used.").Value;
			settings.SaveIfDirty();

			// The hooks that only watch speedrun mode and add the mod list to the anonymous run stats (so a speedrun run says "mods":[]).
			// They change nothing else in the game. A host built into the game has no provider: the game calls GameHooks itself.
			Install(() => PatchProvider?.InstallStartupHooks());

			GameObject home = new GameObject("HalfGripTools");
			UnityEngine.Object.DontDestroyOnLoad(home);
			home.hideFlags = HideFlags.HideAndDontSave;
			home.AddComponent<RuntimeBehaviour>();

			if (SpeedrunGuard.IsOnOrUnknown(Log))
			{
				Log.LogWarning("Speedrun mode is on: no mods are loaded. Turn it off and restart the game to use mods.");
				return;
			}

			Install(() => PatchProvider?.InstallModHooks());
			SceneManager.sceneLoaded += OnSceneLoaded;
			ModLoader.LoadAll(Log);
			Log.LogInfo("Loaded HalfGripTools " + Version + " with " + ModLoader.Mods.Count + " mod(s)");

			// A host that starts late (the standalone one starts when the first scene is up) missed the scenes that are already loaded.
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				OnSceneLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Single);
			}
		}

		private static void Install(Action install)
		{
			try
			{
				install();
			}
			catch (Exception e)
			{
				Log.LogError("A hook failed to install and is off: " + e.Message);
			}
		}

		private static bool failuresShown;

		/// <summary>Once, when the main menu is up: a few lines naming the mods that did not load, so a broken mod is not found out from a log file.</summary>
		private static void ReportFailures()
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

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
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

		/// <summary>The loader's own MonoBehaviour: one poll twice a second for the speedrun switch.</summary>
		private sealed class RuntimeBehaviour : MonoBehaviour
		{
			private float nextCheck;

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
}
