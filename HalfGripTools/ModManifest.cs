using System;
using System.Collections.Generic;
using System.Linq;

namespace HalfGripTools
{
	/// <summary>The contents of a mod's <c>mod.json</c>.</summary>
	public sealed class ModManifest
	{
		/// <summary>Unique, lower case, no spaces. Used for the config file name and the cache.</summary>
		public string id = "";

		public string name = "";

		public string version = "1.0.0";

		public string author = "";

		public string description = "";

		/// <summary>Ids of mods that must load before this one.</summary>
		public string[] requires = new string[0];

		/// <summary>
		/// The game version the mod was made for: "0.418" is exactly that version, "0.x" is any 0.something ("x" matches any part).
		/// Empty means the author did not say. A game that does not match only gives a warning.
		/// </summary>
		public string gameVersion = "";

		internal string Folder;

		internal string DisplayName => string.IsNullOrEmpty(name) ? id : name;

		/// <summary>Reads a mod.json. Throws <see cref="FormatException"/> (with line and column) if it is not valid.</summary>
		internal static ModManifest FromJson(string text, string folder)
		{
			if (!(MiniJson.Parse(text) is Dictionary<string, object> obj))
			{
				throw new FormatException("mod.json must be a JSON object");
			}
			return new ModManifest
			{
				id = MiniJson.String(obj, "id"),
				name = MiniJson.String(obj, "name"),
				version = MiniJson.String(obj, "version", "1.0.0"),
				author = MiniJson.String(obj, "author"),
				description = MiniJson.String(obj, "description"),
				requires = MiniJson.List(obj, "requires").OfType<string>().ToArray(),
				gameVersion = MiniJson.String(obj, "gameVersion"),
				Folder = folder
			};
		}
	}
}
