using System;

namespace HalfGripTools
{
	public enum ModLogLevel
	{
		Debug,
		Info,
		Warning,
		Error,
		Fatal
	}

	/// <summary>
	/// What a host (BepInEx, the standalone loader, one day the game) implements: where HalfGripTools writes its log, and its name.
	/// The host is told once, in <see cref="ModRuntime.Start"/>.
	/// </summary>
	public interface IModHost
	{
		/// <summary>"BepInEx" or "Standalone". Shown in the log.</summary>
		string Name { get; }

		/// <summary>How this host patches the game, or null if it cannot (a host built into the game calls <see cref="GameHooks"/> instead).</summary>
		IPatchProvider Patches { get; }

		/// <summary>Writes one line. <paramref name="source"/> is the mod's name, or "HalfGripTools" for the loader itself.</summary>
		void Write(string source, ModLogLevel level, string text);
	}

	/// <summary>
	/// A mod's log (<c>ctx.Log</c>). Every line goes to the host's log with the mod's name in front. The method names are the ones
	/// BepInEx's logger has, so <c>ctx.Log.LogInfo("hello")</c> reads the same as before.
	/// </summary>
	public sealed class ModLog
	{
		private readonly Action<string, ModLogLevel, string> sink;

		public string Source { get; }

		internal ModLog(string source, Action<string, ModLogLevel, string> sink)
		{
			Source = source;
			this.sink = sink;
		}

		public void Log(ModLogLevel level, object data)
		{
			try
			{
				sink(Source, level, data == null ? "null" : data.ToString());
			}
			catch (Exception)
			{
				// A broken log must never take a mod down with it.
			}
		}

		public void LogDebug(object data)
		{
			Log(ModLogLevel.Debug, data);
		}

		public void LogInfo(object data)
		{
			Log(ModLogLevel.Info, data);
		}

		/// <summary>Same as <see cref="LogInfo"/>; kept for code written against BepInEx's logger.</summary>
		public void LogMessage(object data)
		{
			Log(ModLogLevel.Info, data);
		}

		public void LogWarning(object data)
		{
			Log(ModLogLevel.Warning, data);
		}

		public void LogError(object data)
		{
			Log(ModLogLevel.Error, data);
		}

		public void LogFatal(object data)
		{
			Log(ModLogLevel.Fatal, data);
		}
	}
}
