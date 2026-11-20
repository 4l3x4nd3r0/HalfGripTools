using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HalfGripTools
{
	/// <summary>
	/// A mod's own files (<c>ctx.Storage</c>): text and bytes kept in the mod's folder inside the data folder (<see cref="ModContext.DataFolder"/>), the
	/// one place a mod may write. It exists for mods that run as <c>"trust": "restricted"</c>, which may not use <c>System.IO</c>, but any mod can use it.
	/// Names are relative paths such as <c>"best-times.json"</c> or <c>"runs/last.txt"</c>: letters, digits, space, dot, dash, underscore and <c>/</c>, no
	/// <c>..</c>, no drive, no leading dot. A file may be 1 MB and a mod may keep 20 MB in all, so a mod cannot fill the disk.
	/// </summary>
	public sealed class ModStorage
	{
		/// <summary>The most one file may hold, in bytes.</summary>
		public const int MaxFileBytes = 1024 * 1024;

		/// <summary>The most all of a mod's files may hold, in bytes.</summary>
		public const long MaxTotalBytes = 20L * 1024 * 1024;

		private static readonly Regex Allowed = new Regex(@"^[A-Za-z0-9_\- ][A-Za-z0-9._\- ]*(/[A-Za-z0-9_\- ][A-Za-z0-9._\- ]*)*$", RegexOptions.Compiled);

		private readonly ModContext ctx;

		internal ModStorage(ModContext ctx)
		{
			this.ctx = ctx;
		}

		private string Root => Path.GetFullPath(ctx.DataFolder);

		/// <summary>The full path of a storage name, or null (with a line in the log) if the name is not allowed.</summary>
		private string Resolve(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length > 120 || name.Contains("..") || !Allowed.IsMatch(name))
			{
				ctx.Log.LogWarning("Storage: '" + name + "' is not a usable name (letters, digits, space . - _ and / only, no .., at most 120 characters).");
				return null;
			}
			string root = Root;
			string full = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
			if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				ctx.Log.LogWarning("Storage: '" + name + "' is outside the mod's data folder.");
				return null;
			}
			return full;
		}

		/// <summary>True if the file exists.</summary>
		public bool Exists(string name)
		{
			string path = Resolve(name);
			return path != null && File.Exists(path);
		}

		/// <summary>The file's text, or null if it is missing or cannot be read.</summary>
		public string ReadText(string name)
		{
			byte[] bytes = ReadBytes(name);
			return bytes == null ? null : new UTF8Encoding(false).GetString(bytes);
		}

		/// <summary>The file's bytes, or null if it is missing or cannot be read.</summary>
		public byte[] ReadBytes(string name)
		{
			string path = Resolve(name);
			if (path == null || !File.Exists(path))
			{
				return null;
			}
			try
			{
				return File.ReadAllBytes(path);
			}
			catch (Exception e)
			{
				ctx.Log.LogWarning("Storage: could not read '" + name + "': " + e.Message);
				return null;
			}
		}

		/// <summary>Writes the text as UTF-8. False (with a line in the log) if the name is not allowed, the file is over 1 MB, the mod is over 20 MB, or the disk refuses.</summary>
		public bool WriteText(string name, string text)
		{
			return WriteBytes(name, new UTF8Encoding(false).GetBytes(text ?? ""));
		}

		/// <summary>Writes the bytes. False under the same conditions as <see cref="WriteText"/>.</summary>
		public bool WriteBytes(string name, byte[] bytes)
		{
			string path = Resolve(name);
			if (path == null || bytes == null)
			{
				return false;
			}
			if (bytes.Length > MaxFileBytes)
			{
				ctx.Log.LogWarning("Storage: '" + name + "' would be " + bytes.Length + " bytes; one file may hold " + MaxFileBytes + ".");
				return false;
			}
			try
			{
				long others = TotalBytes() - (File.Exists(path) ? new FileInfo(path).Length : 0);
				if (others + bytes.Length > MaxTotalBytes)
				{
					ctx.Log.LogWarning("Storage: this mod may keep " + MaxTotalBytes / 1024 / 1024 + " MB in all; '" + name + "' would go over.");
					return false;
				}
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				// Written under another name and moved into place, so a crash never leaves half a file.
				string temp = path + ".tmp";
				File.WriteAllBytes(temp, bytes);
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				File.Move(temp, path);
				return true;
			}
			catch (Exception e)
			{
				ctx.Log.LogWarning("Storage: could not write '" + name + "': " + e.Message);
				return false;
			}
		}

		/// <summary>Deletes the file. False if there was none or it could not be deleted.</summary>
		public bool Delete(string name)
		{
			string path = Resolve(name);
			try
			{
				if (path != null && File.Exists(path))
				{
					File.Delete(path);
					return true;
				}
			}
			catch (Exception e)
			{
				ctx.Log.LogWarning("Storage: could not delete '" + name + "': " + e.Message);
			}
			return false;
		}

		/// <summary>The names of the mod's files, with <c>/</c> between folders, optionally only those that start with <paramref name="prefix"/>.</summary>
		public string[] List(string prefix = "")
		{
			string root = Root;
			if (!Directory.Exists(root))
			{
				return new string[0];
			}
			int cut = root.TrimEnd(Path.DirectorySeparatorChar).Length + 1;
			return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
				.Select(f => f.Substring(cut).Replace(Path.DirectorySeparatorChar, '/'))
				.Where(n => !n.EndsWith(".tmp") && n.StartsWith(prefix ?? "", StringComparison.Ordinal))
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToArray();
		}

		private long TotalBytes()
		{
			string root = Root;
			return Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
		}
	}
}
