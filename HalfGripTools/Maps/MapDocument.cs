using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace HalfGripTools.MapBuild
{
	/// <summary>The surfaces a stretch of road can have. Grip and drag are what the game's <c>HandlingModifier</c> gets.</summary>
	internal static class Surfaces
	{
		internal static readonly string[] Names = { "asphalt", "dirt", "ice", "wet" };

		internal static Color ColorOf(string name)
		{
			switch (name)
			{
				case "dirt": return new Color(0.42f, 0.30f, 0.18f);
				case "ice": return new Color(0.72f, 0.84f, 0.92f);
				case "wet": return new Color(0.09f, 0.10f, 0.13f);
				default: return new Color(0.16f, 0.16f, 0.18f);
			}
		}

		/// <summary>Grip and rolling resistance, 1 being plain tarmac.</summary>
		internal static void Handling(string name, out float grip, out float drag)
		{
			switch (name)
			{
				case "dirt": grip = 0.75f; drag = 1.8f; break;
				case "ice": grip = 0.4f; drag = 0.9f; break;
				case "wet": grip = 0.7f; drag = 1.1f; break;
				default: grip = 1f; drag = 1f; break;
			}
		}

		internal static string Next(string name, int step)
		{
			int i = Array.IndexOf(Names, name);
			return Names[((i < 0 ? 0 : i) + step + Names.Length) % Names.Length];
		}
	}

	/// <summary>One control point of the track line.</summary>
	internal sealed class MapPoint
	{
		internal float X, Y, Z;

		/// <summary>The whole width of the road, in game units (a car is about 14 long and 5 wide; a lane of 11 is generous).</summary>
		internal float Width = 36f;

		/// <summary>The surface from this point to the next.</summary>
		internal string Surface = "asphalt";

		internal bool RailLeft, RailRight, WallLeft, WallRight;

		internal Vector3 Pos
		{
			get { return new Vector3(X, Y, Z); }
			set { X = value.x; Y = value.y; Z = value.z; }
		}

		internal MapPoint Copy()
		{
			return (MapPoint)MemberwiseClone();
		}
	}

	/// <summary>A place on the start grid, on the ground (x, z in the map's own space). Cars face along the start line.</summary>
	internal sealed class MapSpot
	{
		internal float X, Z;
	}

	internal sealed class MapProp
	{
		/// <summary>One of <see cref="Props.Types"/>.</summary>
		internal string Type = "tree";

		internal float X, Y, Z;

		/// <summary>Turn about the up axis, in degrees.</summary>
		internal float Rot;

		internal float Scale = 1f;

		internal Vector3 Pos
		{
			get { return new Vector3(X, Y, Z); }
			set { X = value.x; Y = value.y; Z = value.z; }
		}
	}

	/// <summary>The ground round the road: a square grid of heights, centred on the origin. Heights are stored as centimetre-ish steps (see <see cref="Step"/>) in the file.</summary>
	internal sealed class MapTerrain
	{
		/// <summary>A height in the file is a whole number of these.</summary>
		internal const float Step = 0.05f;

		/// <summary>Side of the square, in game units.</summary>
		internal float Size = 1600f;

		/// <summary>Cells along a side; there is one more vertex than that in each direction.</summary>
		internal int Cells = 160;

		internal float[] Heights;

		internal float Cell { get { return Size / Cells; } }

		internal int Verts { get { return Cells + 1; } }

		internal void Ensure()
		{
			int n = Verts * Verts;
			if (Heights == null || Heights.Length != n)
			{
				Heights = new float[n];
			}
		}

		internal float At(int ix, int iz)
		{
			ix = Mathf.Clamp(ix, 0, Cells);
			iz = Mathf.Clamp(iz, 0, Cells);
			return Heights[iz * Verts + ix];
		}

		internal Vector3 Vertex(int ix, int iz)
		{
			float half = Size * 0.5f;
			return new Vector3(-half + ix * Cell, Heights[iz * Verts + ix], -half + iz * Cell);
		}
	}

	/// <summary>
	/// A map as data: the file <c>maps\&lt;name&gt;.json</c> in a mod folder (or the editor's own folder). The editor writes it and the game
	/// builds the same thing from it, so what you see in the editor is what you race on.
	/// </summary>
	internal sealed class MapDocument
	{
		internal const int Format = 1;

		internal string Id = "mymap";
		internal string Name = "My Map";

		/// <summary>One of <see cref="WeatherNames"/>.</summary>
		internal string Weather = "clear";

		/// <summary>Picture for the run map, a PNG next to the file (optional).</summary>
		internal string Icon = "";

		/// <summary>A 16-bit PCM wav next to the file, played as the race music (optional).</summary>
		internal string Music = "";

		internal readonly List<MapPoint> Track = new List<MapPoint>();
		internal readonly List<MapProp> Props = new List<MapProp>();
		internal MapTerrain Terrain = new MapTerrain();

		/// <summary>The start grid: spot 0 is the player's, the rest are one AI car each. Empty = the automatic grid behind the start line.</summary>
		internal readonly List<MapSpot> Spots = new List<MapSpot>();

		/// <summary>Where the start lights stand (only when <see cref="HasLights"/>; else they are put just behind the start line).</summary>
		internal bool HasLights;
		internal float LightX, LightZ;

		internal const int MaxSpots = 12;

		/// <summary>Where the file came from, so Save writes it back there. Not part of the file.</summary>
		internal string Path = "";

		internal static readonly string[] WeatherNames = { "clear", "rain", "fog", "snow", "ash", "night" };

		/// <summary>Limits that keep a hand-edited or damaged file from building something enormous.</summary>
		internal const int MaxPoints = 400;
		internal const int MaxProps = 3000;
		internal const float MaxCoord = 4000f;

		internal static string CleanId(string text)
		{
			var chars = new List<char>();
			foreach (char c in (text ?? "").ToLowerInvariant())
			{
				if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
				{
					chars.Add(c);
				}
				else if (c == ' ' || c == '.')
				{
					chars.Add('-');
				}
			}
			string id = new string(chars.ToArray()).Trim('-');
			return id.Length == 0 ? "map" : (id.Length > 40 ? id.Substring(0, 40) : id);
		}

		// ---- a new map -------------------------------------------------------------------------------------------------------

		/// <summary>An oval of twelve points, the start line at point 0 heading +X (the way the game's start grid faces).</summary>
		internal static MapDocument Oval(string id, string name, float radiusX = 420f, float radiusZ = 300f)
		{
			MapDocument doc = new MapDocument { Id = CleanId(id), Name = name };
			const int n = 12;
			for (int k = 0; k < n; k++)
			{
				float a = 2f * Mathf.PI * k / n;
				doc.Track.Add(new MapPoint { X = radiusX * Mathf.Sin(a), Y = 0f, Z = -radiusZ * Mathf.Cos(a), Width = 40f });
			}
			doc.Terrain.Ensure();
			return doc;
		}

		// ---- reading ---------------------------------------------------------------------------------------------------------

		/// <summary>Reads a map. Throws <see cref="FormatException"/> with a plain reason when the file is not a usable map.</summary>
		internal static MapDocument Parse(string json)
		{
			Dictionary<string, object> root = MiniJson.Parse(json) as Dictionary<string, object>;
			if (root == null)
			{
				throw new FormatException("the file is not a JSON object");
			}
			int format = (int)MiniJson.Number(root, "format", 1f);
			if (format > Format)
			{
				throw new FormatException("the map was made by a newer HalfGripTools (format " + format + ", this one reads " + Format + ")");
			}
			MapDocument doc = new MapDocument
			{
				Id = CleanId(MiniJson.String(root, "id", "map")),
				Name = Clip(MiniJson.String(root, "name", "Map"), 40),
				Weather = Pick(MiniJson.String(root, "weather", "clear"), WeatherNames),
				Icon = SafeFile(MiniJson.String(root, "icon")),
				Music = SafeFile(MiniJson.String(root, "music"))
			};

			foreach (object item in MiniJson.List(root, "track"))
			{
				Dictionary<string, object> o = item as Dictionary<string, object>;
				if (o == null)
				{
					continue;
				}
				if (doc.Track.Count >= MaxPoints)
				{
					throw new FormatException("the track has more than " + MaxPoints + " points");
				}
				doc.Track.Add(new MapPoint
				{
					X = Coord(MiniJson.Number(o, "x")),
					Y = Coord(MiniJson.Number(o, "y")),
					Z = Coord(MiniJson.Number(o, "z")),
					Width = Mathf.Clamp(MiniJson.Number(o, "width", 36f), 12f, 120f),
					Surface = Pick(MiniJson.String(o, "surface", "asphalt"), Surfaces.Names),
					RailLeft = MiniJson.Bool(o, "railLeft"),
					RailRight = MiniJson.Bool(o, "railRight"),
					WallLeft = MiniJson.Bool(o, "wallLeft"),
					WallRight = MiniJson.Bool(o, "wallRight")
				});
			}
			if (doc.Track.Count < 4)
			{
				throw new FormatException("the track needs at least 4 points (it has " + doc.Track.Count + ")");
			}

			foreach (object item in MiniJson.List(root, "props"))
			{
				Dictionary<string, object> o = item as Dictionary<string, object>;
				if (o == null || doc.Props.Count >= MaxProps)
				{
					continue;
				}
				string type = MiniJson.String(o, "type");
				if (Array.IndexOf(PropTypes.Names, type) < 0)
				{
					continue;
				}
				doc.Props.Add(new MapProp
				{
					Type = type,
					X = Coord(MiniJson.Number(o, "x")),
					Y = Coord(MiniJson.Number(o, "y")),
					Z = Coord(MiniJson.Number(o, "z")),
					Rot = MiniJson.Number(o, "rot"),
					Scale = Mathf.Clamp(MiniJson.Number(o, "scale", 1f), 0.25f, 6f)
				});
			}

			Dictionary<string, object> grid = root.ContainsKey("grid") ? root["grid"] as Dictionary<string, object> : null;
			if (grid != null)
			{
				foreach (object item in MiniJson.List(grid, "spots"))
				{
					Dictionary<string, object> o = item as Dictionary<string, object>;
					if (o != null && doc.Spots.Count < MaxSpots)
					{
						doc.Spots.Add(new MapSpot { X = Coord(MiniJson.Number(o, "x")), Z = Coord(MiniJson.Number(o, "z")) });
					}
				}
				Dictionary<string, object> lights = grid.ContainsKey("lights") ? grid["lights"] as Dictionary<string, object> : null;
				if (lights != null)
				{
					doc.HasLights = true;
					doc.LightX = Coord(MiniJson.Number(lights, "x"));
					doc.LightZ = Coord(MiniJson.Number(lights, "z"));
				}
			}

			Dictionary<string, object> t = root.ContainsKey("terrain") ? root["terrain"] as Dictionary<string, object> : null;
			if (t != null)
			{
				doc.Terrain.Size = Mathf.Clamp(MiniJson.Number(t, "size", 1600f), 200f, 4000f);
				doc.Terrain.Cells = Mathf.Clamp((int)MiniJson.Number(t, "cells", 160f), 16, 256);
				doc.Terrain.Heights = Unpack(MiniJson.String(t, "heights"), doc.Terrain.Verts * doc.Terrain.Verts);
			}
			doc.Terrain.Ensure();
			return doc;
		}

		private static float Coord(float v)
		{
			return float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Clamp(v, -MaxCoord, MaxCoord);
		}

		private static string Clip(string s, int max)
		{
			s = (s ?? "").Replace("\n", " ").Replace("\r", " ");
			return s.Length > max ? s.Substring(0, max) : s;
		}

		private static string Pick(string value, string[] allowed)
		{
			return Array.IndexOf(allowed, value) >= 0 ? value : allowed[0];
		}

		/// <summary>A file name inside the map's own folder: no path, no way out of it.</summary>
		private static string SafeFile(string name)
		{
			if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.Contains(".."))
			{
				return "";
			}
			return name;
		}

		private static float[] Unpack(string base64, int count)
		{
			float[] heights = new float[count];
			if (string.IsNullOrEmpty(base64))
			{
				return heights;
			}
			try
			{
				byte[] bytes = Convert.FromBase64String(base64);
				for (int i = 0; i < count && i * 2 + 1 < bytes.Length; i++)
				{
					heights[i] = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8)) * MapTerrain.Step;
				}
			}
			catch (FormatException)
			{
				// A damaged height field is a flat one; the track and props are still good.
			}
			return heights;
		}

		// ---- writing ---------------------------------------------------------------------------------------------------------

		internal string ToJson()
		{
			Dictionary<string, object> root = new Dictionary<string, object>
			{
				{ "format", Format },
				{ "id", Id },
				{ "name", Name },
				{ "weather", Weather }
			};
			if (Icon.Length > 0)
			{
				root["icon"] = Icon;
			}
			if (Music.Length > 0)
			{
				root["music"] = Music;
			}
			List<object> track = new List<object>();
			foreach (MapPoint p in Track)
			{
				Dictionary<string, object> o = new Dictionary<string, object>
				{
					{ "x", Round(p.X) }, { "y", Round(p.Y) }, { "z", Round(p.Z) }, { "width", Round(p.Width) }, { "surface", p.Surface }
				};
				if (p.RailLeft) o["railLeft"] = true;
				if (p.RailRight) o["railRight"] = true;
				if (p.WallLeft) o["wallLeft"] = true;
				if (p.WallRight) o["wallRight"] = true;
				track.Add(o);
			}
			root["track"] = track;
			List<object> props = new List<object>();
			foreach (MapProp p in Props)
			{
				props.Add(new Dictionary<string, object>
				{
					{ "type", p.Type }, { "x", Round(p.X) }, { "y", Round(p.Y) }, { "z", Round(p.Z) }, { "rot", Round(p.Rot) }, { "scale", Round(p.Scale) }
				});
			}
			root["props"] = props;
			if (Spots.Count > 0 || HasLights)
			{
				Dictionary<string, object> grid = new Dictionary<string, object>();
				if (Spots.Count > 0)
				{
					grid["spots"] = Spots.Select(s => (object)new Dictionary<string, object> { { "x", Round(s.X) }, { "z", Round(s.Z) } }).ToList();
				}
				if (HasLights)
				{
					grid["lights"] = new Dictionary<string, object> { { "x", Round(LightX) }, { "z", Round(LightZ) } };
				}
				root["grid"] = grid;
			}
			Terrain.Ensure();
			root["terrain"] = new Dictionary<string, object>
			{
				{ "size", Round(Terrain.Size) }, { "cells", Terrain.Cells }, { "heights", Pack(Terrain.Heights) }
			};
			return MiniJson.Write(root);
		}

		private static double Round(float v)
		{
			return Math.Round(v, 2);
		}

		private static string Pack(float[] heights)
		{
			byte[] bytes = new byte[heights.Length * 2];
			for (int i = 0; i < heights.Length; i++)
			{
				short s = (short)Mathf.Clamp(Mathf.RoundToInt(heights[i] / MapTerrain.Step), short.MinValue, short.MaxValue);
				bytes[i * 2] = (byte)(s & 0xff);
				bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
			}
			return Convert.ToBase64String(bytes);
		}

		/// <summary>Takes everything but the id, name and file from another map (undo and redo put a saved state back into the map the editor holds).</summary>
		internal void CopyFrom(MapDocument other)
		{
			Weather = other.Weather;
			Icon = other.Icon;
			Music = other.Music;
			Track.Clear();
			Track.AddRange(other.Track);
			Props.Clear();
			Props.AddRange(other.Props);
			Spots.Clear();
			Spots.AddRange(other.Spots);
			HasLights = other.HasLights;
			LightX = other.LightX;
			LightZ = other.LightZ;
			Terrain = other.Terrain;
		}

		internal MapDocument Clone()
		{
			MapDocument copy = Parse(ToJson());
			copy.Path = Path;
			return copy;
		}
	}

	/// <summary>One stretch of the track line, sampled: where, which way, how wide and what it is made of.</summary>
	internal struct Sample
	{
		internal Vector3 Pos;
		internal Vector3 Along;     // flat, unit
		internal Vector3 Right;     // flat, unit, to the right of the direction of travel
		internal float Half;        // half the width
		internal int Control;       // index of the control point this stretch starts at
		internal string Surface;
		internal bool RailLeft, RailRight, WallLeft, WallRight;
	}

	/// <summary>
	/// The track line as a closed Catmull-Rom spline through the control points (uniform, which suits points spread evenly), sampled every
	/// <c>spacing</c> units. Geometry, rails and the lap line all come from these samples, so they agree with each other.
	/// </summary>
	internal static class Spline
	{
		internal static List<Sample> Sample(MapDocument doc, float spacing)
		{
			List<MapPoint> c = doc.Track;
			int n = c.Count;
			List<Sample> samples = new List<Sample>();
			for (int i = 0; i < n; i++)
			{
				MapPoint p0 = c[(i + n - 1) % n], p1 = c[i], p2 = c[(i + 1) % n], p3 = c[(i + 2) % n];
				float chord = Vector3.Distance(p1.Pos, p2.Pos);
				int steps = Mathf.Max(2, Mathf.CeilToInt(chord / Mathf.Max(1f, spacing)));
				for (int s = 0; s < steps; s++)
				{
					float t = s / (float)steps;
					Vector3 pos = CatmullRom(p0.Pos, p1.Pos, p2.Pos, p3.Pos, t);
					samples.Add(new Sample
					{
						Pos = pos,
						Half = Mathf.Lerp(p1.Width, p2.Width, t) * 0.5f,
						Control = i,
						Surface = p1.Surface,
						RailLeft = p1.RailLeft,
						RailRight = p1.RailRight,
						WallLeft = p1.WallLeft,
						WallRight = p1.WallRight
					});
				}
			}
			int count = samples.Count;
			for (int i = 0; i < count; i++)
			{
				Vector3 a = samples[(i + count - 1) % count].Pos;
				Vector3 b = samples[(i + 1) % count].Pos;
				Vector3 along = b - a;
				along.y = 0f;
				along = along.sqrMagnitude < 1e-6f ? Vector3.right : along.normalized;
				Sample s = samples[i];
				s.Along = along;
				s.Right = new Vector3(along.z, 0f, -along.x);
				samples[i] = s;
			}
			return samples;
		}

		private static Vector3 CatmullRom(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
		{
			float t2 = t * t, t3 = t2 * t;
			return 0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
		}

		internal static float Length(List<Sample> samples)
		{
			float total = 0f;
			for (int i = 0; i < samples.Count; i++)
			{
				total += Vector3.Distance(samples[i].Pos, samples[(i + 1) % samples.Count].Pos);
			}
			return total;
		}
	}

	/// <summary>The things a map can have standing on it.</summary>
	internal static class PropTypes
	{
		internal static readonly string[] Names = { "tree", "cone", "barrel", "block", "ramp", "arch" };
	}
}
