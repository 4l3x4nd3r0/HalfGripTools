using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HalfGripTools
{
	/// <summary>
	/// Adds the enabled mods to the game's anonymous run stats, so the developers can count runs with mods. <c>RunStats.Fit</c> returns the JSON
	/// text that is stored and uploaded; this inserts <c>"mods":["id@version:hash8",...]</c> before its closing brace. Loaded with the loader even
	/// in speedrun mode, where it sends <c>"mods":[]</c> because no mod is running. Any error leaves the text as the game made it.
	/// </summary>
	internal static class RunStatsMods
	{
		// Names are cut and the list is capped: the server rejects a run over 64 KB and the game trims runs to about 60 KB.
		private const int MaxMods = 60;

		private const int MaxEntryLength = 80;

		/// <summary>The text <c>RunStats.Fit</c> made, with the mods added. On any problem the text is returned as it was. (Reached through <see cref="GameHooks.RunStatsFit"/>.)</summary>
		internal static string Apply(string json)
		{
			try
			{
				return AddMods(json) ?? json;
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Could not add the mod list to the run stats: " + e.Message);
				return json;
			}
		}
		/// <summary>Returns <paramref name="json"/> with the mods field added, or null if it is not a JSON object or already has one.</summary>
		private static string AddMods(string json)
		{
			if (json == null)
			{
				return null;
			}
			int end = json.Length - 1;
			while (end >= 0 && char.IsWhiteSpace(json[end]))
			{
				end--;
			}
			if (end < 1 || json[end] != '}' || json.Contains("\"mods\":"))
			{
				return null;
			}
			// An empty object "{}" takes the field without a leading comma.
			int before = end - 1;
			while (before >= 0 && char.IsWhiteSpace(json[before]))
			{
				before--;
			}
			bool empty = json[before] == '{';

			StringBuilder field = new StringBuilder();
			field.Append(empty ? "" : ",").Append("\"mods\":[");
			bool first = true;
			foreach (string entry in EnabledMods().Take(MaxMods))
			{
				if (!first)
				{
					field.Append(',');
				}
				first = false;
				field.Append('"');
				Escape(field, entry);
				field.Append('"');
			}
			field.Append(']');
			return json.Substring(0, end) + field + json.Substring(end);
		}

		private static IEnumerable<string> EnabledMods()
		{
			foreach (ModContext mod in ModLoader.Mods.ToArray())
			{
				// id@version:hash8, so a changed copy of a mod shows in the stats as a different entry.
				string entry = mod.Manifest.id + "@" + mod.Manifest.version + ":" + ModHash.OfFolder(mod.Folder).Substring(0, 8);
				yield return entry.Length > MaxEntryLength ? entry.Substring(0, MaxEntryLength) : entry;
			}
		}

		private static void Escape(StringBuilder sb, string value)
		{
			foreach (char c in value)
			{
				switch (c)
				{
					case '\\': sb.Append("\\\\"); break;
					case '"': sb.Append("\\\""); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						if (c < ' ')
						{
							sb.Append("\\u").Append(((int)c).ToString("x4"));
						}
						else
						{
							sb.Append(c);
						}
						break;
				}
			}
		}
	}
}
