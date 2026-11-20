using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HalfGripTools
{
	/// <summary>
	/// The second pass of a restricted mod's compile: every name in the source is resolved to what it means and compared with
	/// <c>RestrictedApi.txt</c> (an embedded file of allow and deny rules). The mod is refused if one of them is outside the list.
	/// </summary>
	internal static class RestrictedCheck
	{
		private sealed class Rule
		{
			public string Name;
			public bool Children;
		}

		private static List<Rule> allow;
		private static List<Rule> deny;

		private static void LoadRules()
		{
			if (allow != null)
			{
				return;
			}
			allow = new List<Rule>();
			deny = new List<Rule>();
			Assembly self = typeof(RestrictedCheck).Assembly;
			string resource = self.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("RestrictedApi.txt", StringComparison.OrdinalIgnoreCase));
			if (resource == null)
			{
				// No list means nothing is allowed: a restricted mod then fails, which is the safe way round.
				return;
			}
			using (StreamReader reader = new StreamReader(self.GetManifestResourceStream(resource)))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					line = line.Trim();
					if (line.Length == 0 || line.StartsWith("#"))
					{
						continue;
					}
					string[] parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length != 2)
					{
						continue;
					}
					bool children = parts[1].EndsWith(".*");
					Rule rule = new Rule { Name = children ? parts[1].Substring(0, parts[1].Length - 2) : parts[1].Trim(), Children = children };
					(parts[0] == "allow" ? allow : deny).Add(rule);
				}
			}
		}

		/// <summary>The problems found, one per line as <c>src\File.cs(12,5): restricted: ...</c>; empty if the mod keeps to the list.</summary>
		internal static List<string> Run(ModManifest manifest, CSharpCompilation compilation)
		{
			LoadRules();
			List<string> problems = new List<string>();
			HashSet<string> seen = new HashSet<string>();
			foreach (SyntaxTree tree in compilation.SyntaxTrees)
			{
				SemanticModel model = compilation.GetSemanticModel(tree);
				foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
				{
					if (!(node is SimpleNameSyntax) && !(node is BaseObjectCreationExpressionSyntax) && !(node is AttributeSyntax))
					{
						continue;
					}
					SymbolInfo info = model.GetSymbolInfo(node);
					ISymbol symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
					if (symbol == null || SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly))
					{
						continue;
					}
					string why = Refuse(symbol);
					if (why == null)
					{
						continue;
					}
					FileLinePositionSpan span = node.GetLocation().GetLineSpan();
					string root = Path.GetFullPath(manifest.Folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
					string path = span.Path ?? "";
					if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
					{
						path = path.Substring(root.Length);
					}
					string message = $"{path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): restricted mods may not use {why}";
					if (seen.Add(message))
					{
						problems.Add(message);
					}
				}
			}
			return problems;
		}

		/// <summary>Null if the symbol may be used, otherwise a short reason naming it.</summary>
		private static string Refuse(ISymbol symbol)
		{
			if (symbol is INamespaceSymbol || symbol is ILocalSymbol || symbol is IParameterSymbol || symbol is ITypeParameterSymbol || symbol is IAliasSymbol
				|| symbol is IRangeVariableSymbol || symbol is ILabelSymbol || symbol is IDiscardSymbol)
			{
				return null;
			}
			if (symbol is IArrayTypeSymbol array)
			{
				return Refuse(array.ElementType);
			}
			if (symbol is IPointerTypeSymbol)
			{
				return "pointers";
			}
			INamedTypeSymbol type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
			if (type == null)
			{
				return null;
			}
			type = type.OriginalDefinition;
			INamedTypeSymbol outer = type;
			while (outer.ContainingType != null)
			{
				outer = outer.ContainingType;
			}
			string ns = outer.ContainingNamespace == null || outer.ContainingNamespace.IsGlobalNamespace ? "" : outer.ContainingNamespace.ToDisplayString();
			List<string> chain = new List<string>();
			for (INamedTypeSymbol t = type; t != null; t = t.ContainingType)
			{
				chain.Insert(0, t.Name);
			}
			string typeName = (ns.Length > 0 ? ns + "." : "") + string.Join(".", chain);
			string full = ReferenceEquals(symbol, type) || symbol is INamedTypeSymbol ? typeName : typeName + "." + symbol.Name;

			foreach (Rule rule in deny)
			{
				if (Matches(rule, full) || Matches(rule, typeName))
				{
					return full + " (on the deny list)";
				}
			}
			foreach (Rule rule in allow)
			{
				if (rule.Children ? ns == rule.Name || ns.StartsWith(rule.Name + ".") : ns == rule.Name)
				{
					return null;
				}
			}
			return full + " (the namespace '" + ns + "' is not on the allow list)";
		}

		private static bool Matches(Rule rule, string name)
		{
			return rule.Children ? name == rule.Name || name.StartsWith(rule.Name + ".") : name == rule.Name;
		}
	}
}
