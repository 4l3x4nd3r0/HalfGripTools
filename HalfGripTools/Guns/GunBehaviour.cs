using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>
	/// The behaviour of a mod's gun, written as methods the loader calls at the moments the game's gun classes (<c>NAutomaticGun</c>,
	/// <c>NPumpActionGun</c>, <c>Gun</c>, <c>MagazineManager</c>) go through. A mod gives its gun a subclass and registers it with
	/// <see cref="ModGuns.Add"/>; it needs no <c>[HarmonyPatch]</c> and no reflection into those classes.
	/// <para>
	/// Every method gets the game's gun (or rack) it is about, and a behaviour answers only for the guns that are its own: check
	/// <c>gun.Fitted</c> against the item the mod made, or whatever else marks the gun, and return the default for any other. The default
	/// of every method does nothing and lets the game carry on, so a behaviour overrides only what it needs. Several behaviours (from several
	/// mods) can be registered; the game's own method is skipped if any of them asks for that.
	/// </para>
	/// </summary>
	public abstract class GunBehaviour
	{
		/// <summary>Before the gun component's <c>Awake</c>. Return false to skip the game's own Awake (a mod that replaces the gun object does).</summary>
		public virtual bool BeforeAwake(Gun gun) { return true; }

		/// <summary>After the gun component's <c>Awake</c> (an <c>NAutomaticGun</c> or <c>NPumpActionGun</c>). Components are usually added here.</summary>
		public virtual void AfterAwake(Gun gun) { }

		/// <summary>After the gun component's <c>Start</c> (<c>NAutomaticGun</c>): the gun is set up and the rest of the car is awake.</summary>
		public virtual void AfterStart(Gun gun) { }

		/// <summary>Each time the game calls <c>Shoot</c> (every frame the trigger is held for an automatic). Return false for no shot.</summary>
		public virtual bool BeforeShoot(Gun gun) { return true; }

		/// <summary>The answer to "can the player raise the gun to aim". Return true if you decide it (set <paramref name="result"/>); false leaves it to the game.</summary>
		public virtual bool CanAim(Gun gun, ref bool result) { return false; }

		/// <summary>The answer to "is the gun full of ammo". Return true if you decide it.</summary>
		public virtual bool IsFull(Gun gun, ref bool result) { return false; }

		/// <summary>The answer to "how many shots are left". Return true if you decide it.</summary>
		public virtual bool ShotsLeft(Gun gun, ref int result) { return false; }

		/// <summary>After the pump shotgun's per-frame pump update.</summary>
		public virtual void AfterUpdatePump(NPumpActionGun gun) { }

		/// <summary>After the pump gun works out whether a grab on its parts picks the gun up to aim; change <paramref name="result"/> if needed.</summary>
		public virtual void AfterPartsAim(NPumpActionGun gun, ref bool result) { }

		/// <summary>
		/// A shot is one bullet fired along a ray (<c>Gun.FireRay</c>). Return false to fire something else instead: put where the shot ended in
		/// <paramref name="end"/> (the game uses it as the end of the tracer). <see cref="AfterFireRay"/> follows, even if the shot throws.
		/// </summary>
		public virtual bool BeforeFireRay(Gun gun, Vector3 origin, Vector3 direction, ref Vector3 end) { return true; }

		/// <summary>After the game's <c>Gun.FireRay</c> (or instead of it, when <see cref="BeforeFireRay"/> returned false); always runs.</summary>
		public virtual void AfterFireRay(Gun gun) { }

		/// <summary>Before a rack of magazines wakes up and counts them.</summary>
		public virtual void BeforeRackAwake(MagazineManager rack) { }

		/// <summary>After a rack's <c>Start</c>.</summary>
		public virtual void AfterRackStart(MagazineManager rack) { }

		/// <summary>The rack is given magazines (an ammo box, a kill, a refill). Return true if you handled it, and put how many were taken in <paramref name="taken"/>.</summary>
		public virtual bool GrantMagazines(MagazineManager rack, int magazines, ref int taken) { return false; }

		/// <summary>The rack is about to make its starting magazines. Return true to make none (<paramref name="made"/> is how many it reports).</summary>
		public virtual bool SpawnMagazines(MagazineManager rack, ref int made) { return false; }

		/// <summary>The rack is about to make the magazine objects it shows. Return true to make none.</summary>
		public virtual bool SpawnMagazineObjects(MagazineManager rack, ref int made) { return false; }

		private static readonly MethodInfo OwnerMethod = typeof(MagazineManager).GetMethod("Owner", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

		/// <summary>The gun a rack of magazines belongs to (the game's private <c>MagazineManager.Owner()</c>), or null.</summary>
		public static Gun OwnerOf(MagazineManager rack)
		{
			if (rack == null || OwnerMethod == null)
			{
				return null;
			}
			try
			{
				return OwnerMethod.Invoke(rack, null) as Gun;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static readonly MethodInfo AimPointGetter = typeof(Gun).GetProperty("AimScreenPoint", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetGetMethod(true);

		/// <summary>Where on the screen the gun is aimed (the game's protected <c>Gun.AimScreenPoint</c>). False when the game has no such member.</summary>
		public static bool TryGetAimScreenPoint(Gun gun, out Vector2 point)
		{
			point = Vector2.zero;
			if (gun == null || AimPointGetter == null)
			{
				return false;
			}
			point = (Vector2)AimPointGetter.Invoke(gun, null);
			return true;
		}

		/// <summary>The magazines a rack holds: the game's private list <c>MagazineManager.items</c> (or null).</summary>
		public static List<NAmmo> RackItems(MagazineManager rack)
		{
			return Reflect.GetField<List<NAmmo>>(rack, "items");
		}

		/// <summary>One of the rack's private collections by name (<c>homeSlots</c>, <c>slots</c>, <c>grabbedLastFrame</c>, ...), or null.</summary>
		public static object RackField(MagazineManager rack, string name)
		{
			return Reflect.GetField<object>(rack, name);
		}

		/// <summary>
		/// Takes every magazine out of a rack and destroys it: the game's private <c>items</c>, <c>homeSlots</c>, <c>grabbedLastFrame</c>, <c>grabbableWhenOpen</c>
		/// and the occupants of <c>slots</c>, so nothing in the rack still points at a magazine. Returns false when the rack held none.
		/// <paramref name="area"/> is where their sprites sat (for a mod that draws something in their place), with the layer and sorting of the first.
		/// </summary>
		public static bool EmptyRack(MagazineManager rack, out Bounds area, out int layer, out int sortingLayer, out int order)
		{
			area = new Bounds();
			layer = rack.gameObject.layer;
			sortingLayer = 0;
			order = 0;
			List<NAmmo> items = RackItems(rack);
			if (items == null)
			{
				return false;
			}
			bool any = false;
			foreach (NAmmo ammo in items)
			{
				if (ammo == null)
				{
					continue;
				}
				foreach (SpriteRenderer sprite in ammo.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (!any)
					{
						area = sprite.bounds;
						layer = sprite.gameObject.layer;
						sortingLayer = sprite.sortingLayerID;
						order = sprite.sortingOrder;
						any = true;
					}
					else
					{
						area.Encapsulate(sprite.bounds);
					}
				}
			}
			foreach (NAmmo ammo in items.ToArray())
			{
				if (ammo != null)
				{
					UnityEngine.Object.Destroy(ammo.gameObject);
				}
			}
			items.Clear();
			foreach (string name in new[] { "homeSlots", "grabbedLastFrame", "grabbableWhenOpen" })
			{
				object value = RackField(rack, name);
				if (value is IList list)
				{
					list.Clear();
				}
				else
				{
					value?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(value, null);
				}
			}
			if (RackField(rack, "slots") is IList slots)
			{
				foreach (object slot in slots)
				{
					Reflect.SetField(slot, "occupant", null);
				}
			}
			return any;
		}

		/// <summary>Calls a method of the game's gun class by name, private or not (for example <c>FireShot</c>, <c>ExitAimMode</c>). Returns false if there is none.</summary>
		public static bool CallGun(Gun gun, string name, params object[] args)
		{
			if (gun == null)
			{
				return false;
			}
			for (Type type = gun.GetType(); type != null; type = type.BaseType)
			{
				MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
				if (method != null && method.GetParameters().Length == (args == null ? 0 : args.Length))
				{
					method.Invoke(gun, args);
					return true;
				}
			}
			return false;
		}

		/// <summary>Calls the rack's private method <paramref name="name"/> with no arguments, for example <c>RideLockOnGun</c>. False if there is none.</summary>
		public static bool CallRack(MagazineManager rack, string name)
		{
			MethodInfo method = rack == null ? null : typeof(MagazineManager).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (method == null)
			{
				return false;
			}
			method.Invoke(rack, null);
			return true;
		}
	}

	/// <summary>Registers a mod's <see cref="GunBehaviour"/> (<c>ctx.Guns</c>). Everything a mod registers is removed when the mod is switched off.</summary>
	public sealed class ModGuns
	{
		private readonly ModContext ctx;
		private readonly List<GunBehaviour> mine = new List<GunBehaviour>();

		internal ModGuns(ModContext ctx)
		{
			this.ctx = ctx;
		}

		/// <summary>Starts calling this behaviour. The first behaviour of the whole game makes the loader put its hooks on the gun classes.</summary>
		public void Add(GunBehaviour behaviour)
		{
			if (behaviour == null || mine.Contains(behaviour))
			{
				return;
			}
			mine.Add(behaviour);
			GunHub.Add(behaviour, ctx);
		}

		internal void RemoveAll()
		{
			foreach (GunBehaviour behaviour in mine)
			{
				GunHub.Remove(behaviour);
			}
			mine.Clear();
		}
	}

	/// <summary>Calls every registered behaviour for the hooks of <see cref="GameHooks"/>. A behaviour that throws is logged and the game carries on.</summary>
	internal static class GunHub
	{
		private sealed class Entry
		{
			public GunBehaviour Behaviour;
			public ModContext Mod;
		}

		private static readonly List<Entry> entries = new List<Entry>();
		private static bool hooked;

		internal static void Add(GunBehaviour behaviour, ModContext ctx)
		{
			entries.Add(new Entry { Behaviour = behaviour, Mod = ctx });
			if (!hooked)
			{
				hooked = true;
				if (ModRuntime.PatchProvider != null)
				{
					ModRuntime.PatchProvider.InstallGunHooks();
				}
				else
				{
					ctx.Log.LogWarning("Guns: this host cannot patch the game, so the gun hooks are not on (the game must call GameHooks.Gun* itself).");
				}
			}
		}

		internal static void Remove(GunBehaviour behaviour)
		{
			entries.RemoveAll(e => e.Behaviour == behaviour);
		}

		/// <summary>Runs a call on every behaviour; false from any of them makes the result false (every behaviour still gets the call).</summary>
		internal static bool All(Func<GunBehaviour, bool> call, string name)
		{
			bool result = true;
			for (int i = 0; i < entries.Count; i++)
			{
				Entry e = entries[i];
				try
				{
					if (!call(e.Behaviour))
					{
						result = false;
					}
				}
				catch (Exception ex)
				{
					e.Mod.Log.LogError("Gun hook " + name + " failed: " + ex);
				}
			}
			return result;
		}

		internal static void Each(Action<GunBehaviour> call, string name)
		{
			All(b => { call(b); return true; }, name);
		}

		/// <summary>Asks behaviours in turn until one decides; true if one did.</summary>
		internal static bool Decide(Func<GunBehaviour, bool> call, string name)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				Entry e = entries[i];
				try
				{
					if (call(e.Behaviour))
					{
						return true;
					}
				}
				catch (Exception ex)
				{
					e.Mod.Log.LogError("Gun hook " + name + " failed: " + ex);
				}
			}
			return false;
		}
	}
}
