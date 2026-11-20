using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HalfGripTools.MapBuild
{
	/// <summary>
	/// The kinds of ground the Map Maker's brush can paint off the road. Each is a colour and how the game's <c>HandlingModifier</c> treats a car on it,
	/// the same grip and drag rules the road surfaces use (<see cref="Surfaces"/>), so painted dirt, sand, snow and ice change how the car drives and
	/// not only how the ground looks. The map file stores the kind of every ground vertex, run-length packed.
	/// </summary>
	internal static class GroundTypes
	{
		internal static readonly string[] Names = { "grass", "dirt", "sand", "snow", "ice", "mud", "rock" };

		internal static Color ColorOf(int index)
		{
			switch (Names[Mathf.Clamp(index, 0, Names.Length - 1)])
			{
				case "dirt": return new Color(0.42f, 0.30f, 0.18f);
				case "sand": return new Color(0.78f, 0.69f, 0.44f);
				case "snow": return new Color(0.90f, 0.93f, 0.97f);
				case "ice": return new Color(0.72f, 0.84f, 0.92f);
				case "mud": return new Color(0.25f, 0.17f, 0.10f);
				case "rock": return new Color(0.45f, 0.45f, 0.48f);
				default: return new Color(0.23f, 0.36f, 0.17f);
			}
		}

		/// <summary>Grip and drag (the <c>HandlingModifier</c>'s <c>gripModifier</c> and <c>resistanceModifier</c>) of a kind of ground.</summary>
		internal static void HandlingOf(int index, out float grip, out float drag)
		{
			switch (Names[Mathf.Clamp(index, 0, Names.Length - 1)])
			{
				case "dirt": grip = 0.75f; drag = 1.8f; break;
				case "sand": grip = 0.6f; drag = 3.6f; break;
				case "snow": grip = 0.5f; drag = 2.5f; break;
				case "ice": grip = 0.4f; drag = 0.9f; break;
				case "mud": grip = 0.45f; drag = 4f; break;
				case "rock": grip = 0.9f; drag = 1.5f; break;
				default: grip = 0.7f; drag = 3f; break;
			}
		}

		/// <summary>Run-length text: <c>kind*count</c> pairs, such as <c>0*5000,1*40,0*2000</c>. A map with no paint has none.</summary>
		internal static string Pack(byte[] paint)
		{
			if (paint == null || paint.Length == 0)
			{
				return "";
			}
			var text = new StringBuilder();
			int run = 1;
			for (int i = 1; i <= paint.Length; i++)
			{
				if (i < paint.Length && paint[i] == paint[i - 1])
				{
					run++;
					continue;
				}
				if (text.Length > 0)
				{
					text.Append(',');
				}
				text.Append(paint[i - 1]).Append('*').Append(run);
				run = 1;
			}
			return text.ToString();
		}

		/// <summary>The kinds back from <see cref="Pack"/>; null if there is no text or it does not fit <paramref name="count"/> vertices.</summary>
		internal static byte[] Unpack(string text, int count)
		{
			if (string.IsNullOrEmpty(text))
			{
				return null;
			}
			var paint = new byte[count];
			int at = 0;
			foreach (string pair in text.Split(','))
			{
				string[] parts = pair.Split('*');
				int kind, run;
				if (parts.Length != 2 || !int.TryParse(parts[0], out kind) || !int.TryParse(parts[1], out run) || run < 0 || at + run > count)
				{
					return null;
				}
				byte value = (byte)Mathf.Clamp(kind, 0, Names.Length - 1);
				for (int i = 0; i < run; i++)
				{
					paint[at++] = value;
				}
			}
			return at == count ? paint : null;
		}
	}
}
