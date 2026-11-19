using System;
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
		internal static bool IsOnOrUnknown(ModLog log)
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
			ModRuntime.Log.LogWarning(text);
			ToastBehaviour.Show(text);
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
