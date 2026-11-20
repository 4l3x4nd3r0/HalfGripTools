using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalfGripTools.MapBuild;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripTools
{
	/// <summary>
	/// A race map a mod adds. A map has no scene of its own (a mod cannot add one): it is built from code inside the Stadium scene when its race
	/// loads, and <see cref="ModMaps"/> does the rest: it makes the map's entry in the game's run config, gives it a track id of its own, sends the
	/// game to the Stadium scene for it, and calls <see cref="OnRace"/> once that scene is up.
	/// </summary>
	public sealed class MapInfo
	{
		/// <summary>Unique, lower case ("highway"). The track id is remembered per id, so records and saved runs keep working.</summary>
		public string Id = "";

		/// <summary>The name the player sees.</summary>
		public string DisplayName = "";

		/// <summary>The name a saved run stores. Defaults to <see cref="DisplayName"/>; never change it once a map has shipped.</summary>
		public string SaveName = "";

		/// <summary>The map's badge on the run map (optional: the Stadium's stays when null).</summary>
		public Sprite Icon;

		/// <summary>The race music (optional: the game picks its own when null).</summary>
		public AudioClip Music;

		/// <summary>A track id this map would like (5 to 31), for a map that already has records under one. Highway is 20, Temple of Drift 21.</summary>
		public int PreferredTrackId = -1;

		/// <summary>Length of one lap in game units. The time objectives are Stadium's (1457) scaled by this and <see cref="BenchmarkFactor"/>.</summary>
		public float LapLength = 1457f;

		/// <summary>Lower is harder. Highway's is 0.9.</summary>
		public float BenchmarkFactor = 0.9f;

		/// <summary>Adjusts the map's copy of the weather (switch rain, snow or fog off, say). Optional.</summary>
		public Action<WeatherProfile> Weather;

		/// <summary>Called once with the map's new entry in the run config, to set anything else on it (its badge, its music, its exclusion rules). Optional.</summary>
		public Action<MapDefinition> Adjust;

		/// <summary>Called when a race on this map has loaded: build the map in the Stadium scene. The scene is not started yet, so the start grid can be moved.</summary>
		public Action<Scene, TrackManager> OnRace;

		/// <summary>Not offered in the run map's random pool (the Map Maker's scratch map). It can still be raced when a patch selects it.</summary>
		public bool HiddenFromPool;

		/// <summary>The map's entry in the game's run config, once it exists (it does not at <c>Load</c>: ask again in a scene callback).</summary>
		public MapDefinition Definition { get; internal set; }

		/// <summary>The track id the map got.</summary>
		public int TrackId { get; internal set; }

		internal ModContext Owner;
	}

	/// <summary>Adds race maps (<c>ctx.Maps</c>). Everything registered here is taken out again when mods are switched off.</summary>
	public sealed class ModMaps
	{
		private readonly ModContext ctx;

		private readonly List<MapInfo> mine = new List<MapInfo>();

		internal ModMaps(ModContext ctx)
		{
			this.ctx = ctx;
		}

		/// <summary>Adds a map. It reaches the run config as soon as the game has one (the loader keeps trying at every scene load).</summary>
		public void Register(MapInfo info)
		{
			if (info == null || string.IsNullOrEmpty(info.Id) || string.IsNullOrEmpty(info.DisplayName))
			{
				ctx.Log.LogError("Maps: a map needs an Id and a DisplayName.");
				return;
			}
			info.Id = MapDocument.CleanId(info.Id);
			info.Owner = ctx;
			mine.Add(info);
			MapRegistry.Add(info);
		}

		internal void RemoveAll()
		{
			foreach (MapInfo info in mine)
			{
				MapRegistry.Remove(info);
			}
			mine.Clear();
		}
	}

	/// <summary>Where the maps are kept, the track ids they were given, and the calls the game's patches make.</summary>
	internal static class MapRegistry
	{
		private const float StadiumLap = 1457f;

		private static readonly List<MapInfo> infos = new List<MapInfo>();
		/// <summary>Track ids that maps already in players' hands use, kept free for them.</summary>
		private static readonly int[] Reserved = { 20, 21 };

		private static Dictionary<string, int> ids;
		private static MapDefinition host;
		private static int hostTrack;

		/// <summary>The built map of the race that is up, so it can be taken down.</summary>
		internal static MapWorld Current;

		internal static IEnumerable<MapInfo> All { get { return infos; } }

		internal static void Add(MapInfo info)
		{
			if (infos.Any(i => i.Id == info.Id))
			{
				ModRuntime.Log.LogWarning("Maps: a map with the id '" + info.Id + "' is already registered, the second one is ignored.");
				return;
			}
			infos.Add(info);
			Inject();
		}

		internal static void Remove(MapInfo info)
		{
			infos.Remove(info);
			RunConfig config = RunConfig.Instance;
			if (config != null && info.Definition != null)
			{
				config.maps.RemoveAll(m => m == info.Definition);
			}
			info.Definition = null;
			Teardown();
		}

		/// <summary>Makes each map's definition and puts it in the run config. Safe to call often: it only does what is missing.</summary>
		internal static void Inject()
		{
			RunConfig config = RunConfig.Instance;
			if (config == null || config.maps == null)
			{
				return;
			}
			foreach (MapInfo info in infos.ToArray())
			{
				try
				{
					if (info.Definition == null)
					{
						if (host == null)
						{
							host = config.FindMap("StadiumAsh") ?? config.maps.FirstOrDefault(m => m != null && !infos.Any(i => i.Definition == m));
							if (host == null)
							{
								ModRuntime.Log.LogWarning("Maps: the run config has no map to copy, so no map is added.");
								return;
							}
							hostTrack = host.trackIndex;
						}
						info.TrackId = Allocate(info);
						info.Definition = Make(info);
						ModRuntime.Log.LogInfo("Map " + info.DisplayName + " made from " + host.name + " (track id " + info.TrackId + ", scene track " + hostTrack + ", benchmark " + info.Definition.timeEarly.ToString("0") + "/" + info.Definition.timeMid.ToString("0") + "/" + info.Definition.timeLate.ToString("0") + " s)");
					}
					if (!info.HiddenFromPool && !config.maps.Contains(info.Definition))
					{
						config.maps.Add(info.Definition);
						ModRuntime.Log.LogInfo("Map " + info.DisplayName + " added to the run config (" + config.maps.Count + " maps)");
					}
				}
				catch (Exception e)
				{
					ModRuntime.Log.LogError("Maps: could not add " + info.DisplayName + ": " + e);
				}
			}
		}

		private static MapDefinition Make(MapInfo info)
		{
			MapDefinition map = UnityEngine.Object.Instantiate(host);
			map.hideFlags = HideFlags.HideAndDontSave;
			map.name = string.IsNullOrEmpty(info.SaveName) ? info.DisplayName : info.SaveName;
			map.displayName = info.DisplayName;
			map.conditions = string.Empty;   // no other map has one, and it shows in the run map's tooltip
			map.trackIndex = info.TrackId;
			map.allowMirror = false;
			map.allowReverse = false;
			map.allowMirrorReverse = false;
			map.mirrorIcon = null;
			map.reverseIcon = null;
			map.mirrorReverseIcon = null;

			float scale = info.LapLength / StadiumLap * info.BenchmarkFactor;
			map.timeEarly = host.timeEarly * scale;
			map.timeMid = host.timeMid * scale;
			map.timeLate = host.timeLate * scale;

			if (host.weather != null)
			{
				WeatherProfile weather = UnityEngine.Object.Instantiate(host.weather);
				weather.hideFlags = HideFlags.HideAndDontSave;
				weather.rain = false;
				weather.snow = false;
				weather.ash = false;
				weather.fogActive = false;
				weather.headlightsActive = false;
				weather.gripExponent = 1f;
				weather.gripMultiplier = 1f;
				if (info.Weather != null)
				{
					info.Weather(weather);
				}
				map.weather = weather;
			}
			if (info.Icon != null)
			{
				map.icon = info.Icon;
			}
			if (info.Music != null)
			{
				map.musicTrack = info.Music;
			}
			if (info.Adjust != null)
			{
				info.Adjust(map);
			}
			return map;
		}

		// ---- track ids: kept in the data folder so a map keeps its id (and the records and saved runs that use it) ------------

		private static string IdFile { get { return Path.Combine(Path.Combine(ModPaths.DataDir, "maps"), "trackids.json"); } }

		private static int Allocate(MapInfo info)
		{
			if (ids == null)
			{
				ids = new Dictionary<string, int>();
				try
				{
					if (File.Exists(IdFile))
					{
						Dictionary<string, object> root = MiniJson.Parse(File.ReadAllText(IdFile)) as Dictionary<string, object>;
						if (root != null)
						{
							foreach (KeyValuePair<string, object> pair in root)
							{
								ids[pair.Key] = (int)MiniJson.Number(root, pair.Key, -1f);
							}
						}
					}
				}
				catch (Exception e)
				{
					ModRuntime.Log.LogWarning("Maps: trackids.json could not be read (" + e.Message + "), ids are handed out again.");
				}
			}
			int id;
			if (ids.TryGetValue(info.Id, out id) && id >= 5 && id < 32)
			{
				return id;
			}
			HashSet<int> used = new HashSet<int>(ids.Values);
			if (info.PreferredTrackId >= 5 && info.PreferredTrackId < 32 && !used.Contains(info.PreferredTrackId))
			{
				id = info.PreferredTrackId;
			}
			else
			{
				// A map with no preference must not take an id a shipped map asks for (Highway 20, Temple of Drift 21): it may load first.
				used.UnionWith(Reserved);
				id = Enumerable.Range(20, 12).Concat(Enumerable.Range(5, 15)).FirstOrDefault(i => !used.Contains(i));
				if (id == 0)
				{
					ModRuntime.Log.LogWarning("Maps: all 27 track ids are taken, " + info.DisplayName + " shares the last one.");
					id = 31;
				}
			}
			ids[info.Id] = id;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(IdFile));
				Dictionary<string, object> write = new Dictionary<string, object>();
				foreach (KeyValuePair<string, int> pair in ids)
				{
					write[pair.Key] = pair.Value;
				}
				File.WriteAllText(IdFile, MiniJson.Write(write));
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Maps: could not save trackids.json: " + e.Message);
			}
			return id;
		}

		// ---- what the game's patches call (see GameHooks) ---------------------------------------------------------------------

		/// <summary>The map the player picked for this race, if it is one of ours.</summary>
		private static MapInfo Selected()
		{
			if (!RunState.Active || !RunState.HasSelection)
			{
				return null;
			}
			MapDefinition picked = RunState.Selected.map;
			return picked == null ? null : infos.FirstOrDefault(i => i.Definition == picked);
		}

		/// <summary>MenuManager.TrackIndex: a map of ours is built inside the Stadium scene, so the game is told that scene's index.</summary>
		internal static bool TrackIndex(ref int result)
		{
			if (Selected() == null)
			{
				return false;
			}
			result = hostTrack;
			return true;
		}

		/// <summary>RunState.BeginRace: when the Map Maker is set to open on every race, the race becomes the scratch map.</summary>
		internal static void RaceBeginning()
		{
			if (!MapMaker.AlwaysOpen || !RunState.HasSelection || RunState.Over)
			{
				return;
			}
			MapInfo maker = infos.FirstOrDefault(i => i.Id == MapLibrary.MakerId);
			if (maker != null && maker.Definition != null)
			{
				RunState.Selected.map = maker.Definition;
				RunState.Selected.variant = TrackVariant.Normal;
			}
		}

		/// <summary>A scene is loading: puts in the maps that are missing from the run config and takes down what the last race built.</summary>
		internal static void BeforeScene()
		{
			Inject();
			Teardown();
		}

		/// <summary>A scene loaded (after the mods' own scene callbacks have run): builds the map when a race on one of ours has loaded.</summary>
		internal static void OnSceneLoaded(Scene scene)
		{
			if (infos.Count == 0 || RunState.ActiveMap == null)
			{
				return;
			}
			MapInfo info = infos.FirstOrDefault(i => i.Definition == RunState.ActiveMap);
			if (info == null || info.OnRace == null)
			{
				return;
			}
			TrackManager tm = UnityEngine.Object.FindFirstObjectByType<TrackManager>();
			if (tm == null || tm.playerCarController == null || tm.lapManager == null || tm.lapManager.trackLine == null)
			{
				return;
			}
			try
			{
				info.OnRace(scene, tm);
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogError("Map " + info.DisplayName + " could not be built: " + e);
			}
		}

		/// <summary>Takes down what the last race built (the scene is gone, but a switch-off in a running race lands here too).</summary>
		internal static void Teardown()
		{
			MapRace.Restore();
			MapEditor.Close();
			if (Current != null)
			{
				Current.Destroy();
				Current = null;
			}
		}
	}
}
