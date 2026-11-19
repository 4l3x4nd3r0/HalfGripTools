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
	/// <c>mods.json</c> in the data folder, which is plain JSON you can edit. Mods are on unless listed as off,
	/// so a newly installed mod is on. The exception is a mod that comes from Steam's Workshop: it starts off, because
	/// Steam can change its code at any time, and is on only after you switched it on, for the files it had then
	/// (the folder hash is kept; a changed mod has to be switched on again). Read when the game starts; a change takes effect at the next start.
	/// </summary>
	internal static class ModStore
	{
		private static readonly HashSet<string> disabled = new HashSet<string>();

		internal static List<Playlist> Playlists { get; } = new List<Playlist>();

		/// <summary>Name of the playlist the player last picked, or "" for no playlist.</summary>
		internal static string Active { get; private set; } = "";

		private static string FilePath => Path.Combine(ModPaths.DataDir, "mods.json");

		// Steam's Workshop mods: id -> hash of the folder as it is now (set at every start), and id -> hash it had when the player switched it on.
		private static readonly Dictionary<string, string> workshopNow = new Dictionary<string, string>();

		private static readonly Dictionary<string, string> workshopConsent = new Dictionary<string, string>();

		/// <summary>Tells the store that this mod comes from the Workshop and has these files now. Called by the loader after it found the mods.</summary>
		internal static void RegisterWorkshop(string id, string hash)
		{
			workshopNow[id] = hash;
		}

		/// <summary>True if the mod is a Workshop mod whose files are not the ones it was switched on for (new, or changed by an update).</summary>
		internal static bool NeedsConsent(string id)
		{
			return workshopNow.ContainsKey(id) && !IsEnabled(id);
		}

		internal static bool IsEnabled(string id)
		{
			if (workshopNow.TryGetValue(id, out string now))
			{
				return workshopConsent.TryGetValue(id, out string given) && given == now;
			}
			return !disabled.Contains(id);
		}

		internal static void SetEnabled(string id, bool enabled)
		{
			SetQuietly(id, enabled);
			Save();
		}

		private static void SetQuietly(string id, bool enabled)
		{
			if (workshopNow.TryGetValue(id, out string now))
			{
				if (enabled)
				{
					workshopConsent[id] = now;
				}
				else
				{
					workshopConsent.Remove(id);
				}
				return;
			}
			if (enabled)
			{
				disabled.Remove(id);
			}
			else
			{
				disabled.Add(id);
			}
		}

		internal static Playlist Find(string name)
		{
			return Playlists.FirstOrDefault(p => p.Name == name);
		}

		/// <summary>Switches on exactly the playlist's mods among <paramref name="installed"/>, and everything else off.</summary>
		internal static void Pick(Playlist playlist, IEnumerable<string> installed)
		{
			disabled.Clear();
			foreach (string id in installed)
			{
				SetQuietly(id, playlist.Mods.Contains(id));
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
			// Workshop mods are left as they are: "all mods" does not give consent for code the player has not switched on.
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
				workshopConsent.Clear();
				if (root.TryGetValue("workshop", out object given) && given is Dictionary<string, object> given2)
				{
					foreach (KeyValuePair<string, object> pair in given2)
					{
						if (pair.Value is string hash)
						{
							workshopConsent[pair.Key] = hash;
						}
					}
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
				ModRuntime.Log.LogError($"{Path.GetFileName(FilePath)} could not be read ({e.Message}); all mods stay on and playlists are empty until you change something here.");
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
					["workshop"] = workshopConsent.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => (object)p.Value),
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
				ModRuntime.Log.LogError("Could not save the mod manager settings: " + e.Message);
			}
		}
	}
}
