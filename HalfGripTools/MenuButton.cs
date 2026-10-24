using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HalfGripTools
{
	/// <summary>
	/// The puzzle-piece button in the main menu, one slot above the language flag. It is a copy of the game's own Settings button
	/// (so it looks, presses and sounds like the others) with another picture and another click.
	/// </summary>
	internal static class MenuButton
	{
		private const string ButtonName = "HalfGripToolsButton";

		// 16x17 pixels, the size of the flags. Dark tile like the Settings button (d, D shade), orange puzzle piece (y light, o mid, O shade).
		private static readonly string[] Pixels =
		{
			"...dddddddddd...",
			"dddddyOddddddddd",
			"ddddyyoOdddddddd",
			"ddddyyoOdddddddd",
			"dyyyyyyyyyyddddd",
			"dyooooooooOddddd",
			"dyooooooooOyyydd",
			"dddoooooooooooOd",
			"dddoooooooooooOd",
			"dddoooooooooooOd",
			"dyooooooooOOOOdd",
			"dyooooooooOddddd",
			"dyooooooooOddddd",
			"dyooodddooOddddd",
			"dyOOOdddOOOddddd",
			".dddddddddddddd.",
			"...DDDDDDDDDD..."
		};

		private static GameObject button;

		private static Sprite icon;

		/// <summary>Adds the button to the main menu if this scene has one and it is not there yet.</summary>
		internal static void TryAttach()
		{
			if (button != null)
			{
				return;
			}
			foreach (LanguageButton flag in Object.FindObjectsByType<LanguageButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Transform stack = flag.transform.parent;
				Transform settings = stack.Find("SettingsButton");
				if (settings == null || stack.Find(ButtonName) != null)
				{
					continue;
				}
				// The three buttons are stacked by hand, 76 units apart, bottom up: settings, discord, language. This is the next slot.
				RectTransform last = (RectTransform)flag.transform;
				Vector2 position = last.anchoredPosition + new Vector2(0f, last.anchoredPosition.y - ((RectTransform)stack.Find("DiscordButton") ?? last).anchoredPosition.y);

				button = Object.Instantiate(settings.gameObject, stack);
				button.name = ButtonName;
				RectTransform rect = (RectTransform)button.transform;
				rect.anchoredPosition = position;
				// ButtonDepth eases the button back to the height it had when it woke up, which was the Settings button's.
				ButtonDepth depth = button.GetComponent<ButtonDepth>();
				if (depth != null)
				{
					Traverse.Create(depth).Field("baseY").SetValue(position.y);
				}
				button.GetComponent<Image>().sprite = Icon();
				Button click = button.GetComponent<Button>();
				click.onClick = new Button.ButtonClickedEvent();
				click.onClick.AddListener(ModManagerUI.Open);
				Plugin.Log.LogInfo("Added the mod manager button to the main menu at " + position);
				return;
			}
		}

		/// <summary>Takes the button and the mod manager out of the menu.</summary>
		internal static void Remove()
		{
			ModManagerUI.Close();
			if (button != null)
			{
				Object.Destroy(button);
			}
			button = null;
		}

		private static Sprite Icon()
		{
			if (icon != null)
			{
				return icon;
			}
			Color32 clear = new Color32(0, 0, 0, 0);
			// The button stretches the picture over its whole face, so the 16x17 art sits in a transparent margin to come out the size of the other icons.
			const int padLeft = 2;
			const int padBottom = 2;
			int width = 21;
			int height = 22;
			Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave
			};
			Color32[] blank = new Color32[width * height];
			texture.SetPixels32(blank);
			for (int row = 0; row < Pixels.Length; row++)
			{
				for (int x = 0; x < 16; x++)
				{
					Color32 color;
					switch (Pixels[row][x])
					{
						case 'd': color = new Color32(56, 56, 56, 255); break;
						case 'D': color = new Color32(42, 41, 42, 255); break;
						case 'y': color = new Color32(247, 196, 92, 255); break;
						case 'o': color = new Color32(235, 154, 44, 255); break;
						case 'O': color = new Color32(186, 108, 32, 255); break;
						default: color = clear; break;
					}
					texture.SetPixel(padLeft + x, padBottom + Pixels.Length - 1 - row, color);
				}
			}
			texture.Apply();
			icon = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
			icon.hideFlags = HideFlags.HideAndDontSave;
			return icon;
		}
	}
}
