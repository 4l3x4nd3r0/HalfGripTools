using HalfGripTools;
using HarmonyLib;

namespace HelloMod
{
	public class Mod : HalfGripMod
	{
		public override void Load(ModContext ctx)
		{
			// Saved to BepInEx\config\halfgrip.hellomod.cfg on the first run.
			var greeting = ctx.Config.Bind("General", "Greeting", "hello", "Written once to the log when the mod loads.");
			ctx.Log.LogInfo(greeting.Value);

			// Applies every [HarmonyPatch] class in this mod (here: PlaygroundHello below).
			ctx.PatchAll();
		}
	}

	// Harmony patch: runs our code after the game's own Playground.Awake (the console's `playground` command).
	[HarmonyPatch(typeof(Playground), "Awake")]
	internal static class PlaygroundHello
	{
		private static void Postfix()
		{
			UnityEngine.Debug.Log("Hello from the playground");
		}
	}
}
