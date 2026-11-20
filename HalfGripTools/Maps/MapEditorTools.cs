using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalfGripTools.MapBuild;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>The terrain tool's aim ring and preview, the paint and scatter brushes, and the prop list.</summary>
	internal sealed partial class EditorBehaviour
	{
		// 0 raise, 1 lower, 2 smooth, 3 flatten, 4 paint, 5 scatter
		private int paintKind = 1;
		private string scatterType = "tree";
		private float scatterDensity = 0.5f;
		private float nextScatter;

		private GameObject brushRoot;
		private LineRenderer ringOuter, ringInner;
		private MeshFilter previewFilter;
		private MeshRenderer previewRenderer;
		private Vector2 propScroll;
		private string propCategory = "Shapes";

		private const int RingPoints = 64;
		private const int PreviewRings = 7;
		private const int PreviewSides = 28;

		private static readonly string[] AllBrushNames = { "RAISE", "LOWER", "SMOOTH", "FLATTEN", "PAINT", "SCATTER" };

		private static Color BrushColor(int mode, int paint)
		{
			switch (mode)
			{
				case 0: return new Color(0.3f, 0.95f, 0.4f);
				case 1: return new Color(1f, 0.35f, 0.3f);
				case 2: return new Color(0.4f, 0.65f, 1f);
				case 3: return new Color(1f, 0.9f, 0.3f);
				case 4: return MapBuild.GroundTypes.ColorOf(paint);
				default: return new Color(0.5f, 1f, 0.6f);
			}
		}

		/// <summary>The ground height under a point of the map (in the map's own coordinates), from a ray straight down; false if there is none.</summary>
		private bool GroundAt(float x, float z, out float y)
		{
			Vector3 from = world.W(new Vector3(x, 400f, z));
			RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 900f, ~0, QueryTriggerInteraction.Ignore);
			float best = float.MaxValue;
			y = 0f;
			bool found = false;
			Transform props = world.PropsRoot;
			foreach (RaycastHit hit in hits)
			{
				if (hit.collider == null || !hit.collider.transform.IsChildOf(world.Root.transform) || hit.collider.name == "void floor"
					|| (props != null && hit.collider.transform.IsChildOf(props)))
				{
					continue;
				}
				if (hit.distance < best)
				{
					best = hit.distance;
					y = world.D(hit.point).y;
					found = true;
				}
			}
			return found;
		}

		private void EnsureBrush()
		{
			if (brushRoot != null)
			{
				return;
			}
			brushRoot = new GameObject("HalfGripMapBrush");
			Shader shader = Shader.Find("Sprites/Default");
			Material line = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			ringOuter = MakeRing("outer", line, 1f);
			ringInner = MakeRing("inner", line, 0.55f);
			var disc = new GameObject("preview");
			disc.transform.SetParent(brushRoot.transform, false);
			previewFilter = disc.AddComponent<MeshFilter>();
			previewRenderer = disc.AddComponent<MeshRenderer>();
			previewRenderer.sharedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			previewRenderer.sortingOrder = 100;
			previewFilter.sharedMesh = new Mesh { name = "brush preview", hideFlags = HideFlags.HideAndDontSave };
			// A fixed grid of vertices that is moved every frame: the centre, then rings, then the triangles between them.
			var tris = new List<int>();
			for (int r = 0; r < PreviewRings; r++)
			{
				for (int s = 0; s < PreviewSides; s++)
				{
					int a = r == 0 ? 0 : 1 + (r - 1) * PreviewSides + s;
					int b = r == 0 ? 0 : 1 + (r - 1) * PreviewSides + (s + 1) % PreviewSides;
					int c = 1 + r * PreviewSides + s;
					int d = 1 + r * PreviewSides + (s + 1) % PreviewSides;
					if (r == 0)
					{
						tris.Add(0); tris.Add(c); tris.Add(d);
						tris.Add(0); tris.Add(d); tris.Add(c);
					}
					else
					{
						tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
						tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c);
					}
				}
			}
			var verts = new Vector3[1 + PreviewRings * PreviewSides];
			previewFilter.sharedMesh.vertices = verts;
			previewFilter.sharedMesh.colors = new Color[verts.Length];
			previewFilter.sharedMesh.triangles = tris.ToArray();
		}

		private LineRenderer MakeRing(string name, Material material, float alpha)
		{
			var go = new GameObject(name);
			go.transform.SetParent(brushRoot.transform, false);
			LineRenderer line = go.AddComponent<LineRenderer>();
			line.sharedMaterial = material;
			line.loop = true;
			line.useWorldSpace = true;
			line.positionCount = RingPoints;
			line.numCornerVertices = 2;
			line.sortingOrder = 101;
			return line;
		}

		/// <summary>The ring and the preview, under the mouse, while the terrain tool is out and nothing is in the way.</summary>
		private void BrushTick(bool overUi)
		{
			if (tool != Tool.Terrain || overUi || menuOpen)
			{
				HideBrush();
				return;
			}
			Vector3 ground;
			if (!Pick(out ground))
			{
				HideBrush();
				return;
			}
			EnsureBrush();
			brushRoot.SetActive(true);
			Vector3 c = world.D(ground);
			Color color = BrushColor(brushMode, paintKind);
			float widthOuter = Mathf.Max(0.7f, brushRadius * 0.012f);
			ringOuter.startWidth = ringOuter.endWidth = widthOuter;
			ringInner.startWidth = ringInner.endWidth = widthOuter * 0.6f;
			ringOuter.startColor = ringOuter.endColor = new Color(color.r, color.g, color.b, 0.95f);
			ringInner.startColor = ringInner.endColor = new Color(color.r, color.g, color.b, 0.5f);
			for (int i = 0; i < RingPoints; i++)
			{
				float a = i * Mathf.PI * 2f / RingPoints;
				float ox = Mathf.Cos(a), oz = Mathf.Sin(a);
				float yo, yi;
				if (!GroundAt(c.x + ox * brushRadius, c.z + oz * brushRadius, out yo)) { yo = c.y; }
				if (!GroundAt(c.x + ox * brushRadius * 0.55f, c.z + oz * brushRadius * 0.55f, out yi)) { yi = c.y; }
				ringOuter.SetPosition(i, world.W(new Vector3(c.x + ox * brushRadius, yo + 0.6f, c.z + oz * brushRadius)));
				ringInner.SetPosition(i, world.W(new Vector3(c.x + ox * brushRadius * 0.55f, yi + 0.6f, c.z + oz * brushRadius * 0.55f)));
			}
			UpdatePreview(c, color);
		}

		private void HideBrush()
		{
			if (brushRoot != null && brushRoot.activeSelf)
			{
				brushRoot.SetActive(false);
			}
		}

		/// <summary>
		/// A faint disc over the ground showing what a stroke here would do: a dome that rises for RAISE and sinks for LOWER (drawn bigger than one
		/// stroke so it can be seen), a flat plate at the height FLATTEN will pull to, the colour of the ground for PAINT, a green wash for SCATTER.
		/// </summary>
		private void UpdatePreview(Vector3 c, Color color)
		{
			Mesh mesh = previewFilter.sharedMesh;
			var verts = new Vector3[1 + PreviewRings * PreviewSides];
			var colors = new Color[verts.Length];
			MapTerrain t = doc.Terrain;
			float centreHeight;
			if (!GroundAt(c.x, c.z, out centreHeight)) { centreHeight = c.y; }
			float target = centreHeight;
			if (brushMode == 3 && !painting)
			{
				target = t.At(Mathf.RoundToInt((c.x + t.Size * 0.5f) / t.Cell), Mathf.RoundToInt((c.z + t.Size * 0.5f) / t.Cell));
			}
			else if (brushMode == 3)
			{
				target = flattenTo;
			}
			for (int r = 0; r <= PreviewRings; r++)
			{
				for (int s = 0; s < (r == 0 ? 1 : PreviewSides); s++)
				{
					float f = r / (float)PreviewRings;
					float a = s * Mathf.PI * 2f / PreviewSides;
					float x = c.x + Mathf.Cos(a) * brushRadius * f, z = c.z + Mathf.Sin(a) * brushRadius * f;
					float y;
					if (!GroundAt(x, z, out y)) { y = centreHeight; }
					float w = Mathf.SmoothStep(0f, 1f, 1f - f);
					float lift = 0.5f;
					switch (brushMode)
					{
						case 0: lift += brushStrength * w * 3.5f; break;
						case 1: lift -= brushStrength * w * 3.5f; break;
						case 3: lift = Mathf.Max(0.5f, 0.5f + (target - y)); break;
					}
					int index = r == 0 ? 0 : 1 + (r - 1) * PreviewSides + s;
					verts[index] = world.W(new Vector3(x, y + lift, z));
					colors[index] = new Color(color.r, color.g, color.b, brushMode == 4 || brushMode == 5 ? 0.45f : 0.3f * (0.4f + 0.6f * w));
				}
			}
			// the loop above indexes ring r as r-1; ring 0 (the centre) is vertex 0
			mesh.vertices = verts;
			mesh.colors = colors;
			mesh.RecalculateBounds();
		}

		/// <summary>The paint brush: sets the kind of ground on the vertices under the brush.</summary>
		private void PaintGround(MapTerrain t, Vector3 c)
		{
			float cell = t.Cell, half = t.Size * 0.5f;
			if (t.Paint == null || t.Paint.Length != t.Verts * t.Verts)
			{
				t.Paint = new byte[t.Verts * t.Verts];
			}
			int rc = Mathf.CeilToInt(brushRadius / cell);
			int cx = Mathf.RoundToInt((c.x + half) / cell), cz = Mathf.RoundToInt((c.z + half) / cell);
			for (int iz = Mathf.Max(0, cz - rc); iz <= Mathf.Min(t.Cells, cz + rc); iz++)
			{
				for (int ix = Mathf.Max(0, cx - rc); ix <= Mathf.Min(t.Cells, cx + rc); ix++)
				{
					float dx = -half + ix * cell - c.x, dz = -half + iz * cell - c.z;
					if (Mathf.Sqrt(dx * dx + dz * dz) <= brushRadius)
					{
						t.Paint[iz * t.Verts + ix] = (byte)paintKind;
					}
				}
			}
		}

		/// <summary>The scatter brush: drops props of the chosen kind at random inside the brush, off the road and not on top of each other.</summary>
		private void Scatter(Vector3 c)
		{
			int want = Mathf.Max(1, Mathf.RoundToInt(scatterDensity * brushRadius * brushRadius / 1500f));
			int placed = 0;
			for (int attempt = 0; attempt < want * 6 && placed < want; attempt++)
			{
				if (doc.Props.Count >= MapDocument.MaxProps)
				{
					Say("The map already has the most props it can (" + MapDocument.MaxProps + ").");
					break;
				}
				Vector2 p = UnityEngine.Random.insideUnitCircle * brushRadius;
				float x = Clamp(c.x + p.x), z = Clamp(c.z + p.y);
				if (NearRoad(x, z) || NearProp(x, z, 5f))
				{
					continue;
				}
				float y;
				if (!GroundAt(x, z, out y))
				{
					continue;
				}
				doc.Props.Add(new MapProp { Type = scatterType, X = x, Y = y, Z = z, Rot = UnityEngine.Random.Range(0f, 360f), Scale = Mathf.Clamp(propScale * UnityEngine.Random.Range(0.8f, 1.25f), 0.3f, 6f) });
				placed++;
			}
			if (placed > 0)
			{
				dirtyProps = true;
				rebuildAt = 0f;
			}
		}

		private bool NearRoad(float x, float z)
		{
			foreach (Sample s in world.Dense)
			{
				float dx = s.Pos.x - x, dz = s.Pos.z - z;
				float reach = s.Half + 8f;
				if (dx * dx + dz * dz < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		private bool NearProp(float x, float z, float distance)
		{
			float limit = distance * distance;
			for (int i = Mathf.Max(0, doc.Props.Count - 400); i < doc.Props.Count; i++)
			{
				float dx = doc.Props[i].X - x, dz = doc.Props[i].Z - z;
				if (dx * dx + dz * dz < limit)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A scrolling list of everything that can be placed, by category: the built-in shapes, then the game's pieces and other mods'.</summary>
		private string PropPicker(string current, int height)
		{
			if (propCategory == null)
			{
				propCategory = "Shapes";
			}
			MapBuild.MapPropCatalog.DiscoverGame();
			var categories = new List<string> { "Shapes" };
			categories.AddRange(MapBuild.MapPropCatalog.All.Select(p => p.Category).Distinct());
			int ci = Mathf.Max(0, categories.IndexOf(propCategory));
			ci = GUILayout.SelectionGrid(ci, categories.Select(n => n.ToUpper()).ToArray(), 2);
			propCategory = categories[ci];
			string[] ids, names;
			if (propCategory == "Shapes")
			{
				ids = MapBuild.PropTypes.Names;
				names = ids.Select(n => n.ToUpper()).ToArray();
			}
			else
			{
				List<MapBuild.CatalogProp> list = MapBuild.MapPropCatalog.All.Where(p => p.Category == propCategory).ToList();
				ids = list.Select(p => p.Id).ToArray();
				names = list.Select(p => p.Name.ToUpper()).ToArray();
			}
			propScroll = GUILayout.BeginScrollView(propScroll, GUILayout.Height(height));
			int index = Array.IndexOf(ids, current);
			int pick = GUILayout.SelectionGrid(index, names, 2);
			GUILayout.EndScrollView();
			return pick >= 0 && pick < ids.Length ? ids[pick] : current;
		}

		// ---- test aid: pictures of the real screen, panels included ----------------------------------------------------------------

		/// <summary>A mouse position to use instead of the real one (the test aid has no mouse).</summary>
		private Vector3? fakeMouse;

		private Vector3 MouseNow()
		{
			return fakeMouse ?? Input.mousePosition;
		}

		private string pendingShot;

		private System.Collections.IEnumerator ScreenShot(string path)
		{
			// A headless game has no screen to capture, so the next repaint draws the scene and the panels into a texture instead.
			if (Application.isBatchMode)
			{
				// A headless game never calls OnGUI, so there is no picture of the panels to take.
				yield break;
			}
			pendingShot = path;
			float until = Time.realtimeSinceStartup + 5f;
			while (pendingShot != null && Time.realtimeSinceStartup < until)
			{
				yield return null;
			}
			pendingShot = null;
		}

		private void TakeUiShot()
		{
			string path = pendingShot;
			pendingShot = null;
			RenderTexture target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
			RenderTexture previous = RenderTexture.active;
			try
			{
				cam.targetTexture = target;
				cam.Render();
				cam.targetTexture = null;
				RenderTexture.active = target;
				DrawUi();
				var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
				picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
				picture.Apply();
				File.WriteAllBytes(path, picture.EncodeToPNG());
				UnityEngine.Object.Destroy(picture);
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Map Maker: UI shot failed: " + e.Message);
			}
			finally
			{
				cam.targetTexture = null;
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(target);
			}
		}

		/// <summary>Walks the tools with a pretend mouse and saves the screen each time (<c>HALFGRIPTOOLS_MAPSHOT</c>), to check the panels, the brush ring and the painted ground without a window.</summary>
		private System.Collections.IEnumerator UiShots(string folder)
		{
			cam.nearClipPlane = 1f;
			topDown = false;
			string buttons = MapBuild.EditorSkin.DumpButtons(folder);
			ModRuntime.Log.LogInfo("Map Maker: editor skin built, buttons from " + buttons);
			Vector3 start = world.W(doc.Track[0].Pos);
			cam.transform.position = start + new Vector3(-70f, 45f, 0f);
			cam.transform.rotation = Quaternion.LookRotation((start + new Vector3(30f, 0f, 0f)) - cam.transform.position, Vector3.up);
			fakeMouse = new Vector3(Screen.width * 0.62f, Screen.height * 0.45f, 0f);
			yield return new WaitForSecondsRealtime(0.5f);
			tool = Tool.Track;
			yield return StartCoroutine(ScreenShot(Path.Combine(folder, "ui_track.png")));
			tool = Tool.Props;
			yield return new WaitForSecondsRealtime(0.3f);
			yield return StartCoroutine(ScreenShot(Path.Combine(folder, "ui_props.png")));
			tool = Tool.Terrain;
			brushMode = 0;
			brushRadius = 60f;
			yield return new WaitForSecondsRealtime(0.4f);
			yield return StartCoroutine(ScreenShot(Path.Combine(folder, "ui_raise.png")));
			brushMode = 4;
			paintKind = 3;
			Vector3 ground;
			if (Pick(out ground))
			{
				PaintGround(doc.Terrain, world.D(ground));
				dirtyTerrain = true;
				rebuildAt = 0f;
			}
			yield return new WaitForSecondsRealtime(0.6f);
			yield return StartCoroutine(ScreenShot(Path.Combine(folder, "ui_paint.png")));
			brushMode = 5;
			scatterType = MapBuild.MapPropCatalog.Find("highway.cone") != null ? "highway.cone" : "cone";
			scatterDensity = 1.5f;
			if (Pick(out ground))
			{
				Scatter(world.D(ground));
			}
			yield return new WaitForSecondsRealtime(0.6f);
			yield return StartCoroutine(ScreenShot(Path.Combine(folder, "ui_scatter.png")));
			tool = Tool.Map;
			menuOpen = true;
			yield return new WaitForSecondsRealtime(0.4f);
			yield return StartCoroutine(ScreenShot(Path.Combine(folder, "ui_menu.png")));
			menuOpen = false;
			fakeMouse = null;
			ModRuntime.Log.LogInfo("Map Maker: UI shots written to " + folder + "; painted vertices: " + (doc.Terrain.Paint == null ? 0 : doc.Terrain.Paint.Count(v => v != 0)) + ", props " + doc.Props.Count);
		}
	}
}
