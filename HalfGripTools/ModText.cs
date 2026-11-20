using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace HalfGripTools
{
	/// <summary>
	/// Translations for a mod (<c>ctx.Text</c>), the way the game does them. The game shows every TextMeshPro text through <c>Loc.T(english)</c>: the
	/// English text is the key, and a table of "English,translation" rows is read for the chosen language. A mod adds its own rows to that table from
	/// <c>lang\&lt;language&gt;.csv</c> in its folder (the game's own language names: <c>french.csv</c>, <c>german.csv</c>, ...), whenever the game
	/// loads a language and once when the mod starts. A label that is plain English in the code is then translated when its text is in the file.
	/// Text with a name or a number in it goes through <see cref="F"/>, so the file has one row for the whole sentence with {0} where the value goes.
	/// A missing file or row leaves the English text.
	/// </summary>
	public sealed class ModText
	{
		// The game's private text tables (Loc.table, Loc.byLower). These are the lines that break if the game changes Loc.
		private static readonly FieldInfo TableField = typeof(Loc).GetField("table", BindingFlags.Static | BindingFlags.NonPublic);
		private static readonly FieldInfo LowerField = typeof(Loc).GetField("byLower", BindingFlags.Static | BindingFlags.NonPublic);

		private readonly ModContext ctx;

		internal ModText(ModContext ctx)
		{
			this.ctx = ctx;
		}

		/// <summary>The text in the player's language, or the English text itself.</summary>
		public string T(string english)
		{
			return Loc.T(english);
		}

		/// <summary>A sentence with values in it: the row in the language file is the format string, {0} {1} where the values go.</summary>
		public string F(string english, params object[] args)
		{
			return Loc.F(english, args);
		}

		/// <summary>Adds the rows for the language the game is in now (the mod started after the game had already loaded it).</summary>
		internal void MergeCurrent()
		{
			try
			{
				Merge(Loc.Language);
			}
			catch (Exception e)
			{
				ctx.Log.LogWarning("Translations: the game's language is not known yet: " + e.Message);
			}
		}

		/// <summary>Adds this mod's rows for a language to the game's table. The game has just (re)built the table for that language.</summary>
		internal void Merge(string language)
		{
			try
			{
				if (language == null || language == Loc.English)
				{
					return;
				}
				string path = Path.Combine(Path.Combine(ctx.Folder, "lang"), language + ".csv");
				if (!File.Exists(path))
				{
					return;
				}
				Dictionary<string, string> table = TableField?.GetValue(null) as Dictionary<string, string>;
				Dictionary<string, string> lower = LowerField?.GetValue(null) as Dictionary<string, string>;
				if (table == null || lower == null)
				{
					ctx.Log.LogWarning("Translations: the game's text table was not found, so lang\\" + language + ".csv is not used.");
					return;
				}
				List<List<string>> rows = LocCsv.Parse(File.ReadAllText(path));
				int added = 0;
				for (int i = 1; i < rows.Count; i++)
				{
					if (rows[i].Count < 2)
					{
						continue;
					}
					string key = Loc.Key(rows[i][0]);
					if (key.Length == 0 || rows[i][1].Trim().Length == 0)
					{
						continue;
					}
					table[key] = rows[i][1];
					if (Loc.CasingOf(key) != Loc.Casing.Other)
					{
						string small = key.ToLowerInvariant();
						if (!lower.ContainsKey(small))
						{
							lower[small] = rows[i][1];
						}
					}
					added++;
				}
				ctx.Log.LogInfo("Translations: " + added + " row(s) from lang\\" + language + ".csv.");
			}
			catch (Exception e)
			{
				ctx.Log.LogWarning("Translations for " + language + " could not be read: " + e.Message);
			}
		}
	}
}
