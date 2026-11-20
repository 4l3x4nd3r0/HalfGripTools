using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripTools.MapBuild;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HalfGripTools
{
	/// <summary>
	/// The Map Maker's panels as a real uGUI canvas, built the way the game builds its own menus: the red button picture copied from the main menu,
	/// the m6x11 pixel font in TextMeshPro, dark panels with a red edge, sliders and text fields in the same style. The panel is rebuilt whenever
	/// what it shows changes shape (a tool, a selection, a brush); live numbers are updated in place.
	/// </summary>
	internal sealed partial class EditorBehaviour
	{
		private Canvas canvas;
		private RectTransform leftRect, barRect, menuRoot;
		private RectTransform content;
		private TMP_Text barText;
		private string panelSignature = "";
		private readonly List<KeyValuePair<TMP_Text, Func<string>>> live = new List<KeyValuePair<TMP_Text, Func<string>>>();
		private readonly List<TMP_InputField> fields = new List<TMP_InputField>();
		private float nextLive;
		private bool uiTyping;

		private static TMP_FontAsset uiFont;
		private static Sprite redSprite, darkSprite, panelSprite, fieldSprite;
		private static readonly Color Ink = new Color(0.95f, 0.93f, 0.9f);
		private static readonly Color Accent = new Color(1f, 0.35f, 0.28f);

		// ---- the canvas -------------------------------------------------------------------------------------------------------------

		private void BuildUi()
		{
			if (canvas != null)
			{
				return;
			}
			MakeSprites();
			var go = new GameObject("HalfGripMapEditorUi", typeof(RectTransform));
			canvas = go.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 4000;
			CanvasScaler scaler = go.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			scaler.matchWidthOrHeight = 0.5f;
			go.AddComponent<GraphicRaycaster>();
			if (EventSystem.current == null)
			{
				var es = new GameObject("HalfGripMapEditorEvents");
				es.transform.SetParent(go.transform, false);
				es.AddComponent<EventSystem>();
				es.AddComponent<StandaloneInputModule>();
			}

			// the left panel: a scrolling column
			leftRect = Panel(go.transform, "LeftPanel", panelSprite);
			leftRect.anchorMin = new Vector2(0f, 0f);
			leftRect.anchorMax = new Vector2(0f, 1f);
			leftRect.pivot = new Vector2(0f, 0.5f);
			leftRect.offsetMin = new Vector2(16f, 16f);
			leftRect.offsetMax = new Vector2(16f + 600f, -16f);
			var view = new GameObject("View", typeof(RectTransform), typeof(RectMask2D));
			view.transform.SetParent(leftRect, false);
			var viewRect = (RectTransform)view.transform;
			Stretch(viewRect, 10f);
			var holder = new GameObject("Content", typeof(RectTransform));
			holder.transform.SetParent(viewRect, false);
			content = (RectTransform)holder.transform;
			content.anchorMin = new Vector2(0f, 1f);
			content.anchorMax = new Vector2(1f, 1f);
			content.pivot = new Vector2(0.5f, 1f);
			content.offsetMin = content.offsetMax = Vector2.zero;
			VerticalLayoutGroup column = holder.AddComponent<VerticalLayoutGroup>();
			column.spacing = 8f;
			column.padding = new RectOffset(4, 4, 4, 8);
			column.childControlWidth = true;
			column.childControlHeight = true;
			column.childForceExpandWidth = true;
			column.childForceExpandHeight = false;
			holder.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			ScrollRect scroll = leftRect.gameObject.AddComponent<ScrollRect>();
			scroll.viewport = viewRect;
			scroll.content = content;
			scroll.horizontal = false;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 40f;

			// the bar at the bottom
			barRect = Panel(go.transform, "Bar", panelSprite);
			barRect.anchorMin = new Vector2(0f, 0f);
			barRect.anchorMax = new Vector2(1f, 0f);
			barRect.pivot = new Vector2(0.5f, 0f);
			barRect.offsetMin = new Vector2(640f, 16f);
			barRect.offsetMax = new Vector2(-16f, 16f + 48f);
			barText = Text(barRect, "", 22f, Ink, TextAlignmentOptions.MidlineLeft);
			Stretch((RectTransform)barText.transform, 12f);

			BuildMenu(go.transform);
			panelSignature = "";
		}

		private void DestroyUi()
		{
			if (canvas != null)
			{
				UnityEngine.Object.Destroy(canvas.gameObject);
			}
			canvas = null;
			leftRect = barRect = menuRoot = content = null;
			live.Clear();
			fields.Clear();
		}

		// ---- per frame --------------------------------------------------------------------------------------------------------------

		private void UiTick()
		{
			if (canvas == null)
			{
				return;
			}
			bool show = open && !menuOpen;
			leftRect.gameObject.SetActive(show);
			barRect.gameObject.SetActive(show);
			menuRoot.gameObject.SetActive(open && menuOpen);
			if (!open)
			{
				return;
			}
			uiTyping = fields.Any(f => f != null && f.isFocused);
			string signature = tool + "|" + selPoint + "|" + selProp + "|" + selSpot + "|" + selLights + "|" + brushMode + "|" + propCategory + "|" + armedNew + "|" + (doc.Spots.Count > 0) + "|" + doc.HasLights;
			if (signature != panelSignature)
			{
				panelSignature = signature;
				RebuildPanel();
			}
			if (Time.unscaledTime >= nextLive)
			{
				nextLive = Time.unscaledTime + 0.2f;
				foreach (KeyValuePair<TMP_Text, Func<string>> pair in live)
				{
					if (pair.Key != null)
					{
						pair.Key.text = pair.Value();
					}
				}
			}
			barText.text = Time.unscaledTime < statusUntil ? status : "Esc menu   1-5 tools   T top view   Right mouse: look   WASD/QE fly   Scroll: speed";
			// where the panels are, in the units the rest of the editor uses, so a click on them is not a click on the map
			leftPanel = GuiRect(leftRect);
			bottomPanel = GuiRect(barRect);
		}

		private Rect GuiRect(RectTransform r)
		{
			var corners = new Vector3[4];
			r.GetWorldCorners(corners);
			float k = UiScale();
			return new Rect(corners[0].x / k, (Screen.height - corners[1].y) / k, (corners[2].x - corners[0].x) / k, (corners[1].y - corners[0].y) / k);
		}

		private void RebuildPanel()
		{
			for (int i = content.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
			}
			live.Clear();
			fields.Clear();
			TMP_Text title = Text(content, "<color=#ff5a46>MAP MAKER</color>  " + doc.Name, 26f, Ink, TextAlignmentOptions.MidlineLeft);
			live.Add(new KeyValuePair<TMP_Text, Func<string>>(title, () => "<color=#ff5a46>MAP MAKER</color>  " + doc.Name));
			Grid(content, new[] { "TRACK", "PROPS", "TERRAIN", "MAP", "START" }, (int)tool, i => { tool = (Tool)i; }, 3);
			Row(content, r =>
			{
				Button(r, "UNDO", Undo);
				Button(r, "REDO", Redo);
			});
			switch (tool)
			{
				case Tool.Track: TrackPanel(); break;
				case Tool.Props: PropsPanel(); break;
				case Tool.Terrain: TerrainPanel(); break;
				case Tool.Start: StartPanel(); break;
				default: MapPanel(); break;
			}
			LayoutRebuilder.ForceRebuildLayoutImmediate(content);
		}

		// ---- the Esc menu -----------------------------------------------------------------------------------------------------------

		private void BuildMenu(Transform parent)
		{
			var dim = new GameObject("Menu", typeof(RectTransform), typeof(Image));
			dim.transform.SetParent(parent, false);
			menuRoot = (RectTransform)dim.transform;
			Stretch(menuRoot, 0f);
			dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
			RectTransform box = Panel(menuRoot, "Box", panelSprite);
			box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
			box.sizeDelta = new Vector2(620f, 620f);
			var column = new GameObject("Column", typeof(RectTransform));
			column.transform.SetParent(box, false);
			var cr = (RectTransform)column.transform;
			Stretch(cr, 24f);
			VerticalLayoutGroup layout = column.AddComponent<VerticalLayoutGroup>();
			layout.spacing = 12f;
			layout.childControlWidth = layout.childControlHeight = true;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = false;
			TMP_Text title = Text(cr, "<color=#ff5a46>MAP MAKER</color>", 30f, Ink, TextAlignmentOptions.Center);
			live.Add(new KeyValuePair<TMP_Text, Func<string>>(title, () => "<color=#ff5a46>MAP MAKER</color>  " + doc.Name));
			Button(cr, "RESUME", () => { menuOpen = false; }, 56f);
			Row(cr, r => { Button(r, "UNDO", Undo, 52f); Button(r, "REDO", Redo, 52f); });
			Button(cr, "SAVE", () => { DoSave(); menuOpen = false; }, 56f);
			Button(cr, "TEST DRIVE", DoTest, 56f);
			Button(cr, MapMaker.Standalone ? "SAVE AND QUIT EDITOR" : "SAVE AND LEAVE EDITOR", () =>
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
			}, 56f);
			Button(cr, MapMaker.Standalone ? "QUIT EDITOR WITHOUT SAVING" : "LEAVE EDITOR WITHOUT SAVING", QuitEditor, 56f);
		}

		// ---- panels -----------------------------------------------------------------------------------------------------------------

		private void Changed()
		{
			dirtyTrack = true;
			rebuildAt = 0f;
		}

		private void TrackPanel()
		{
			Label("Click empty ground: add a point (on the road: between). Drag a point to move it. Delete removes it. [ ] or PgUp/PgDn change its height.");
			LiveLabel(() => "Points: " + doc.Track.Count + "   Lap: " + Spline.Length(world.Dense).ToString("0") + " units");
			if (selPoint < 0 || selPoint >= doc.Track.Count)
			{
				Label("No point selected.");
				return;
			}
			MapPoint p = doc.Track[selPoint];
			Label("Point " + selPoint + (selPoint == 0 ? " (the start line)" : ""));
			SliderRow("Width", p.Width, 12f, 120f, "0", true, v => { p.Width = Mathf.Round(v); Changed(); });
			LiveLabel(() => "Height " + p.Y.ToString("0.0"));
			Row(content, r =>
			{
				foreach (float step in new[] { -5f, -1f, 1f, 5f })
				{
					float d = step;
					Button(r, (d > 0 ? "+" : "") + d.ToString("0"), () => { p.Y += d; Changed(); });
				}
			});
			Button(content, "Surface: " + p.Surface.ToUpper(), null, 46f).onClick.AddListener(() =>
			{
				p.Surface = Surfaces.Next(p.Surface, 1);
				Changed();
				panelSignature = "";
			});
			Toggle("Guardrail left", p.RailLeft, v => { p.RailLeft = v; Changed(); });
			Toggle("Guardrail right", p.RailRight, v => { p.RailRight = v; Changed(); });
			Toggle("Invisible wall left", p.WallLeft, v => { p.WallLeft = v; Changed(); });
			Toggle("Invisible wall right", p.WallRight, v => { p.WallRight = v; Changed(); });
			Row(content, r =>
			{
				Button(r, "RAILS ALL ON", () => { SetAll(t => { t.RailLeft = true; t.RailRight = true; }); panelSignature = ""; });
				Button(r, "ALL OFF", () => { SetAll(t => { t.RailLeft = false; t.RailRight = false; }); panelSignature = ""; });
			});
			Row(content, r =>
			{
				Button(r, "WALLS ALL ON", () => { SetAll(t => { t.WallLeft = true; t.WallRight = true; }); panelSignature = ""; });
				Button(r, "ALL OFF", () => { SetAll(t => { t.WallLeft = false; t.WallRight = false; }); panelSignature = ""; });
			});
			Button(content, "Same width everywhere", () => { float w = p.Width; SetAll(t => t.Width = w); });
			Button(content, "Delete point", DeletePoint);
		}

		private void PropsPanel()
		{
			Label("Pick a kind, click the ground to place it. Click a prop to select and drag it. R / F turn it, Delete removes it.");
			propType = PropPickerUi(propType, 330f);
			SliderRow("Size", propScale, 0.4f, 4f, "0.0", false, v => { propScale = v; });
			SliderRow("Turn", propRot, 0f, 359f, "0", true, v => { propRot = v; });
			LiveLabel(() => "Props: " + doc.Props.Count + " of " + MapDocument.MaxProps);
			if (selProp >= 0 && selProp < doc.Props.Count)
			{
				MapProp p = doc.Props[selProp];
				Label("Selected: " + p.Type);
				SliderRow("Turn", p.Rot, 0f, 359f, "0", true, v => { p.Rot = v; dirtyProps = true; });
				SliderRow("Size", p.Scale, 0.4f, 4f, "0.0", false, v => { p.Scale = v; dirtyProps = true; });
				Button(content, "Delete prop", () =>
				{
					if (selProp >= 0 && selProp < doc.Props.Count)
					{
						doc.Props.RemoveAt(selProp);
					}
					selProp = -1;
					dirtyProps = true;
					rebuildAt = 0f;
				});
			}
		}

		private void StartPanel()
		{
			Label("The start grid and the lights. Drag a place or the lights to move them. Click empty ground to add a place (one more AI car each, up to " + MapDocument.MaxSpots + "). Delete removes the selected one. Cars face along the start line.");
			LiveLabel(() => (doc.Spots.Count > 0 ? "Your grid: " : "Automatic grid: ") + ShownSpots.Count + " places (P is yours)." + (doc.HasLights ? "  Lights moved." : ""));
			if (!selLights && selSpot > 0)
			{
				Button(content, "Delete place " + selSpot, DeleteSpot);
			}
			Button(content, "Automatic grid again", () => { doc.Spots.Clear(); selSpot = -1; });
			Button(content, "Lights back behind the line", () => { doc.HasLights = false; selLights = false; });
			Label("Test drive to see the cars on their places.");
		}

		private void TerrainPanel()
		{
			Label("Hold the left mouse on the ground. The ring is the brush: the inner ring is where it is strongest. The ground is always pressed down under the road.");
			Grid(content, BrushNames, brushMode, i => { brushMode = i; }, 3);
			SliderRow("Size", brushRadius, 20f, 260f, "0", true, v => { brushRadius = v; });
			if (brushMode <= 3)
			{
				SliderRow("Strength", brushStrength, 0.1f, 2f, "0.0", false, v => { brushStrength = v; });
				Label(brushMode == 0 ? "Raises the ground. A faint dome shows how much." : brushMode == 1 ? "Digs the ground down." : brushMode == 2 ? "Smooths bumps." : "Pulls the ground to the height under the first click; the plate shows it.");
			}
			else if (brushMode == 4)
			{
				Label("Paint the ground. Painted ground drives like it looks: ice is slippery, sand drags.");
				Grid(content, GroundTypes.Names.Select(n => n.ToUpper()).ToArray(), paintKind, i => { paintKind = i; }, 3);
				Button(content, "Paint everything with " + GroundTypes.Names[paintKind].ToUpper(), () =>
				{
					MapTerrain t = doc.Terrain;
					t.Paint = new byte[t.Verts * t.Verts];
					for (int i = 0; i < t.Paint.Length; i++) { t.Paint[i] = (byte)paintKind; }
					dirtyTerrain = true;
					rebuildAt = 0f;
				});
			}
			else
			{
				Label("Scatter props over the ground, off the road. Pick what to scatter:");
				scatterType = PropPickerUi(scatterType, 260f);
				SliderRow("Density", scatterDensity, 0.1f, 2f, "0.0", false, v => { scatterDensity = v; });
				SliderRow("Size", propScale, 0.4f, 4f, "0.0", false, v => { propScale = v; });
				LiveLabel(() => "Props: " + doc.Props.Count + " of " + MapDocument.MaxProps);
			}
			Button(content, "Flatten everything", () =>
			{
				Array.Clear(doc.Terrain.Heights, 0, doc.Terrain.Heights.Length);
				dirtyTerrain = true;
				rebuildAt = 0f;
			});
		}

		private void MapPanel()
		{
			Label("Name");
			InputField(nameField, 40, v => { nameField = v; });
			Button(content, "Weather: " + doc.Weather.ToUpper(), () =>
			{
				int i = Array.IndexOf(MapDocument.WeatherNames, doc.Weather);
				doc.Weather = MapDocument.WeatherNames[(i + 1) % MapDocument.WeatherNames.Length];
				Say("Weather: " + doc.Weather + ". Test drive to see it.");
				panelSignature = "";
			});
			Button(content, "SAVE", DoSave);
			Label("Save as a new map:");
			InputField(saveAsField ?? "", 40, v => { saveAsField = v; });
			Button(content, "SAVE AS", DoSaveAs);
			Button(content, "EXPORT AS A MOD FOLDER", DoExport);
			Button(content, "TEST DRIVE (saves, then starts a race on the map)", DoTest, 58f);
			Button(content, armedNew ? "Really start over with a new oval?" : "New oval (start over)", () =>
			{
				if (!armedNew)
				{
					armedNew = true;
					return;
				}
				armedNew = false;
				MapDocument fresh = MapDocument.Oval(doc.Id, doc.Name);
				doc.Track.Clear();
				doc.Track.AddRange(fresh.Track);
				doc.Props.Clear();
				Array.Clear(doc.Terrain.Heights, 0, doc.Terrain.Heights.Length);
				selPoint = selProp = -1;
				dirtyTrack = dirtyProps = true;
				rebuildAt = 0f;
			});
			Button(content, "CLOSE EDITOR (race as it stands)", MapEditor.Close);
			Label("Saved maps live in " + MapLibrary.UserFolder + ". Your own maps join your runs when the game starts.");
		}

		/// <summary>The category tabs and a scrolling list of everything that can be placed; returns the chosen id.</summary>
		private string PropPickerUi(string current, float height)
		{
			string chosen = current;
			MapPropCatalog.DiscoverGame();
			var categories = new List<string> { "Shapes" };
			categories.AddRange(MapPropCatalog.All.Select(p => p.Category).Distinct());
			if (propCategory == null || !categories.Contains(propCategory))
			{
				propCategory = "Shapes";
			}
			Grid(content, categories.Select(c => c.ToUpper()).ToArray(), categories.IndexOf(propCategory), i => { propCategory = categories[i]; }, 3);
			string[] ids, names;
			if (propCategory == "Shapes")
			{
				ids = PropTypes.Names;
				names = ids.Select(n => n.ToUpper()).ToArray();
			}
			else
			{
				List<CatalogProp> list = MapPropCatalog.All.Where(p => p.Category == propCategory).ToList();
				ids = list.Select(p => p.Id).ToArray();
				names = list.Select(p => p.Name.ToUpper()).ToArray();
			}
			// a short scrolling list inside the panel
			var box = new GameObject("Picker", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(LayoutElement));
			box.transform.SetParent(content, false);
			box.GetComponent<Image>().sprite = fieldSprite;
			box.GetComponent<Image>().type = Image.Type.Sliced;
			box.GetComponent<Image>().pixelsPerUnitMultiplier = 0.5f;
			box.GetComponent<LayoutElement>().preferredHeight = height;
			var inner = new GameObject("Items", typeof(RectTransform));
			inner.transform.SetParent(box.transform, false);
			var ir = (RectTransform)inner.transform;
			ir.anchorMin = new Vector2(0f, 1f);
			ir.anchorMax = new Vector2(1f, 1f);
			ir.pivot = new Vector2(0.5f, 1f);
			ir.offsetMin = new Vector2(4f, 0f);
			ir.offsetMax = new Vector2(-4f, 0f);
			GridLayoutGroup grid = inner.AddComponent<GridLayoutGroup>();
			grid.cellSize = new Vector2(264f, 42f);
			grid.spacing = new Vector2(6f, 6f);
			grid.padding = new RectOffset(0, 0, 4, 4);
			inner.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			ScrollRect scroll = box.AddComponent<ScrollRect>();
			scroll.viewport = (RectTransform)box.transform;
			scroll.content = ir;
			scroll.horizontal = false;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 30f;
			for (int i = 0; i < ids.Length; i++)
			{
				string id = ids[i];
				Button b = MakeButton(inner.transform, names[i], () => { chosen = id; if (inner != null) { MarkPicked(inner.transform, id); } PickedProp(id); }, 42f, id == current);
				b.gameObject.name = id;
			}
			return chosen;
		}

		private void MarkPicked(Transform items, string id)
		{
			for (int i = 0; i < items.childCount; i++)
			{
				Image image = items.GetChild(i).GetComponent<Image>();
				if (image != null)
				{
					image.sprite = items.GetChild(i).name == id ? redSprite : darkSprite;
				}
			}
		}

		/// <summary>A prop was picked in the list: it goes to the prop tool or the scatter brush, whichever is out.</summary>
		private void PickedProp(string id)
		{
			if (tool == Tool.Terrain)
			{
				scatterType = id;
			}
			else
			{
				propType = id;
			}
		}

		// ---- widgets ----------------------------------------------------------------------------------------------------------------

		private static void MakeSprites()
		{
			if (redSprite != null)
			{
				return;
			}
			uiFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name.StartsWith("m6x11", StringComparison.OrdinalIgnoreCase)) ?? TMP_Settings.defaultFontAsset;
			Texture2D red, dark, panel, field;
			RectOffset border;
			EditorSkin.Textures(out red, out border, out dark, out panel, out field);
			redSprite = Slice(red, border);
			darkSprite = Slice(dark, new RectOffset(1, 1, 1, 1));
			panelSprite = Slice(panel, new RectOffset(1, 1, 1, 1));
			fieldSprite = Slice(field, new RectOffset(1, 1, 1, 1));
		}

		private static Sprite Slice(Texture2D t, RectOffset border)
		{
			return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border.left, border.bottom, border.right, border.top));
		}

		private static void Stretch(RectTransform r, float margin)
		{
			r.anchorMin = Vector2.zero;
			r.anchorMax = Vector2.one;
			r.offsetMin = new Vector2(margin, margin);
			r.offsetMax = new Vector2(-margin, -margin);
		}

		private static RectTransform Panel(Transform parent, string name, Sprite sprite)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			Image image = go.GetComponent<Image>();
			image.sprite = sprite;
			image.type = Image.Type.Sliced;
			image.pixelsPerUnitMultiplier = 0.5f;
			return (RectTransform)go.transform;
		}

		private static TMP_Text Text(Transform parent, string text, float size, Color colour, TextAlignmentOptions align)
		{
			var go = new GameObject("Text", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
			t.font = uiFont;
			t.fontSize = size;
			t.text = text;
			t.color = colour;
			t.alignment = align;
			t.richText = true;
			t.raycastTarget = false;
			t.textWrappingMode = TextWrappingModes.Normal;
			return t;
		}

		private void Label(string text)
		{
			TMP_Text t = Text(content, text, 22f, Ink, TextAlignmentOptions.TopLeft);
			t.margin = new Vector4(2f, 2f, 2f, 2f);
		}

		private void LiveLabel(Func<string> text)
		{
			TMP_Text t = Text(content, text(), 22f, Ink, TextAlignmentOptions.TopLeft);
			live.Add(new KeyValuePair<TMP_Text, Func<string>>(t, text));
		}

		private static void Row(Transform parent, Action<Transform> fill)
		{
			var go = new GameObject("Row", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			HorizontalLayoutGroup h = go.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 6f;
			h.childControlWidth = h.childControlHeight = true;
			h.childForceExpandWidth = true;
			h.childForceExpandHeight = false;
			fill(go.transform);
		}

		private static Button MakeButton(Transform parent, string label, Action click, float height, bool on)
		{
			var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
			go.transform.SetParent(parent, false);
			Image image = go.GetComponent<Image>();
			image.sprite = on ? redSprite : darkSprite;
			image.type = Image.Type.Sliced;
			image.pixelsPerUnitMultiplier = 0.5f;
			Button button = go.GetComponent<Button>();
			button.targetGraphic = image;
			ColorBlock colours = button.colors;
			colours.normalColor = Color.white;
			colours.highlightedColor = new Color(1.25f, 1.15f, 1.15f);
			colours.pressedColor = new Color(0.7f, 0.7f, 0.7f);
			colours.selectedColor = Color.white;
			colours.fadeDuration = 0.05f;
			button.colors = colours;
			if (click != null)
			{
				button.onClick.AddListener(() => click());
			}
			go.GetComponent<LayoutElement>().preferredHeight = height;
			go.GetComponent<LayoutElement>().flexibleWidth = 1f;
			TMP_Text text = Text(go.transform, label, 22f, Color.white, TextAlignmentOptions.Center);
			Stretch((RectTransform)text.transform, 6f);
			text.textWrappingMode = TextWrappingModes.Normal;
			return button;
		}

		private Button Button(Transform parent, string label, Action click, float height = 46f)
		{
			return MakeButton(parent, label, click, height, true);
		}

		/// <summary>One of several choices: the chosen one is red, the others dark.</summary>
		private void Grid(Transform parent, string[] names, int chosen, Action<int> pick, int columns)
		{
			var go = new GameObject("Grid", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			GridLayoutGroup grid = go.AddComponent<GridLayoutGroup>();
			grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
			grid.constraintCount = columns;
			grid.spacing = new Vector2(6f, 6f);
			float width = 584f - (columns - 1) * 6f;
			grid.cellSize = new Vector2(Mathf.Floor(width / columns), 44f);
			go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			var buttons = new List<Button>();
			for (int i = 0; i < names.Length; i++)
			{
				int index = i;
				Button b = MakeButton(go.transform, names[i], null, 44f, i == chosen);
				buttons.Add(b);
				b.onClick.AddListener(() =>
				{
					pick(index);
					for (int k = 0; k < buttons.Count; k++)
					{
						buttons[k].GetComponent<Image>().sprite = k == index ? redSprite : darkSprite;
					}
				});
			}
		}

		private void Toggle(string label, bool value, Action<bool> change)
		{
			bool state = value;
			Button b = MakeButton(content, label + (state ? "   ON" : "   OFF"), null, 44f, state);
			TMP_Text text = b.GetComponentInChildren<TMP_Text>();
			text.alignment = TextAlignmentOptions.MidlineLeft;
			b.onClick.AddListener(() =>
			{
				state = !state;
				change(state);
				b.GetComponent<Image>().sprite = state ? redSprite : darkSprite;
				text.text = label + (state ? "   ON" : "   OFF");
			});
		}

		private void SliderRow(string label, float value, float min, float max, string format, bool whole, Action<float> change)
		{
			var row = new GameObject("Slider", typeof(RectTransform), typeof(LayoutElement));
			row.transform.SetParent(content, false);
			row.GetComponent<LayoutElement>().preferredHeight = 44f;
			HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 10f;
			h.childControlWidth = h.childControlHeight = true;
			h.childForceExpandWidth = false;
			h.childForceExpandHeight = true;
			TMP_Text text = Text(row.transform, label + " " + value.ToString(format), 22f, Ink, TextAlignmentOptions.MidlineLeft);
			text.gameObject.AddComponent<LayoutElement>().preferredWidth = 210f;

			var sliderObject = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(Slider), typeof(LayoutElement));
			sliderObject.transform.SetParent(row.transform, false);
			sliderObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
			Image track = sliderObject.GetComponent<Image>();
			track.sprite = fieldSprite;
			track.type = Image.Type.Sliced;
			track.pixelsPerUnitMultiplier = 0.5f;
			var area = new GameObject("Area", typeof(RectTransform));
			area.transform.SetParent(sliderObject.transform, false);
			var ar = (RectTransform)area.transform;
			ar.anchorMin = new Vector2(0f, 0f);
			ar.anchorMax = new Vector2(1f, 1f);
			ar.offsetMin = new Vector2(12f, 8f);
			ar.offsetMax = new Vector2(-12f, -8f);
			var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
			fill.transform.SetParent(area.transform, false);
			fill.GetComponent<Image>().sprite = redSprite;
			fill.GetComponent<Image>().type = Image.Type.Sliced;
			fill.GetComponent<Image>().pixelsPerUnitMultiplier = 0.5f;
			var fr = (RectTransform)fill.transform;
			fr.anchorMin = new Vector2(0f, 0f);
			fr.anchorMax = new Vector2(0f, 1f);
			fr.offsetMin = fr.offsetMax = Vector2.zero;
			var handleArea = new GameObject("Handle Area", typeof(RectTransform));
			handleArea.transform.SetParent(sliderObject.transform, false);
			Stretch((RectTransform)handleArea.transform, 0f);
			((RectTransform)handleArea.transform).offsetMin = new Vector2(12f, 0f);
			((RectTransform)handleArea.transform).offsetMax = new Vector2(-12f, 0f);
			var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
			handle.transform.SetParent(handleArea.transform, false);
			handle.GetComponent<Image>().sprite = redSprite;
			handle.GetComponent<Image>().type = Image.Type.Sliced;
			handle.GetComponent<Image>().pixelsPerUnitMultiplier = 0.5f;
			((RectTransform)handle.transform).sizeDelta = new Vector2(22f, 0f);
			((RectTransform)handle.transform).anchorMin = new Vector2(0f, 0f);
			((RectTransform)handle.transform).anchorMax = new Vector2(0f, 1f);
			Slider slider = sliderObject.GetComponent<Slider>();
			slider.fillRect = fr;
			slider.handleRect = (RectTransform)handle.transform;
			slider.targetGraphic = handle.GetComponent<Image>();
			slider.minValue = min;
			slider.maxValue = max;
			slider.wholeNumbers = whole;
			slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
			slider.onValueChanged.AddListener(v =>
			{
				text.text = label + " " + v.ToString(format);
				change(v);
			});
		}

		private void InputField(string value, int limit, Action<string> change)
		{
			var go = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
			go.transform.SetParent(content, false);
			go.GetComponent<LayoutElement>().preferredHeight = 46f;
			Image bg = go.GetComponent<Image>();
			bg.sprite = fieldSprite;
			bg.type = Image.Type.Sliced;
			bg.pixelsPerUnitMultiplier = 0.5f;
			var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
			area.transform.SetParent(go.transform, false);
			Stretch((RectTransform)area.transform, 8f);
			TMP_Text text = Text(area.transform, "", 22f, new Color(1f, 0.95f, 0.85f), TextAlignmentOptions.MidlineLeft);
			Stretch((RectTransform)text.transform, 0f);
			text.textWrappingMode = TextWrappingModes.NoWrap;
			TMP_InputField field = go.GetComponent<TMP_InputField>();
			field.textViewport = (RectTransform)area.transform;
			field.textComponent = text;
			field.characterLimit = limit;
			field.text = value;
			field.caretColor = Accent;
			field.selectionColor = new Color(0.8f, 0.2f, 0.2f, 0.6f);
			field.onValueChanged.AddListener(v => change(v));
			fields.Add(field);
		}
	}
}
