using HalfGripTools;

namespace RestrictedFile
{
	// This mod is refused on purpose. The two lines below are what a restricted mod may not do: use System.IO, or read a game class by name.
	public class Mod : HalfGripMod
	{
		public override void Load(ModContext ctx)
		{
			System.IO.File.WriteAllText("hello.txt", "a restricted mod must use ctx.Storage");
			var type = typeof(Mod).Assembly.GetType("Something");
			ctx.Log.LogInfo("never reached " + type);
		}
	}
}
