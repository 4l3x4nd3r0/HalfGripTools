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

		/// <summary>
		/// The oldest HalfGripTools version the mod runs on, for example "1.0.2". Empty means any. A loader that is older than this
		/// (and knows the field) does not load the mod and says why.
		/// </summary>
		public string tools = "";

		/// <summary>
		/// The same thing as <see cref="tools"/> under the name the official API will use: the oldest loader/API version the mod runs on.
		/// Either field may be given; both end up holding the same text, so the loader reads one.
		/// </summary>
		public string apiVersion = "";

		/// <summary>
		/// "full" (the default: the whole game, Harmony, files) or "restricted" (the public API only, planned). Only recorded for now: a
		/// restricted mod still runs with full access until the restricted compile mode exists.
		/// </summary>
		public string trust = "full";

		/// <summary>What the mod is: "item", "map", "mode" or "library". Empty means not said. For lists and, later, Workshop.</summary>
		public string kind = "";

		/// <summary>Free words for finding the mod ("gun", "map", "cosmetic"). Lower case, no spaces.</summary>
		public string[] tags = new string[0];

		/// <summary>
		/// A picture for the mod, shown in the mod manager (and in installers): a PNG inside the mod's folder, for example "icon.png".
		/// It is only a picture; the game's own sprites for the mod's items are separate. Empty, a missing file or a path that leaves the
		/// folder means no picture.
		/// </summary>
		public string icon = "";

		internal string Folder;

		/// <summary>The id written in mod.json, set only when another mod already had it and this copy was given a new id.</summary>
		internal string OriginalId;

		internal string DisplayName => string.IsNullOrEmpty(name) ? id : name;

		/// <summary>Reads a mod.json. Throws <see cref="FormatException"/> (with line and column) if it is not valid.</summary>
		internal static ModManifest FromJson(string text, string folder)
		{
			if (!(MiniJson.Parse(text) is Dictionary<string, object> obj))
			{
				throw new FormatException("mod.json must be a JSON object");
			}
			string tools = MiniJson.String(obj, "tools");
			string api = MiniJson.String(obj, "apiVersion");
			// One value for both names. If a mod gives both, the newer one counts, so neither can let it run on a loader that is too old.
			string needed = tools == "" ? api : api == "" ? tools : (Version.TryParse(api, out Version a) && Version.TryParse(tools, out Version t) && a > t ? api : tools);
			ModManifest manifest = new ModManifest
			{
				id = MiniJson.String(obj, "id"),
				name = MiniJson.String(obj, "name"),
				version = MiniJson.String(obj, "version", "1.0.0"),
				author = MiniJson.String(obj, "author"),
				description = MiniJson.String(obj, "description"),
				requires = MiniJson.List(obj, "requires").OfType<string>().ToArray(),
				gameVersion = MiniJson.String(obj, "gameVersion"),
				tools = needed,
				apiVersion = needed,
				trust = MiniJson.String(obj, "trust", "full"),
				kind = MiniJson.String(obj, "kind"),
				tags = MiniJson.List(obj, "tags").OfType<string>().ToArray(),
				icon = MiniJson.String(obj, "icon"),
				Folder = folder
			};
			return manifest;
		}

		internal static readonly string[] Trusts = { "full", "restricted" };

		internal static readonly string[] Kinds = { "item", "map", "mode", "library" };

		/// <summary>
		/// What is wrong with the new fields, one sentence each. None of it stops the mod: an unknown value is put back to the default, so
		/// a mod made for a later loader that knows more words still runs here.
		/// </summary>
		internal List<string> CheckNewFields()
		{
			List<string> problems = new List<string>();
			string trustWas = trust.ToLowerInvariant();
			if (!Trusts.Contains(trustWas))
			{
				problems.Add($"'trust' is '{trust}', not one of {string.Join(", ", Trusts)}; using 'full'");
				trustWas = "full";
			}
			trust = trustWas;
			string kindWas = kind.ToLowerInvariant();
			if (kindWas != "" && !Kinds.Contains(kindWas))
			{
				problems.Add($"'kind' is '{kind}', not one of {string.Join(", ", Kinds)}; leaving it empty");
				kindWas = "";
			}
			kind = kindWas;
			tags = tags.Select(t => t.Trim().ToLowerInvariant().Replace(' ', '-')).Where(t => t != "").Distinct().ToArray();
			return problems;
		}

		/// <summary>The full path of the icon, or null when there is none: it must be a .png that sits inside the mod's own folder.</summary>
		internal string IconPath()
		{
			if (string.IsNullOrEmpty(icon) || string.IsNullOrEmpty(Folder) || !icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || System.IO.Path.IsPathRooted(icon))
			{
				return null;
			}
			string root = System.IO.Path.GetFullPath(Folder).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
			string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Folder, icon));
			return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path) ? path : null;
		}
	}
}
