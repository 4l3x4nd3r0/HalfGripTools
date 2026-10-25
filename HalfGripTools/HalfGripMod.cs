using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace HalfGripTools
{
	/// <summary>
	/// The entry point of a code mod. Put exactly one class that derives from this in the mod's <c>src</c> folder.
	/// HalfGripTools compiles the mod, creates the class and calls <see cref="Load"/>.
	/// </summary>
	public abstract class HalfGripMod
	{
		/// <summary>Set the mod up: bind config, register items, apply patches.</summary>
		public abstract void Load(ModContext ctx);

		/// <summary>
		/// Called when mods are switched off while the game runs (speedrun mode turned on). Undo anything the mod added to a scene.
		/// Harmony patches, items, scene callbacks and <see cref="ModContext.OnUnload"/> callbacks are removed for you.
		/// </summary>
		public virtual void Unload()
		{
		}
	}

	/// <summary>What HalfGripTools hands a mod when it loads.</summary>
	public sealed class ModContext
	{
		private readonly List<Action> unloadCallbacks = new List<Action>();

		internal readonly List<Action<Scene>> SceneCallbacks = new List<Action<Scene>>();

		internal Assembly Assembly;

		internal bool Active = true;

		public ModManifest Manifest { get; }

		/// <summary>The mod's folder (where mod.json is). Sprites and data files are read from here.</summary>
		public string Folder { get; }

		public ManualLogSource Log { get; }

		/// <summary>Settings, saved to <c>BepInEx\config\halfgrip.&lt;id&gt;.cfg</c>.</summary>
		public ConfigFile Config { get; }

		/// <summary>A Harmony instance owned by this mod. Everything patched with it is removed when mods switch off.</summary>
		public Harmony Harmony { get; }

		/// <summary>Loads PNGs from the mod's <c>sprites</c> folder.</summary>
		public SpriteLoader Sprites { get; }

		/// <summary>Registers items in the game's catalog.</summary>
		public ModItems Items { get; }

		/// <summary>Adds keys to the game's Settings > Controls list.</summary>
		public ModKeys Keys { get; }

		internal ModContext(ModManifest manifest, ManualLogSource log)
		{
			Manifest = manifest;
			Folder = manifest.Folder;
			Log = log;
			Config = new ConfigFile(Path.Combine(BepInEx.Paths.ConfigPath, "halfgrip." + manifest.id + ".cfg"), true);
			Harmony = new Harmony("halfgrip.mod." + manifest.id);
			Sprites = new SpriteLoader(Path.Combine(Folder, "sprites"), log);
			Items = new ModItems(this);
			Keys = new ModKeys(this);
		}

		/// <summary>
		/// Applies every <c>[HarmonyPatch]</c> class in the mod. A patch that no longer fits the game is logged and skipped,
		/// so one broken hook does not take the rest of the mod down.
		/// </summary>
		public void PatchAll()
		{
			foreach (Type type in Assembly.GetTypes())
			{
				if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
				{
					continue;
				}
				try
				{
					Harmony.CreateClassProcessor(type).Patch();
				}
				catch (Exception e)
				{
					Log.LogError("Patch " + type.Name + " failed and is off: " + e.Message);
				}
			}
		}

		/// <summary>Calls <paramref name="callback"/> every time a scene has loaded. Stops when mods are switched off.</summary>
		public void OnSceneLoaded(Action<Scene> callback)
		{
			SceneCallbacks.Add(callback);
		}

		/// <summary>Runs <paramref name="callback"/> when mods are switched off, after <see cref="HalfGripMod.Unload"/>.</summary>
		public void OnUnload(Action callback)
		{
			unloadCallbacks.Add(callback);
		}

		internal void Shutdown()
		{
			Active = false;
			foreach (Action callback in unloadCallbacks)
			{
				try
				{
					callback();
				}
				catch (Exception e)
				{
					Log.LogError("Unload callback failed: " + e.Message);
				}
			}
			unloadCallbacks.Clear();
			SceneCallbacks.Clear();
			try
			{
				Harmony.UnpatchSelf();
			}
			catch (Exception e)
			{
				Log.LogError("Unpatch failed: " + e.Message);
			}
			Items.RemoveAll();
			Keys.RemoveAll();
		}
	}
}
