using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Hook census: for every mod folder, which game members it uses, which Harmony targets it patches, which members it reaches by
// reflection string, which loader API it calls, and what keeps it from running in a restricted (no file, no network...) mode.
// Roslyn, offline, same references and language version as the compiler inside HalfGripTools. Nothing is written but the report.
//   dotnet run -- --mods <folder of mods> --game <game dir> --tools <HalfGripTools.dll> --out <report.md>
namespace HookCensus
{
	internal sealed class Table
	{
		// key -> mod -> count
		internal readonly Dictionary<string, Dictionary<string, int>> Rows = new Dictionary<string, Dictionary<string, int>>();

		internal void Add(string key, string mod)
		{
			if (!Rows.TryGetValue(key, out Dictionary<string, int> perMod))
			{
				Rows[key] = perMod = new Dictionary<string, int>();
			}
			perMod[mod] = perMod.TryGetValue(mod, out int n) ? n + 1 : 1;
		}

		internal IEnumerable<KeyValuePair<string, Dictionary<string, int>>> Sorted()
		{
			return Rows.OrderByDescending(r => r.Value.Count).ThenByDescending(r => r.Value.Values.Sum()).ThenBy(r => r.Key, StringComparer.Ordinal);
		}
	}

	internal sealed class ModStats
	{
		internal string Name;
		internal int Files;
		internal int Errors;
		internal int PatchAttributes;
		internal int TraverseCreate;
		internal int AccessToolsCalls;
		internal int HarmonyTargets;
		internal readonly Dictionary<string, SortedSet<string>> Trust = new Dictionary<string, SortedSet<string>>();
		internal bool HasCode;
	}

	internal static class Program
	{
		private static readonly Table Harmony = new Table();
		private static readonly Table Reflected = new Table();
		private static readonly Table GameSymbols = new Table();
		private static readonly Table LoaderApi = new Table();
		private static readonly List<ModStats> Mods = new List<ModStats>();

		private static int Main(string[] args)
		{
			string modsPath = Arg(args, "--mods", @"F:\Git\HalfGripMods\mods");
			string game = Arg(args, "--game", @"F:\SteamLibrary\steamapps\common\Half Grip Demo");
			string tools = Arg(args, "--tools", @"F:\Git\halfGripTools\dist\BepInEx\plugins\HalfGripTools\HalfGripTools.dll");
			string outFile = Arg(args, "--out", @"F:\Git\HalfGrip\HOOK_CENSUS.md");
			string harmony = Arg(args, "--harmony", "");

			List<MetadataReference> references = References(game, tools, harmony);
			Console.WriteLine("References: " + references.Count);
			foreach (string dir in Directory.GetDirectories(modsPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
			{
				Census(dir, references);
			}
			File.WriteAllText(outFile, Report(modsPath, game), new UTF8Encoding(false));
			Console.WriteLine("Wrote " + outFile);
			return 0;
		}

		private static string Arg(string[] args, string name, string fallback)
		{
			int i = Array.IndexOf(args, name);
			return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
		}

		/// <summary>The same set the loader compiles against: every managed DLL in the game's Managed folder, BepInEx, Harmony, HalfGripTools.</summary>
		private static List<MetadataReference> References(string game, string tools, string harmony)
		{
			List<MetadataReference> list = new List<MetadataReference>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void Add(string path)
			{
				if (!File.Exists(path) || !seen.Add(Path.GetFileName(path)))
				{
					return;
				}
				try
				{
					AssemblyName.GetAssemblyName(path);
					list.Add(MetadataReference.CreateFromFile(path));
				}
				catch (BadImageFormatException)
				{
				}
			}
			foreach (string path in Directory.GetFiles(Path.Combine(game, "HalfGrip_Data", "Managed"), "*.dll"))
			{
				Add(path);
			}
			// Harmony as given (the one HalfGripTools ships), then BepInEx's types if the game has BepInEx, so a mod made for 1.x can still be read.
			if (harmony != "")
			{
				Add(harmony);
			}
			Add(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"));
			Add(Path.Combine(game, "BepInEx", "core", "0Harmony.dll"));
			Add(tools);
			return list;
		}

		private static void Census(string dir, List<MetadataReference> references)
		{
			ModStats stats = new ModStats { Name = Path.GetFileName(dir) };
			Mods.Add(stats);
			string src = Path.Combine(dir, "src");
			string[] files = Directory.Exists(src) ? Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories) : new string[0];
			stats.Files = files.Length;
			if (files.Length == 0)
			{
				Console.WriteLine(stats.Name + ": no code");
				return;
			}
			stats.HasCode = true;
			CSharpParseOptions parse = new CSharpParseOptions(LanguageVersion.CSharp10);
			List<SyntaxTree> trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f, Encoding.UTF8), parse, f)).ToList();
			CSharpCompilation compilation = CSharpCompilation.Create("census." + stats.Name, trees, references,
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
			stats.Errors = compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
			Console.WriteLine(stats.Name + ": " + files.Length + " files, " + stats.Errors + " compile errors");

			foreach (SyntaxTree tree in trees)
			{
				SemanticModel model = compilation.GetSemanticModel(tree);
				SyntaxNode root = tree.GetRoot();
				foreach (SyntaxToken token in root.DescendantTokens().Where(t => t.IsKind(SyntaxKind.UnsafeKeyword)))
				{
					AddTrust(stats, "unsafe", "unsafe");
				}
				foreach (SyntaxNode node in root.DescendantNodes())
				{
					switch (node)
					{
						case IdentifierNameSyntax id:
							Symbol(stats, model, id, id.Identifier.ValueText);
							break;
						case GenericNameSyntax generic:
							Symbol(stats, model, generic, generic.Identifier.ValueText);
							break;
						case InvocationExpressionSyntax call:
							Invocation(stats, model, call);
							break;
						case AttributeSyntax attribute:
							if (AttributeName(attribute) == "HarmonyPatch")
							{
								stats.PatchAttributes++;
							}
							else if (AttributeName(attribute) == "DllImport")
							{
								AddTrust(stats, "native code", "DllImport");
							}
							break;
					}
				}
				foreach (ClassDeclarationSyntax cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
				{
					PatchTargets(stats, model, cls);
				}
			}
		}

		private static string AttributeName(AttributeSyntax a)
		{
			string name = a.Name.ToString();
			name = name.Substring(name.LastIndexOf('.') + 1);
			return name.EndsWith("Attribute") ? name.Substring(0, name.Length - "Attribute".Length) : name;
		}

		private static ISymbol SymbolOf(SemanticModel model, SyntaxNode node)
		{
			SymbolInfo info = model.GetSymbolInfo(node);
			return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
		}

		private static void AddTrust(ModStats stats, string category, string what)
		{
			if (!stats.Trust.TryGetValue(category, out SortedSet<string> set))
			{
				stats.Trust[category] = set = new SortedSet<string>(StringComparer.Ordinal);
			}
			set.Add(what);
		}

		/// <summary>Counts one name in the code: a game member, a loader member, or something that keeps a mod from running restricted.</summary>
		private static void Symbol(ModStats stats, SemanticModel model, SyntaxNode node, string text)
		{
			if (text == "dynamic" && model.GetTypeInfo(node).Type?.TypeKind == TypeKind.Dynamic)
			{
				AddTrust(stats, "dynamic", "dynamic");
				return;
			}
			ISymbol symbol = SymbolOf(model, node);
			if (symbol == null || !(symbol.Kind == SymbolKind.NamedType || symbol.Kind == SymbolKind.Method || symbol.Kind == SymbolKind.Field ||
				symbol.Kind == SymbolKind.Property || symbol.Kind == SymbolKind.Event))
			{
				return;
			}
			INamedTypeSymbol type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
			if (type == null)
			{
				return;
			}
			string key = symbol is INamedTypeSymbol ? type.Name : type.Name + "." + (symbol is IMethodSymbol m && m.MethodKind == MethodKind.Constructor ? ".ctor" : symbol.Name);
			string assembly = symbol.ContainingAssembly?.Name ?? "";
			if (assembly == "Assembly-CSharp")
			{
				GameSymbols.Add(key, stats.Name);
			}
			else if (assembly == "HalfGripTools")
			{
				LoaderApi.Add(key, stats.Name);
			}

			string ns = type.ContainingNamespace?.ToDisplayString() ?? "";
			if (ns == "System.IO" || ns.StartsWith("System.IO."))
			{
				AddTrust(stats, "file access", type.Name);
			}
			else if (ns == "System.Net" || ns.StartsWith("System.Net."))
			{
				AddTrust(stats, "network", type.Name);
			}
			else if (ns == "System.Diagnostics" && (type.Name == "Process" || type.Name == "ProcessStartInfo"))
			{
				AddTrust(stats, "process", type.Name);
			}
			else if (ns == "System.Reflection" || ns.StartsWith("System.Reflection."))
			{
				AddTrust(stats, "reflection", type.Name);
			}
			else if (ns == "System.Threading" || ns.StartsWith("System.Threading."))
			{
				AddTrust(stats, "threading", type.Name);
			}
			else if (ns == "System.Runtime.InteropServices" && (type.Name == "Marshal" || type.Name == "DllImportAttribute"))
			{
				AddTrust(stats, "native code", type.Name);
			}
			else if (assembly == "0Harmony")
			{
				AddTrust(stats, "Harmony", type.Name);
			}
			else if (assembly == "BepInEx")
			{
				AddTrust(stats, "BepInEx types", type.Name);
			}
		}

		private static string ConstString(SemanticModel model, ExpressionSyntax e)
		{
			Optional<object> value = model.GetConstantValue(e);
			return value.HasValue ? value.Value as string : null;
		}

		/// <summary>The type name from a <c>typeof(X)</c>, or from the static type of any other expression.</summary>
		private static string TypeName(SemanticModel model, ExpressionSyntax e)
		{
			if (e is TypeOfExpressionSyntax t)
			{
				return model.GetTypeInfo(t.Type).Type?.Name ?? t.Type.ToString();
			}
			return model.GetTypeInfo(e).Type?.Name;
		}

		private static void Invocation(ModStats stats, SemanticModel model, InvocationExpressionSyntax call)
		{
			IMethodSymbol method = SymbolOf(model, call) as IMethodSymbol;
			if (method == null)
			{
				return;
			}
			string owner = method.ContainingType.Name;
			SeparatedSyntaxList<ArgumentSyntax> args = call.ArgumentList.Arguments;
			if (owner == "AccessTools" && method.ContainingAssembly.Name == "0Harmony")
			{
				stats.AccessToolsCalls++;
				string type = args.Count > 0 ? (ConstString(model, args[0].Expression) != null ? "\"" + ConstString(model, args[0].Expression) + "\"" : TypeName(model, args[0].Expression)) : "?";
				string name = args.Count > 1 ? ConstString(model, args[1].Expression) : null;
				Reflected.Add((type ?? "?") + "::" + (name ?? "(" + method.Name + ")"), stats.Name);
			}
			else if (owner == "Traverse" && method.ContainingAssembly.Name == "0Harmony")
			{
				if (method.Name == "Create")
				{
					stats.TraverseCreate++;
				}
				else if ((method.Name == "Field" || method.Name == "Property" || method.Name == "Method") && args.Count > 0)
				{
					string name = ConstString(model, args[0].Expression);
					if (name != null)
					{
						Reflected.Add(TraverseReceiver(model, call) + "::" + name, stats.Name);
					}
				}
			}
			else if (owner == "Type" && (method.Name == "GetField" || method.Name == "GetMethod" || method.Name == "GetProperty" || method.Name == "GetNestedType") && args.Count > 0)
			{
				string name = ConstString(model, args[0].Expression);
				if (name != null)
				{
					string receiver = (call.Expression as MemberAccessExpressionSyntax)?.Expression is ExpressionSyntax r ? TypeName(model, r) : null;
					Reflected.Add((receiver == "Type" ? "?" : receiver ?? "?") + "::" + name, stats.Name);
				}
			}
		}

		/// <summary>For <c>Traverse.Create(x).Field("a").Field("b")</c>: the type of <c>x</c>. A traverse kept in a variable gives "?".</summary>
		private static string TraverseReceiver(SemanticModel model, InvocationExpressionSyntax call)
		{
			ExpressionSyntax e = (call.Expression as MemberAccessExpressionSyntax)?.Expression;
			while (e is InvocationExpressionSyntax inner)
			{
				IMethodSymbol m = SymbolOf(model, inner) as IMethodSymbol;
				if (m != null && m.ContainingType.Name == "Traverse" && m.Name == "Create" && inner.ArgumentList.Arguments.Count > 0)
				{
					return TypeName(model, inner.ArgumentList.Arguments[0].Expression) ?? "?";
				}
				e = (inner.Expression as MemberAccessExpressionSyntax)?.Expression;
			}
			return "?";
		}

		private static (string type, string name) Pieces(SemanticModel model, AttributeSyntax attribute)
		{
			string type = null;
			string name = null;
			if (attribute.ArgumentList != null)
			{
				foreach (AttributeArgumentSyntax arg in attribute.ArgumentList.Arguments)
				{
					if (arg.Expression is TypeOfExpressionSyntax)
					{
						type = type ?? TypeName(model, arg.Expression);
					}
					else if (ConstString(model, arg.Expression) is string s)
					{
						name = name ?? s;
					}
				}
			}
			return (type, name);
		}

		/// <summary>The game methods a patch class patches: class-level and method-level <c>[HarmonyPatch]</c>, or a <c>TargetMethod()</c> that calls AccessTools.</summary>
		private static void PatchTargets(ModStats stats, SemanticModel model, ClassDeclarationSyntax cls)
		{
			List<AttributeSyntax> classAttrs = cls.AttributeLists.SelectMany(l => l.Attributes).Where(a => AttributeName(a) == "HarmonyPatch").ToList();
			List<AttributeSyntax> methodAttrs = cls.Members.OfType<MethodDeclarationSyntax>().SelectMany(m => m.AttributeLists).SelectMany(l => l.Attributes)
				.Where(a => AttributeName(a) == "HarmonyPatch").ToList();
			if (classAttrs.Count == 0 && methodAttrs.Count == 0)
			{
				return;
			}
			string classType = null;
			string className = null;
			foreach (AttributeSyntax a in classAttrs)
			{
				(string t, string n) = Pieces(model, a);
				classType = classType ?? t;
				className = className ?? n;
			}
			List<string> targets = new List<string>();
			if (methodAttrs.Count > 0)
			{
				foreach (AttributeSyntax a in methodAttrs)
				{
					(string t, string n) = Pieces(model, a);
					targets.Add((t ?? classType ?? "?") + "." + (n ?? className ?? "?"));
				}
			}
			else
			{
				foreach (AttributeSyntax a in classAttrs)
				{
					(string t, string n) = Pieces(model, a);
					targets.Add((t ?? classType ?? "?") + "." + (n ?? className ?? "?"));
				}
			}
			// A patch with only "[HarmonyPatch]" and a TargetMethod() that returns AccessTools.Method(typeof(X), "name").
			MethodDeclarationSyntax finder = cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.ValueText == "TargetMethod" || m.Identifier.ValueText == "TargetMethods");
			if (finder != null && targets.All(t => t.StartsWith("?.") || t.EndsWith(".?")))
			{
				targets.Clear();
				foreach (InvocationExpressionSyntax call in finder.DescendantNodes().OfType<InvocationExpressionSyntax>())
				{
					IMethodSymbol m = SymbolOf(model, call) as IMethodSymbol;
					if (m != null && m.ContainingType.Name == "AccessTools" && call.ArgumentList.Arguments.Count > 1)
					{
						targets.Add((TypeName(model, call.ArgumentList.Arguments[0].Expression) ?? "?") + "." + (ConstString(model, call.ArgumentList.Arguments[1].Expression) ?? "?") + " (TargetMethod)");
					}
				}
				if (targets.Count == 0)
				{
					targets.Add("? (TargetMethod, computed)");
				}
			}
			foreach (string target in targets)
			{
				stats.HarmonyTargets++;
				Harmony.Add(target, stats.Name);
			}
		}

		private static string Cell(Dictionary<string, int> perMod)
		{
			return string.Join(", ", perMod.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + (p.Value > 1 ? " ×" + p.Value : "")));
		}

		private static void WriteTable(StringBuilder sb, string title, string note, string firstColumn, Table table, int limit)
		{
			sb.AppendLine("## " + title).AppendLine();
			sb.AppendLine(note).AppendLine();
			sb.AppendLine("| " + firstColumn + " | mods | uses | used by |").AppendLine("|---|--:|--:|---|");
			foreach (KeyValuePair<string, Dictionary<string, int>> row in table.Sorted().Take(limit))
			{
				sb.AppendLine("| `" + row.Key.Replace("|", "\\|") + "` | " + row.Value.Count + " | " + row.Value.Values.Sum() + " | " + Cell(row.Value) + " |");
			}
			if (table.Rows.Count > limit)
			{
				sb.AppendLine().AppendLine("(" + (table.Rows.Count - limit) + " more rows, used by one mod each or fewer times, left out.)");
			}
			sb.AppendLine();
		}

		private static string Report(string modsPath, string game)
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("# Hook census").AppendLine();
			sb.AppendLine("Made by `tools\\hook-census.ps1` in HalfGripTools on " + DateTime.Now.ToString("yyyy-MM-dd") + ", from the source in `" + modsPath + "`, compiled against the game in `" + game + "` (the same references and C# 10 as the in-game compiler). Counts are from the compiler's own symbols, so a name that only looks the same is not counted. Run it again after the game or a mod changes; do not edit this file by hand.").AppendLine();
			sb.AppendLine("What it is for: the list of game members and hooks real mods depend on, ranked by how many mods use them. It decides what the official API should offer first (roadmap steps 7 to 10) and which mods could run restricted (step 11).").AppendLine();

			sb.AppendLine("## Per mod").AppendLine();
			sb.AppendLine("| mod | .cs files | compile errors | `[HarmonyPatch]` | patch targets | `Traverse.Create` | `AccessTools.*` | blockers for restricted mode |").AppendLine("|---|--:|--:|--:|--:|--:|--:|---|");
			foreach (ModStats m in Mods)
			{
				sb.AppendLine("| " + m.Name + " | " + m.Files + " | " + (m.HasCode ? m.Errors.ToString() : "-") + " | " + m.PatchAttributes + " | " + m.HarmonyTargets + " | " + m.TraverseCreate + " | " + m.AccessToolsCalls + " | " + Verdict(m) + " |");
			}
			sb.AppendLine("| **total** | " + Mods.Sum(m => m.Files) + " | " + Mods.Sum(m => m.Errors) + " | " + Mods.Sum(m => m.PatchAttributes) + " | " + Mods.Sum(m => m.HarmonyTargets) + " | " + Mods.Sum(m => m.TraverseCreate) + " | " + Mods.Sum(m => m.AccessToolsCalls) + " | |");
			sb.AppendLine().AppendLine("A mod with compile errors was only read as far as the compiler could understand it, so its numbers are too low.").AppendLine();

			WriteTable(sb, "Harmony targets", "Game methods that mods patch. `?` means the compiler could not tell (a computed target).", "target", Harmony, 400);
			WriteTable(sb, "Members reached by reflection string", "`AccessTools.*`, `Traverse...Field/Property/Method` and `Type.GetField/GetMethod/GetProperty` with a constant name. `Type::name`; `?` is a receiver the census could not follow. These are the lines that break first when the game changes.", "member", Reflected, 400);
			WriteTable(sb, "Loader API in use", "Members of HalfGripTools that mods call (`ctx.*`, `HalfGripMod`, helpers).", "member", LoaderApi, 400);
			WriteTable(sb, "Game symbols", "Types and members of `Assembly-CSharp` named in the code, by number of mods.", "symbol", GameSymbols, 250);

			sb.AppendLine("## Restricted mode: what stands in the way").AppendLine();
			sb.AppendLine("A mod could run restricted if it has no file access, network, process, reflection, threading, native code, `unsafe`, `dynamic` and no Harmony. BepInEx types alone do not count: roadmap step 12 removes them from the mod API.").AppendLine();
			foreach (ModStats m in Mods)
			{
				sb.AppendLine("**" + m.Name + "**: " + (m.Trust.Count == 0 ? (m.HasCode ? "nothing found" : "no code (data only)") : string.Join("; ", m.Trust.OrderBy(t => t.Key).Select(t => t.Key + " (" + string.Join(", ", t.Value.Take(8)) + (t.Value.Count > 8 ? ", ..." : "") + ")")))).AppendLine();
			}
			return sb.ToString();
		}

		private static string Verdict(ModStats m)
		{
			if (!m.HasCode)
			{
				return "none (data only)";
			}
			string[] blockers = m.Trust.Keys.Where(k => k != "BepInEx types").OrderBy(k => k).ToArray();
			return blockers.Length == 0 ? "none" : string.Join(", ", blockers);
		}
	}
}
