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
	/// The Map Maker. It lives inside a race on a map made from a file: time is held, a free camera takes over the view, and what you change
	/// is changed in the map that is standing there, so what you see is what you race on. Nothing leaves the editor except by SAVE, which
	/// writes the map's file; TEST DRIVE saves, then reloads the race so the game lays out its start grid and AI on the changed map.
	/// </summary>
	internal static class MapEditor
	{
		private static EditorBehaviour current;

		internal static void Attach(MapWorld world, MapDocument doc, TrackManager tm, MapInfo info, bool open)
		{
			Close();
			MapMaker.Reloading = false;
			GameObject go = new GameObject("HalfGripMapEditor");
			current = go.AddComponent<EditorBehaviour>();
			current.Init(world, doc, tm, info);
			if (open)
			{
				current.Open();
			}
		}

		internal static void Close()
		{
			if (current != null)
			{
				current.Shutdown();
				UnityEngine.Object.Destroy(current.gameObject);
			}
			current = null;
		}
	}

	internal sealed class EditorBehaviour : MonoBehaviour
	{
		private enum Tool { Track, Props, Terrain, Map, Start }

		private MapWorld world;
		private MapDocument doc;
		private TrackManager tm;
		private MapInfo info;

		private bool open;
		private Camera cam;
		private float yaw, pitch = 25f, speed = 140f;
		private bool topDown;
		private CursorLockMode savedLock;
		private bool savedVisible;

		private Tool tool = Tool.Track;
		private int selPoint = -1, selProp = -1;
		private bool dragging;
		private float dragHeight;
		private string propType = "tree";
		private float propRot, propScale = 1f;
		private int brushMode;          // 0 raise, 1 lower, 2 smooth, 3 flatten
		private float brushRadius = 90f, brushStrength = 0.6f;
		private float flattenTo;
		private bool painting;
		private float nextPaint;

		private bool dirtyTrack, dirtyTerrain, dirtyProps;
		private float rebuildAt;

		private string status = "";
		private float statusUntil;
		private string nameField = "";
		private string saveAsField = "";
		private bool armedNew;
		private bool menuOpen;

		// the start grid: the automatic one (shown until you change it) and what is selected
		private List<MapSpot> autoSpots = new List<MapSpot>();
		private MapSpot autoLights = new MapSpot();
		private int selSpot = -1;
		private bool selLights;

		// undo: the file text of the map as it stood after the last change that had settled
		private readonly List<string> undoStack = new List<string>();
		private readonly List<string> redoStack = new List<string>();
		private string stable = "";
		private float nextSettle;
		private const int MaxUndo = 60;

		/// <summary>The name of the text box that has the keyboard, as the last OnGUI saw it ("" when none). While one has it, keys are typing, not flying.</summary>
		private string focused = "";

		private Texture2D white;
		private GUIStyle small;
		private Rect leftPanel, bottomPanel;

		private static readonly string[] ToolNames = { "TRACK", "PROPS", "TERRAIN", "MAP", "START" };
		private static readonly string[] BrushNames = { "RAISE", "LOWER", "SMOOTH", "FLATTEN" };

		internal void Init(MapWorld w, MapDocument d, TrackManager t, MapInfo i)
		{
			world = w;
			doc = d;
			tm = t;
			info = i;
			nameField = d.Name;
			stable = d.ToJson();
			RefreshAuto();
			saveAsField = d.Id == MapLibrary.MakerId ? "" : d.Name;
			white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
			white.SetPixel(0, 0, Color.white);
			white.Apply();
		}

		// ---- opening and closing -------------------------------------------------------------------------------------------------

		internal void Open()
		{
			if (open)
			{
				return;
			}
			open = true;
			TimeScale.Hold(this, true);
			GameInput.Mute(this, true);
			savedLock = Cursor.lockState;
			savedVisible = Cursor.visible;
			FreeCursor();
			// The game's hand pointer (Grabber) keeps its own cursor in the middle of the screen and locks the real one: it sleeps while the editor is open.
			foreach (Grabber grabber in FindObjectsByType<Grabber>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				if (grabber.enabled)
				{
					grabber.enabled = false;
					sleepingGrabbers.Add(grabber);
				}
			}

			var go = new GameObject("MapEditorCamera");
			cam = go.AddComponent<Camera>();
			// Above every camera the race has (the interior camera draws the cockpit on top of the world camera), drawing the sky itself.
			cam.depth = 1000f;
			cam.clearFlags = CameraClearFlags.Skybox;
			cam.nearClipPlane = 1f;
			cam.farClipPlane = 9000f;
			cam.fieldOfView = 70f;
			Sample s0 = world.Dense[0];
			Vector3 along = world.Frame * s0.Along;
			cam.transform.position = world.W(s0.Pos) - along * 220f + Vector3.up * 130f;
			yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
			pitch = 24f;
			Say("MAP MAKER: right mouse look, WASD fly, Q/E down/up, shift faster, T top view");
			ModRuntime.Log.LogInfo("Map Maker opened on " + doc.Name + " (time held, free camera). File: " + (doc.Path.Length > 0 ? doc.Path : "none"));
			string shots = Environment.GetEnvironmentVariable("HALFGRIPTOOLS_MAPSHOT");
			if (!string.IsNullOrEmpty(shots))
			{
				StartCoroutine(Shots(shots));
			}
		}

		/// <summary>For testing without a window: renders the editor's camera into PNG files in the folder the environment variable names.</summary>
		private System.Collections.IEnumerator Shots(string folder)
		{
			yield return new WaitForSecondsRealtime(4f);
			try
			{
				Directory.CreateDirectory(folder);
				Shot(Path.Combine(folder, "editor_fly_" + doc.Id + ".png"));
				Vector3 lo = new Vector3(float.MaxValue, 0f, float.MaxValue), hi = new Vector3(float.MinValue, 0f, float.MinValue);
				foreach (Sample sample in world.Dense)
				{
					Vector3 w = world.W(sample.Pos);
					lo = Vector3.Min(lo, w);
					hi = Vector3.Max(hi, w);
				}
				Vector3 centre = (lo + hi) * 0.5f;
				float extent = Mathf.Max(hi.x - lo.x, hi.z - lo.z);
				cam.transform.position = new Vector3(centre.x, extent * 0.95f, centre.z);
				// A high camera needs a farther near plane or the depth buffer cannot tell the road from the ground under it.
				cam.nearClipPlane = 12f;
				topDown = true;
				yaw = 0f;
				cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Map Maker: shot failed: " + e.Message);
				yield break;
			}
			yield return new WaitForSecondsRealtime(0.5f);
			try
			{
				Shot(Path.Combine(folder, "editor_top_" + doc.Id + ".png"));
				// Close-ups of the first stretch of each kind of thing the map has, seen from behind and above.
				cam.nearClipPlane = 1f;
				topDown = false;
				CloseUp(folder, "rail", p => p.RailLeft || p.RailRight);
				CloseUp(folder, "wall", p => p.WallLeft || p.WallRight);
				CloseUp(folder, "dirt", p => p.Surface != "asphalt");
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Map Maker: shot failed: " + e.Message);
			}
		}

		private void CloseUp(string folder, string name, Func<MapPoint, bool> wanted)
		{
			int index = doc.Track.FindIndex(p => wanted(p));
			if (index < 0)
			{
				return;
			}
			Sample at = world.Dense.FirstOrDefault(s => s.Control == index);
			Vector3 along = world.Frame * at.Along;
			cam.transform.position = world.W(at.Pos) - along * 55f + Vector3.up * 28f + world.Frame * at.Right * (at.Half * 0.5f);
			cam.transform.rotation = Quaternion.LookRotation(along * 70f + Vector3.down * 16f, Vector3.up);
			Shot(Path.Combine(folder, "editor_" + name + "_" + doc.Id + ".png"));
		}

		private void Shot(string path)
		{
			var rt = new RenderTexture(1280, 720, 24);
			cam.targetTexture = rt;
			cam.Render();
			RenderTexture.active = rt;
			var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
			tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
			tex.Apply();
			File.WriteAllBytes(path, tex.EncodeToPNG());
			cam.targetTexture = null;
			RenderTexture.active = null;
			Destroy(rt);
			Destroy(tex);
			ModRuntime.Log.LogInfo("Map Maker: wrote " + path);
		}

		internal void Shutdown()
		{
			if (!open)
			{
				return;
			}
			open = false;
			ModRuntime.Log.LogInfo("Map Maker closed (time released)");
			TimeScale.Hold(this, false);
			GameInput.Mute(this, false);
			Cursor.lockState = savedLock;
			Cursor.visible = savedVisible;
			foreach (Grabber grabber in sleepingGrabbers)
			{
				if (grabber != null)
				{
					grabber.enabled = true;
				}
			}
			sleepingGrabbers.Clear();
			if (cam != null)
			{
				Destroy(cam.gameObject);
			}
		}

		private readonly List<Grabber> sleepingGrabbers = new List<Grabber>();

		/// <summary>An ordinary mouse pointer, free to go anywhere on the screen like in a menu.</summary>
		private static void FreeCursor()
		{
			if (Cursor.lockState != CursorLockMode.None)
			{
				Cursor.lockState = CursorLockMode.None;
			}
			if (!Cursor.visible)
			{
				Cursor.visible = true;
			}
		}

		/// <summary>The race's own scripts lock the cursor when they wake up and when the pause closes: the editor takes it back every frame, after them.</summary>
		private void LateUpdate()
		{
			if (open)
			{
				FreeCursor();
			}
		}

		private void OnDestroy()
		{
			Shutdown();
			if (!MapMaker.Reloading)
			{
				MapMaker.EndStandalone();
			}
			if (white != null)
			{
				Destroy(white);
			}
		}

		private void Say(string text)
		{
			status = text;
			statusUntil = Time.unscaledTime + 6f;
		}

		/// <summary>Loads the race scene again: the game lays out its start grid, AI and lap line on the map as it is now.</summary>
		private void Reload()
		{
			MapMaker.RestartRace();
		}

		// ---- every frame -------------------------------------------------------------------------------------------------------------

		private void Update()
		{
			if (!open)
			{
				// In a race on a map made from a file, the key takes you into the editor (the race is loaded again with the editor open).
				if (Input.GetKeyDown(MapMaker.Key) && MapMaker.Enabled)
				{
					MapMaker.OpenNext = true;
					MapMaker.Testing = false;
					Reload();
				}
				return;
			}
			if (cam == null)
			{
				return;
			}
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				menuOpen = !menuOpen;
				GUI.FocusControl("");
			}
			FreeCursor();
			if (menuOpen)
			{
				return;
			}
			bool typing = focused == "nameField" || focused == "saveAsField";
			Flying(typing);
			if (typing)
			{
				return;
			}
			if (Input.GetKeyDown(KeyCode.T))
			{
				topDown = !topDown;
				if (topDown)
				{
					pitch = 89f;
				}
			}
			if (Input.GetKeyDown(KeyCode.Alpha1)) tool = Tool.Track;
			if (Input.GetKeyDown(KeyCode.Alpha2)) tool = Tool.Props;
			if (Input.GetKeyDown(KeyCode.Alpha3)) tool = Tool.Terrain;
			if (Input.GetKeyDown(KeyCode.Alpha4)) tool = Tool.Map;
			if (Input.GetKeyDown(KeyCode.Alpha5)) tool = Tool.Start;
			bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
			if (ctrl && Input.GetKeyDown(KeyCode.Z))
			{
				if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Redo(); else Undo();
			}
			if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();

			bool overUi = IsOverUi() || GUIUtility.hotControl != 0;
			// A click on a track point, a prop, a start spot or the lights picks it up whichever of those tools is showing.
			if (!dragging && !overUi && Input.GetMouseButtonDown(0) && (tool == Tool.Track || tool == Tool.Props || tool == Tool.Start))
			{
				Tool wanted = ToolUnderMouse();
				if (wanted != tool)
				{
					tool = wanted;
				}
			}
			switch (tool)
			{
				case Tool.Track: TrackTool(overUi); break;
				case Tool.Props: PropsTool(overUi); break;
				case Tool.Terrain: TerrainTool(overUi); break;
				case Tool.Start: StartTool(overUi); break;
			}
			Flush();
			UndoTick();
		}

		private void Flying(bool typing)
		{
			float dt = Time.unscaledDeltaTime;
			if (Input.GetMouseButton(1))
			{
				yaw += Input.GetAxis("Mouse X") * 2.2f;
				pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2.2f, -89f, 89f);
			}
			float scroll = Input.mouseScrollDelta.y;
			if (Mathf.Abs(scroll) > 0.01f && !IsOverUi())
			{
				if (topDown)
				{
					Vector3 p = cam.transform.position;
					p.y = Mathf.Clamp(p.y * Mathf.Pow(0.88f, scroll), 20f, 3000f);
					cam.transform.position = p;
				}
				else
				{
					speed = Mathf.Clamp(speed * Mathf.Pow(1.2f, scroll), 15f, 1500f);
				}
			}
			if (!typing)
			{
				Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
				Vector3 forward = topDown ? Quaternion.Euler(0f, yaw, 0f) * Vector3.forward : look * Vector3.forward;
				Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
				Vector3 move = Vector3.zero;
				if (Input.GetKey(KeyCode.W)) move += forward;
				if (Input.GetKey(KeyCode.S)) move -= forward;
				if (Input.GetKey(KeyCode.D)) move += right;
				if (Input.GetKey(KeyCode.A)) move -= right;
				if (!topDown)
				{
					if (Input.GetKey(KeyCode.E)) move += Vector3.up;
					if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
				}
				float boost = Input.GetKey(KeyCode.LeftShift) ? 4f : (Input.GetKey(KeyCode.LeftControl) ? 0.25f : 1f);
				float s = topDown ? Mathf.Max(60f, cam.transform.position.y * 1.2f) : speed;
				cam.transform.position += move * (s * boost * dt);
			}
			cam.transform.rotation = Quaternion.Euler(topDown ? 90f : pitch, yaw, 0f);
		}

		/// <summary>The world point under the mouse: the first piece of the map the ray meets, else the flat plane at the map's base height.</summary>
		private bool Pick(out Vector3 worldPoint, bool ignoreProps = false)
		{
			Transform propsRoot = ignoreProps ? world.PropsRoot : null;
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			RaycastHit[] hits = Physics.RaycastAll(ray, 9000f, ~0, QueryTriggerInteraction.Ignore);
			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
			foreach (RaycastHit hit in hits)
			{
				if (hit.collider != null && hit.collider.transform.IsChildOf(world.Root.transform) && hit.collider.name != "void floor"
					&& (propsRoot == null || !hit.collider.transform.IsChildOf(propsRoot)))
				{
					worldPoint = hit.point;
					return true;
				}
			}
			Plane plane = new Plane(Vector3.up, new Vector3(0f, world.Root.transform.position.y, 0f));
			float enter;
			if (plane.Raycast(ray, out enter))
			{
				worldPoint = ray.GetPoint(enter);
				return true;
			}
			worldPoint = Vector3.zero;
			return false;
		}

		/// <summary>Where the mouse ray crosses the horizontal plane at <paramref name="height"/> (world), for dragging.</summary>
		private bool PickPlane(float height, out Vector3 worldPoint)
		{
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			float enter;
			if (new Plane(Vector3.up, new Vector3(0f, height, 0f)).Raycast(ray, out enter))
			{
				worldPoint = ray.GetPoint(enter);
				return true;
			}
			worldPoint = Vector3.zero;
			return false;
		}

		private bool ToScreen(Vector3 worldPoint, out Vector2 gui)
		{
			Vector3 s = cam.WorldToScreenPoint(worldPoint);
			float k = UiScale();
			gui = new Vector2(s.x / k, (Screen.height - s.y) / k);
			return s.z > 1f;
		}

		private float UiScale()
		{
			return Mathf.Clamp(Screen.height / 720f, 1f, 2.5f);
		}

		private bool IsOverUi()
		{
			float k = UiScale();
			Vector2 m = new Vector2(Input.mousePosition.x / k, (Screen.height - Input.mousePosition.y) / k);
			return leftPanel.Contains(m) || bottomPanel.Contains(m);
		}

		private float Clamp(float v)
		{
			float limit = doc.Terrain.Size * 0.5f - 30f;
			return Mathf.Clamp(v, -limit, limit);
		}

		// ---- the track tool --------------------------------------------------------------------------------------------------------

		private int NearestPoint(float within)
		{
			Vector2 mouse = new Vector2(Input.mousePosition.x / UiScale(), (Screen.height - Input.mousePosition.y) / UiScale());
			int best = -1;
			float bestD = within;
			for (int i = 0; i < doc.Track.Count; i++)
			{
				Vector2 g;
				if (ToScreen(world.W(doc.Track[i].Pos), out g) && Vector2.Distance(g, mouse) < bestD)
				{
					bestD = Vector2.Distance(g, mouse);
					best = i;
				}
			}
			return best;
		}

		private void TrackTool(bool overUi)
		{
			if (dragging)
			{
				if (!Input.GetMouseButton(0) || selPoint < 0 || selPoint >= doc.Track.Count)
				{
					dragging = false;
					dirtyTrack = true;
					rebuildAt = 0f;
				}
				else
				{
					Vector3 at;
					if (PickPlane(dragHeight, out at))
					{
						Vector3 d = world.D(at);
						MapPoint p = doc.Track[selPoint];
						p.X = Clamp(d.x);
						p.Z = Clamp(d.z);
						dirtyTrack = true;
					}
				}
				return;
			}
			if (selPoint >= 0 && selPoint < doc.Track.Count)
			{
				if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
				{
					DeletePoint();
				}
				float step = Input.GetKey(KeyCode.LeftShift) ? 5f : 1f;
				if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.RightBracket)) Raise(step);
				if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.LeftBracket)) Raise(-step);
			}
			if (!Input.GetMouseButtonDown(0) || overUi)
			{
				return;
			}
			int hit = NearestPoint(16f);
			if (hit >= 0)
			{
				selPoint = hit;
				dragging = true;
				dragHeight = world.W(doc.Track[hit].Pos).y;
				return;
			}
			Vector3 ground;
			if (!Pick(out ground))
			{
				return;
			}
			if (doc.Track.Count >= MapDocument.MaxPoints)
			{
				Say("The track already has the most points it can (" + MapDocument.MaxPoints + ").");
				return;
			}
			Vector3 pos = world.D(ground);
			MapPoint added = new MapPoint { X = Clamp(pos.x), Y = pos.y, Z = Clamp(pos.z) };
			// Between its neighbours if the click is on the road, else at the end of the track (before the start, closing the loop).
			int insertAt = doc.Track.Count;
			float near = float.MaxValue;
			foreach (Sample s in world.Dense)
			{
				float d = Vector2.Distance(new Vector2(s.Pos.x, s.Pos.z), new Vector2(pos.x, pos.z));
				if (d < near)
				{
					near = d;
					insertAt = s.Control + 1;
				}
			}
			MapPoint basis = doc.Track[Mathf.Clamp(insertAt - 1, 0, doc.Track.Count - 1)];
			if (near > basis.Width * 0.5f + 12f)
			{
				insertAt = doc.Track.Count;
				basis = doc.Track[doc.Track.Count - 1];
			}
			added.Width = basis.Width;
			added.Surface = basis.Surface;
			added.RailLeft = basis.RailLeft;
			added.RailRight = basis.RailRight;
			added.WallLeft = basis.WallLeft;
			added.WallRight = basis.WallRight;
			doc.Track.Insert(insertAt, added);
			selPoint = insertAt;
			dragging = true;
			dragHeight = ground.y;
			dirtyTrack = true;
		}

		private void DeletePoint()
		{
			if (doc.Track.Count <= 4)
			{
				Say("A track needs at least 4 points.");
				return;
			}
			doc.Track.RemoveAt(selPoint);
			selPoint = Mathf.Min(selPoint, doc.Track.Count - 1);
			dirtyTrack = true;
			rebuildAt = 0f;
		}

		private void Raise(float amount)
		{
			doc.Track[selPoint].Y += amount;
			dirtyTrack = true;
			rebuildAt = 0f;
		}

		// ---- the start tool: the grid and the lights ---------------------------------------------------------------------------------

		private List<MapSpot> ShownSpots { get { return doc.Spots.Count > 0 ? doc.Spots : autoSpots; } }

		private MapSpot ShownLights { get { return doc.HasLights ? new MapSpot { X = doc.LightX, Z = doc.LightZ } : autoLights; } }

		private Vector3 SpotWorld(MapSpot spot)
		{
			return world.W(new Vector3(spot.X, doc.Track[0].Y, spot.Z));
		}

		private void RefreshAuto()
		{
			try
			{
				MapSpot lights;
				autoSpots = MapRace.AutoGrid(world, out lights);
				autoLights = lights;
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Map Maker: could not work out the automatic grid: " + e.Message);
			}
		}

		/// <summary>Starts a custom grid from the automatic one, so a spot can be moved, added or removed.</summary>
		private void EnsureSpots()
		{
			if (doc.Spots.Count == 0)
			{
				foreach (MapSpot s in autoSpots)
				{
					doc.Spots.Add(new MapSpot { X = s.X, Z = s.Z });
				}
			}
		}

		private Vector2 MouseGui()
		{
			return new Vector2(Input.mousePosition.x / UiScale(), (Screen.height - Input.mousePosition.y) / UiScale());
		}

		private float DistTo(Vector3 worldPoint)
		{
			Vector2 g;
			return ToScreen(worldPoint, out g) ? Vector2.Distance(g, MouseGui()) : float.MaxValue;
		}

		private int NearestSpot(float within)
		{
			int best = -1;
			float bestD = within;
			List<MapSpot> spots = ShownSpots;
			for (int i = 0; i < spots.Count; i++)
			{
				float d = DistTo(SpotWorld(spots[i]));
				if (d < bestD)
				{
					bestD = d;
					best = i;
				}
			}
			return best;
		}

		private bool OverLights(float within)
		{
			return DistTo(SpotWorld(ShownLights) + Vector3.up * 2f) < within;
		}

		/// <summary>Which tool the thing under the mouse belongs to (the current one when nothing is there).</summary>
		private Tool ToolUnderMouse()
		{
			float point = float.MaxValue, prop = float.MaxValue, spot = float.MaxValue;
			int p = NearestPoint(16f);
			if (p >= 0) point = DistTo(world.W(doc.Track[p].Pos));
			int q = NearestProp(16f);
			if (q >= 0) prop = DistTo(world.W(doc.Props[q].Pos + Vector3.up * 3f));
			int sp = NearestSpot(18f);
			if (sp >= 0) spot = DistTo(SpotWorld(ShownSpots[sp]));
			if (OverLights(18f)) spot = Mathf.Min(spot, 1f);
			float best = Mathf.Min(point, Mathf.Min(prop, spot));
			if (best == float.MaxValue)
			{
				return tool;
			}
			return best == spot ? Tool.Start : (best == point ? Tool.Track : Tool.Props);
		}

		private void StartTool(bool overUi)
		{
			if (dragging)
			{
				if (!Input.GetMouseButton(0) || (!selLights && selSpot < 0))
				{
					dragging = false;
					return;
				}
				Vector3 at;
				if (Pick(out at, true))
				{
					Vector3 d = world.D(at);
					if (selLights)
					{
						doc.HasLights = true;
						doc.LightX = Clamp(d.x);
						doc.LightZ = Clamp(d.z);
					}
					else
					{
						EnsureSpots();
						doc.Spots[selSpot].X = Clamp(d.x);
						doc.Spots[selSpot].Z = Clamp(d.z);
					}
				}
				return;
			}
			if (!selLights && selSpot > 0 && (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
			{
				DeleteSpot();
			}
			if (!Input.GetMouseButtonDown(0) || overUi)
			{
				return;
			}
			if (OverLights(18f))
			{
				selLights = true;
				selSpot = -1;
				dragging = true;
				return;
			}
			int hit = NearestSpot(18f);
			if (hit >= 0)
			{
				selLights = false;
				selSpot = hit;
				dragging = true;
				return;
			}
			Vector3 ground;
			if (!Pick(out ground, true))
			{
				return;
			}
			if (ShownSpots.Count >= MapDocument.MaxSpots)
			{
				Say("The grid already has the most places it can (" + MapDocument.MaxSpots + ").");
				return;
			}
			EnsureSpots();
			Vector3 pos = world.D(ground);
			doc.Spots.Add(new MapSpot { X = Clamp(pos.x), Z = Clamp(pos.z) });
			selSpot = doc.Spots.Count - 1;
			selLights = false;
			dragging = true;
		}

		private void DeleteSpot()
		{
			if (selSpot <= 0)
			{
				Say("The player's place (P) stays; pick a numbered one to delete.");
				return;
			}
			EnsureSpots();
			if (selSpot < doc.Spots.Count)
			{
				doc.Spots.RemoveAt(selSpot);
			}
			selSpot = -1;
		}

		// ---- undo and redo -----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Once the mouse is up and the map has stopped changing, a change that happened since the last check becomes one undo step. A whole drag,
		/// brush stroke or slider move is one step, whatever made it.
		/// </summary>
		private void UndoTick(bool force = false)
		{
			if (!force && (Time.unscaledTime < nextSettle || Input.GetMouseButton(0) || dragging || painting || GUIUtility.hotControl != 0))
			{
				return;
			}
			nextSettle = Time.unscaledTime + 0.25f;
			string now = doc.ToJson();
			if (now == stable)
			{
				return;
			}
			undoStack.Add(stable);
			if (undoStack.Count > MaxUndo)
			{
				undoStack.RemoveAt(0);
			}
			redoStack.Clear();
			stable = now;
		}

		private void Undo()
		{
			UndoTick(true);
			if (undoStack.Count == 0)
			{
				Say("Nothing to undo.");
				return;
			}
			redoStack.Add(stable);
			string back = undoStack[undoStack.Count - 1];
			undoStack.RemoveAt(undoStack.Count - 1);
			Restore(back);
			Say("Undone (" + undoStack.Count + " more steps back). Ctrl+Y goes forward again.");
		}

		private void Redo()
		{
			UndoTick(true);
			if (redoStack.Count == 0)
			{
				Say("Nothing to redo.");
				return;
			}
			undoStack.Add(stable);
			string next = redoStack[redoStack.Count - 1];
			redoStack.RemoveAt(redoStack.Count - 1);
			Restore(next);
			Say("Redone (" + redoStack.Count + " more steps forward).");
		}

		private void Restore(string json)
		{
			try
			{
				doc.CopyFrom(MapDocument.Parse(json));
				stable = json;
				selPoint = Mathf.Min(selPoint, doc.Track.Count - 1);
				selProp = Mathf.Min(selProp, doc.Props.Count - 1);
				selSpot = Mathf.Min(selSpot, ShownSpots.Count - 1);
				dragging = false;
				dirtyTrack = dirtyTerrain = dirtyProps = true;
				rebuildAt = 0f;
				nextSettle = Time.unscaledTime + 0.25f;
			}
			catch (Exception e)
			{
				Say("Could not go back: " + e.Message);
			}
		}

		// ---- the props tool --------------------------------------------------------------------------------------------------------

		private int NearestProp(float within)
		{
			Vector2 mouse = new Vector2(Input.mousePosition.x / UiScale(), (Screen.height - Input.mousePosition.y) / UiScale());
			int best = -1;
			float bestD = within;
			for (int i = 0; i < doc.Props.Count; i++)
			{
				Vector2 g;
				if (ToScreen(world.W(doc.Props[i].Pos + Vector3.up * 3f), out g) && Vector2.Distance(g, mouse) < bestD)
				{
					bestD = Vector2.Distance(g, mouse);
					best = i;
				}
			}
			return best;
		}

		private void PropsTool(bool overUi)
		{
			if (dragging)
			{
				if (!Input.GetMouseButton(0) || selProp < 0 || selProp >= doc.Props.Count)
				{
					dragging = false;
					dirtyProps = true;
					rebuildAt = 0f;
				}
				else
				{
					Vector3 at;
					if (Pick(out at, true))
					{
						Vector3 d = world.D(at);
						MapProp p = doc.Props[selProp];
						p.X = Clamp(d.x);
						p.Z = Clamp(d.z);
						p.Y = d.y;
						dirtyProps = true;
					}
				}
				return;
			}
			if (selProp >= 0 && selProp < doc.Props.Count)
			{
				if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
				{
					doc.Props.RemoveAt(selProp);
					selProp = -1;
					dirtyProps = true;
					rebuildAt = 0f;
					return;
				}
				MapProp sel = doc.Props[selProp];
				if (Input.GetKeyDown(KeyCode.R)) { sel.Rot = (sel.Rot + 15f) % 360f; dirtyProps = true; rebuildAt = 0f; }
				if (Input.GetKeyDown(KeyCode.F)) { sel.Rot = (sel.Rot + 345f) % 360f; dirtyProps = true; rebuildAt = 0f; }
			}
			if (!Input.GetMouseButtonDown(0) || overUi)
			{
				return;
			}
			int hit = NearestProp(18f);
			if (hit >= 0)
			{
				selProp = hit;
				dragging = true;
				return;
			}
			Vector3 ground;
			if (!Pick(out ground))
			{
				return;
			}
			if (doc.Props.Count >= MapDocument.MaxProps)
			{
				Say("The map already has the most props it can (" + MapDocument.MaxProps + ").");
				return;
			}
			Vector3 pos = world.D(ground);
			doc.Props.Add(new MapProp { Type = propType, X = Clamp(pos.x), Y = pos.y, Z = Clamp(pos.z), Rot = propRot, Scale = propScale });
			selProp = doc.Props.Count - 1;
			dirtyProps = true;
			rebuildAt = 0f;
		}

		// ---- the terrain tool ------------------------------------------------------------------------------------------------------

		private void TerrainTool(bool overUi)
		{
			bool down = Input.GetMouseButton(0) && !overUi;
			if (!down)
			{
				if (painting)
				{
					painting = false;
					dirtyTerrain = true;
					rebuildAt = 0f;
				}
				return;
			}
			Vector3 ground;
			if (!Pick(out ground))
			{
				return;
			}
			Vector3 c = world.D(ground);
			MapTerrain t = doc.Terrain;
			if (!painting)
			{
				painting = true;
				flattenTo = t.At(Mathf.RoundToInt((c.x + t.Size * 0.5f) / t.Cell), Mathf.RoundToInt((c.z + t.Size * 0.5f) / t.Cell));
			}
			if (Time.unscaledTime < nextPaint)
			{
				return;
			}
			nextPaint = Time.unscaledTime + 0.05f;
			Paint(t, c);
			dirtyTerrain = true;
		}

		private void Paint(MapTerrain t, Vector3 c)
		{
			float cell = t.Cell, half = t.Size * 0.5f;
			int rc = Mathf.CeilToInt(brushRadius / cell);
			int cx = Mathf.RoundToInt((c.x + half) / cell), cz = Mathf.RoundToInt((c.z + half) / cell);
			var copy = brushMode == 2 ? (float[])t.Heights.Clone() : null;
			for (int iz = Mathf.Max(0, cz - rc); iz <= Mathf.Min(t.Cells, cz + rc); iz++)
			{
				for (int ix = Mathf.Max(0, cx - rc); ix <= Mathf.Min(t.Cells, cx + rc); ix++)
				{
					float dx = -half + ix * cell - c.x, dz = -half + iz * cell - c.z;
					float d = Mathf.Sqrt(dx * dx + dz * dz);
					if (d > brushRadius)
					{
						continue;
					}
					float w = Mathf.SmoothStep(0f, 1f, 1f - d / brushRadius);
					int index = iz * t.Verts + ix;
					float h = t.Heights[index];
					switch (brushMode)
					{
						case 0: h += brushStrength * w * 0.7f; break;
						case 1: h -= brushStrength * w * 0.7f; break;
						case 2:
							float avg = (copy[Mathf.Clamp(iz - 1, 0, t.Cells) * t.Verts + ix] + copy[Mathf.Clamp(iz + 1, 0, t.Cells) * t.Verts + ix]
								+ copy[iz * t.Verts + Mathf.Clamp(ix - 1, 0, t.Cells)] + copy[iz * t.Verts + Mathf.Clamp(ix + 1, 0, t.Cells)]) * 0.25f;
							h = Mathf.Lerp(h, avg, Mathf.Clamp01(w * brushStrength * 0.6f));
							break;
						default:
							h = Mathf.Lerp(h, flattenTo, Mathf.Clamp01(w * brushStrength * 0.4f));
							break;
					}
					t.Heights[index] = Mathf.Clamp(h, -300f, 300f);
				}
			}
		}

		// ---- rebuilding what changed -------------------------------------------------------------------------------------------------

		private void Flush()
		{
			if (!(dirtyTrack || dirtyTerrain || dirtyProps) || Time.unscaledTime < rebuildAt)
			{
				return;
			}
			rebuildAt = Time.unscaledTime + 0.15f;
			try
			{
				if (dirtyTrack)
				{
					world.RebuildTrack();
						RefreshAuto();
				}
				else if (dirtyTerrain)
				{
					world.RebuildTerrain();
				}
				if (dirtyProps)
				{
					world.RebuildProps();
				}
				Physics.SyncTransforms();
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogError("Map Maker: could not rebuild the map: " + e);
				Say("Could not rebuild the map: " + e.Message);
			}
			dirtyTrack = dirtyTerrain = dirtyProps = false;
		}

		// ---- files -----------------------------------------------------------------------------------------------------------------

		private string Save()
		{
			Directory.CreateDirectory(MapLibrary.UserFolder);
			string path = Path.Combine(MapLibrary.UserFolder, doc.Id + ".json");
			doc.Name = string.IsNullOrWhiteSpace(nameField) ? doc.Name : nameField.Trim();
			File.WriteAllText(path, doc.ToJson());
			doc.Path = path;
			return path;
		}

		private void DoSave()
		{
			try
			{
				Say("Saved " + Save());
			}
			catch (Exception e)
			{
				Say("Could not save: " + e.Message);
			}
		}

		private void DoSaveAs()
		{
			string name = (saveAsField ?? "").Trim();
			if (name.Length == 0)
			{
				Say("Type a name first.");
				return;
			}
			string id = MapDocument.CleanId(name);
			if (id == MapLibrary.MakerId)
			{
				Say("That name is the scratch map's. Pick another.");
				return;
			}
			doc.Id = id;
			nameField = name;
			DoSave();
			Say("Saved as '" + name + "'. It joins your runs the next time the game starts. " + doc.Path);
		}

		private void DoExport()
		{
			try
			{
				DoSave();
				string folder = Path.Combine(Path.Combine(ModPaths.DataDir, "export"), doc.Id);
				Directory.CreateDirectory(Path.Combine(folder, "maps"));
				File.Copy(doc.Path, Path.Combine(Path.Combine(folder, "maps"), doc.Id + ".json"), true);
				var mod = new Dictionary<string, object>
				{
					{ "id", doc.Id }, { "name", doc.Name }, { "version", "1.0.0" }, { "author", "" },
					{ "description", "A map made with the HalfGripTools Map Maker." },
					{ "kind", "map" }, { "apiVersion", "2.0.0" }
				};
				File.WriteAllText(Path.Combine(folder, "mod.json"), MiniJson.Write(mod));
				Say("Exported a mod folder: " + folder + " (put it in the game's Mods folder, or upload it to the Workshop)");
			}
			catch (Exception e)
			{
				Say("Could not export: " + e.Message);
			}
		}

		private void DoTest()
		{
			try
			{
				Save();
			}
			catch (Exception e)
			{
				Say("Could not save before the test drive: " + e.Message);
				return;
			}
			MapMaker.Testing = true;
			Reload();
		}

		// ---- drawing ---------------------------------------------------------------------------------------------------------------

		private void Dot(Vector2 at, float size, Color color)
		{
			GUI.color = color;
			GUI.DrawTexture(new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size), white);
		}

		private void OnGUI()
		{
			if (!open || cam == null || world == null || world.Root == null)
			{
				return;
			}
			float k = UiScale();
			GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
			if (small == null)
			{
				small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
			}
			if (Event.current.type == EventType.Repaint)
			{
				focused = GUI.GetNameOfFocusedControl();
			}
			DrawGizmos();
			if (menuOpen)
			{
				leftPanel = bottomPanel = Rect.zero;
				DrawMenu(Screen.width / k, Screen.height / k);
				return;
			}

			leftPanel = new Rect(10, 10, 270, Mathf.Min(Screen.height / k - 20f, 560f));
			GUI.color = new Color(0f, 0f, 0f, 0.72f);
			GUI.DrawTexture(leftPanel, white);
			GUI.color = Color.white;
			GUILayout.BeginArea(new Rect(leftPanel.x + 8, leftPanel.y + 6, leftPanel.width - 16, leftPanel.height - 12));
			GUILayout.Label("<b>MAP MAKER</b>  " + doc.Name, small);
			tool = (Tool)GUILayout.SelectionGrid((int)tool, ToolNames, 3);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("UNDO (Ctrl+Z)")) { Undo(); }
			if (GUILayout.Button("REDO (Ctrl+Y)")) { Redo(); }
			GUILayout.EndHorizontal();
			GUILayout.Space(4);
			switch (tool)
			{
				case Tool.Track: TrackPanel(); break;
				case Tool.Props: PropsPanel(); break;
				case Tool.Terrain: TerrainPanel(); break;
				case Tool.Start: StartPanel(); break;
				default: MapPanel(); break;
			}
			GUILayout.EndArea();

			float w = Screen.width / k;
			bottomPanel = new Rect(290, Screen.height / k - 34f, w - 300f, 24f);
			GUI.color = new Color(0f, 0f, 0f, 0.6f);
			GUI.DrawTexture(bottomPanel, white);
			GUI.color = Color.white;
			string help = Time.unscaledTime < statusUntil ? status : "Esc menu   1-4 tools   T top view   Right mouse: look   WASD/QE fly   Scroll: speed";
			GUI.Label(new Rect(bottomPanel.x + 6, bottomPanel.y + 2, bottomPanel.width - 12, 20), help, small);
		}

		/// <summary>The Esc menu: the way out of the editor.</summary>
		private void DrawMenu(float width, float height)
		{
			GUI.color = new Color(0f, 0f, 0f, 0.6f);
			GUI.DrawTexture(new Rect(0, 0, width, height), white);
			GUI.color = Color.white;
			Rect box = new Rect(width * 0.5f - 160f, height * 0.5f - 170f, 320f, 340f);
			GUI.color = new Color(0.08f, 0.08f, 0.1f, 0.96f);
			GUI.DrawTexture(box, white);
			GUI.color = Color.white;
			GUILayout.BeginArea(new Rect(box.x + 16, box.y + 12, box.width - 32, box.height - 24));
			GUILayout.Label("<b>MAP MAKER</b>  " + doc.Name, small);
			GUILayout.Space(6);
			if (GUILayout.Button("RESUME", GUILayout.Height(34))) { menuOpen = false; }
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("UNDO", GUILayout.Height(30))) { Undo(); }
			if (GUILayout.Button("REDO", GUILayout.Height(30))) { Redo(); }
			GUILayout.EndHorizontal();
			if (GUILayout.Button("SAVE", GUILayout.Height(34))) { DoSave(); menuOpen = false; }
			if (GUILayout.Button("TEST DRIVE", GUILayout.Height(34))) { DoTest(); }
			if (GUILayout.Button(MapMaker.Standalone ? "SAVE AND QUIT EDITOR" : "SAVE AND LEAVE EDITOR", GUILayout.Height(34)))
			{
				try
				{
					Save();
					QuitEditor();
				}
				catch (Exception e)
				{
					Say("Could not save: " + e.Message);
					menuOpen = false;
				}
			}
			if (GUILayout.Button(MapMaker.Standalone ? "QUIT EDITOR WITHOUT SAVING" : "LEAVE EDITOR WITHOUT SAVING", GUILayout.Height(34))) { QuitEditor(); }
			GUILayout.EndArea();
		}

		/// <summary>Out of the editor. Started from the menu: back to the main menu, nothing of a run touched. Opened with F10 in a race: the race goes on without the editor.</summary>
		private void QuitEditor()
		{
			if (!MapMaker.Standalone)
			{
				MapMaker.OpenNext = false;
				MapMaker.Testing = true;
				Reload();
				return;
			}
			string to = MapMaker.LeaveScene;
			MapMaker.Reloading = false;
			Shutdown();
			MapMaker.EndStandalone();
			TimeScale.ResetAll();
			Cursor.lockState = CursorLockMode.Confined;
			Cursor.visible = true;
			if (string.IsNullOrEmpty(to) || SceneLoader.Instance == null)
			{
				Reload();
				return;
			}
			SceneLoader.Instance.LoadScene(to);
		}

		private void DrawGrid()
		{
			List<MapSpot> spots = ShownSpots;
			for (int i = 0; i < spots.Count; i++)
			{
				Vector2 g;
				if (!ToScreen(SpotWorld(spots[i]), out g))
				{
					continue;
				}
				bool sel = !selLights && i == selSpot && tool == Tool.Start;
				Color c = i == 0 ? new Color(0.25f, 0.95f, 0.4f) : new Color(1f, 0.55f, 0.15f);
				Dot(g, sel ? 16f : 12f, sel ? new Color(1f, 0.9f, 0.2f) : c);
				GUI.color = Color.white;
				GUI.Label(new Rect(g.x + 9, g.y - 10, 60, 20), i == 0 ? "P" : i.ToString(), small);
			}
			Vector2 l;
			if (ToScreen(SpotWorld(ShownLights) + Vector3.up * 2f, out l))
			{
				bool sel = selLights && tool == Tool.Start;
				Dot(l, sel ? 16f : 12f, sel ? new Color(1f, 0.9f, 0.2f) : new Color(0.95f, 0.2f, 0.2f));
				GUI.color = Color.white;
				GUI.Label(new Rect(l.x + 9, l.y - 10, 80, 20), "LIGHTS", small);
			}
			GUI.color = Color.white;
		}

		private void DrawGizmos()
		{
			if (tool != Tool.Terrain && tool != Tool.Map)
			{
				DrawGrid();
			}
			List<Sample> d = world.Dense;
			for (int i = 0; i < d.Count; i += 2)
			{
				Vector2 g;
				if (ToScreen(world.W(d[i].Pos), out g))
				{
					Dot(g, 3f, new Color(1f, 1f, 1f, 0.55f));
				}
			}
			for (int i = 0; i < doc.Track.Count; i++)
			{
				Vector2 g;
				if (!ToScreen(world.W(doc.Track[i].Pos), out g))
				{
					continue;
				}
				bool selected = i == selPoint && tool == Tool.Track;
				Dot(g, selected ? 15f : 11f, selected ? new Color(1f, 0.85f, 0.1f) : (i == 0 ? new Color(0.2f, 0.95f, 0.3f) : new Color(0.95f, 0.95f, 0.95f)));
				if (i == 0)
				{
					GUI.color = Color.white;
					GUI.Label(new Rect(g.x + 9, g.y - 10, 90, 20), "START", small);
				}
			}
			if (tool == Tool.Props || tool == Tool.Track)
			{
				for (int i = 0; i < doc.Props.Count; i++)
				{
					Vector2 g;
					if (ToScreen(world.W(doc.Props[i].Pos + Vector3.up * 3f), out g))
					{
						Dot(g, i == selProp && tool == Tool.Props ? 10f : 6f, i == selProp && tool == Tool.Props ? new Color(1f, 0.85f, 0.1f) : new Color(0.4f, 0.9f, 1f, 0.9f));
					}
				}
			}
			GUI.color = Color.white;
		}

		private float Slide(string label, float value, float min, float max, string format = "0")
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label(label + " " + value.ToString(format), small, GUILayout.Width(120));
			value = GUILayout.HorizontalSlider(value, min, max);
			GUILayout.EndHorizontal();
			return value;
		}

		private bool Toggle(string label, bool value)
		{
			return GUILayout.Toggle(value, label);
		}

		private void TrackPanel()
		{
			GUILayout.Label("Click empty ground: add a point (on the road: between). Drag a point to move it. Delete removes it. [ ] or PgUp/PgDn change its height.", small);
			GUILayout.Label("Points: " + doc.Track.Count + "   Lap: " + Spline.Length(world.Dense).ToString("0") + " units", small);
			if (selPoint < 0 || selPoint >= doc.Track.Count)
			{
				GUILayout.Label("No point selected.", small);
				return;
			}
			MapPoint p = doc.Track[selPoint];
			GUILayout.Label("Point " + selPoint + (selPoint == 0 ? " (the start line)" : ""), small);
			bool changed = false;
			float width = Slide("Width", p.Width, 12f, 120f);
			if (Math.Abs(width - p.Width) > 0.01f) { p.Width = Mathf.Round(width); changed = true; }
			GUILayout.BeginHorizontal();
			GUILayout.Label("Height " + p.Y.ToString("0.0"), small, GUILayout.Width(120));
			if (GUILayout.Button("-5")) { p.Y -= 5f; changed = true; }
			if (GUILayout.Button("-1")) { p.Y -= 1f; changed = true; }
			if (GUILayout.Button("+1")) { p.Y += 1f; changed = true; }
			if (GUILayout.Button("+5")) { p.Y += 5f; changed = true; }
			GUILayout.EndHorizontal();
			if (GUILayout.Button("Surface: " + p.Surface.ToUpper() + "  (click to change)")) { p.Surface = Surfaces.Next(p.Surface, 1); changed = true; }
			bool rl = Toggle("Guardrail left", p.RailLeft), rr = Toggle("Guardrail right", p.RailRight);
			bool wl = Toggle("Invisible wall left", p.WallLeft), wr = Toggle("Invisible wall right", p.WallRight);
			if (rl != p.RailLeft || rr != p.RailRight || wl != p.WallLeft || wr != p.WallRight)
			{
				p.RailLeft = rl; p.RailRight = rr; p.WallLeft = wl; p.WallRight = wr;
				changed = true;
			}
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Rails: all on")) { SetAll(t => { t.RailLeft = true; t.RailRight = true; }); }
			if (GUILayout.Button("all off")) { SetAll(t => { t.RailLeft = false; t.RailRight = false; }); }
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Walls: all on")) { SetAll(t => { t.WallLeft = true; t.WallRight = true; }); }
			if (GUILayout.Button("all off")) { SetAll(t => { t.WallLeft = false; t.WallRight = false; }); }
			GUILayout.EndHorizontal();
			if (GUILayout.Button("Same width everywhere")) { float w = p.Width; SetAll(t => t.Width = w); }
			if (GUILayout.Button("Delete point")) { DeletePoint(); }
			if (changed)
			{
				dirtyTrack = true;
				rebuildAt = 0f;
			}
		}

		private void SetAll(Action<MapPoint> change)
		{
			foreach (MapPoint t in doc.Track)
			{
				change(t);
			}
			dirtyTrack = true;
			rebuildAt = 0f;
		}

		private void PropsPanel()
		{
			GUILayout.Label("Pick a kind, click the ground to place it. Click a prop to select and drag it. R / F turn it, Delete removes it.", small);
			int index = Array.IndexOf(PropTypes.Names, propType);
			int pick = GUILayout.SelectionGrid(index < 0 ? 0 : index, PropTypes.Names.Select(n => n.ToUpper()).ToArray(), 3);
			propType = PropTypes.Names[pick];
			propScale = Slide("Size", propScale, 0.4f, 4f, "0.0");
			propRot = Slide("Turn", propRot, 0f, 359f);
			GUILayout.Label("Props: " + doc.Props.Count + " of " + MapDocument.MaxProps, small);
			if (selProp >= 0 && selProp < doc.Props.Count)
			{
				MapProp p = doc.Props[selProp];
				GUILayout.Label("Selected: " + p.Type, small);
				float rot = Slide("Turn", p.Rot, 0f, 359f);
				float scale = Slide("Size", p.Scale, 0.4f, 4f, "0.0");
				if (Math.Abs(rot - p.Rot) > 0.01f || Math.Abs(scale - p.Scale) > 0.001f)
				{
					p.Rot = rot;
					p.Scale = scale;
					dirtyProps = true;
				}
				if (GUILayout.Button("Delete prop"))
				{
					doc.Props.RemoveAt(selProp);
					selProp = -1;
					dirtyProps = true;
					rebuildAt = 0f;
				}
			}
		}

		private void StartPanel()
		{
			GUILayout.Label("The start grid and the lights. Drag a place or the lights to move them. Click empty ground to add a place (one more AI car each, up to " + MapDocument.MaxSpots + "). Delete removes the selected one. Cars face along the start line.", small);
			GUILayout.Label((doc.Spots.Count > 0 ? "Your grid: " : "Automatic grid: ") + ShownSpots.Count + " places (P is yours)." + (doc.HasLights ? "  Lights moved." : ""), small);
			if (!selLights && selSpot > 0 && GUILayout.Button("Delete place " + selSpot)) { DeleteSpot(); }
			if (GUILayout.Button("Automatic grid again")) { doc.Spots.Clear(); selSpot = -1; }
			if (GUILayout.Button("Lights back behind the line")) { doc.HasLights = false; selLights = false; }
			GUILayout.Label("Test drive to see the cars on their places.", small);
		}

		private void TerrainPanel()
		{
			GUILayout.Label("Hold the left mouse on the ground to paint. The ground is always pressed down under the road, so hills never cover it.", small);
			brushMode = GUILayout.SelectionGrid(brushMode, BrushNames, 2);
			brushRadius = Slide("Size", brushRadius, 20f, 260f);
			brushStrength = Slide("Strength", brushStrength, 0.1f, 2f, "0.0");
			if (GUILayout.Button("Flatten everything"))
			{
				Array.Clear(doc.Terrain.Heights, 0, doc.Terrain.Heights.Length);
				dirtyTerrain = true;
				rebuildAt = 0f;
			}
		}

		private void MapPanel()
		{
			GUILayout.Label("Name", small);
			GUI.SetNextControlName("nameField");
			nameField = GUILayout.TextField(nameField, 40);
			if (GUILayout.Button("Weather: " + doc.Weather.ToUpper() + "  (click to change)"))
			{
				int i = Array.IndexOf(MapDocument.WeatherNames, doc.Weather);
				doc.Weather = MapDocument.WeatherNames[(i + 1) % MapDocument.WeatherNames.Length];
				Say("Weather: " + doc.Weather + ". Test drive to see it.");
			}
			GUILayout.Space(6);
			if (GUILayout.Button("SAVE")) { DoSave(); }
			GUILayout.Label("Save as a new map:", small);
			GUI.SetNextControlName("saveAsField");
			saveAsField = GUILayout.TextField(saveAsField ?? "", 40);
			if (GUILayout.Button("SAVE AS")) { DoSaveAs(); }
			if (GUILayout.Button("EXPORT AS A MOD FOLDER")) { DoExport(); }
			GUILayout.Space(6);
			if (GUILayout.Button("TEST DRIVE (saves, then starts a race on the map)")) { DoTest(); }
			GUILayout.Space(6);
			if (GUILayout.Button(armedNew ? "Really start over with a new oval?" : "New oval (start over)"))
			{
				if (!armedNew)
				{
					armedNew = true;
				}
				else
				{
					armedNew = false;
					MapDocument fresh = MapDocument.Oval(doc.Id, doc.Name);
					doc.Track.Clear();
					doc.Track.AddRange(fresh.Track);
					doc.Props.Clear();
					Array.Clear(doc.Terrain.Heights, 0, doc.Terrain.Heights.Length);
					selPoint = selProp = -1;
					dirtyTrack = dirtyProps = true;
					rebuildAt = 0f;
				}
			}
			if (GUILayout.Button("CLOSE EDITOR (race as it stands)")) { MapEditor.Close(); }
			GUILayout.Label("Saved maps live in " + MapLibrary.UserFolder + ". Your own maps join your runs when the game starts.", small);
		}
	}
}
