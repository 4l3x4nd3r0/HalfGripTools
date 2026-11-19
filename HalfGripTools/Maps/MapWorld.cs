using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripTools.MapBuild
{
	/// <summary>Flat-coloured materials that borrow the shader of the Stadium ground, so lighting and shadows work like the rest of the scene.</summary>
	internal static class MapMaterials
	{
		private static readonly Dictionary<Color, Material> made = new Dictionary<Color, Material>();

		internal static Material Make(Material source, Color color)
		{
			Material m;
			if (made.TryGetValue(color, out m) && m != null)
			{
				return m;
			}
			Shader shader = source != null ? source.shader : null;
			m = shader != null ? new Material(shader) : null;
			if (m == null || (!m.HasProperty("_BaseColor") && !m.HasProperty("_Color")))
			{
				shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
				m = new Material(shader);
			}
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", color);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", color);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			if (m.HasProperty("_Metallic"))
			{
				m.SetFloat("_Metallic", 0f);
			}
			m.hideFlags = HideFlags.HideAndDontSave;
			made[color] = m;
			return m;
		}

		internal static void Forget()
		{
			foreach (Material m in made.Values)
			{
				if (m != null)
				{
					UnityEngine.Object.Destroy(m);
				}
			}
			made.Clear();
		}
	}

	/// <summary>
	/// A <see cref="MapDocument"/> built into the scene: the road, the ground, the guardrail, the invisible walls and the props. It builds in the
	/// document's own space under <see cref="Root"/>, which is turned so that the start line points along +X (the way the game's start grid
	/// faces); the guardrail is the only part that lives outside it, because it is tested against the cars in world space.
	/// </summary>
	internal sealed class MapWorld
	{
		internal readonly MapDocument Doc;
		internal GameObject Root;
		internal int Layer;
		internal Material Source;
		internal TrackManager Tm;

		/// <summary>The track line sampled every 6 units, in the document's space.</summary>
		internal List<Sample> Dense = new List<Sample>();

		private GameObject road, terrain, props, railObject;
		private int railChunk, railInChunk;
		private readonly List<RailPiece> rails = new List<RailPiece>();
		private static PhysicsMaterial frictionless;

		internal MapWorld(MapDocument doc, GameObject env, TrackManager tm)
		{
			Doc = doc;
			Tm = tm;
			Transform ground = env != null ? env.transform.Find("Ground") : null;
			Layer = ground != null ? ground.gameObject.layer : 0;
			MeshRenderer groundRenderer = ground != null ? ground.GetComponent<MeshRenderer>() : null;
			Source = groundRenderer != null ? groundRenderer.sharedMaterial : null;
		}

		/// <summary>Document space to the world, and back.</summary>
		internal Vector3 W(Vector3 p) { return Root.transform.TransformPoint(p); }

		internal Vector3 D(Vector3 p) { return Root.transform.InverseTransformPoint(p); }

		internal Quaternion Frame { get { return Root.transform.rotation; } }

		/// <summary>The turn that puts the start line's direction along +X.</summary>
		internal static Quaternion FrameFor(MapDocument doc)
		{
			List<Sample> s = Spline.Sample(doc, 12f);
			float angle = Vector3.SignedAngle(s[0].Along, Vector3.right, Vector3.up);
			return Quaternion.Euler(0f, angle, 0f);
		}

		internal void BuildAll(Quaternion frame, float yShift)
		{
			Root = new GameObject("HalfGripMap " + Doc.Id);
			Root.transform.SetPositionAndRotation(new Vector3(0f, yShift, 0f), frame);
			Doc.Terrain.Ensure();
			RebuildTrack();
			RebuildProps();

			// Anything that falls off the edge of the ground ends up here, where the game's reset can find a road to put it back on.
			GameObject floor = new GameObject("void floor") { layer = Layer };
			floor.transform.SetParent(Root.transform, false);
			floor.transform.localPosition = new Vector3(0f, -90f, 0f);
			floor.AddComponent<BoxCollider>().size = new Vector3(12000f, 2f, 12000f);
		}

		internal void Destroy()
		{
			if (Root != null)
			{
				UnityEngine.Object.Destroy(Root);
			}
			if (railObject != null)
			{
				UnityEngine.Object.Destroy(railObject);
			}
			Root = null;
			railObject = null;
		}

		// ---- the track: road, walls, rails and the ground carved under them --------------------------------------------------

		internal void RebuildTrack()
		{
			Dense = Spline.Sample(Doc, 6f);
			if (road != null)
			{
				UnityEngine.Object.Destroy(road);
			}
			road = new GameObject("road") { layer = Layer };
			road.transform.SetParent(Root.transform, false);

			var surfaces = new Dictionary<string, Batch>();
			var lines = new Batch("lines", new Color(0.88f, 0.88f, 0.84f), false);
			var walls = new Batch("wall", Color.white, true, true) { Invisible = true, Slippery = true };
			int n = Dense.Count;
			for (int i = 0; i < n; i++)
			{
				Sample a = Dense[i], b = Dense[(i + 1) % n];
				Batch batch;
				if (!surfaces.TryGetValue(a.Surface, out batch))
				{
					batch = new Batch("road-" + a.Surface, Surfaces.ColorOf(a.Surface), true);
					surfaces[a.Surface] = batch;
				}
				Vector3 al = a.Pos - a.Right * a.Half, ar = a.Pos + a.Right * a.Half;
				Vector3 bl = b.Pos - b.Right * b.Half, br = b.Pos + b.Right * b.Half;
				batch.Quad(al, ar, br, bl, Vector3.up);

				if (a.Surface == "asphalt" || a.Surface == "wet")
				{
					// An edge line on each side and a dashed one down the middle.
					Strip(lines, a, b, -(a.Half - 1.6f), -(a.Half - 0.9f), 0.06f);
					Strip(lines, a, b, a.Half - 0.9f, a.Half - 1.6f, 0.06f);
					if (i % 4 < 2)
					{
						Strip(lines, a, b, -0.35f, 0.35f, 0.06f);
					}
				}
				if (a.WallLeft && b.WallLeft)
				{
					Wall(walls, a, b, -1);
				}
				if (a.WallRight && b.WallRight)
				{
					Wall(walls, a, b, 1);
				}
			}
			// The start line: a white band across the road at sample 0.
			Sample s0 = Dense[0];
			Vector3 f = s0.Along * 3f, raise = Vector3.up * 0.07f;
			var start = new Batch("start-line", Color.white, false);
			start.Quad(s0.Pos - s0.Right * s0.Half + raise, s0.Pos + s0.Right * s0.Half + raise, s0.Pos + s0.Right * s0.Half + f + raise, s0.Pos - s0.Right * s0.Half + f + raise, Vector3.up);

			foreach (Batch b in surfaces.Values)
			{
				Spawn(b, road.transform);
			}
			Spawn(lines, road.transform);
			Spawn(start, road.transform);
			Spawn(walls, road.transform);

			RebuildTerrain();
			RebuildRails();
		}

		private static void Strip(Batch batch, Sample a, Sample b, float latA, float latB, float lift)
		{
			Vector3 up = Vector3.up * lift;
			batch.Quad(a.Pos + a.Right * latA + up, a.Pos + a.Right * latB + up, b.Pos + b.Right * latB + up, b.Pos + b.Right * latA + up, Vector3.up);
		}

		private static void Wall(Batch batch, Sample a, Sample b, int side)
		{
			// Standing at the road's edge, from below the ground to well above a car, facing the road: the same invisible, frictionless wall
			// Temple of Drift's cave has, so a car that meets it slides along instead of stopping dead.
			Vector3 pa = a.Pos + a.Right * side * (a.Half + 0.3f), pb = b.Pos + b.Right * side * (b.Half + 0.3f);
			batch.Quad(pa + Vector3.down * 1.5f, pb + Vector3.down * 1.5f, pb + Vector3.up * 14f, pa + Vector3.up * 14f, -a.Right * side);
		}

		private void Spawn(Batch batch, Transform parent)
		{
			if (batch.Count == 0)
			{
				return;
			}
			GameObject go = new GameObject(batch.Name) { layer = Layer };
			go.transform.SetParent(parent, false);
			Mesh mesh = batch.ToMesh();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			if (!batch.Invisible)
			{
				go.AddComponent<MeshRenderer>().sharedMaterial = MapMaterials.Make(Source, batch.Color);
			}
			if (batch.Collide)
			{
				MeshCollider collider = go.AddComponent<MeshCollider>();
				collider.sharedMesh = mesh;
				if (batch.Slippery)
				{
					collider.sharedMaterial = Frictionless();
				}
			}
			if (batch.Wall)
			{
				try
				{
					go.tag = "Wall";
				}
				catch (Exception)
				{
					// The tag only plays the game's wall sound.
				}
			}
			if (batch.Name.StartsWith("road-"))
			{
				string surface = batch.Name.Substring(5);
				if (surface != "asphalt")
				{
					float grip, drag;
					Surfaces.Handling(surface, out grip, out drag);
					HandlingModifier handling = go.AddComponent<HandlingModifier>();
					handling.gripModifier = grip;
					handling.resistanceModifier = drag;
					handling.background = false;
				}
			}
		}

		private static PhysicsMaterial Frictionless()
		{
			if (frictionless == null)
			{
				frictionless = new PhysicsMaterial("HalfGripMapFrictionless")
				{
					dynamicFriction = 0f,
					staticFriction = 0f,
					bounciness = 0f,
					frictionCombine = PhysicsMaterialCombine.Minimum,
					bounceCombine = PhysicsMaterialCombine.Minimum,
					hideFlags = HideFlags.HideAndDontSave
				};
			}
			return frictionless;
		}

		// ---- the ground --------------------------------------------------------------------------------------------------------

		/// <summary>The ground mesh: the painted heights, pressed down under the road (and eased back up over 16 units either side) so the road always sits on it.</summary>
		internal void RebuildTerrain()
		{
			if (terrain != null)
			{
				UnityEngine.Object.Destroy(terrain);
			}
			MapTerrain t = Doc.Terrain;
			t.Ensure();
			int vn = t.Verts;
			float[] h = (float[])t.Heights.Clone();
			// For each ground vertex: how far the nearest road sample is, how high it is and how wide its road is.
			float[] bestDist = new float[h.Length];
			float[] bestTarget = new float[h.Length];
			float[] bestHalf = new float[h.Length];
			for (int i = 0; i < bestDist.Length; i++)
			{
				bestDist[i] = float.MaxValue;
			}
			float half = t.Size * 0.5f, cell = t.Cell;
			foreach (Sample s in Dense)
			{
				// Flat out to a cell and a half beyond the road's edge: a ground cell that spans the edge has all four corners down, or it would
				// slope up through the road's outer strip. Then eased back up to the painted height over a few more cells.
				float inner = s.Half + cell * 1.5f + 1f;
				float blend = Mathf.Max(16f, cell * 2f);
				float reach = inner + blend;
				int rc = Mathf.CeilToInt(reach / cell);
				int cx = Mathf.RoundToInt((s.Pos.x + half) / cell), cz = Mathf.RoundToInt((s.Pos.z + half) / cell);
				for (int iz = Mathf.Max(0, cz - rc); iz <= Mathf.Min(t.Cells, cz + rc); iz++)
				{
					for (int ix = Mathf.Max(0, cx - rc); ix <= Mathf.Min(t.Cells, cx + rc); ix++)
					{
						float dx = -half + ix * cell - s.Pos.x, dz = -half + iz * cell - s.Pos.z;
						float d = Mathf.Sqrt(dx * dx + dz * dz);
						if (d > reach)
						{
							continue;
						}
						int index = iz * vn + ix;
						if (d < bestDist[index])
						{
							bestDist[index] = d;
							bestTarget[index] = s.Pos.y - 0.6f;
							bestHalf[index] = s.Half;
						}
					}
				}
			}
			var verts = new Vector3[vn * vn];
			for (int iz = 0; iz < vn; iz++)
			{
				for (int ix = 0; ix < vn; ix++)
				{
					int index = iz * vn + ix;
					float inner = bestHalf[index] + cell * 1.5f + 1f, blend = Mathf.Max(16f, cell * 2f);
					float d = bestDist[index];
					float w = d <= inner ? 1f : (d >= inner + blend ? 0f : 1f - Mathf.SmoothStep(0f, 1f, (d - inner) / blend));
					float y = Mathf.Lerp(h[index], bestTarget[index], w);
					verts[index] = new Vector3(-half + ix * cell, y, -half + iz * cell);
				}
			}
			var tris = new int[t.Cells * t.Cells * 6];
			int k = 0;
			for (int iz = 0; iz < t.Cells; iz++)
			{
				for (int ix = 0; ix < t.Cells; ix++)
				{
					int a = iz * vn + ix, b = a + 1, c = a + vn, d = c + 1;
					tris[k++] = a; tris[k++] = c; tris[k++] = b;
					tris[k++] = b; tris[k++] = c; tris[k++] = d;
				}
			}
			var mesh = new Mesh { name = "Map_ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
			mesh.vertices = verts;
			mesh.triangles = tris;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			terrain = new GameObject("ground") { layer = Layer };
			terrain.transform.SetParent(Root.transform, false);
			terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
			terrain.AddComponent<MeshRenderer>().sharedMaterial = MapMaterials.Make(Source, new Color(0.23f, 0.36f, 0.17f));
			terrain.AddComponent<MeshCollider>().sharedMesh = mesh;
			if (Environment.GetEnvironmentVariable("HALFGRIPTOOLS_MAPSHOT") != null)
			{
				CarveCheck(verts, t);
			}
			// Off the road, so the game counts it as off-track and it grips less.
			HandlingModifier handling = terrain.AddComponent<HandlingModifier>();
			handling.gripModifier = 0.7f;
			handling.resistanceModifier = 3f;
			handling.background = true;
		}

		/// <summary>Test aid: how far above the road the ground mesh is, across the road's width, at every 4th sample. Logs the worst spots.</summary>
		private void CarveCheck(Vector3[] verts, MapTerrain t)
		{
			int bad = 0, total = 0;
			float worst = float.MinValue;
			string where = "";
			for (int i = 0; i < Dense.Count; i += 4)
			{
				Sample s = Dense[i];
				for (float lat = -0.95f; lat <= 0.951f; lat += 0.475f)
				{
					Vector3 p = s.Pos + s.Right * (lat * s.Half);
					float gx = (p.x + t.Size * 0.5f) / t.Cell, gz = (p.z + t.Size * 0.5f) / t.Cell;
					int ix = Mathf.Clamp(Mathf.FloorToInt(gx), 0, t.Cells - 1), iz = Mathf.Clamp(Mathf.FloorToInt(gz), 0, t.Cells - 1);
					float fx = gx - ix, fz = gz - iz, vn = t.Verts;
					float h00 = verts[iz * t.Verts + ix].y, h10 = verts[iz * t.Verts + ix + 1].y, h01 = verts[(iz + 1) * t.Verts + ix].y, h11 = verts[(iz + 1) * t.Verts + ix + 1].y;
					float h = Mathf.Max(Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz), Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11)) - 0.001f);
					float above = Mathf.Max(h00, h10, h01, h11) - s.Pos.y;
					total++;
					if (above > -0.05f)
					{
						bad++;
					}
					if (above > worst)
					{
						worst = above;
						where = "sample " + i + " (control " + s.Control + ") lateral " + lat.ToString("0.00") + " road y " + s.Pos.y.ToString("0.00") + " ground corner max " + (above + s.Pos.y).ToString("0.00") + " half " + s.Half.ToString("0.0");
						string corners = "";
						for (int c = 0; c < 4; c++)
						{
							Vector3 v = verts[(iz + (c / 2)) * t.Verts + ix + (c % 2)];
							float nearest = float.MaxValue;
							foreach (Sample q in Dense)
							{
								nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(q.Pos.x, q.Pos.z), new Vector2(v.x, v.z)));
							}
							corners += " [" + v.x.ToString("0") + "," + v.z.ToString("0") + " y " + v.y.ToString("0.0") + " nearest " + nearest.ToString("0.0") + "]";
						}
						where += " corners" + corners + " point " + p.x.ToString("0") + "," + p.z.ToString("0");
					}
				}
			}
			ModRuntime.Log.LogInfo("Map carve check: " + bad + " of " + total + " road points have a ground cell corner at or above the road; worst " + worst.ToString("0.00") + " above it at " + where);
		}

		// ---- guardrail ---------------------------------------------------------------------------------------------------------

		private void RebuildRails()
		{
			if (railObject != null)
			{
				UnityEngine.Object.Destroy(railObject);
				railObject = null;
			}
			rails.Clear();
			railChunk = 0;
			railInChunk = 0;
			for (int side = -1; side <= 1; side += 2)
			{
				var run = new List<Vector3>();
				for (int i = 0; i < Dense.Count; i++)
				{
					Sample s = Dense[i];
					bool on = side < 0 ? s.RailLeft : s.RailRight;
					if (on)
					{
						run.Add(s.Pos + s.Right * side * (s.Half + 1f));
					}
					else
					{
						SteelRun(run, side);
						run = new List<Vector3>();
					}
				}
				SteelRun(run, side);
			}
			if (rails.Count == 0 || Tm == null)
			{
				return;
			}
			Vector3 centre;
			float halfWidth, halfLength;
			MeasureCar(Tm, out centre, out halfWidth, out halfLength);
			railObject = new GameObject("HalfGripMap guardrail") { layer = Layer };
			railObject.AddComponent<RailField>().Setup(rails, MapMaterials.Make(Source, new Color(0.66f, 0.68f, 0.72f)), MapMaterials.Make(Source, new Color(0.36f, 0.37f, 0.40f)), Tm, centre, halfWidth, halfLength, Layer);
		}

		/// <summary>A stretch of guardrail along <paramref name="run"/>, cut into pieces of about 8 units, in world space.</summary>
		private void SteelRun(List<Vector3> run, int side)
		{
			if (run.Count < 2)
			{
				return;
			}
			float[] cum = Geo.Cumulative(run);
			float total = cum[cum.Length - 1];
			int n = Mathf.Max(1, Mathf.RoundToInt(total / 8f));
			float len = total / n;
			railChunk++;
			railInChunk = 0;
			int previous = -1;
			Quaternion frame = Frame;
			for (int k = 0; k < n; k++)
			{
				Vector3 pos, right;
				Geo.PointAt(run, cum, (k + 0.5f) * len, out pos, out right);
				if (railInChunk >= 16)
				{
					railChunk++;
					railInChunk = 0;
				}
				railInChunk++;
				if (previous >= 0)
				{
					RailPiece before = rails[previous];
					before.Next = rails.Count;
					rails[previous] = before;
				}
				// Geo.Right is to the right of the direction of travel; Out is on the side of the rail's own side of the road.
				rails.Add(new RailPiece
				{
					Pos = W(pos),
					Along = frame * new Vector3(-right.z, 0f, right.x),
					Out = frame * (right * side),
					Len = len,
					Chunk = railChunk,
					Prev = previous,
					Next = -1
				});
				previous = rails.Count - 1;
			}
		}

		/// <summary>The player car's footprint: the centre of its body in its own space, and half its width and length.</summary>
		internal static void MeasureCar(TrackManager tm, out Vector3 centre, out float halfWidth, out float halfLength)
		{
			centre = Vector3.zero;
			halfWidth = 2.65f;
			halfLength = 7.1f;
			try
			{
				Bounds bounds = default(Bounds);
				bool any = false;
				int interior = LayerMask.NameToLayer("carInterior");
				foreach (MeshRenderer r in tm.playerCarController.GetComponentsInChildren<MeshRenderer>())
				{
					if (r.gameObject.layer == interior)
					{
						continue;
					}
					if (!any)
					{
						bounds = r.bounds;
						any = true;
					}
					else
					{
						bounds.Encapsulate(r.bounds);
					}
				}
				if (any)
				{
					// The car stands facing +X on the grid, so its length is along x.
					centre = tm.playerCarController.transform.InverseTransformPoint(bounds.center);
					halfLength = bounds.size.x * 0.5f;
					halfWidth = bounds.size.z * 0.5f;
				}
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Maps: could not measure the car: " + e.Message);
			}
		}

		// ---- props -------------------------------------------------------------------------------------------------------------

		internal void RebuildProps()
		{
			if (props != null)
			{
				UnityEngine.Object.Destroy(props);
			}
			props = new GameObject("props") { layer = Layer };
			props.transform.SetParent(Root.transform, false);
			var batches = new Dictionary<string, Batch>();
			foreach (MapProp p in Doc.Props)
			{
				Stamp(batches, p);
			}
			foreach (Batch b in batches.Values)
			{
				Spawn(b, props.transform);
			}
		}

		private static Batch Get(Dictionary<string, Batch> batches, string name, Color color, bool collide, bool wall = false)
		{
			Batch b;
			if (!batches.TryGetValue(name, out b))
			{
				b = new Batch(name, color, collide, wall);
				batches[name] = b;
			}
			return b;
		}

		/// <summary>One prop, built from boxes and prisms. Sizes are in game units (a car is 14 long) and follow <see cref="MapProp.Scale"/>.</summary>
		private static void Stamp(Dictionary<string, Batch> batches, MapProp p)
		{
			Vector3 o = p.Pos;
			float s = p.Scale;
			Quaternion rot = Quaternion.Euler(0f, p.Rot, 0f);
			switch (p.Type)
			{
				case "tree":
					Get(batches, "trunks", new Color(0.30f, 0.20f, 0.11f), true).Box(o + Vector3.up * (5f * s), new Vector3(1.8f, 10f, 1.8f) * s, rot);
					Cone(Get(batches, "crowns", new Color(0.10f, 0.30f, 0.12f), false), o + Vector3.up * (6f * s), 6.5f * s, 17f * s, 7);
					break;
				case "cone":
					Cone(Get(batches, "cones", new Color(0.95f, 0.42f, 0.05f), true), o + Vector3.up * (0.3f * s), 1.5f * s, 3.4f * s, 8);
					Get(batches, "cone-feet", new Color(0.05f, 0.05f, 0.05f), true).Box(o + Vector3.up * (0.15f * s), new Vector3(3.4f, 0.3f, 3.4f) * s, rot);
					break;
				case "barrel":
					Get(batches, "barrels", new Color(0.72f, 0.12f, 0.08f), true).Cylinder(o, 1.6f * s, 3.6f * s, 10);
					break;
				case "block":
					Get(batches, "blocks", new Color(0.66f, 0.66f, 0.64f), true, true).Box(o + Vector3.up * (1.6f * s), new Vector3(12f, 3.2f, 3.4f) * s, rot);
					break;
				case "ramp":
					Ramp(Get(batches, "ramps", new Color(0.36f, 0.36f, 0.38f), true), o, rot, s);
					break;
				case "arch":
					Batch arch = Get(batches, "arches", new Color(0.88f, 0.76f, 0.12f), true);
					arch.Box(o + rot * new Vector3(0f, 7.5f * s, -22f * s), new Vector3(2.4f, 15f, 2.4f) * s, rot);
					arch.Box(o + rot * new Vector3(0f, 7.5f * s, 22f * s), new Vector3(2.4f, 15f, 2.4f) * s, rot);
					arch.Box(o + rot * new Vector3(0f, 15f * s, 0f), new Vector3(3f, 3f, 48f) * s, rot);
					break;
			}
		}

		private static void Cone(Batch batch, Vector3 baseCentre, float radius, float height, int sides)
		{
			Vector3 apex = baseCentre + Vector3.up * height;
			for (int i = 0; i < sides; i++)
			{
				float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
				Vector3 r0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
				Vector3 r1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
				batch.Tri(apex, baseCentre + r0, baseCentre + r1, r0 + r1 + Vector3.up * radius);
				batch.Tri(baseCentre, baseCentre + r0, baseCentre + r1, Vector3.down);
			}
		}

		/// <summary>A wedge that rises along its local +X: 22 long, 16 wide, 5.5 tall at the high end.</summary>
		private static void Ramp(Batch batch, Vector3 o, Quaternion rot, float s)
		{
			float length = 22f * s, width = 16f * s, height = 5.5f * s;
			Func<float, float, float, Vector3> P = (x, y, z) => o + rot * new Vector3(x - length * 0.5f, y, z);
			Vector3 a0 = P(0f, 0f, -width * 0.5f), a1 = P(0f, 0f, width * 0.5f);
			Vector3 b0 = P(length, 0f, -width * 0.5f), b1 = P(length, 0f, width * 0.5f);
			Vector3 t0 = P(length, height, -width * 0.5f), t1 = P(length, height, width * 0.5f);
			batch.Quad(a0, a1, t1, t0, Vector3.up);
			batch.Quad(b0, b1, t1, t0, rot * Vector3.right);
			batch.Tri(a0, b0, t0, rot * Vector3.back);
			batch.Tri(a1, b1, t1, rot * Vector3.forward);
		}
	}
}
