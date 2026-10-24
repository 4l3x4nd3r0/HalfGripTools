using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>Loads a mod's PNGs from its <c>sprites</c> folder and turns them into sprites that fit the game's art.</summary>
	public sealed class SpriteLoader
	{
		private readonly string folder;

		private readonly ManualLogSource log;

		private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

		private readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();

		internal SpriteLoader(string folder, ManualLogSource log)
		{
			this.folder = folder;
			this.log = log;
		}

		/// <summary>Reads a PNG as a point-filtered texture, or null (with a warning) if it is missing.</summary>
		public Texture2D LoadTexture(string file)
		{
			if (textureCache.TryGetValue(file, out Texture2D cached) && cached != null)
			{
				return cached;
			}
			string path = Path.Combine(folder, file);
			if (!File.Exists(path))
			{
				log.LogWarning("Missing sprite " + path + ", keeping the original");
				return null;
			}
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave
			};
			if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
			{
				log.LogWarning("Could not read " + path);
				return null;
			}
			textureCache[file] = texture;
			return texture;
		}

		/// <summary>
		/// Makes a sprite from a PNG. It takes its pivot (and, unless overridden, its pixel density) from the sprite it replaces,
		/// so it lines up with the original. Art that is a whole number of times the original's size keeps the original's size on screen.
		/// </summary>
		public Sprite LoadSprite(string file, Sprite like, float ppuOverride = 0f)
		{
			if (spriteCache.TryGetValue(file, out Sprite cached) && cached != null)
			{
				return cached;
			}
			Texture2D texture = LoadTexture(file);
			if (texture == null)
			{
				return null;
			}
			Vector2 pivot = new Vector2(0.5f, 0.5f);
			float ppu = 10f;
			if (like != null)
			{
				ppu = like.pixelsPerUnit;
				pivot = new Vector2(like.pivot.x / like.rect.width, like.pivot.y / like.rect.height);
				float k = texture.width / like.rect.width;
				bool denser = k > 1.001f && Mathf.Abs(k - texture.height / like.rect.height) < 0.001f;
				if (denser)
				{
					ppu *= k;
				}
				else if (ppuOverride <= 0f && ((int)like.rect.width != texture.width || (int)like.rect.height != texture.height)
					&& Mathf.Abs(texture.width / (float)texture.height - like.rect.width / like.rect.height) > 0.05f * like.rect.width / like.rect.height)
				{
					log.LogWarning($"{file} is {texture.width}x{texture.height} but the sprite it replaces is {like.rect.width}x{like.rect.height}");
				}
			}
			if (ppuOverride > 0f)
			{
				ppu = ppuOverride;
				pivot = new Vector2(0.5f, 0.5f);
			}
			Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), pivot, ppu);
			sprite.name = Path.GetFileNameWithoutExtension(file);
			sprite.hideFlags = HideFlags.HideAndDontSave;
			spriteCache[file] = sprite;
			return sprite;
		}

		/// <summary>Swaps sprites by name on every SpriteRenderer in the scenes. <paramref name="ppu"/> lets a replacement draw at its own pixel density (file name to pixels per unit).</summary>
		public int Reskin(Dictionary<string, string> map, Dictionary<string, float> ppu = null)
		{
			int swapped = 0;
			foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Sprite current = renderer.sprite;
				if (current == null || !map.TryGetValue(current.name, out string file))
				{
					continue;
				}
				Sprite replacement = LoadSprite(file, current, ppu != null && ppu.TryGetValue(file, out float p) ? p : 0f);
				if (replacement != null)
				{
					renderer.sprite = replacement;
					swapped++;
				}
			}
			return swapped;
		}
	}
}
