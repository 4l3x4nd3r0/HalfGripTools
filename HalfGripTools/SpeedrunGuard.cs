using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>
	/// Mods and speedruns do not mix: a speedrun's seed fingerprints the item catalog, and mod items would change it. So when the game's
	/// speedrun mode is on, no mod is loaded; if it is turned on while the game is running, every mod is switched off on the spot.
	/// If the state cannot be read, mods are left off (fail closed).
	/// </summary>
	internal static class SpeedrunGuard
	{
		/// <summary>True if speedrun mode is on, or if that could not be found out.</summary>
		internal static bool IsOnOrUnknown(ManualLogSource log)
		{
			try
			{
				return Speedrun.On;
			}
			catch (Exception e)
			{
				log.LogError("Could not read the speedrun setting, so no mods are loaded: " + e.Message);
				return true;
			}
		}

		internal static void Notify(string text)
		{
			Plugin.Log.LogWarning(text);
			ToastBehaviour.Show(text);
		}
	}

	/// <summary>Turning the mode on in the settings panel calls this setter.</summary>
	[HarmonyPatch(typeof(Speedrun), nameof(Speedrun.Mode), MethodType.Setter)]
	internal static class SpeedrunModePatch
	{
		private static void Postfix()
		{
			if (Speedrun.On)
			{
				if (ModLoader.Running)
				{
					ModLoader.Shutdown(Plugin.Log);
					SpeedrunGuard.Notify("Speedrun mode is on: all mods are switched off. Restart the game before a clean speedrun.");
				}
			}
			else if (!ModLoader.Running)
			{
				SpeedrunGuard.Notify("Speedrun mode is off. Restart the game to switch the mods back on.");
			}
		}
	}

	/// <summary>A run begins by fingerprinting the catalog: never with mods in it.</summary>
	[HarmonyPatch(typeof(Speedrun), nameof(Speedrun.BeginRun))]
	internal static class SpeedrunBeginRunPatch
	{
		private static void Prefix()
		{
			if (Speedrun.On && ModLoader.Running)
			{
				ModLoader.Shutdown(Plugin.Log);
				SpeedrunGuard.Notify("Speedrun run starting: mods switched off. Restart the game if you want the catalog clean.");
			}
		}
	}

	/// <summary>A few lines of text in the corner, for messages the player must not miss.</summary>
	internal sealed class ToastBehaviour : MonoBehaviour
	{
		private static ToastBehaviour instance;

		private string text = "";

		private float until;

		internal static void Show(string message)
		{
			if (instance == null)
			{
				GameObject host = new GameObject("HalfGripTools.Toast");
				UnityEngine.Object.DontDestroyOnLoad(host);
				host.hideFlags = HideFlags.HideAndDontSave;
				instance = host.AddComponent<ToastBehaviour>();
			}
			instance.text = message;
			instance.until = Time.unscaledTime + 10f;
		}

		private void OnGUI()
		{
			if (Time.unscaledTime > until)
			{
				return;
			}
			GUIStyle style = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 18, alignment = TextAnchor.MiddleCenter };
			GUI.Box(new Rect(Screen.width * 0.25f, 20f, Screen.width * 0.5f, 70f), "HalfGripTools\n" + text, style);
		}
	}
}
