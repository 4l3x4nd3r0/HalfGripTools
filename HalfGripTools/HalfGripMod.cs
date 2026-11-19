using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
		/// Patches, items, scene callbacks and <see cref="ModContext.OnUnload"/> callbacks are removed for you.
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

		private readonly IModPatcher patcher;

		public ModManifest Manifest { get; }

		/// <summary>The mod's folder (where mod.json is). Sprites and data files are read from here.</summary>
		public string Folder { get; }

		public ModLog Log { get; }

		/// <summary>Settings, saved to <c>config\&lt;id&gt;.cfg</c> in the data folder (see <see cref="ModRuntime.DataFolder"/>). Bind them in <c>Load</c>.</summary>
		public ModConfig Config { get; }

		/// <summary>
		/// A folder the mod may write to, <c>storage\&lt;id&gt;</c> in the data folder, made the first time it is asked for. Use it for anything the mod
		/// has to remember. Do not write into <see cref="Folder"/>: when the mod comes from Steam's Workshop, Steam owns that folder and replaces it on an update.
		/// </summary>
		public string DataFolder => ModPaths.StorageFor(Manifest.id);

		/// <summary>The id to give a Harmony instance of your own (<c>new Harmony(ctx.HarmonyId)</c>), if <see cref="PatchAll"/> is not enough. Call <c>UnpatchSelf</c> from <see cref="OnUnload"/>.</summary>
		public string HarmonyId => "halfgrip.mod." + Manifest.id;

		/// <summary>Loads PNGs from the mod's <c>sprites</c> folder.</summary>
		public SpriteLoader Sprites { get; }

		/// <summary>Registers items in the game's catalog.</summary>
		public ModItems Items { get; }

		/// <summary>Adds keys to the game's Settings > Controls list.</summary>
		public ModKeys Keys { get; }

		/// <summary>Talks to the same mod on the other players' machines, and adds game modes, when the Multiplayer mod is installed (1.0.6 and newer). Does nothing without it.</summary>
		public ModNet Net { get; }

		internal ModContext(ModManifest manifest, ModLog log)
		{
			Manifest = manifest;
			Folder = manifest.Folder;
			Log = log;
			Config = new ModConfig(Path.Combine(ModPaths.ConfigDir, manifest.id + ".cfg"));
			patcher = ModRuntime.PatchProvider?.CreatePatcher(manifest.id);
			Sprites = new SpriteLoader(Path.Combine(Folder, "sprites"), log);
			Items = new ModItems(this);
			Keys = new ModKeys(this);
			Net = new ModNet(this);
		}

		/// <summary>
		/// Applies every <c>[HarmonyPatch]</c> class in the mod. A patch that no longer fits the game is logged and skipped, so one broken
		/// hook does not take the rest of the mod down. Needs a host that can patch the game (BepInEx and the standalone host can); in a
		/// host built into the game it logs an error and does nothing.
		/// </summary>
		public void PatchAll()
		{
			if (patcher == null)
			{
				Log.LogError("This host cannot patch the game, so [HarmonyPatch] classes are not applied. A mod that needs patches cannot run here.");
				return;
			}
			patcher.PatchAll(Assembly, Log);
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
				patcher?.UnpatchAll();
			}
			catch (Exception e)
			{
				Log.LogError("Unpatch failed: " + e.Message);
			}
			Items.RemoveAll();
			Keys.RemoveAll();
			Net.Shutdown();
		}
	}
}
