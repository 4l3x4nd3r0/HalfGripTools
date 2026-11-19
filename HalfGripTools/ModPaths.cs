using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>
	/// Where things are. Mods are read from folders next to the game (and from Steam's Workshop folder), and nothing a mod or the loader writes
	/// goes into any of them: settings, the compile cache, a mod's own files and the log are in one data folder, the same for every host:
	/// <c>%USERPROFILE%\AppData\LocalLow\Half Games\HalfGrip\Mods</c> (Unity's persistent data path, next to the game's Player.log).
	/// A Workshop folder is replaced by Steam on every update, so writing there would lose the data.
	/// </summary>
	internal static class ModPaths
	{
		internal static string GameDir { get; private set; } = "";

		internal static string DataDir { get; private set; } = "";

		internal static string ConfigDir => Path.Combine(DataDir, "config");

		internal static string CacheDir => Path.Combine(DataDir, "cache");

		internal static string StorageRoot => Path.Combine(DataDir, "storage");

		/// <summary>The mods you install: <c>&lt;game&gt;\Mods</c>.</summary>
		internal static string LocalMods => Path.Combine(GameDir, "Mods");

		/// <summary>Where 1.x put the mods. Still read when it exists, so a 1.x install keeps working.</summary>
		internal static string LegacyMods => Path.Combine(GameDir, "BepInEx", "HalfGripMods");

		internal static string LegacyConfig => Path.Combine(GameDir, "BepInEx", "config");

		internal static void Init()
		{
			GameDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
			// HALFGRIPTOOLS_DATA moves the whole data folder (settings, cache, mod data, log): for tests, or a game on a stick.
			string custom = Environment.GetEnvironmentVariable("HALFGRIPTOOLS_DATA");
			DataDir = Path.GetFullPath(string.IsNullOrWhiteSpace(custom) ? Path.Combine(Application.persistentDataPath, "Mods") : custom.Trim());
			Directory.CreateDirectory(DataDir);
			Directory.CreateDirectory(ConfigDir);
		}

		/// <summary>A mod's own writable folder, <c>storage\&lt;id&gt;</c>, made when asked for.</summary>
		internal static string StorageFor(string modId)
		{
			string folder = Path.Combine(StorageRoot, modId);
			Directory.CreateDirectory(folder);
			return folder;
		}

		/// <summary>
		/// Steam's Workshop content folder for this game (<c>steamapps\workshop\content\&lt;app id&gt;</c>), or null. Found without Steam:
		/// the game folder is <c>steamapps\common\&lt;installdir&gt;</c>, and the <c>appmanifest_&lt;id&gt;.acf</c> next to <c>common</c> that names
		/// that installdir gives the app id. The folder only exists once something was subscribed to.
		/// </summary>
		internal static string WorkshopContent()
		{
			try
			{
				string common = Path.GetDirectoryName(GameDir.TrimEnd('\\', '/'));
				string steamapps = common == null ? null : Path.GetDirectoryName(common);
				if (steamapps == null || !string.Equals(Path.GetFileName(common), "common", StringComparison.OrdinalIgnoreCase) ||
					!string.Equals(Path.GetFileName(steamapps), "steamapps", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
				string installDir = Path.GetFileName(GameDir.TrimEnd('\\', '/'));
				foreach (string manifest in Directory.GetFiles(steamapps, "appmanifest_*.acf"))
				{
					Match name = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
					Match id = Regex.Match(Path.GetFileName(manifest), "^appmanifest_(\\d+)\\.acf$");
					if (name.Success && id.Success && string.Equals(name.Groups[1].Value, installDir, StringComparison.OrdinalIgnoreCase))
					{
						string content = Path.Combine(steamapps, "workshop", "content", id.Groups[1].Value);
						return Directory.Exists(content) ? content : null;
					}
				}
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Could not look for Steam's Workshop folder: " + e.Message);
			}
			return null;
		}
	}

	/// <summary>
	/// One-time copy of what 1.x kept in BepInEx's config folder into the data folder: settings (<c>halfgrip.&lt;id&gt;.cfg</c> becomes
	/// <c>config\&lt;id&gt;.cfg</c>), other files a mod wrote there (<c>halfgrip.&lt;id&gt;.&lt;name&gt;</c> becomes <c>storage\&lt;id&gt;\&lt;name&gt;</c>) and the mod
	/// manager's choices. It only copies, only where the new file does not exist yet, and never deletes, so the old files stay for a 1.x loader.
	/// </summary>
	internal static class LegacyMigration
	{
		internal static void Run()
		{
			try
			{
				string source = ModPaths.LegacyConfig;
				if (!Directory.Exists(source))
				{
					return;
				}
				int copied = 0;
				foreach (string file in Directory.GetFiles(source, "halfgrip.*"))
				{
					string[] parts = Path.GetFileName(file).Split(new[] { '.' }, 3);
					if (parts.Length < 3)
					{
						continue;
					}
					string id = parts[1];
					string rest = parts[2];
					string target = rest == "cfg"
						? Path.Combine(ModPaths.ConfigDir, id == "tools" ? "HalfGripTools.cfg" : id + ".cfg")
						: Path.Combine(ModPaths.StorageFor(id), rest);
					copied += Copy(file, target);
				}
				copied += Copy(Path.Combine(source, "HalfGripTools.mods.json"), Path.Combine(ModPaths.DataDir, "mods.json"));
				if (copied > 0)
				{
					ModRuntime.Log.LogInfo($"Copied {copied} file(s) of settings from {source} to {ModPaths.DataDir}. The old files were left where they are.");
				}
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Could not copy the old settings: " + e.Message);
			}
		}

		private static int Copy(string from, string to)
		{
			if (!File.Exists(from) || File.Exists(to))
			{
				return 0;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(to));
			File.Copy(from, to);
			return 1;
		}
	}
}
