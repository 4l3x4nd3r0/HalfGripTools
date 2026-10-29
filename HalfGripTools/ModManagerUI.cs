using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HalfGripTools
{
	/// <summary>
	/// The mod manager: a panel over the main menu that lists the installed mods, switches them on and off, and keeps playlists
	/// (named sets of mods). Changes are saved at once and take effect the next time the game starts.
	/// </summary>
	internal sealed class ModManagerUI : MonoBehaviour
	{
		private static readonly Color Dim = new Color(0f, 0f, 0f, 0.8f);
		private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.1f, 1f);
		private static readonly Color Border = new Color(0.3f, 0.3f, 0.36f, 1f);
		private static readonly Color RowColor = new Color(0.13f, 0.13f, 0.16f, 1f);
		private static readonly Color Red = new Color(0.82f, 0.16f, 0.16f, 1f);
		private static readonly Color Green = new Color(0.25f, 0.72f, 0.35f, 1f);
		private static readonly Color Grey = new Color(0.35f, 0.35f, 0.4f, 1f);
		private static readonly Color Amber = new Color(0.95f, 0.72f, 0.2f, 1f);
		private static readonly Color Soft = new Color(0.62f, 0.62f, 0.68f, 1f);

		private static ModManagerUI open;

		private TMP_FontAsset font;

		private RectTransform playlistColumn;

		private RectTransform modContent;

		private TMP_Text subtitle;

		private TMP_InputField nameField;

		private GameObject nameRow;

		internal static void Open()
		{
			if (open != null || !ModLoader.Running)
			{
				return;
			}
			GameObject root = new GameObject("HalfGripTools Mod Manager", typeof(RectTransform));
			open = root.AddComponent<ModManagerUI>();
			open.Build();
		}

		internal static void Close()
		{
			if (open != null)
			{
				Destroy(open.gameObject);
				open = null;
			}
		}

		private void OnDestroy()
		{
			GameInput.Mute(this, muted: false);
			if (open == this)
			{
				open = null;
			}
		}

		private void Update()
		{
			if (Input.GetKeyUp(KeyCode.Escape))
			{
				Close();
			}
		}

		// ---------------------------------------------------------------- layout

		private void Build()
		{
			GameInput.Mute(this, muted: true);
			if (EventSystem.current != null)
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
			font = FindFont();

			Canvas canvas = gameObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30000;
			CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(2560f, 1440f);
			scaler.matchWidthOrHeight = 0.5f;
			gameObject.AddComponent<GraphicRaycaster>();

			// A click outside the panel closes it.
			RectTransform dim = Box("Dim", transform, Dim);
			Stretch(dim, 0f);
			dim.gameObject.AddComponent<Button>().onClick.AddListener(Close);

			RectTransform frame = Box("Frame", transform, Border);
			Center(frame, 1760f, 1240f);
			RectTransform panel = Box("Panel", frame, PanelColor);
			Stretch(panel, 6f);

			Label("Title", panel, "MODS", 84, Color.white, TextAlignmentOptions.TopLeft, new Vector2(48f, -30f), new Vector2(600f, 100f));
			Label("Credit", panel, "HalfGripTools " + Plugin.Version + " by " + Plugin.Author + "   |   game " + GameVersion.Current, 30, Soft, TextAlignmentOptions.TopRight, new Vector2(-48f, -52f), new Vector2(900f, 40f), right: true);
			subtitle = Label("Subtitle", panel, "", 34, Soft, TextAlignmentOptions.TopLeft, new Vector2(48f, -128f), new Vector2(1660f, 50f));

			// Left column: playlists.
			Label("PlaylistsHeader", panel, "PLAYLISTS", 42, Amber, TextAlignmentOptions.TopLeft, new Vector2(48f, -200f), new Vector2(500f, 56f));
			playlistColumn = Box("Playlists", panel, Color.clear);
			Anchor(playlistColumn, new Vector2(0f, 1f), new Vector2(48f, -262f), new Vector2(520f, 820f));

			// Right column: mods, in a scroll view.
			Label("ModsHeader", panel, "INSTALLED MODS", 42, Amber, TextAlignmentOptions.TopLeft, new Vector2(620f, -200f), new Vector2(700f, 56f));
			RectTransform viewport = Box("Viewport", panel, new Color(0f, 0f, 0f, 0.01f));
			Anchor(viewport, new Vector2(0f, 1f), new Vector2(620f, -262f), new Vector2(1070f, 820f));
			viewport.gameObject.AddComponent<RectMask2D>();
			modContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
			modContent.SetParent(viewport, false);
			modContent.anchorMin = new Vector2(0f, 1f);
			modContent.anchorMax = new Vector2(1f, 1f);
			modContent.pivot = new Vector2(0.5f, 1f);
			modContent.anchoredPosition = Vector2.zero;
			modContent.sizeDelta = Vector2.zero;
			VerticalLayoutGroup list = modContent.gameObject.AddComponent<VerticalLayoutGroup>();
			list.spacing = 14f;
			list.childControlWidth = true;
			list.childControlHeight = true;
			list.childForceExpandWidth = true;
			list.childForceExpandHeight = false;
			modContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
			scroll.content = modContent;
			scroll.viewport = viewport;
			scroll.horizontal = false;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 60f;

			RectTransform barBox = Box("Scrollbar", panel, new Color(0.13f, 0.13f, 0.16f, 1f));
			Anchor(barBox, new Vector2(0f, 1f), new Vector2(1704f, -262f), new Vector2(22f, 820f));
			RectTransform slide = Box("Sliding Area", barBox, Color.clear);
			Stretch(slide, 3f);
			RectTransform handle = Box("Handle", slide, Soft);
			Stretch(handle, 0f);
			Scrollbar bar = barBox.gameObject.AddComponent<Scrollbar>();
			bar.handleRect = handle;
			bar.targetGraphic = handle.GetComponent<Image>();
			bar.direction = Scrollbar.Direction.BottomToTop;
			scroll.verticalScrollbar = bar;
			scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

			// Footer.
			Label("Hint", panel, "Changes are saved now and take effect the next time the game starts.", 32, Soft, TextAlignmentOptions.BottomLeft, new Vector2(48f, 36f), new Vector2(1000f, 60f), bottom: true);
			Button(panel, "QUIT GAME", new Vector2(-420f, 36f), new Vector2(300f, 84f), Grey, () => Application.Quit(), bottomRight: true);
			Button(panel, "CLOSE", new Vector2(-48f, 36f), new Vector2(340f, 84f), Red, Close, bottomRight: true);

			Refresh();
		}

		/// <summary>Rebuilds both lists from the current state.</summary>
		private void Refresh()
		{
			IReadOnlyList<ModEntry> mods = ModLoader.Entries;
			List<string> ids = mods.Select(m => m.Manifest.id).ToList();

			int pending = mods.Count(m => ModStore.IsEnabled(m.Manifest.id) != (m.Status == ModStatus.Loaded) && m.Status != ModStatus.Failed && m.Status != ModStatus.NeedsOther);
			subtitle.text = mods.Count == 0
				? "No mods are installed. Put mod folders in BepInEx\\HalfGripMods."
				: $"{mods.Count(m => m.Status == ModStatus.Loaded)} of {mods.Count} running" + (pending > 0 ? $"   |   {pending} change{(pending == 1 ? "" : "s")} waiting for a restart" : "");
			subtitle.color = pending > 0 ? Amber : Soft;

			Clear(playlistColumn);
			float y = 0f;
			y = PlaylistRow("All mods", ModStore.Active == "", () => { ModStore.PickAll(); Refresh(); }, y);
			foreach (Playlist playlist in ModStore.Playlists)
			{
				Playlist p = playlist;
				bool active = ModStore.Active == p.Name;
				string text = p.Name + (active && ModStore.ActiveModified(ids) ? " *" : "");
				y = PlaylistRow(text, active, () => { ModStore.Pick(p, ids); Refresh(); }, y);
			}
			y -= 14f;
			Playlist selected = ModStore.Find(ModStore.Active);
			Button(playlistColumn, "SAVE AS NEW", new Vector2(0f, y), new Vector2(520f, 70f), Green, ShowNameRow, topLeft: true);
			y -= 82f;
			if (selected != null)
			{
				Button(playlistColumn, "UPDATE", new Vector2(0f, y), new Vector2(250f, 70f), Grey, () => { ModStore.UpdateActive(ids); Refresh(); }, topLeft: true);
				Button(playlistColumn, "DELETE", new Vector2(270f, y), new Vector2(250f, 70f), Grey, () => { ModStore.DeleteActive(); Refresh(); }, topLeft: true);
				y -= 82f;
			}
			BuildNameRow(y);

			Clear(modContent);
			foreach (ModEntry entry in mods)
			{
				ModRow(entry);
			}
		}

		private float PlaylistRow(string text, bool active, Action click, float y)
		{
			RectTransform row = Button(playlistColumn, text, new Vector2(0f, y), new Vector2(520f, 70f), active ? Red : RowColor, click, topLeft: true, left: true);
			return y - 80f;
		}

		private void BuildNameRow(float y)
		{
			nameRow = new GameObject("NameRow", typeof(RectTransform));
			RectTransform rect = (RectTransform)nameRow.transform;
			rect.SetParent(playlistColumn, false);
			rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = new Vector2(0f, y);
			rect.sizeDelta = new Vector2(520f, 160f);

			RectTransform field = Box("Input", rect, new Color(0.04f, 0.04f, 0.05f, 1f));
			Anchor(field, new Vector2(0f, 1f), Vector2.zero, new Vector2(520f, 70f));
			RectTransform area = Box("TextArea", field, Color.clear);
			Stretch(area, 14f);
			area.gameObject.AddComponent<RectMask2D>();
			TMP_Text text = Label("Text", area, "", 40, Color.white, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
			Stretch((RectTransform)text.transform, 0f);
			TMP_Text hint = Label("Hint", area, "Playlist name", 40, new Color(1f, 1f, 1f, 0.3f), TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
			Stretch((RectTransform)hint.transform, 0f);
			nameField = field.gameObject.AddComponent<TMP_InputField>();
			nameField.textViewport = area;
			nameField.textComponent = text;
			nameField.placeholder = hint;
			nameField.fontAsset = font;
			nameField.pointSize = 40f;
			nameField.characterLimit = 24;
			nameField.targetGraphic = field.GetComponent<Image>();
			nameField.lineType = TMP_InputField.LineType.SingleLine;
			nameField.onSubmit.AddListener(_ => SaveNewPlaylist());

			Button(rect, "OK", new Vector2(0f, -82f), new Vector2(250f, 70f), Green, SaveNewPlaylist, topLeft: true);
			Button(rect, "CANCEL", new Vector2(270f, -82f), new Vector2(250f, 70f), Grey, () => nameRow.SetActive(false), topLeft: true);
			nameRow.SetActive(false);
		}

		private void ShowNameRow()
		{
			nameRow.SetActive(true);
			nameField.text = "Playlist " + (ModStore.Playlists.Count + 1);
			nameField.ActivateInputField();
			nameField.Select();
		}

		private void SaveNewPlaylist()
		{
			ModStore.SaveNew(nameField.text, ModLoader.Entries.Select(m => m.Manifest.id));
			Refresh();
		}

		private void ModRow(ModEntry entry)
		{
			ModManifest manifest = entry.Manifest;
			bool wanted = ModStore.IsEnabled(manifest.id);

			RectTransform row = Box("Row " + manifest.id, modContent, RowColor);
			bool warn = entry.VersionWarning != "";
			row.gameObject.AddComponent<LayoutElement>().preferredHeight = warn ? 156f : 118f;

			// A mod made for another game version gets a warning sign in front of its name and a line saying why.
			float left = 24f;
			Sprite picture = IconOf(manifest);
			if (picture != null)
			{
				RectTransform frame = Box("Icon", row, new Color(0.055f, 0.055f, 0.07f, 1f));
				Anchor(frame, new Vector2(0f, 1f), new Vector2(left, -11f), new Vector2(96f, 96f));
				frame.GetComponent<Image>().raycastTarget = false;
				RectTransform art = Box("Picture", frame, Color.white);
				Stretch(art, 6f);
				Image artImage = art.GetComponent<Image>();
				artImage.sprite = picture;
				artImage.preserveAspect = true;
				artImage.raycastTarget = false;
				left += 112f;
			}
			if (warn)
			{
				RectTransform sign = Box("Warning", row, Amber);
				Anchor(sign, new Vector2(0f, 1f), new Vector2(left, -16f), new Vector2(56f, 56f));
				TMP_Text mark = Label("Mark", sign, "!", 50, new Color(0.1f, 0.1f, 0.12f, 1f), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
				Stretch((RectTransform)mark.transform, 0f);
				left += 72f;
			}
			string by = string.IsNullOrEmpty(manifest.author) ? "" : "  by " + manifest.author;
			Label("Name", row, manifest.DisplayName, 46, Color.white, TextAlignmentOptions.TopLeft, new Vector2(left, -10f), new Vector2(650f, 56f));
			Label("Info", row, "v" + manifest.version + by, 30, Soft, TextAlignmentOptions.TopLeft, new Vector2(left, -64f), new Vector2(650f, 40f));
			if (warn)
			{
				Label("VersionWarning", row, entry.VersionWarning, 30, Amber, TextAlignmentOptions.TopLeft, new Vector2(24f, -108f), new Vector2(1000f, 40f));			}

			string status;
			Color statusColor;
			switch (entry.Status)
			{
				case ModStatus.Failed:
					status = "ERROR: " + entry.Detail;
					statusColor = Red;
					break;
				case ModStatus.NeedsOther:
					status = "NEEDS " + entry.Detail;
					statusColor = Red;
					break;
				default:
					bool running = entry.Status == ModStatus.Loaded;
					status = running == wanted ? (running ? "RUNNING" : "OFF") : (wanted ? "ON AT RESTART" : "OFF AT RESTART");
					statusColor = running == wanted ? (running ? Green : Soft) : Amber;
					break;
			}
			TMP_Text statusText = Label("Status", row, status, 32, statusColor, TextAlignmentOptions.TopRight, new Vector2(-240f, -20f), new Vector2(300f, 90f), right: true);
			statusText.textWrappingMode = TextWrappingModes.Normal;
			statusText.overflowMode = TextOverflowModes.Ellipsis;

			ModEntry captured = entry;
			Button(row, wanted ? "ON" : "OFF", new Vector2(-24f, -19f), new Vector2(190f, 80f), wanted ? Green : Grey, () =>
			{
				ModStore.SetEnabled(captured.Manifest.id, !ModStore.IsEnabled(captured.Manifest.id));
				Refresh();
			}, topRight: true);
		}

		private static readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();

		/// <summary>The mod's own picture (its mod.json "icon"), read once and kept. Null if it has none or the file cannot be read.</summary>
		private static Sprite IconOf(ModManifest manifest)
		{
			string path = manifest.IconPath();
			if (path == null)
			{
				return null;
			}
			if (iconCache.TryGetValue(path, out Sprite cached) && cached != null)
			{
				return cached;
			}
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave
			};
			try
			{
				if (!ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path)))
				{
					return null;
				}
			}
			catch (Exception)
			{
				return null;
			}
			Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
			sprite.hideFlags = HideFlags.HideAndDontSave;
			iconCache[path] = sprite;
			return sprite;
		}

		// ---------------------------------------------------------------- building blocks

		private static TMP_FontAsset FindFont()
		{
			TMP_FontAsset pixel = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name.StartsWith("m6x11", StringComparison.OrdinalIgnoreCase));
			return pixel != null ? pixel : TMP_Settings.defaultFontAsset;
		}

		private static void Clear(RectTransform parent)
		{
			for (int i = parent.childCount - 1; i >= 0; i--)
			{
				Destroy(parent.GetChild(i).gameObject);
			}
		}

		private static RectTransform Box(string name, Transform parent, Color color)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			go.transform.SetParent(parent, false);
			Image image = go.GetComponent<Image>();
			image.color = color;
			image.raycastTarget = color.a > 0.002f;
			return (RectTransform)go.transform;
		}

		private static void Stretch(RectTransform rect, float margin)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(margin, margin);
			rect.offsetMax = new Vector2(-margin, -margin);
		}

		private static void Center(RectTransform rect, float width, float height)
		{
			rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = new Vector2(width, height);
		}

		/// <summary>Pins the top-left (or the given corner) of a rect at an offset from that corner of its parent.</summary>
		private static void Anchor(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size)
		{
			rect.anchorMin = rect.anchorMax = rect.pivot = corner;
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
		}

		private TMP_Text Label(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, Vector2 position, Vector2 box, bool bottom = false, bool right = false)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
			go.transform.SetParent(parent, false);
			RectTransform rect = (RectTransform)go.transform;
			Vector2 corner = bottom ? new Vector2(0f, 0f) : right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
			Anchor(rect, corner, position, box);
			TMP_Text label = go.GetComponent<TMP_Text>();
			label.font = font;
			label.text = text;
			label.fontSize = size;
			label.color = color;
			label.alignment = align;
			label.textWrappingMode = TextWrappingModes.NoWrap;
			label.overflowMode = TextOverflowModes.Ellipsis;
			label.raycastTarget = false;
			return label;
		}

		/// <summary>A flat button. The corner flags say which corner of the parent the position is measured from.</summary>
		private RectTransform Button(Transform parent, string text, Vector2 position, Vector2 size, Color color, Action click, bool topLeft = false, bool topRight = false, bool bottomRight = false, bool left = false)
		{
			RectTransform rect = Box("Button " + text, parent, color);
			Vector2 corner = bottomRight ? new Vector2(1f, 0f) : topRight ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
			Anchor(rect, corner, position, size);
			Button button = rect.gameObject.AddComponent<Button>();
			button.targetGraphic = rect.GetComponent<Image>();
			ColorBlock colors = button.colors;
			colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
			colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
			colors.fadeDuration = 0.08f;
			button.colors = colors;
			button.onClick.AddListener(() => click());
			TMP_Text label = Label("Label", rect, text, 40, Color.white, left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
			Stretch((RectTransform)label.transform, 8f);
			if (left)
			{
				// More room at the left edge only: a margin all round would leave the text less height than it needs.
				((RectTransform)label.transform).offsetMin = new Vector2(22f, 8f);
			}
			label.ForceMeshUpdate();
			return rect;
		}
	}
}
