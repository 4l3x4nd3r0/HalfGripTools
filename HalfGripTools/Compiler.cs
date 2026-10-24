using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HalfGripTools
{
	/// <summary>
	/// Compiles a mod's C# source with Roslyn, on the player's PC, against the game's own assemblies.
	/// The result is cached in <c>BepInEx\cache\HalfGripMods</c>, keyed by a hash of the source, the game DLL and this tool's version,
	/// so a mod only recompiles when something it depends on changed. The cache is never downloaded, only generated here.
	/// </summary>
	internal static class Compiler
	{
		private static List<MetadataReference> references;

		private static string gameHash;

		/// <summary>Returns the compiled mod, or null (with the errors logged) if it does not compile.</summary>
		internal static Assembly Build(ModManifest manifest, string[] sourceFiles, ManualLogSource log)
		{
			string cacheDir = Path.Combine(BepInEx.Paths.BepInExRootPath, "cache", "HalfGripMods");
			Directory.CreateDirectory(cacheDir);

			string hash = Hash(manifest, sourceFiles);
			string cached = Path.Combine(cacheDir, manifest.id + "-" + hash + ".dll");
			if (File.Exists(cached))
			{
				log.LogInfo($"{manifest.DisplayName}: using the cached build ({Path.GetFileName(cached)})");
				return Assembly.Load(File.ReadAllBytes(cached));
			}

			Stopwatch watch = Stopwatch.StartNew();
			CSharpParseOptions parse = new CSharpParseOptions(LanguageVersion.CSharp10);
			List<SyntaxTree> trees = new List<SyntaxTree>();
			foreach (string file in sourceFiles)
			{
				trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file, Encoding.UTF8), parse, file));
			}
			CSharpCompilation compilation = CSharpCompilation.Create(
				"HalfGripMod." + manifest.id,
				trees,
				References(log),
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));

			using (MemoryStream dll = new MemoryStream())
			{
				var result = compilation.Emit(dll);
				if (!result.Success)
				{
					log.LogError($"{manifest.DisplayName} does not compile:");
					foreach (Diagnostic d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20))
					{
						log.LogError("  " + d);
					}
					return null;
				}
				byte[] bytes = dll.ToArray();
				foreach (string old in Directory.GetFiles(cacheDir, manifest.id + "-*.dll"))
				{
					TryDelete(old);
				}
				File.WriteAllBytes(cached, bytes);
				log.LogInfo($"{manifest.DisplayName}: compiled {sourceFiles.Length} files in {watch.ElapsedMilliseconds} ms");
				return Assembly.Load(bytes);
			}
		}

		private static List<MetadataReference> References(ManualLogSource log)
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
			string core = Path.Combine(BepInEx.Paths.BepInExRootPath, "core");
			Add(Path.Combine(core, "BepInEx.dll"));
			Add(Path.Combine(core, "0Harmony.dll"));
			Add(typeof(Compiler).Assembly.Location);
			log.LogInfo("Compiler references: " + references.Count + " assemblies");
			return references;
		}

		private static string Hash(ModManifest manifest, string[] sourceFiles)
		{
			using (SHA256 sha = SHA256.Create())
			{
				StringBuilder text = new StringBuilder();
				// The module id of a deterministic build comes from the DLL's content, so any change to HalfGripTools (its API
				// included) rebuilds every mod, even if the version number stayed the same.
				text.Append(typeof(Compiler).Module.ModuleVersionId).Append('|').Append(GameHash()).Append('|').Append(manifest.id);
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
