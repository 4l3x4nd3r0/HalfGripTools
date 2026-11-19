using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>Turns a mod's <c>items</c> folder into catalog items, with no code in the mod. The format is described in docs/WRITING_MODS.md.</summary>
	internal static class JsonItems
	{
		internal static void Register(ModContext ctx)
		{
			string dir = Path.Combine(ctx.Folder, "items");
			if (!Directory.Exists(dir))
			{
				return;
			}
			foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
			{
				string label = Path.GetFileName(file);
				try
				{
					if (!(MiniJson.Parse(File.ReadAllText(file)) is Dictionary<string, object> def))
					{
						ctx.Log.LogError(label + ": an item file must be a JSON object. Skipped.");
						continue;
					}
					string id = MiniJson.String(def, "id");
					string name = MiniJson.String(def, "name");
					if (id == "" || name == "")
					{
						ctx.Log.LogError(label + ": 'id' and 'name' are required. Skipped.");
						continue;
					}
					string tierName = MiniJson.String(def, "tier", "Stock");
					if (!Enum.TryParse(tierName, true, out ItemTier tier))
					{
						ctx.Log.LogError($"{label}: unknown tier '{tierName}' (use {string.Join(", ", Enum.GetNames(typeof(ItemTier)))}). Skipped.");
						continue;
					}
					List<StatBoost> boosts = ReadBoosts(ctx, label, def);
					if (boosts == null)
					{
						continue;
					}
					ctx.Items.Add(id, catalog => Create(ctx, catalog, def, tier, boosts));
				}
				catch (Exception e)
				{
					ctx.Log.LogError(label + ": " + e.Message + ". Skipped.");
				}
			}
		}

		/// <summary>Null (after logging why) if a boost names a stat the game does not have.</summary>
		private static List<StatBoost> ReadBoosts(ModContext ctx, string label, Dictionary<string, object> def)
		{
			List<StatBoost> boosts = new List<StatBoost>();
			foreach (Dictionary<string, object> boost in MiniJson.List(def, "statBoosts").OfType<Dictionary<string, object>>())
			{
				string statName = MiniJson.String(boost, "stat");
				if (!Enum.TryParse(statName, true, out Stat stat))
				{
					ctx.Log.LogError($"{label}: unknown stat '{statName}' (use one of: {string.Join(", ", Enum.GetNames(typeof(Stat)))}). Item skipped.");
					return null;
				}
				boosts.Add(new StatBoost
				{
					stat = stat,
					amount = MiniJson.Number(boost, "amount"),
					addsToBase = MiniJson.Bool(boost, "addsToBase"),
					multiplicative = MiniJson.Bool(boost, "multiplicative"),
					hideLine = MiniJson.Bool(boost, "hideLine")
				});
			}
			return boosts;
		}

		/// <summary>Fields that would change what the item is, not how it behaves: they stay as the clone had them.</summary>
		private static readonly HashSet<string> LockedFields = new HashSet<string> { "id", "name", "hideFlags" };

		/// <summary>
		/// <c>"fields": { "speedMultiplier": 92, "loadSensitivity": "SPIKED" }</c>: sets public fields of the cloned item by name. Numbers, true/false,
		/// text and the names of an enum are accepted; anything else (sprites, curves, lists) is refused with a line in the log. Use <c>copyOf</c> to
		/// start from the kind of item you want: an engine's fields on a clone of an engine, a tire's on a tire.
		/// </summary>
		private static void ApplyFields(ModContext ctx, string id, UpgradeComponent made, Dictionary<string, object> def)
		{
			Dictionary<string, object> fields = def.ContainsKey("fields") ? def["fields"] as Dictionary<string, object> : null;
			if (fields == null)
			{
				return;
			}
			var done = new List<string>();
			foreach (KeyValuePair<string, object> pair in fields)
			{
				System.Reflection.FieldInfo field = made.GetType().GetField(pair.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
				if (field == null || LockedFields.Contains(pair.Key))
				{
					ctx.Log.LogWarning($"{id}: {made.GetType().Name} has no field '{pair.Key}' that can be set from JSON, so it is left as it was.");
					continue;
				}
				try
				{
					Type type = field.FieldType;
					object value = pair.Value;
					if (type == typeof(float) && value is IConvertible)
					{
						field.SetValue(made, Convert.ToSingle(value));
						done.Add(pair.Key + "=" + field.GetValue(made));
					}
					else if (type == typeof(int) && value is IConvertible && !(value is string))
					{
						field.SetValue(made, Convert.ToInt32(value));
						done.Add(pair.Key + "=" + field.GetValue(made));
					}
					else if (type == typeof(bool) && value is bool)
					{
						field.SetValue(made, value);
						done.Add(pair.Key + "=" + field.GetValue(made));
					}
					else if (type == typeof(string) && value is string)
					{
						field.SetValue(made, value);
						done.Add(pair.Key + "=" + field.GetValue(made));
					}
					else if (type.IsEnum && value is string)
					{
						field.SetValue(made, Enum.Parse(type, (string)value, true));
						done.Add(pair.Key + "=" + field.GetValue(made));
					}
					else
					{
						ctx.Log.LogWarning($"{id}: field '{pair.Key}' is a {type.Name}, which JSON cannot set (numbers, true/false, text and enum names only), so it is left as it was.");
					}
				}
				catch (Exception e)
				{
					ctx.Log.LogWarning($"{id}: field '{pair.Key}' could not be set to {pair.Value}: {e.Message}");
				}
			}
			if (done.Count > 0)
			{
				ctx.Log.LogInfo($"{id}: fields set on the {made.GetType().Name}: {string.Join(", ", done)}");
			}
		}

		private static UpgradeComponent Create(ModContext ctx, ItemCatalog catalog, Dictionary<string, object> def, ItemTier tier, List<StatBoost> boosts)
		{
			string id = MiniJson.String(def, "id");
			string copyOf = MiniJson.String(def, "copyOf");
			UpgradeComponent basis;
			if (copyOf != "")
			{
				basis = catalog.items.FirstOrDefault(i => i != null && i.id == copyOf);
			}
			else
			{
				basis = ctx.Items.FirstOf<PassiveUpgrade>(catalog, p => p.id == "treeFreshener") ?? ctx.Items.FirstOf<PassiveUpgrade>(catalog);
			}
			if (basis == null)
			{
				ctx.Log.LogError($"{id}: nothing to copy (copyOf '{copyOf}' is not in the catalog).");
				return null;
			}

			string name = MiniJson.String(def, "name");
			string flavour = MiniJson.String(def, "flavour");
			UpgradeComponent made = ctx.Items.Clone(basis, id);
			// First line is the name; the grey line under it is how the game styles every item's flavour text.
			made.description = name + (flavour == "" ? "" : "\n<color=#7e7e7e>" + flavour + "</color>");
			made.interaction = "";
			made.secondaryDescription = "";
			made.tier = tier;
			made.cost = (int)MiniJson.Number(def, "cost", 20f);
			made.disabled = false;
			made.startsLocked = false;
			made.unlockKind = UnlockKind.Discovery;
			made.hideStatLines = false;
			made.forceShow = new List<string>();
			made.forceHide = new List<string>();
			made.customSprites = new List<CustomSprite>();
			made.statBoosts = boosts;
			if (made is PassiveUpgrade passive)
			{
				passive.charmVariant = 0;
			}
			ApplyFields(ctx, id, made, def);

			string iconFile = MiniJson.String(def, "icon");
			if (iconFile != "")
			{
				Sprite icon = ctx.Sprites.LoadSprite(iconFile, basis.image);
				if (icon != null)
				{
					made.image = icon;
				}
			}
			string charmFile = MiniJson.String(def, "charm");
			if (charmFile != "" && made is PassiveUpgrade withCharm)
			{
				Sprite charm = ctx.Sprites.LoadSprite(charmFile, withCharm.charmSprite);
				if (charm != null)
				{
					withCharm.charmSprite = charm;
				}
			}
			ctx.Log.LogInfo($"Created {name} from '{basis.name}': {string.Join(", ", boosts.Select(b => b.stat + " " + b.amount.ToString("+0.##;-0.##")))}");
			return made;
		}
	}
}
