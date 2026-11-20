using System.Collections.Generic;
using HalfGripTools;
using UnityEngine;

namespace RestrictedOk
{
	// A restricted mod may use the engine's types, plain C# and the loader's API (ctx.Log, ctx.Config, ctx.Storage, ctx.Events, ...).
	// It may not touch System.IO, the network, reflection or the game's own classes: HalfGripTools refuses it with the file and line.
	public class Mod : HalfGripMod
	{
		public override void Load(ModContext ctx)
		{
			int starts = 0;
			string text = ctx.Storage.ReadText("starts.txt");
			if (text != null)
			{
				int.TryParse(text.Trim(), out starts);
			}
			starts++;
			ctx.Storage.WriteText("starts.txt", starts.ToString());
			var words = new List<string> { "started", starts.ToString(), "time(s)" };
			ctx.Log.LogInfo("Restricted OK: " + string.Join(" ", words) + ", screen " + Screen.width + "x" + Screen.height);
		}
	}
}
