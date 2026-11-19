using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HalfGripTools.HarmonyHost;

namespace HalfGripTools.BepInExHost
{
	/// <summary>
	/// The BepInEx host: starts HalfGripTools inside BepInEx and writes its log into BepInEx's log. Everything else is in HalfGripTools.dll,
	/// which does not know BepInEx exists. Use this host when other BepInEx plugins are in the game; otherwise the standalone host needs no BepInEx.
	/// </summary>
	[BepInPlugin(Guid, "HalfGripTools", ModRuntime.Version)]
	public class Plugin : BaseUnityPlugin, IModHost
	{
		public const string Guid = "halfgrip.tools";

		private readonly Dictionary<string, ManualLogSource> sources = new Dictionary<string, ManualLogSource>();

		public string Name => "BepInEx";

		// BepInEx's own Harmony (the same 2.9.0 HalfGripTools.Harmony is built against) does the patching.
		public IPatchProvider Patches { get; } = new HarmonyProvider();

		private void Awake()
		{
			ModRuntime.Start(this);
		}

		public void Write(string source, ModLogLevel level, string text)
		{
			ManualLogSource log;
			lock (sources)
			{
				if (!sources.TryGetValue(source, out log))
				{
					sources[source] = log = source == "HalfGripTools" ? Logger : BepInEx.Logging.Logger.CreateLogSource(source);
				}
			}
			LogLevel mapped;
			switch (level)
			{
				case ModLogLevel.Debug: mapped = LogLevel.Debug; break;
				case ModLogLevel.Warning: mapped = LogLevel.Warning; break;
				case ModLogLevel.Error: mapped = LogLevel.Error; break;
				case ModLogLevel.Fatal: mapped = LogLevel.Fatal; break;
				default: mapped = LogLevel.Info; break;
			}
			log.Log(mapped, text);
		}
	}
}
