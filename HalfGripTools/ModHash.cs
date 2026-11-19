using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HalfGripTools
{
	/// <summary>
	/// One SHA-256 over everything in a mod folder: each file's relative path and bytes, in a fixed order. Two PCs with the same mod get the same
	/// hash, so rooms, saves and later Workshop updates can tell "the same mod" from "a changed one". Moved here from the Multiplayer mod
	/// with the same rule, so the checksums it made before are unchanged.
	/// </summary>
	internal static class ModHash
	{
		private static readonly string[] TextExtensions = { ".cs", ".json", ".txt", ".md" };

		private static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The hash of a mod folder as 64 lower-case hex digits. Kept for the rest of the session: a mod's files do not change while the game runs.</summary>
		internal static string OfFolder(string folder)
		{
			string root = Path.GetFullPath(folder);
			lock (cache)
			{
				if (!cache.TryGetValue(root, out string hash))
				{
					cache[root] = hash = Compute(root);
				}
				return hash;
			}
		}

		/// <summary>
		/// What does not count. Translations only change what one player reads. The rest is what an editor or a build writes next to the mod
		/// (the IntelliSense project of <c>new-mod.ps1</c>): the game never reads it, so it must not put anybody in another room.
		/// </summary>
		private static bool Ignored(string relativePath)
		{
			string p = relativePath.ToLowerInvariant();
			return p.StartsWith("lang/") || p.StartsWith("bin/") || p.StartsWith("obj/") || p.StartsWith(".vs/") || p == ".gitignore" ||
				p.EndsWith(".csproj") || p.EndsWith(".csproj.user");
		}

		private static string Compute(string root)
		{
			using (SHA256 sha = SHA256.Create())
			{
				string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
				List<string> relative = files.Select(f => f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/')).Where(p => !Ignored(p)).ToList();
				relative.Sort(string.CompareOrdinal);
				foreach (string path in relative)
				{
					byte[] name = Encoding.UTF8.GetBytes(path + "\n");
					sha.TransformBlock(name, 0, name.Length, null, 0);
					byte[] data = File.ReadAllBytes(Path.Combine(root, path));
					if (TextExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
					{
						data = StripCarriageReturns(data);
					}
					sha.TransformBlock(data, 0, data.Length, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				StringBuilder text = new StringBuilder(64);
				foreach (byte b in sha.Hash)
				{
					text.Append(b.ToString("x2"));
				}
				return text.ToString();
			}
		}

		/// <summary>A text file checked out with CRLF on one PC and LF on another is still the same mod.</summary>
		private static byte[] StripCarriageReturns(byte[] data)
		{
			List<byte> kept = new List<byte>(data.Length);
			foreach (byte b in data)
			{
				if (b != 13)
				{
					kept.Add(b);
				}
			}
			return kept.ToArray();
		}
	}

	/// <summary>A mod that is running, as other mods and the game's records see it.</summary>
	public sealed class LoadedMod
	{
		public string Id { get; internal set; }

		public string Version { get; internal set; }

		/// <summary>SHA-256 of the mod's folder, 64 lower-case hex digits. Changes whenever any file the game reads changes.</summary>
		public string Hash { get; internal set; }

		/// <summary>The mod's folder on this PC.</summary>
		public string Folder { get; internal set; }

		/// <summary>From mod.json: <c>item</c>, <c>map</c>, <c>mode</c>, <c>library</c> or empty.</summary>
		public string Kind { get; internal set; }

		/// <summary>From mod.json: <c>full</c> or <c>restricted</c> (only recorded for now).</summary>
		public string Trust { get; internal set; }

		/// <summary>Where it was found: local (the Mods folder), legacy (BepInEx\HalfGripMods), extra (a folder from the settings) or workshop:&lt;item id&gt;.</summary>
		public string Source { get; internal set; }

		/// <summary>The short form for logs, run stats and rooms: <c>id@version:first 8 hash digits</c>.</summary>
		public string Line => Id + "@" + Version + ":" + Hash.Substring(0, 8);
	}

	/// <summary>The mods that are loaded right now. Read-only, sorted by id; empty when mods are switched off (speedrun mode).</summary>
	public static class LoadedMods
	{
		public static IReadOnlyList<LoadedMod> All
		{
			get
			{
				List<LoadedMod> list = new List<LoadedMod>();
				foreach (ModContext context in ModLoader.Mods.ToArray())
				{
					ModManifest m = context.Manifest;
					list.Add(new LoadedMod { Id = m.id, Version = m.version, Hash = ModHash.OfFolder(context.Folder), Folder = context.Folder, Kind = m.kind, Trust = m.trust, Source = m.Source });
				}
				list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
				return list;
			}
		}
	}
}
