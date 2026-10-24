using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>
	/// A mod's view of the item catalog. The game builds its id lookup once, in <c>UpgradeMapper.Awake</c>, so an item has to be in
	/// <c>ItemCatalog.Instance.items</c> before that. HalfGripTools calls every mod's <see cref="Add"/> and <see cref="OnInject"/>
	/// callbacks at each point where that matters (before UpgradeMapper wakes, before a new run takes its loadout, and after each
	/// scene load), so they must be safe to call any number of times.
	/// </summary>
	public sealed class ModItems
	{
		private sealed class Entry
		{
			public string Id;
			public Func<ItemCatalog, UpgradeComponent> Create;
			public UpgradeComponent Item;
			public bool Failed;
		}

		private readonly ModContext ctx;

		private readonly List<Entry> entries = new List<Entry>();

		private readonly List<Action<ItemCatalog>> hooks = new List<Action<ItemCatalog>>();

		internal ModItems(ModContext ctx)
		{
			this.ctx = ctx;
		}

		/// <summary>
		/// Registers an item. <paramref name="create"/> runs once, as soon as the catalog exists, and its result is added to the catalog.
		/// Return null (after logging why) if the item cannot be made. An id already in the catalog is skipped with a warning.
		/// </summary>
		public void Add(string id, Func<ItemCatalog, UpgradeComponent> create)
		{
			entries.Add(new Entry { Id = id, Create = create });
		}

		/// <summary>Runs <paramref name="hook"/> at every injection point (for example to add an item to a loadout). It must be idempotent.</summary>
		public void OnInject(Action<ItemCatalog> hook)
		{
			hooks.Add(hook);
		}

		/// <summary>The item registered under <paramref name="id"/> by this mod, once it has been made.</summary>
		public UpgradeComponent Get(string id)
		{
			return entries.FirstOrDefault(e => e.Id == id)?.Item;
		}

		/// <summary>Makes a copy of an existing item to build on. It is hidden from the editor and survives scene loads.</summary>
		public T Clone<T>(T basis, string id) where T : UpgradeComponent
		{
			T made = UnityEngine.Object.Instantiate(basis);
			made.hideFlags = HideFlags.HideAndDontSave;
			made.name = id;
			made.id = id;
			return made;
		}

		/// <summary>The first catalog item of type <c>T</c> (exactly that type, not a subclass) that matches, or null.</summary>
		public T FirstOf<T>(ItemCatalog catalog, Func<T, bool> where = null) where T : UpgradeComponent
		{
			return catalog.items.OfType<T>().FirstOrDefault(i => i != null && i.GetType() == typeof(T) && (where == null || where(i)));
		}

		internal void Inject(ItemCatalog catalog)
		{
			foreach (Entry entry in entries)
			{
				if (entry.Failed)
				{
					continue;
				}
				try
				{
					if (entry.Item == null)
					{
						if (catalog.items.Any(i => i != null && i.id == entry.Id))
						{
							ctx.Log.LogWarning("Id '" + entry.Id + "' is already in the catalog, skipping it");
							entry.Failed = true;
							continue;
						}
						entry.Item = entry.Create(catalog);
						if (entry.Item == null)
						{
							entry.Failed = true;
							continue;
						}
					}
					if (!catalog.items.Contains(entry.Item))
					{
						catalog.items.Add(entry.Item);
						ctx.Log.LogInfo($"Added {entry.Id} to the item catalog: tier={entry.Item.tier} cost={entry.Item.cost}");
					}
				}
				catch (Exception e)
				{
					entry.Failed = true;
					ctx.Log.LogError("Item " + entry.Id + " failed: " + e);
				}
			}
			foreach (Action<ItemCatalog> hook in hooks)
			{
				try
				{
					hook(catalog);
				}
				catch (Exception e)
				{
					ctx.Log.LogError("Inject hook failed: " + e);
				}
			}
		}

		/// <summary>Takes this mod's items out of the catalog and the id lookup. The objects are kept, so anything still holding one stays valid.</summary>
		internal void RemoveAll()
		{
			ItemCatalog catalog = ItemCatalog.Instance;
			UpgradeMapper mapper = UpgradeMapper.Instance;
			foreach (Entry entry in entries)
			{
				if (entry.Item == null)
				{
					continue;
				}
				catalog?.items.Remove(entry.Item);
				if (mapper != null)
				{
					mapper.allUpgrades?.Remove(entry.Item);
					Traverse.Create(mapper).Field("lookup").GetValue<Dictionary<string, UpgradeComponent>>()?.Remove(entry.Id);
				}
				ctx.Log.LogInfo("Removed " + entry.Id + " from the item catalog");
			}
			entries.Clear();
			hooks.Clear();
		}
	}

	/// <summary>The injection points, shared by all mods.</summary>
	internal static class ItemInjector
	{
		internal static void InjectAll()
		{
			ItemCatalog catalog = ItemCatalog.Instance;
			if (catalog == null || !ModLoader.Running)
			{
				return;
			}
			foreach (ModContext mod in ModLoader.Mods)
			{
				mod.Items.Inject(catalog);
			}
		}
	}

	[HarmonyPatch(typeof(UpgradeMapper), "Awake")]
	internal static class UpgradeMapperAwakePatch
	{
		private static void Prefix()
		{
			ItemInjector.InjectAll();
		}
	}

	[HarmonyPatch(typeof(LoadoutProgress), nameof(LoadoutProgress.ApplyToNewRun))]
	internal static class LoadoutApplyPatch
	{
		private static void Prefix()
		{
			ItemInjector.InjectAll();
		}
	}
}
