using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HalfGripTools.MapBuild
{
	/// <summary>One thing the Map Maker can place that is not one of its built-in shapes.</summary>
	internal sealed class CatalogProp
	{
		/// <summary>The id that goes into the map file: <c>game.barrel</c>, <c>highway.container</c>. Lower case, no spaces.</summary>
		internal string Id;

		/// <summary>What the editor calls it.</summary>
		internal string Name;

		/// <summary>The editor groups the list by this ("Pickups", "Hazards", "Scenery", or the mod's name).</summary>
		internal string Category;

		/// <summary>Makes a new copy of it, active, with no parent. The scale argument is the size the map asks for.</summary>
		internal Func<float, GameObject> Make;

		internal ModContext Owner;
	}

	/// <summary>
	/// What the Map Maker can place besides its built-in shapes: things taken from the game itself (its pickups, hazards, barrels and scenery),
	/// found by type while a scene that has them is loaded and kept as hidden copies, and pieces other mods offer through <see cref="ModMaps.AddProp"/>.
	/// Each has a stable string id that goes into the map file; an id nobody provides any more is left out of the map with a line in the log, never a
	/// failure.
	/// </summary>
	internal static class MapPropCatalog
	{
		private static readonly List<CatalogProp> all = new List<CatalogProp>();
		private static GameObject keeper;
		private static readonly HashSet<string> warned = new HashSet<string>();

		internal static IReadOnlyList<CatalogProp> All => all;

		internal static CatalogProp Find(string id)
		{
			return all.FirstOrDefault(p => p.Id == id);
		}

		internal static void Add(CatalogProp prop)
		{
			all.RemoveAll(p => p.Id == prop.Id);
			all.Add(prop);
		}

		internal static void RemoveOwned(ModContext owner)
		{
			all.RemoveAll(p => p.Owner == owner);
		}

		private static string CleanName(string name)
		{
			string clean = Regex.Replace(name.Replace("(Clone)", ""), @"\s*\(\d+\)\s*$", "").Trim();
			return Regex.Replace(clean.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
		}

		/// <summary>
		/// Looks through what is loaded for the game's own placeable things and keeps a hidden copy of the first of each name. Safe to call again:
		/// it only adds what it has not got. Call it when a scene that has them is up (the editor does it when it opens).
		/// </summary>
		internal static void DiscoverGame()
		{
			if (keeper == null)
			{
				keeper = new GameObject("HalfGripTools prop templates") { hideFlags = HideFlags.HideAndDontSave };
				keeper.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(keeper);
			}
			Take(typeof(AmmoDrop), "Pickups");
			Take(typeof(HealthDrop), "Pickups");
			Take(typeof(MoneyDrop), "Pickups");
			Take(typeof(ExplosiveBarrel), "Hazards");
			Take(typeof(MudPuddle), "Hazards");
			Take(typeof(Spikes), "Hazards");
			Take(typeof(DestructibleObject), "Scenery");
			// The road traps the game drops as it likes: its own spawner holds the prefabs.
			foreach (TrapsSpawner spawner in Resources.FindObjectsOfTypeAll<TrapsSpawner>())
			{
				if (spawner == null)
				{
					continue;
				}
				TakeObject(spawner.mudPrefab, "Hazards", "mud puddle");
				TakeObject(spawner.spikePrefab, "Hazards", "spike strip");
			}
		}

		private static void Take(Type type, string category)
		{
			foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(type))
			{
				Component component = found as Component;
				if (component != null)
				{
					TakeObject(component.gameObject, category, null);
				}
			}
		}

		private static void TakeObject(GameObject source, string category, string name)
		{
			if (source == null || source.GetComponentInChildren<EnemyController>(true) != null || source.GetComponentInChildren<CarControllerNew>(true) != null)
			{
				return;
			}
			if (source.GetComponentsInChildren<Renderer>(true).Length == 0)
			{
				return;
			}
			string shown = string.IsNullOrEmpty(name) ? Regex.Replace(source.name.Replace("(Clone)", ""), @"\s*\(\d+\)\s*$", "").Trim().ToLowerInvariant() : name;
			string id = "game." + CleanName(shown);
			if (id.Length <= 5 || all.Any(p => p.Id == id))
			{
				return;
			}
			GameObject template = UnityEngine.Object.Instantiate(source, keeper.transform);
			template.name = shown;
			template.SetActive(false);
			all.Add(new CatalogProp
			{
				Id = id,
				Name = shown,
				Category = category,
				Make = scale =>
				{
					GameObject copy = UnityEngine.Object.Instantiate(template);
					copy.name = shown;
					copy.transform.localScale = template.transform.localScale * scale;
					copy.SetActive(true);
					return copy;
				}
			});
			ModRuntime.Log.LogInfo("Map Maker: game prop '" + id + "' (" + category + ") is available");
		}

		/// <summary>A new copy of a catalog prop standing where the map says, under <paramref name="parent"/>. Null if nobody provides that id.</summary>
		internal static GameObject Spawn(string id, Transform parent, Vector3 localPosition, float yaw, float scale)
		{
			CatalogProp prop = Find(id);
			if (prop == null)
			{
				if (warned.Add(id))
				{
					ModRuntime.Log.LogWarning("Maps: no mod or game object provides the prop '" + id + "', so it is left out of the map.");
				}
				return null;
			}
			GameObject made = null;
			try
			{
				made = prop.Make(scale);
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogWarning("Maps: could not make prop '" + id + "': " + e.Message);
			}
			if (made == null)
			{
				return null;
			}
			made.transform.SetParent(parent, false);
			made.transform.localPosition = localPosition;
			made.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
			return made;
		}
	}
}
