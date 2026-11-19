using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

		/// <summary>The first compile errors (file, line, message) or the exception that stopped the mod, for the mod manager. The log has the rest.</summary>
		internal readonly List<string> Errors = new List<string>();

		/// <summary>Where the mod was found: 0 is the first. This is the load order, apart from <c>requires</c>, which always puts a mod after what it needs.</summary>
		internal int Index;

		/// <summary>Set when the mod says it was made for another game version than this one. The mod manager shows it as a warning.</summary>
		internal string VersionWarning = "";
	}

	/// <summary>Finds the mods in the mod folders, compiles them and loads them, one failure never stopping the others.</summary>
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

		/// <summary>Extra folders to read mods from, separated by ';' (the loader's own setting). Set before <see cref="LoadAll"/>.</summary>
		internal static string ExtraFolders = "";

		/// <summary>A folder mods are read from. <see cref="Kind"/> is local, legacy, extra or workshop.</summary>
		internal sealed class ModRoot
		{
			internal string Path;

			internal string Kind;
		}

		private static IReadOnlyList<ModRoot> cachedRoots;

		/// <summary>
		/// The folders mods are read from, in the order that decides the load order and which copy of an id wins: <c>Mods</c> next to the game, the 1.x folder
		/// <c>BepInEx\HalfGripMods</c> if there is one, the extra folders as written, and last Steam's Workshop folder, so a copy you made yourself is used
		/// in place of the subscribed one.
		/// </summary>
		internal static IReadOnlyList<ModRoot> Roots()
		{
			if (cachedRoots != null)
			{
				return cachedRoots;
			}
			List<ModRoot> roots = new List<ModRoot> { new ModRoot { Path = Path.GetFullPath(ModPaths.LocalMods), Kind = "local" } };
			void Add(string path, string kind)
			{
				string full = Path.GetFullPath(path);
				if (!roots.Any(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase)))
				{
					roots.Add(new ModRoot { Path = full, Kind = kind });
				}
			}
			if (Directory.Exists(ModPaths.LegacyMods))
			{
				Add(ModPaths.LegacyMods, "legacy");
			}
			foreach (string part in (ExtraFolders ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string trimmed = part.Trim().Trim('"');
				if (trimmed == "")
				{
					continue;
				}
				if (!Directory.Exists(trimmed))
				{
					ModRuntime.Log.LogWarning($"Extra mod folder '{Path.GetFullPath(trimmed)}' does not exist and is ignored");
				}
				else
				{
					Add(trimmed, "extra");
				}
			}
			string workshop = ModPaths.WorkshopContent();
			if (workshop != null)
			{
				Add(workshop, "workshop");
			}
			cachedRoots = roots;
			return roots;
		}

		/// <summary>The mod folders in a root. In Steam's Workshop folder every item is a folder, and a mod is the item itself or a folder inside it.</summary>
		private static IEnumerable<string> ModFolders(ModRoot root)
		{
			foreach (string dir in Directory.GetDirectories(root.Path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
			{
				if (File.Exists(Path.Combine(dir, "mod.json")) || root.Kind != "workshop")
				{
					yield return dir;
				}
				else
				{
					foreach (string inner in Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
					{
						yield return inner;
					}
				}
			}
		}
		internal static void LoadAll(ModLog log)
		{
			Running = true;
			Directory.CreateDirectory(ModPaths.LocalMods);
			ModStore.Load();
			// Index = place in the load order. The mod manager lists by name, the loader loads by Index.
			List<ModManifest> discovered = Discover(log);
			foreach (ModManifest m in discovered.Where(m => m.Source.StartsWith("workshop")))
			{
				ModStore.RegisterWorkshop(m.id, ModHash.OfFolder(m.Folder));
			}
			foreach (var item in discovered.Select((m, i) => new { m, i }).OrderBy(x => x.m.DisplayName, StringComparer.OrdinalIgnoreCase))
			{
				entries.Add(new ModEntry { Manifest = item.m, Index = item.i });
			}
			log.LogInfo($"Found {entries.Count} mod(s) in {string.Join("; ", Roots().Select(r => r.Path))}. Game version {GameVersion.Current}");
			foreach (ModEntry entry in entries)
			{
				ModManifest m = entry.Manifest;
				log.LogInfo($"  {m.id} {m.version}  kind={(m.kind == "" ? "-" : m.kind)} trust={m.trust} api={(m.apiVersion == "" ? "-" : m.apiVersion)} tags=[{string.Join(",", m.tags)}] source={m.Source}");
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
					entry.Detail = $"needs HalfGripTools {manifest.tools} or newer, this is {ModRuntime.Version}";
					log.LogError($"{manifest.DisplayName} {entry.Detail}. Skipped.");
				}
				else if (!ModStore.IsEnabled(manifest.id))
				{
					entry.Status = ModStatus.Off;
					log.LogInfo(ModStore.NeedsConsent(manifest.id) ? $"{manifest.DisplayName} comes from Steam's Workshop and stays off until you switch it on in the mod manager" : $"{manifest.DisplayName} is switched off in the mod manager");
				}
				else
				{
					candidates.Add(entry);
				}
			}

			List<ModEntry> loadOrder = Order(candidates, log).ToList();
			log.LogInfo("Load order: " + string.Join(", ", loadOrder.Select(e => e.Manifest.id)));
			foreach (ModEntry entry in loadOrder)
			{
				try
				{
					Load(entry, log);
				}
				catch (Exception e)
				{
					entry.Status = ModStatus.Failed;
					entry.Detail = e.GetType().Name + ": " + e.Message;
					entry.Errors.Add(entry.Detail);
					log.LogError($"{entry.Manifest.DisplayName} failed to load and is off: {e}");
				}
			}
		}

		/// <summary>"local", "legacy" or "extra", or <c>workshop:&lt;item id&gt;</c> (the item folder's name) for a mod from Steam's Workshop.</summary>
		private static string SourceOf(ModRoot root, string modFolder)
		{
			if (root.Kind != "workshop")
			{
				return root.Kind;
			}
			string parent = Path.GetDirectoryName(modFolder);
			bool inside = !string.Equals(parent, root.Path, StringComparison.OrdinalIgnoreCase);
			return "workshop:" + Path.GetFileName(inside ? parent : modFolder);
		}
		private static bool NeedsNewerTools(ModManifest manifest)
		{
			Version needed;
			Version have;
			return Version.TryParse(manifest.tools, out needed) && Version.TryParse(ModRuntime.Version, out have) && have < needed;
		}

		private static List<ModManifest> Discover(ModLog log)
		{
			List<ModManifest> found = new List<ModManifest>();
			foreach (var found2 in Roots().SelectMany(root => ModFolders(root).Select(d => new { root, d })))
			{
				string dir = found2.d;
				string file = Path.Combine(dir, "mod.json");
				if (!File.Exists(file))
				{
					continue;
				}
				try
				{
					ModManifest manifest = ModManifest.FromJson(File.ReadAllText(file), dir);
					manifest.Source = SourceOf(found2.root, dir);
					foreach (string problem in manifest.CheckNewFields())
					{
						log.LogWarning($"{file}: {problem}");
					}
					if (string.IsNullOrEmpty(manifest.id) || manifest.id != manifest.id.ToLowerInvariant() || manifest.id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
					{
						log.LogError(file + ": 'id' must be lower case letters, digits, '-' or '_'. Skipped.");
						continue;
					}
					// The first copy found keeps the id (load order: the main folder, then the extra folders as written, each by folder name).
					// A later copy with the same files is ignored. One with different files is a different mod that picked the same id, so
					// it gets a new id made from its own hash (stable: its config and its on/off setting stay with it between starts).
					ModManifest first = found.FirstOrDefault(m => (m.OriginalId ?? m.id) == manifest.id);
					if (first != null)
					{
						string hash = ModHash.OfFolder(manifest.Folder);
						ModManifest same = found.FirstOrDefault(m => (m.OriginalId ?? m.id) == manifest.id && ModHash.OfFolder(m.Folder) == hash);
						if (same != null)
						{
							log.LogWarning($"{file}: same files as {same.Folder} (id '{manifest.id}'). Ignored.");
							continue;
						}
						string original = manifest.id;
						manifest.OriginalId = original;
						manifest.id = original + "-" + hash.Substring(0, 6);
						manifest.name = manifest.DisplayName + " (" + manifest.id + ")";
						log.LogWarning($"{file}: id '{original}' is already used by {first.Folder} with different files. Loaded as '{manifest.id}'; mods that require '{original}' get the first one.");
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
		private static IEnumerable<ModEntry> Order(List<ModEntry> candidates, ModLog log)
		{
			List<ModEntry> ordered = new List<ModEntry>();
			List<ModEntry> waiting = candidates.OrderBy(e => e.Index).ToList();
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

		private static void Load(ModEntry entry, ModLog log)
		{
			ModManifest manifest = entry.Manifest;
			ModLog modLog = ModRuntime.CreateLog(manifest.DisplayName);
			ModContext ctx = new ModContext(manifest, modLog);

			string src = Path.Combine(manifest.Folder, "src");
			string[] sources = Directory.Exists(src) ? Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories) : new string[0];
			HalfGripMod mod = null;
			if (sources.Length > 0)
			{
				Assembly assembly = Compiler.Build(manifest, sources, log, entry.Errors);
				if (assembly == null)
				{
					entry.Status = ModStatus.Failed;
					// A 1.x mod names BepInEx's types, which 2.0 does not have: say what to do instead of a wall of "type not found".
					if (sources.Any(f => File.ReadAllText(f).Contains("using BepInEx")))
					{
						entry.Errors.Insert(0, "Made for HalfGripTools 1.x (it uses BepInEx types). It needs a small update: see docs\\WRITING_MODS.md, 'Porting to 2.0'.");
						log.LogError($"{manifest.DisplayName} uses BepInEx types and was made for HalfGripTools 1.x. See docs\\WRITING_MODS.md, 'Porting to 2.0'.");
					}
					// A host built into the game has no Harmony, so a mod with [HarmonyPatch] classes cannot even compile there.
					if (ModRuntime.PatchProvider == null && sources.Any(f => File.ReadAllText(f).Contains("HarmonyLib")))
					{
						entry.Errors.Insert(0, $"This host ({ModRuntime.HostName}) cannot patch the game, and this mod uses Harmony. It cannot run here.");
						log.LogError($"{manifest.DisplayName} uses Harmony, which this host ({ModRuntime.HostName}) does not have, so it was skipped.");
					}
					entry.Detail = entry.Errors.Count > 0 ? entry.Errors[0] : "does not compile (see the log)";
					return;
				}
				ctx.Assembly = assembly;
				Type[] entryPoints = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(HalfGripMod).IsAssignableFrom(t)).ToArray();
				if (entryPoints.Length != 1)
				{
					entry.Status = ModStatus.Failed;
					entry.Detail = $"needs exactly one HalfGripMod class, found {entryPoints.Length}";
					entry.Errors.Add(entry.Detail);
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
				ctx.Config.SaveIfDirty();
			}
			entry.Status = ModStatus.Loaded;
			log.LogInfo($"Loaded {manifest.DisplayName} {manifest.version}");
		}

		/// <summary>Switches every mod off: unload hooks, patches and items. Nothing is destroyed, so objects already in use stay valid.</summary>
		internal static void Shutdown(ModLog log)
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
