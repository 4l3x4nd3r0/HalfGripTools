using System;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>The game's version (for example "0.418") and the check mods use to say which versions they were made for.</summary>
	internal static class GameVersion
	{
		internal static string Current => Application.version;

		/// <summary>
		/// True if <paramref name="actual"/> fits <paramref name="spec"/>. Parts are separated by dots and compared one by one:
		/// "0.418" fits only 0.418, "0.x" fits 0.1, 0.418, 0.999 and so on, "0.4x" is not supported ("x" must be a whole part).
		/// An empty spec fits everything.
		/// </summary>
		internal static bool Matches(string spec, string actual)
		{
			if (string.IsNullOrWhiteSpace(spec))
			{
				return true;
			}
			string[] want = spec.Trim().Split('.');
			string[] have = (actual ?? "").Trim().Split('.');
			for (int i = 0; i < want.Length; i++)
			{
				if (want[i].Equals("x", StringComparison.OrdinalIgnoreCase) || want[i] == "*")
				{
					return true;
				}
				if (i >= have.Length || !SamePart(want[i], have[i]))
				{
					return false;
				}
			}
			return want.Length == have.Length;
		}

		private static bool SamePart(string a, string b)
		{
			return int.TryParse(a, out int x) && int.TryParse(b, out int y) ? x == y : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}
	}
}
