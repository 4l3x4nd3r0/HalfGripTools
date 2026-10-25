using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>What became of one installed mod when the game started.</summary>
	internal enum ModStatus
	{
		Loaded,
		/// <summary>Switched off in the mod manager.</summary>
		Off,
		/// <summary>Did not compile or threw while loading. <see cref="ModEntry.Detail"/> says why.</summary>
		Failed,
		/// <summary>A mod it needs is missing, off or failed.</summary>
		NeedsOther
	}

	internal sealed class ModEntry
	{
		internal ModManifest Manifest;

		internal ModStatus Status;

		internal string Detail = "";

		/// <summary>Set when the mod says it was made for another game version than this one. The mod manager shows it as a warning.</summary>
		internal string VersionWarning = "";
	}

	/// <summary>Finds the mods in <c>BepInEx\HalfGripMods</c>, compiles them and loads them, one failure never stopping the others.</summary>
	internal static class ModLoader
	{
		private static readonly List<ModContext> loaded = new List<ModContext>();

		private static readonly List<HalfGripMod> instances = new List<HalfGripMod>();

		private static readonly List<ModEntry> entries = new List<ModEntry>();

		/// <summary>True from the moment mods are loaded until they are switched off.</summary>
		internal static bool Running { get; private set; }

		internal static IReadOnlyList<ModContext> Mods => loaded;

		/// <summary>Every installed mod with a valid mod.json, loaded or not, in name order. What the mod manager lists.</summary>
		internal static IReadOnlyList<ModEntry> Entries => entries;

		internal static string ModsFolder => Path.Combine(BepInEx.Paths.BepInExRootPath, "HalfGripMods");

		internal static void LoadAll(ManualLogSource log)
		{
			Running = true;
			Directory.CreateDirectory(ModsFolder);
			ModStore.Load();
			foreach (ModManifest manifest in Discover(log).OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase))
			{
				entries.Add(new ModEntry { Manifest = manifest });
			}
			log.LogInfo($"Found {entries.Count} mod(s) in {ModsFolder}. Game version {GameVersion.Current}");
			foreach (ModEntry entry in entries)
			{
				if (!GameVersion.Matches(entry.Manifest.gameVersion, GameVersion.Current))
				{
					entry.VersionWarning = $"Made for game {entry.Manifest.gameVersion}, this is {GameVersion.Current}. It might not work correctly.";
					log.LogWarning($"{entry.Manifest.DisplayName}: {entry.VersionWarning}");
				}
			}

			List<ModEntry> candidates = new List<ModEntry>();
			foreach (ModEntry entry in entries)
			{
				ModManifest manifest = entry.Manifest;
				if (NeedsNewerTools(manifest))
				{
					entry.Status = ModStatus.Failed;
					entry.Detail = $"needs HalfGripTools {manifest.tools} or newer, this is {Plugin.Version}";
					log.LogError($"{manifest.DisplayName} {entry.Detail}. Skipped.");
				}
				else if (!ModStore.IsEnabled(manifest.id))
				{
					entry.Status = ModStatus.Off;
					log.LogInfo($"{manifest.DisplayName} is switched off in the mod manager");
				}
				else
				{
					candidates.Add(entry);
				}
			}

			foreach (ModEntry entry in Order(candidates, log))
			{
				try
				{
					Load(entry, log);
				}
				catch (Exception e)
				{
					entry.Status = ModStatus.Failed;
					entry.Detail = e.Message;
					log.LogError($"{entry.Manifest.DisplayName} failed to load and is off: {e}");
				}
			}
		}

		private static bool NeedsNewerTools(ModManifest manifest)
		{
			Version needed;
			Version have;
			return Version.TryParse(manifest.tools, out needed) && Version.TryParse(Plugin.Version, out have) && have < needed;
		}

		private static List<ModManifest> Discover(ManualLogSource log)
		{
			List<ModManifest> found = new List<ModManifest>();
			foreach (string dir in Directory.GetDirectories(ModsFolder))
			{
				string file = Path.Combine(dir, "mod.json");
				if (!File.Exists(file))
				{
					continue;
				}
				try
				{
					ModManifest manifest = ModManifest.FromJson(File.ReadAllText(file), dir);
					if (string.IsNullOrEmpty(manifest.id) || manifest.id != manifest.id.ToLowerInvariant() || manifest.id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
					{
						log.LogError(file + ": 'id' must be lower case letters, digits, '-' or '_'. Skipped.");
						continue;
					}
					if (found.Any(m => m.id == manifest.id))
					{
						log.LogError($"{file}: id '{manifest.id}' is used twice. Skipped.");
						continue;
					}
					found.Add(manifest);
				}
				catch (Exception e)
				{
					log.LogError($"{file} could not be read: {e.Message}");
				}
			}
			return found;
		}

		/// <summary>Mods that others <c>require</c> come first. A missing, off or circular requirement leaves that mod out.</summary>
		private static IEnumerable<ModEntry> Order(List<ModEntry> candidates, ManualLogSource log)
		{
			List<ModEntry> ordered = new List<ModEntry>();
			List<ModEntry> waiting = candidates.OrderBy(e => e.Manifest.id, StringComparer.Ordinal).ToList();
			while (waiting.Count > 0)
			{
				ModEntry next = waiting.FirstOrDefault(e => e.Manifest.requires.All(r => ordered.Any(o => o.Manifest.id == r)));
				if (next == null)
				{
					foreach (ModEntry e in waiting)
					{
						string missing = string.Join(", ", e.Manifest.requires.Where(r => !ordered.Any(o => o.Manifest.id == r)));
						e.Status = ModStatus.NeedsOther;
						e.Detail = missing;
						log.LogError($"{e.Manifest.DisplayName} needs {missing}, which is not installed, is switched off, or is circular. Skipped.");
					}
					break;
				}
				waiting.Remove(next);
				ordered.Add(next);
			}
			return ordered;
		}

		private static void Load(ModEntry entry, ManualLogSource log)
		{
			ModManifest manifest = entry.Manifest;
			ManualLogSource modLog = BepInEx.Logging.Logger.CreateLogSource(manifest.DisplayName);
			ModContext ctx = new ModContext(manifest, modLog);

			string src = Path.Combine(manifest.Folder, "src");
			string[] sources = Directory.Exists(src) ? Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories) : new string[0];
			HalfGripMod mod = null;
			if (sources.Length > 0)
			{
				Assembly assembly = Compiler.Build(manifest, sources, log);
				if (assembly == null)
				{
					entry.Status = ModStatus.Failed;
					entry.Detail = "does not compile (see the log)";
					return;
				}
				ctx.Assembly = assembly;
				Type[] entryPoints = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(HalfGripMod).IsAssignableFrom(t)).ToArray();
				if (entryPoints.Length != 1)
				{
					entry.Status = ModStatus.Failed;
					entry.Detail = $"needs exactly one HalfGripMod class, found {entryPoints.Length}";
					log.LogError($"{manifest.DisplayName}: expected exactly one class deriving from HalfGripMod, found {entryPoints.Length}. Skipped.");
					return;
				}
				mod = (HalfGripMod)Activator.CreateInstance(entryPoints[0]);
			}

			loaded.Add(ctx);
			JsonItems.Register(ctx);
			if (mod != null)
			{
				instances.Add(mod);
				mod.Load(ctx);
			}
			entry.Status = ModStatus.Loaded;
			log.LogInfo($"Loaded {manifest.DisplayName} {manifest.version}");
		}

		/// <summary>Switches every mod off: unload hooks, patches and items. Nothing is destroyed, so objects already in use stay valid.</summary>
		internal static void Shutdown(ManualLogSource log)
		{
			if (!Running)
			{
				return;
			}
			Running = false;
			MenuButton.Remove();
			for (int i = instances.Count - 1; i >= 0; i--)
			{
				try
				{
					instances[i].Unload();
				}
				catch (Exception e)
				{
					log.LogError("Unload failed: " + e.Message);
				}
			}
			for (int i = loaded.Count - 1; i >= 0; i--)
			{
				loaded[i].Shutdown();
			}
			instances.Clear();
			loaded.Clear();
			entries.Clear();
			log.LogInfo("All mods are switched off");
		}
	}
}
