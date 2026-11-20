using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HalfGripTools.MapBuild;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripTools
{
	/// <summary>
	/// What a map built in the Stadium scene has to do to be raced: hide Stadium's own scenery, give the lap counter and the AI the map's track
	/// line, put the start grid on the map, and spread the hazard spots. Highway does the same by hand; a map made from a file gets it from here.
	/// </summary>
	internal static class MapRace
	{
		private static readonly List<GameObject> hidden = new List<GameObject>();
		private static Collider rainPlane;

		/// <summary>Puts back what the map hid. The scene is usually gone by then; this is for a map switched off in a running race.</summary>
		internal static void Restore()
		{
			foreach (GameObject go in hidden)
			{
				if (go != null)
				{
					go.SetActive(true);
				}
			}
			hidden.Clear();
			if (rainPlane != null)
			{
				rainPlane.enabled = true;
			}
			rainPlane = null;
		}

		internal static void HideStadium(GameObject env)
		{
			Restore();
			foreach (string name in new[] { "Ground", "Track", "Decoration/Outside", "Decoration/Bridge", "Decoration/TowerDamaged" })
			{
				Transform t = env.transform.Find(name);
				if (t != null && t.gameObject.activeSelf)
				{
					t.gameObject.SetActive(false);
					hidden.Add(t.gameObject);
				}
			}
			Transform rain = env.transform.Find("Weather/ParticleColliderPlane");
			if (rain != null && rain.TryGetComponent(out rainPlane))
			{
				rainPlane.enabled = false;
			}
		}

		/// <summary>Builds a map made from a file into the race scene that has just loaded.</summary>
		internal static void BuildDocument(Scene scene, TrackManager tm, MapInfo info, MapDocument doc)
		{
			GameObject env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ENVIROMENT");
			if (env == null)
			{
				throw new InvalidOperationException("this scene has no ENVIROMENT object, so it is not the Stadium scene");
			}
			HideStadium(env);
			List<Sample> start = Spline.Sample(doc, 12f);
			var world = new MapWorld(doc, env, tm);
			// The game's start grid faces +X and puts the cars at height 1, so the map is turned and lowered until its start line is like that.
			world.BuildAll(MapWorld.FrameFor(doc), -start[0].Pos.y);
			MapRegistry.Current = world;
			Rewire(tm, world);
			PlaceGrid(tm, env, world);
			SpreadHazards(world);
			GridCheck(tm);
			ModRuntime.Log.LogInfo("Map " + doc.Name + " built: " + doc.Track.Count + " track points, " + world.Dense.Count + " samples, lap " + Spline.Length(world.Dense).ToString("0") + " units, " + doc.Props.Count + " props");

			if (MapMaker.Enabled)
			{
				// HALFGRIPTOOLS_MAPSHOT (a test aid: the editor draws pictures of the map into that folder) opens it on every map made from a file.
				bool open = MapMaker.OpenNext || (info.Id == MapLibrary.MakerId && !MapMaker.Testing) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALFGRIPTOOLS_MAPSHOT"));
				MapMaker.OpenNext = false;
				MapEditor.Attach(world, doc, tm, info, open);
			}
		}

		/// <summary>Logs how many of the grid places (and the player's) have the map's road under them. A grid off the road is the first thing to look for when a map does not race.</summary>
		private static void GridCheck(TrackManager tm)
		{
			try
			{
				int onRoad = 0, total = 0;
				var places = new List<Vector3> { tm.playerCarController.transform.position };
				foreach (Transform slot in tm.startingPositionsContainer.transform)
				{
					if (slot.gameObject.activeSelf && slot.name != "PlayerPosition")
					{
						places.Add(slot.position);
					}
				}
				foreach (Vector3 p in places)
				{
					total++;
					RaycastHit hit = default(RaycastHit);
					bool found = false;
					RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
					Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
					foreach (RaycastHit candidate in hits)
					{
						// The car that stands there is not what it stands on.
						if (candidate.collider.GetComponentInParent<CarControllerNew>() != null)
						{
							continue;
						}
						hit = candidate;
						found = true;
						break;
					}
					if (found && hit.collider.name.StartsWith("road-"))
					{
						onRoad++;
					}
					else
					{
						ModRuntime.Log.LogInfo("Map grid check: " + p + " has " + (found ? hit.collider.name + " (" + hit.collider.transform.root.name + ") at " + hit.point : "nothing") + " under it, not the road");
					}
				}
				ModRuntime.Log.LogInfo("Map grid check: " + onRoad + " of " + total + " places (the player's first) have the road under them; the player is at " + tm.playerCarController.transform.position);
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Map grid check failed: " + e.Message);
			}
		}

		/// <summary>Replaces Stadium's track line with the map's lap, and tells the lap counter, the AI and the scatter about it.</summary>
		internal static void Rewire(TrackManager tm, MapWorld world)
		{
			LapManager lap = tm.lapManager;
			PathManager line = lap.trackLine;
			foreach (Transform child in line.transform.Cast<Transform>().ToList())
			{
				child.SetParent(null);
				UnityEngine.Object.Destroy(child.gameObject);
			}
			// A control point about every 30 units: the game's own spline through them follows the map's closely.
			int k = 0;
			for (int i = 0; i < world.Dense.Count; i += 5)
			{
				Sample s = world.Dense[i];
				GameObject point = new GameObject("HG" + (++k));
				point.transform.SetParent(line.transform, true);
				point.transform.position = world.W(s.Pos);
				// The scale is data: x the right half-width, y the grip, z the left half-width.
				point.transform.localScale = new Vector3(s.Half, 1f, s.Half);
				point.AddComponent<PathControlPointEditorController>();
			}
			MethodInfo init = typeof(PathManager).GetMethod("InitializePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			init.Invoke(line, null);
			Reflect.SetField(line, "snapLanesToGround", false);
			line.BakeRacingLine();

			// The other track lines belong to Stadium.
			lap.enemyTrackLines = new List<PathManager> { line };
			foreach (PathManager other in UnityEngine.Object.FindObjectsByType<PathManager>(FindObjectsSortMode.None))
			{
				if (other != line)
				{
					other.gameObject.SetActive(false);
					hidden.Add(other.gameObject);
				}
			}
			TrackScatter scatter = UnityEngine.Object.FindFirstObjectByType<TrackScatter>();
			if (scatter != null)
			{
				scatter.path = line;
			}
			ModRuntime.Log.LogInfo("Map track line: " + k + " control points, " + line.sampledPoints.Count + " samples, " + line.trackLength.ToString("0") + " units, " + line.lanes.Count + " lanes");
		}

		/// <summary>Where a car stands <paramref name="distance"/> units behind the start line, on the line, in the document's space.</summary>
		private static Sample Behind(MapWorld world, float distance)
		{
			List<Sample> d = world.Dense;
			int n = d.Count;
			float travelled = 0f;
			int i = 0;
			while (travelled < distance && n > 1)
			{
				int prev = (i + n - 1) % n;
				float step = Vector3.Distance(d[i].Pos, d[prev].Pos);
				if (travelled + step >= distance)
				{
					float t = (distance - travelled) / Mathf.Max(0.001f, step);
					Sample a = d[i];
					a.Pos = Vector3.Lerp(d[i].Pos, d[prev].Pos, t);
					return a;
				}
				travelled += step;
				i = prev;
			}
			return d[i];
		}

		/// <summary>The grid: six places on two columns behind the start line, the player's the last.</summary>
		internal static void PlaceGrid(TrackManager tm, GameObject env, MapWorld world)
		{
			Transform container = tm.startingPositionsContainer.transform;
			Transform playerSlot = container.Cast<Transform>().FirstOrDefault(t => t.name == "PlayerPosition");
			CarControllerNew car = tm.playerCarController;
			float lift = playerSlot != null ? car.transform.position.y - playerSlot.position.y : 0.5f;

			MapDocument doc = world.Doc;
			bool custom = doc.Spots.Count > 0;
			var others = container.Cast<Transform>().Where(t => t != playerSlot).ToList();
			if (custom)
			{
				// One AI car comes out of each place that is switched on, so more places are made by copying one.
				while (others.Count < doc.Spots.Count - 1 && others.Count > 0)
				{
					GameObject copy = UnityEngine.Object.Instantiate(others[0].gameObject, container);
					copy.name = others[0].name;
					others.Add(copy.transform);
				}
			}
			int n = 0;
			foreach (Transform slot in others)
			{
				if (n >= (custom ? doc.Spots.Count - 1 : 5))
				{
					slot.gameObject.SetActive(false);
					continue;
				}
				if (custom)
				{
					slot.gameObject.SetActive(true);
				}
				slot.position = custom ? SpotAt(world, doc.Spots[n + 1]) : SlotAt(world, 26f * (n + 1), n % 2 == 0 ? -1 : 1);
				n++;
			}
			Vector3 playerAt = custom ? SpotAt(world, doc.Spots[0]) : SlotAt(world, 26f * 6f, -1);
			if (playerSlot != null)
			{
				playerSlot.position = playerAt;
			}

			AudioListener listener = car.GetComponentInChildren<AudioListener>(true);
			bool listenerOn = listener != null && listener.enabled;
			if (listenerOn)
			{
				listener.enabled = false;
			}
			Vector3 target = playerAt + Vector3.up * lift;
			car.transform.position = target;
			Rigidbody body = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			if (body != null)
			{
				body.position = target;
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}
			if (listenerOn)
			{
				listener.enabled = true;
			}
			Physics.SyncTransforms();

			Transform semaphore = env.transform.Find("RaceStarterSemaphore");
			if (semaphore != null)
			{
				Vector3 at = doc.HasLights ? SpotAt(world, new MapSpot { X = doc.LightX, Z = doc.LightZ }) : SlotAt(world, 22f, 0);
				semaphore.position = new Vector3(at.x, semaphore.position.y, at.z);
			}
		}

		private static Vector3 SpotAt(MapWorld world, MapSpot spot)
		{
			Vector3 w = world.W(new Vector3(spot.X, 0f, spot.Z));
			return new Vector3(w.x, 0f, w.z);
		}

		/// <summary>The automatic grid in the map's own space, for the editor to show and to start a custom one from: the player's place first, then the AI cars'. Also where the lights go.</summary>
		internal static List<MapSpot> AutoGrid(MapWorld world, out MapSpot lights)
		{
			var spots = new List<MapSpot>();
			Sample player = Behind(world, 26f * 6f);
			spots.Add(new MapSpot { X = (player.Pos - player.Right * player.Half * 0.42f).x, Z = (player.Pos - player.Right * player.Half * 0.42f).z });
			for (int n = 0; n < 5; n++)
			{
				Sample s = Behind(world, 26f * (n + 1));
				Vector3 p = s.Pos + s.Right * ((n % 2 == 0 ? -1 : 1) * s.Half * 0.42f);
				spots.Add(new MapSpot { X = p.x, Z = p.z });
			}
			Sample l = Behind(world, 22f);
			lights = new MapSpot { X = l.Pos.x, Z = l.Pos.z };
			return spots;
		}

		/// <summary>A place behind the start line: <paramref name="side"/> -1 and 1 are the two columns, 0 the middle. In the world, on the road (the start line is at height 0).</summary>
		private static Vector3 SlotAt(MapWorld world, float behind, int side)
		{
			Sample s = Behind(world, behind);
			Vector3 pos = s.Pos + s.Right * (side * s.Half * 0.42f);
			Vector3 w = world.W(pos);
			return new Vector3(w.x, 0f, w.z);
		}

		/// <summary>Chicken and trap spots all round the lap, away from the grid.</summary>
		internal static void SpreadHazards(MapWorld world)
		{
			HazardsSpawner_FINAL spawner = UnityEngine.Object.FindFirstObjectByType<HazardsSpawner_FINAL>();
			if (spawner == null)
			{
				return;
			}
			var rng = new System.Random(77);
			var spots = new List<GameObject>();
			int n = world.Dense.Count;
			int count = Mathf.Clamp(n / 12, 12, 60);
			for (int i = 0; i < count; i++)
			{
				Sample s = world.Dense[Mathf.Clamp(Mathf.RoundToInt(n * 0.12f + (n * 0.8f) * i / (count - 1)), 0, n - 1)];
				GameObject spot = new GameObject("HazardSpot" + i);
				spot.transform.SetParent(world.Root.transform, false);
				spot.transform.position = world.W(s.Pos + s.Right * (float)(rng.NextDouble() * s.Half - s.Half * 0.5f));
				spots.Add(spot);
			}
			Reflect.SetField(spawner, "spawnPositions", spots);
		}
	}
}
