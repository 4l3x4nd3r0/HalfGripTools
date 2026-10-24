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
