using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>Loads sound from the mod's own folder (<c>ctx.Audio</c>). 16-bit PCM wav only, mono or stereo.</summary>
	public sealed class ModAudio
	{
		private readonly ModContext ctx;
		private readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

		internal ModAudio(ModContext ctx)
		{
			this.ctx = ctx;
		}

		/// <summary>
		/// A wav inside the mod's folder, for example <c>"sounds/boom.wav"</c>, as a clip (loaded once, then kept). Null with a line in the log when the
		/// file is missing or is not 16-bit PCM. The path must stay inside the mod's folder: no drive, no <c>..</c>.
		/// </summary>
		public AudioClip LoadWav(string relativePath)
		{
			if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(".."))
			{
				ctx.Log.LogWarning("Audio: '" + relativePath + "' is not a path inside the mod's folder.");
				return null;
			}
			AudioClip clip;
			if (cache.TryGetValue(relativePath, out clip) && clip != null)
			{
				return clip;
			}
			string full = Path.GetFullPath(Path.Combine(ctx.Folder, relativePath));
			string root = Path.GetFullPath(ctx.Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				ctx.Log.LogWarning("Audio: '" + relativePath + "' is outside the mod's folder.");
				return null;
			}
			clip = WavFile.Load(full);
			if (clip != null)
			{
				cache[relativePath] = clip;
			}
			return clip;
		}

		internal void Clear()
		{
			cache.Clear();
		}
	}
}
