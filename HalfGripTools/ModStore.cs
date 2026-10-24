using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HalfGripTools
{
	/// <summary>A named set of mods that are switched on together.</summary>
	internal sealed class Playlist
	{
		public string Name;

		/// <summary>Ids of the mods in the playlist. May name mods that are not installed (any more).</summary>
		public List<string> Mods = new List<string>();
	}

	/// <summary>
	/// What the player chose in the mod manager: which mods are off, and the saved playlists. Kept in
	/// <c>BepInEx\config\HalfGripTools.mods.json</c>, which is plain JSON you can edit. Mods are on unless listed as off,
	/// so a newly installed mod is on. Read when the game starts; a change takes effect at the next start.
	/// </summary>
	internal static class ModStore
	{
		private static readonly HashSet<string> disabled = new HashSet<string>();

		internal static List<Playlist> Playlists { get; } = new List<Playlist>();

		/// <summary>Name of the playlist the player last picked, or "" for no playlist.</summary>
		internal static string Active { get; private set; } = "";

		private static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "HalfGripTools.mods.json");

		internal static bool IsEnabled(string id)
		{
			return !disabled.Contains(id);
		}

		internal static void SetEnabled(string id, bool enabled)
		{
			if (enabled)
			{
				disabled.Remove(id);
			}
			else
			{
				disabled.Add(id);
			}
			Save();
		}

		internal static Playlist Find(string name)
		{
			return Playlists.FirstOrDefault(p => p.Name == name);
		}

		/// <summary>Switches on exactly the playlist's mods among <paramref name="installed"/>, and everything else off.</summary>
		internal static void Pick(Playlist playlist, IEnumerable<string> installed)
		{
			disabled.Clear();
			foreach (string id in installed.Where(id => !playlist.Mods.Contains(id)))
			{
				disabled.Add(id);
			}
			Active = playlist.Name;
			Save();
		}

		/// <summary>Switches every installed mod on and drops the playlist selection.</summary>
		internal static void PickAll()
		{
			disabled.Clear();
			Active = "";
			Save();
		}

		/// <summary>Saves the current choice as a playlist (a unique name is made from <paramref name="name"/>) and selects it.</summary>
		internal static Playlist SaveNew(string name, IEnumerable<string> installed)
		{
			name = string.IsNullOrWhiteSpace(name) ? "Playlist" : name.Trim();
			string unique = name;
			for (int n = 2; Find(unique) != null; n++)
			{
				unique = name + " " + n;
			}
			Playlist playlist = new Playlist { Name = unique, Mods = installed.Where(IsEnabled).ToList() };
			Playlists.Add(playlist);
			Active = unique;
			Save();
			return playlist;
		}

		/// <summary>Overwrites the selected playlist with the mods that are on now.</summary>
		internal static void UpdateActive(IEnumerable<string> installed)
		{
			Playlist playlist = Find(Active);
			if (playlist != null)
			{
				playlist.Mods = installed.Where(IsEnabled).ToList();
				Save();
			}
		}

		internal static void DeleteActive()
		{
			Playlist playlist = Find(Active);
			if (playlist != null)
			{
				Playlists.Remove(playlist);
			}
			Active = "";
			Save();
		}

		/// <summary>True if the selected playlist no longer matches what is switched on.</summary>
		internal static bool ActiveModified(IEnumerable<string> installed)
		{
			Playlist playlist = Find(Active);
			if (playlist == null)
			{
				return false;
			}
			return installed.Any(id => IsEnabled(id) != playlist.Mods.Contains(id));
		}

		internal static void Load()
		{
			disabled.Clear();
			Playlists.Clear();
			Active = "";
			if (!File.Exists(FilePath))
			{
				return;
			}
			try
			{
				if (!(MiniJson.Parse(File.ReadAllText(FilePath)) is Dictionary<string, object> root))
				{
					throw new FormatException("the file must be a JSON object");
				}
				foreach (string id in MiniJson.List(root, "disabled").OfType<string>())
				{
					disabled.Add(id);
				}
				foreach (Dictionary<string, object> entry in MiniJson.List(root, "playlists").OfType<Dictionary<string, object>>())
				{
					string name = MiniJson.String(entry, "name");
					if (name != "" && Find(name) == null)
					{
						Playlists.Add(new Playlist { Name = name, Mods = MiniJson.List(entry, "mods").OfType<string>().ToList() });
					}
				}
				string active = MiniJson.String(root, "active");
				Active = Find(active) != null ? active : "";
			}
			catch (Exception e)
			{
				// Keep what the player wrote: it is only replaced when they next change something.
				Plugin.Log.LogError($"{Path.GetFileName(FilePath)} could not be read ({e.Message}); all mods stay on and playlists are empty until you change something here.");
				disabled.Clear();
				Playlists.Clear();
				Active = "";
			}
		}

		private static void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				Dictionary<string, object> root = new Dictionary<string, object>
				{
					["disabled"] = disabled.OrderBy(id => id, StringComparer.Ordinal).Cast<object>().ToList(),
					["active"] = Active,
					["playlists"] = Playlists.Select(p => (object)new Dictionary<string, object>
					{
						["name"] = p.Name,
						["mods"] = p.Mods.Cast<object>().ToList()
					}).ToList()
				};
				File.WriteAllText(FilePath, MiniJson.Write(root));
			}
			catch (Exception e)
			{
				Plugin.Log.LogError("Could not save the mod manager settings: " + e.Message);
			}
		}
	}
}
