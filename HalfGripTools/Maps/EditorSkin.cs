using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HalfGripTools.MapBuild
{
	/// <summary>
	/// How the Map Maker's panels look: the game's own red button and pixel font, on dark panels, as a <c>GUISkin</c>. The button is copied from the
	/// main menu's biggest button the first time the menu is up (the editor runs in a race, where the menu is gone), and drawn as a nine-slice with a
	/// lighter picture under the mouse and a darker one pressed. If the menu was never seen a red button of the same kind is drawn in code.
	/// </summary>
	internal static class EditorSkin
	{
		private static GUISkin skin;
		private static Texture2D captured;
		private static RectOffset capturedBorder;
		private static Font font;
		private static Texture2D redNormal, redOver, redPressed, darkTex, panelTex, fieldTex;

		/// <summary>Copies the red button's picture out of the main menu. Cheap to call again; does nothing once it has one.</summary>
		internal static void TryCapture()
		{
			if (captured != null)
			{
				return;
			}
			try
			{
				Button best = null;
				float bestArea = 0f;
				foreach (Button button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
				{
					Image image = button.GetComponent<Image>() ?? button.GetComponentInChildren<Image>(true);
					if (image == null || image.sprite == null || button.GetComponentInChildren<TMP_Text>(true) == null)
					{
						continue;
					}
					Rect rect = image.sprite.rect;
					Color average = Average(image.sprite);
					bool red = average.r > 0.35f && average.r > average.g * 1.6f && average.r > average.b * 1.6f;
					if (red && rect.width * rect.height > bestArea)
					{
						best = button;
						bestArea = rect.width * rect.height;
					}
				}
				if (best == null)
				{
					return;
				}
				Sprite sprite = (best.GetComponent<Image>() ?? best.GetComponentInChildren<Image>(true)).sprite;
				captured = Copy(sprite);
				Vector4 b = sprite.border;
				capturedBorder = new RectOffset(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.z), Mathf.RoundToInt(b.w), Mathf.RoundToInt(b.y));
				if (capturedBorder.left + capturedBorder.right <= 0 || capturedBorder.top + capturedBorder.bottom <= 0)
				{
					int edge = Mathf.Max(2, Mathf.Min(captured.width, captured.height) / 4);
					capturedBorder = new RectOffset(edge, edge, edge, edge);
				}
				skin = null;
				ModRuntime.Log.LogInfo("Map Maker: took the main menu's red button (" + sprite.name + ", " + captured.width + "x" + captured.height + ") for the editor's buttons");
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Map Maker: could not copy the menu's button: " + e.Message);
			}
		}

		private static Color Average(Sprite sprite)
		{
			Texture2D copy = Copy(sprite);
			if (copy == null)
			{
				return Color.black;
			}
			Color32[] pixels = copy.GetPixels32();
			float r = 0, g = 0, b = 0;
			int n = 0;
			foreach (Color32 p in pixels)
			{
				if (p.a > 200)
				{
					r += p.r; g += p.g; b += p.b; n++;
				}
			}
			UnityEngine.Object.Destroy(copy);
			return n == 0 ? Color.black : new Color(r / n / 255f, g / n / 255f, b / n / 255f);
		}

		/// <summary>The sprite's pixels as a texture of their own (the game's textures cannot be read, so it goes through a render texture).</summary>
		private static Texture2D Copy(Sprite sprite)
		{
			Texture source = sprite.texture;
			RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0);
			Graphics.Blit(source, target);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = target;
			Rect r = sprite.textureRect;
			var copy = new Texture2D(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
			copy.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
			copy.Apply();
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(target);
			return copy;
		}

		/// <summary>Test aid: writes the three button pictures (normal, under the mouse, pressed) and says where they came from.</summary>
		internal static string DumpButtons(string folder)
		{
			Get();
			foreach (var pair in new[] { new { n = "skin_button.png", t = redNormal }, new { n = "skin_button_hover.png", t = redOver }, new { n = "skin_button_down.png", t = redPressed } })
			{
				System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, pair.n), pair.t.EncodeToPNG());
			}
			return captured != null ? "the game's own button" : "drawn in code";
		}

		/// <summary>The pictures the uGUI panels are made of: the red button (and its border), and nine-slice dark, panel and text-field pictures.</summary>
		internal static void Textures(out Texture2D red, out RectOffset border, out Texture2D dark, out Texture2D panel, out Texture2D field)
		{
			Get();
			red = redNormal;
			border = capturedBorder ?? new RectOffset(3, 3, 3, 3);
			dark = darkTex;
			panel = panelTex;
			field = fieldTex;
		}

		internal static GUISkin Get()
		{
			if (skin == null)
			{
				Build();
			}
			return skin;
		}

		private static Texture2D Nine(Color fill, Color light, Color dark, Color edge)
		{
			const int n = 8;
			var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					Color c = fill;
					if (y == n - 2) c = light;
					if (y == 1) c = dark;
					if (x == 0 || x == n - 1 || y == 0 || y == n - 1) c = edge;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply();
			return t;
		}

		private static Texture2D Tinted(Texture2D source, float factor)
		{
			Color32[] pixels = source.GetPixels32();
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = new Color32((byte)Mathf.Clamp(pixels[i].r * factor, 0, 255), (byte)Mathf.Clamp(pixels[i].g * factor, 0, 255), (byte)Mathf.Clamp(pixels[i].b * factor, 0, 255), pixels[i].a);
			}
			var t = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
			t.SetPixels32(pixels);
			t.Apply();
			return t;
		}

		private static void Build()
		{
			skin = ScriptableObject.CreateInstance<GUISkin>();
			skin.hideFlags = HideFlags.HideAndDontSave;
			if (font == null)
			{
				TMP_FontAsset asset = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name.StartsWith("m6x11", StringComparison.OrdinalIgnoreCase));
				font = asset != null ? asset.sourceFontFile : null;
			}

			// the red button: the game's own picture if it was seen, otherwise one in the same style
			Texture2D red = captured ?? Nine(new Color(0.78f, 0.16f, 0.14f), new Color(0.95f, 0.42f, 0.32f), new Color(0.46f, 0.07f, 0.07f), new Color(0.12f, 0.02f, 0.02f));
			RectOffset border = capturedBorder ?? new RectOffset(3, 3, 3, 3);
			Texture2D redHover = Tinted(red, 1.18f);
			Texture2D redDown = Tinted(red, 0.72f);
			redNormal = red;
			redOver = redHover;
			redPressed = redDown;
			Texture2D dark = Nine(new Color(0.12f, 0.12f, 0.15f), new Color(0.2f, 0.2f, 0.25f), new Color(0.06f, 0.06f, 0.08f), new Color(0.02f, 0.02f, 0.03f));
			Texture2D darkHover = Tinted(dark, 1.5f);
			Texture2D panel = Nine(new Color(0.06f, 0.06f, 0.08f, 0.93f), new Color(0.06f, 0.06f, 0.08f, 0.93f), new Color(0.06f, 0.06f, 0.08f, 0.93f), new Color(0.5f, 0.1f, 0.1f, 1f));
			Texture2D field = Nine(new Color(0.03f, 0.03f, 0.04f), new Color(0.03f, 0.03f, 0.04f), new Color(0.03f, 0.03f, 0.04f), new Color(0.4f, 0.4f, 0.45f));
			darkTex = dark;
			panelTex = panel;
			fieldTex = field;
			Texture2D fieldFocus = Nine(new Color(0.03f, 0.03f, 0.04f), new Color(0.03f, 0.03f, 0.04f), new Color(0.03f, 0.03f, 0.04f), new Color(0.95f, 0.3f, 0.25f));

			GUIStyle label = skin.label;
			label.font = font;
			label.fontSize = 11;
			label.normal.textColor = new Color(0.95f, 0.93f, 0.9f);
			label.richText = true;
			label.wordWrap = true;

			GUIStyle box = skin.box;
			box.normal.background = panel;
			box.border = new RectOffset(1, 1, 1, 1);

			GUIStyle button = skin.button;
			button.font = font;
			button.fontSize = 11;
			button.normal.background = red;
			button.hover.background = redHover;
			button.active.background = redDown;
			button.focused.background = red;
			button.onNormal.background = redDown;
			button.onHover.background = redDown;
			button.onActive.background = redDown;
			button.border = border;
			button.margin = new RectOffset(2, 2, 2, 2);
			button.padding = new RectOffset(6, 6, 4, 5);
			button.alignment = TextAnchor.MiddleCenter;
			button.richText = true;
			foreach (GUIStyleState state in new[] { button.normal, button.hover, button.active, button.focused, button.onNormal, button.onHover, button.onActive, button.onFocused })
			{
				state.textColor = Color.white;
			}
			button.onNormal.textColor = new Color(1f, 0.92f, 0.5f);
			button.onHover.textColor = new Color(1f, 0.92f, 0.5f);

			// A toggle is a button that stays down: dark when off, red and pressed when on.
			GUIStyle toggle = skin.toggle;
			toggle.font = font;
			toggle.fontSize = 11;
			toggle.normal.background = dark;
			toggle.hover.background = darkHover;
			toggle.active.background = darkHover;
			toggle.onNormal.background = red;
			toggle.onHover.background = redHover;
			toggle.onActive.background = redHover;
			toggle.border = new RectOffset(1, 1, 1, 1);
			toggle.margin = new RectOffset(2, 2, 2, 2);
			toggle.padding = new RectOffset(18, 6, 4, 4);
			toggle.overflow = new RectOffset(0, 0, 0, 0);
			toggle.alignment = TextAnchor.MiddleLeft;
			foreach (GUIStyleState state in new[] { toggle.normal, toggle.hover, toggle.active, toggle.onNormal, toggle.onHover, toggle.onActive })
			{
				state.textColor = Color.white;
			}

			GUIStyle text = skin.textField;
			text.font = font;
			text.fontSize = 11;
			text.normal.background = field;
			text.focused.background = fieldFocus;
			text.hover.background = field;
			text.border = new RectOffset(1, 1, 1, 1);
			text.padding = new RectOffset(5, 5, 4, 4);
			foreach (GUIStyleState state in new[] { text.normal, text.hover, text.focused, text.active })
			{
				state.textColor = new Color(1f, 0.95f, 0.85f);
			}

			GUIStyle track = skin.horizontalSlider;
			track.normal.background = Nine(new Color(0.03f, 0.03f, 0.04f), new Color(0.03f, 0.03f, 0.04f), new Color(0.03f, 0.03f, 0.04f), new Color(0.4f, 0.4f, 0.45f));
			track.border = new RectOffset(1, 1, 1, 1);
			track.fixedHeight = 8;
			track.margin = new RectOffset(4, 4, 6, 6);
			GUIStyle thumb = skin.horizontalSliderThumb;
			thumb.normal.background = red;
			thumb.hover.background = redHover;
			thumb.active.background = redDown;
			thumb.border = border;
			thumb.fixedWidth = 10;
			thumb.fixedHeight = 16;
			thumb.overflow = new RectOffset(0, 0, 4, 4);

			skin.settings.cursorColor = new Color(1f, 0.9f, 0.6f);
			skin.settings.selectionColor = new Color(0.8f, 0.2f, 0.2f, 0.6f);
		}
	}
}
