using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalfGripTools.MapBuild;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>The Map Maker's settings (<c>HalfGripTools.cfg</c>, section MapMaker and Guardrail).</summary>
	internal static class MapMaker
	{
		/// <summary>Adds the scratch map "Map Maker" and the editor (F10 in a race on a map made from a file).</summary>
		internal static bool Enabled;

		/// <summary>Every race you start becomes the Map Maker map, like Highway's TestMode, so a run takes you straight to the editor.</summary>
		internal static bool AlwaysOpen;

		/// <summary>The maps you saved in the editor are dealt in your runs like any other map.</summary>
		internal static bool MyMapsInRuns = true;

		internal static KeyCode Key = KeyCode.F10;

		/// <summary>Set when the editor sends you into a test drive, so the reloaded race does not open the editor again.</summary>
		internal static bool Testing;

		/// <summary>Set when the player asked for the editor (F10 in a race), so the reloaded race opens it.</summary>
		internal static bool OpenNext;

		internal static void Init(ModConfig settings)
		{
			Enabled = settings.Bind("MapMaker", "Enabled", false, "Adds the Map Maker: a scratch map and an editor you can open in any race on a map made from a file (F10). Off, none of it is loaded.").Value;
			AlwaysOpen = settings.Bind("MapMaker", "AlwaysOpen", false, "Every race you start is the Map Maker's scratch map, so a run takes you straight to the editor. For making maps. Needs Enabled.").Value;
			MyMapsInRuns = settings.Bind("MapMaker", "MyMapsInRuns", true, "Maps you saved from the editor are dealt in your runs like the game's own maps.").Value;
			string key = settings.Bind("MapMaker", "Key", "F10", "The key that opens the editor in a race on a map made from a file.").Value;
			try
			{
				Key = (KeyCode)Enum.Parse(typeof(KeyCode), key, true);
			}
			catch (ArgumentException)
			{
				Key = KeyCode.F10;
			}
			RailRules.ContainSpeed = settings.Bind("Guardrail", "ContainSpeed", 90f, "For maps made from a file: the speed into the guardrail (the dash's number) up to which it holds like a wall.").Value;
			RailRules.BreakSpeed = settings.Bind("Guardrail", "BreakSpeed", 120f, "The speed into the guardrail from which it breaks. Between the two it holds, bends and costs speed.").Value;
			RailRules.BendSpeedLoss = settings.Bind("Guardrail", "BendSpeedLoss", 0.2f, "Share of the car's speed lost at the top of the bending band.").Value;
			RailRules.SpeedLossPerPiece = settings.Bind("Guardrail", "SpeedLossPerPiece", 0.3f, "Share of the car's speed each broken piece takes.").Value;
			RailRules.LogHits = settings.Bind("Guardrail", "LogHits", false, "Log every guardrail hit: the speed into the rail, what it did, the speed before and after.").Value;
		}
	}

	/// <summary>Finds the maps made from files and registers them: <c>maps\*.json</c> in each mod folder, and in the data folder (the editor's own).</summary>
	internal static class MapLibrary
	{
		internal const string MakerId = "mapmaker";

		private static readonly List<MapInfo> userInfos = new List<MapInfo>();

		internal static string UserFolder { get { return Path.Combine(ModPaths.DataDir, "maps"); } }

		/// <summary>The editor's maps, and the scratch map. Called once at start, before the mods load, so a file in the data folder wins over a mod's with the same id.</summary>
		internal static void LoadUserMaps()
		{
			if (!MapMaker.Enabled && !Directory.Exists(UserFolder))
			{
				return;
			}
			try
			{
				Directory.CreateDirectory(UserFolder);
				if (MapMaker.Enabled && !File.Exists(Path.Combine(UserFolder, MakerId + ".json")))
				{
					MapDocument fresh = MapDocument.Oval(MakerId, "Map Maker");
					File.WriteAllText(Path.Combine(UserFolder, MakerId + ".json"), fresh.ToJson());
				}
				foreach (string file in Directory.GetFiles(UserFolder, "*.json"))
				{
					if (Path.GetFileName(file) == "trackids.json")
					{
						continue;
					}
					MapDocument doc = Read(file);
					if (doc == null || (doc.Id == MakerId && !MapMaker.Enabled))
					{
						continue;
					}
					MapInfo info = MakeInfo(doc, Path.GetDirectoryName(file));
					info.HiddenFromPool = doc.Id == MakerId || !MapMaker.MyMapsInRuns;
					userInfos.Add(info);
					MapRegistry.Add(info);
				}
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogError("Maps: could not read the editor's maps: " + e.Message);
			}
		}

		/// <summary>Registers the maps in a mod's own <c>maps</c> folder (a mod can be nothing but a mod.json and a maps folder).</summary>
		internal static void LoadModMaps(ModContext ctx)
		{
			string folder = Path.Combine(ctx.Folder, "maps");
			if (!Directory.Exists(folder))
			{
				return;
			}
			foreach (string file in Directory.GetFiles(folder, "*.json"))
			{
				MapDocument doc = Read(file);
				if (doc == null)
				{
					continue;
				}
				if (MapRegistry.All.Any(i => i.Id == doc.Id))
				{
					ctx.Log.LogWarning("Maps: " + Path.GetFileName(file) + " has the id '" + doc.Id + "', which another map already has, so it is not added.");
					continue;
				}
				ctx.Maps.Register(MakeInfo(doc, folder));
			}
		}

		internal static void Shutdown()
		{
			foreach (MapInfo info in userInfos)
			{
				MapRegistry.Remove(info);
			}
			userInfos.Clear();
		}

		private static MapDocument Read(string file)
		{
			try
			{
				MapDocument doc = MapDocument.Parse(File.ReadAllText(file));
				doc.Path = file;
				return doc;
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Maps: " + file + " is not a usable map: " + e.Message);
				return null;
			}
		}

		internal static MapInfo MakeInfo(MapDocument doc, string folder)
		{
			var info = new MapInfo
			{
				Id = doc.Id,
				DisplayName = doc.Name,
				SaveName = "HGMap " + doc.Id,
				LapLength = Mathf.Max(300f, Spline.Length(Spline.Sample(doc, 12f))),
				BenchmarkFactor = 1.0f
			};
			string weather = doc.Weather;
			info.Weather = p => Preset(weather, p);
			if (doc.Icon.Length > 0)
			{
				info.Icon = Picture(Path.Combine(folder, doc.Icon));
			}
			if (doc.Music.Length > 0)
			{
				info.Music = WavFile.Load(Path.Combine(folder, doc.Music));
			}
			info.OnRace = (scene, tm) => MapRace.BuildDocument(scene, tm, info, doc);
			return info;
		}

		private static void Preset(string name, WeatherProfile p)
		{
			switch (name)
			{
				case "rain":
					p.rain = true;
					p.gripMultiplier = 0.85f;
					break;
				case "fog":
					p.fogActive = true;
					p.fogStart = 40f;
					p.fogEnd = 520f;
					p.fogColor = new Color(0.72f, 0.74f, 0.78f);
					break;
				case "snow":
					p.snow = true;
					p.gripMultiplier = 0.75f;
					break;
				case "ash":
					p.ash = true;
					break;
				case "night":
					p.directionalLightIntensity *= 0.12f;
					p.headlightsActive = true;
					break;
			}
		}

		/// <summary>A PNG as a sprite for the run map (100 pixels to the unit, like the game's map badges).</summary>
		private static Sprite Picture(string path)
		{
			try
			{
				if (!File.Exists(path))
				{
					return null;
				}
				var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
				if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
				{
					return null;
				}
				texture.filterMode = FilterMode.Point;
				texture.hideFlags = HideFlags.HideAndDontSave;
				Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
				sprite.hideFlags = HideFlags.HideAndDontSave;
				return sprite;
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Maps: could not read the picture " + path + ": " + e.Message);
				return null;
			}
		}
	}

	/// <summary>A 16-bit PCM wav as a clip (the loader has no audio loader yet; this is the same parser Temple of Drift's music uses).</summary>
	internal static class WavFile
	{
		internal static AudioClip Load(string path)
		{
			try
			{
				if (!File.Exists(path))
				{
					return null;
				}
				byte[] bytes = File.ReadAllBytes(path);
				int channels = BitConverter.ToInt16(bytes, 22);
				int rate = BitConverter.ToInt32(bytes, 24);
				int bits = BitConverter.ToInt16(bytes, 34);
				if (bits != 16 || channels < 1 || channels > 2)
				{
					ModRuntime.Log.LogWarning("Maps: " + Path.GetFileName(path) + " must be 16-bit PCM, mono or stereo.");
					return null;
				}
				int pos = 12, dataAt = -1, dataLength = 0;
				while (pos + 8 <= bytes.Length)
				{
					string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
					int size = BitConverter.ToInt32(bytes, pos + 4);
					if (id == "data")
					{
						dataAt = pos + 8;
						dataLength = Math.Min(size, bytes.Length - dataAt);
						break;
					}
					pos += 8 + size + (size & 1);
				}
				if (dataAt < 0)
				{
					return null;
				}
				int samples = dataLength / 2;
				var data = new float[samples];
				for (int i = 0; i < samples; i++)
				{
					data[i] = BitConverter.ToInt16(bytes, dataAt + i * 2) / 32768f;
				}
				AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples / channels, channels, rate, false);
				clip.SetData(data, 0);
				clip.hideFlags = HideFlags.HideAndDontSave;
				return clip;
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Maps: could not read the music " + path + ": " + e.Message);
				return null;
			}
		}
	}
}
