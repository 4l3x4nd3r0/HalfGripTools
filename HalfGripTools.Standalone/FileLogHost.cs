using System;
using System.IO;
using System.Text;
using HalfGripTools.HarmonyHost;
using UnityEngine;

namespace HalfGripTools.StandaloneHost
{
	/// <summary>
	/// The host without BepInEx: the log goes to <c>HalfGripTools.log</c> in the data folder (rewritten at every start, the old one kept as
	/// <c>HalfGripTools.previous.log</c>) and, so it is also in the game's own Player.log, to Unity's log.
	/// </summary>
	internal sealed class FileLogHost : IModHost
	{
		private readonly object gate = new object();

		private string file;

		public string Name => "Standalone";

		/// <summary>Harmony. HALFGRIPTOOLS_PATCHES=none starts without it, to see what HalfGripTools does in a host that cannot patch (as one built into the game).</summary>
		public IPatchProvider Patches => Environment.GetEnvironmentVariable("HALFGRIPTOOLS_PATCHES") == "none" ? null : provider ?? (provider = new HarmonyProvider());

		private IPatchProvider provider;

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
						string previous = Path.Combine(ModRuntime.DataFolder, "HalfGripTools.previous.log");
						if (File.Exists(file))
						{
							File.Copy(file, previous, true);
						}
						File.WriteAllText(file, "", new UTF8Encoding(false));
					}
					File.AppendAllText(file, line + "\r\n", new UTF8Encoding(false));
				}
				catch (Exception)
				{
					// No file: the line still goes to Unity's log below.
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
