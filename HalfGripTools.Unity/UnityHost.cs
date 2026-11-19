using System;
using System.IO;
using System.Text;
using UnityEngine;

// The host for HalfGripTools built into the game's own Unity project. Copy this file and HalfGripTools.dll (plus the Roslyn DLLs next to it) into
// the project; there is nothing else to install: no Doorstop, no BepInEx, no Harmony. Unity itself starts it, before the first scene wakes.
//
// What the game has to do in return: call GameHooks where it says in docs\HOSTING.md (six one-line calls), because nothing patches the game
// from outside here. A mod that needs [HarmonyPatch] cannot run in this host (ModContext.PatchAll logs it); data-only mods and mods written
// against the API (items, keys, sprites, maps, events) run as they do everywhere.
namespace HalfGripTools.UnityHost
{
	internal static class Boot
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Start()
		{
			ModRuntime.Start(new UnityHost());
		}
	}

	/// <summary>Logs to Unity's log (Player.log) and to <c>HalfGripTools.log</c> in the data folder.</summary>
	internal sealed class UnityHost : IModHost
	{
		private readonly object gate = new object();

		private string file;

		public string Name => "Unity";

		/// <summary>No patching: the game calls <see cref="GameHooks"/> itself.</summary>
		public IPatchProvider Patches => null;

		public void Write(string source, ModLogLevel level, string text)
		{
			string line = $"[{level.ToString().PadRight(7)}:{source}] {text}";
			lock (gate)
			{
				try
				{
					if (file == null)
					{
						file = Path.Combine(ModRuntime.DataFolder, "HalfGripTools.log");
						File.WriteAllText(file, "", new UTF8Encoding(false));
					}
					File.AppendAllText(file, line + "\r\n", new UTF8Encoding(false));
				}
				catch (Exception)
				{
				}
			}
			if (level >= ModLogLevel.Error)
			{
				Debug.LogError("[HalfGripTools] " + line);
			}
			else if (level == ModLogLevel.Warning)
			{
				Debug.LogWarning("[HalfGripTools] " + line);
			}
			else
			{
				Debug.Log("[HalfGripTools] " + line);
			}
		}
	}
}
