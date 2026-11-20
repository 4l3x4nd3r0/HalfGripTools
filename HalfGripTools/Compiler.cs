using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HalfGripTools
{
	/// <summary>
	/// Compiles a mod's C# source with Roslyn, on the player's PC, against the game's own assemblies.
	/// The result is cached in the data folder (<c>cache</c>), keyed by a hash of the source, the game DLL and this tool's version,
	/// so a mod only recompiles when something it depends on changed. The cache is never downloaded, only generated here.
	/// </summary>
	internal static class Compiler
	{
		private static List<MetadataReference> references;

		private static List<MetadataReference> restrictedReferences;

		private static string gameHash;

		/// <summary>Returns the compiled mod, or null (with the errors logged) if it does not compile.</summary>
		internal static Assembly Build(ModManifest manifest, string[] sourceFiles, ModLog log, List<string> errors = null)
		{
			string cacheDir = ModPaths.CacheDir;
			Directory.CreateDirectory(cacheDir);

			string hash = Hash(manifest, sourceFiles);
			string cached = Path.Combine(cacheDir, manifest.id + "-" + hash + ".dll");
			if (File.Exists(cached))
			{
				try
				{
					Assembly fromCache = Assembly.Load(File.ReadAllBytes(cached));
					log.LogInfo($"{manifest.DisplayName}: using the cached build ({Path.GetFileName(cached)})");
					return fromCache;
				}
				catch (Exception e)
				{
					// Half written by another game that is starting at the same time, or damaged: compile again.
					log.LogWarning($"{manifest.DisplayName}: the cached build could not be used ({e.GetType().Name}); compiling again");
				}
			}

			Stopwatch watch = Stopwatch.StartNew();
			CSharpParseOptions parse = new CSharpParseOptions(LanguageVersion.CSharp10);
			List<SyntaxTree> trees = new List<SyntaxTree>();
			foreach (string file in sourceFiles)
			{
				trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file, Encoding.UTF8), parse, file));
			}
			// "trust": "restricted": no reference to the game's own assembly, Harmony or Steamworks, no unsafe code, and a second pass over
			// everything the source names (RestrictedCheck) against the allow list. See docs\WRITING_MODS.md, "Restricted mods".
			bool restricted = manifest.trust == "restricted";
			CSharpCompilation compilation = CSharpCompilation.Create(
				"HalfGripMod." + manifest.id,
				trees,
				restricted ? RestrictedReferences(log) : References(log),
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: !restricted));

			using (MemoryStream dll = new MemoryStream())
			{
				var result = compilation.Emit(dll);
				if (!result.Success)
				{
					log.LogError($"{manifest.DisplayName} does not compile:");
					bool missingType = false;
					foreach (Diagnostic d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20))
					{
						log.LogError("  " + d);
						missingType |= d.Id == "CS0012" || d.Id == "CS0246" || d.Id == "CS0234";
						if (errors != null && errors.Count < MaxShownErrors)
						{
							errors.Add(Describe(manifest, d));
						}
					}
					if (restricted && missingType)
					{
						string hint = "A restricted mod has no access to the game's own classes, Harmony or System.IO: use ctx (Items, Maps, Events, Storage, ...) or set \"trust\" to \"full\".";
						log.LogError("  " + hint);
						if (errors != null && errors.Count < MaxShownErrors)
						{
							errors.Add(hint);
						}
					}
					return null;
				}
				if (restricted)
				{
					List<string> refused = RestrictedCheck.Run(manifest, compilation);
					if (refused.Count > 0)
					{
						log.LogError($"{manifest.DisplayName} is a restricted mod and uses what it may not:");
						foreach (string line in refused.Take(20))
						{
							log.LogError("  " + line);
						}
						if (errors != null)
						{
							foreach (string line in refused.Take(MaxShownErrors))
							{
								if (errors.Count < MaxShownErrors)
								{
									errors.Add(line);
								}
							}
						}
						return null;
					}
					log.LogInfo($"{manifest.DisplayName}: restricted mod, checked against the allow list");
				}
				byte[] bytes = dll.ToArray();
				foreach (string old in Directory.GetFiles(cacheDir, manifest.id + "-*.dll"))
				{
					TryDelete(old);
				}
				try
				{
					// Written under another name and moved into place, so a second game starting now never reads half a file.
					string temp = cached + "." + Process.GetCurrentProcess().Id + ".tmp";
					File.WriteAllBytes(temp, bytes);
					File.Move(temp, cached);
				}
				catch (Exception e)
				{
					log.LogWarning($"{manifest.DisplayName}: the compiled build could not be cached ({e.Message}); it runs this time and is compiled again next start");
				}
				log.LogInfo($"{manifest.DisplayName}: compiled {sourceFiles.Length} files in {watch.ElapsedMilliseconds} ms");
				return Assembly.Load(bytes);
			}
		}

		/// <summary>How many compile errors are kept for the mod manager. The log has more.</summary>
		internal const int MaxShownErrors = 5;

		/// <summary>One error as <c>src\File.cs(12,5): CS1002 ; expected</c>, the path relative to the mod's folder, for people who read it in the game.</summary>
		private static string Describe(ModManifest manifest, Diagnostic d)
		{
			FileLinePositionSpan span = d.Location.GetLineSpan();
			string path = span.Path ?? "";
			string root = Path.GetFullPath(manifest.Folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
			if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				path = path.Substring(root.Length);
			}
			return $"{path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {d.Id} {d.GetMessage()}";
		}

		private static List<MetadataReference> References(ModLog log)
		{
			if (references != null)
			{
				return references;
			}
			references = new List<MetadataReference>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void Add(string path)
			{
				string name = Path.GetFileName(path);
				if (!seen.Add(name))
				{
					return;
				}
				try
				{
					AssemblyName.GetAssemblyName(path);      // throws for native DLLs
					references.Add(MetadataReference.CreateFromFile(path));
				}
				catch (BadImageFormatException)
				{
				}
			}
			string managed = Path.Combine(UnityEngine.Application.dataPath, "Managed");
			foreach (string path in Directory.GetFiles(managed, "*.dll"))
			{
				Add(path);
			}
			// The patching API of the host, if it has one (Harmony), so mods with [HarmonyPatch] classes compile; and this assembly, which is the mod API.
			Assembly patchApi = ModRuntime.PatchProvider?.Api;
			if (patchApi != null)
			{
				Add(patchApi.Location);
			}
			Add(typeof(Compiler).Assembly.Location);
			log.LogInfo("Compiler references: " + references.Count + " assemblies");
			return references;
		}

		/// <summary>
		/// What a restricted mod compiles against: the runtime's own libraries, Unity's engine modules and this assembly (the mod API). Not the game's
		/// assembly, Harmony, Steamworks, Newtonsoft or the compiler.
		/// </summary>
		private static List<MetadataReference> RestrictedReferences(ModLog log)
		{
			if (restrictedReferences != null)
			{
				return restrictedReferences;
			}
			restrictedReferences = new List<MetadataReference>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void Add(string path)
			{
				string name = Path.GetFileName(path);
				if (!seen.Add(name))
				{
					return;
				}
				try
				{
					AssemblyName.GetAssemblyName(path);
					restrictedReferences.Add(MetadataReference.CreateFromFile(path));
				}
				catch (BadImageFormatException)
				{
				}
			}
			string managed = Path.Combine(UnityEngine.Application.dataPath, "Managed");
			foreach (string path in Directory.GetFiles(managed, "*.dll"))
			{
				string name = Path.GetFileNameWithoutExtension(path);
				bool runtime = name == "mscorlib" || name == "netstandard" || name == "System" || name.StartsWith("System.");
				bool unity = name.StartsWith("UnityEngine.") || name == "UnityEngine" || name == "Unity.TextMeshPro";
				if (runtime || unity)
				{
					Add(path);
				}
			}
			Add(typeof(Compiler).Assembly.Location);
			log.LogInfo("Compiler references for restricted mods: " + restrictedReferences.Count + " assemblies");
			return restrictedReferences;
		}

		private static string Hash(ModManifest manifest, string[] sourceFiles)
		{
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder text = new StringBuilder();
				// The module id of a deterministic build comes from the DLL's content, so any change to HalfGripTools (its API
				// included) rebuilds every mod, even if the version number stayed the same.
				text.Append(typeof(Compiler).Module.ModuleVersionId).Append('|').Append(GameHash()).Append('|').Append(manifest.id).Append('|').Append(manifest.trust);
				byte[] head = Encoding.UTF8.GetBytes(text.ToString());
				sha.TransformBlock(head, 0, head.Length, null, 0);
				foreach (string file in sourceFiles.OrderBy(f => f, StringComparer.Ordinal))
				{
					byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(file));
					sha.TransformBlock(name, 0, name.Length, null, 0);
					byte[] body = File.ReadAllBytes(file);
					sha.TransformBlock(body, 0, body.Length, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return BitConverter.ToString(sha.Hash).Replace("-", "").Substring(0, 16).ToLowerInvariant();
			}
		}

		internal static string GameHash()
		{
			if (gameHash == null)
			{
				string dll = Path.Combine(UnityEngine.Application.dataPath, "Managed", "Assembly-CSharp.dll");
				using (SHA256 sha = SHA256.Create())
				using (FileStream stream = File.OpenRead(dll))
				{
					gameHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
				}
			}
			return gameHash;
		}

		private static void TryDelete(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (IOException)
			{
			}
		}
	}
}
