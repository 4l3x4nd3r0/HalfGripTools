using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

// Unity Doorstop (winhttp.dll in the game folder, config doorstop_config.ini) loads this assembly before Unity does and calls
// Doorstop.Entrypoint.Start(). Nothing of Unity or the game exists yet, so Start only sets up two things: where to find our DLLs, and a wait for
// Unity's core module. When that loads, one Harmony patch is placed on the engine's "active scene changed" callback, which fires for the very first
// scene, before the game's own Awake methods. That is where HalfGripTools starts. (Found by trying: patching Application's static constructor never
// fired, and the game's own classes are not in the first scenes.)
namespace Doorstop
{
	public static class Entrypoint
	{
		public static void Start()
		{
			try
			{
				HalfGripTools.StandaloneHost.Boot.Install();
			}
			catch (Exception e)
			{
				HalfGripTools.StandaloneHost.Boot.Crash("Doorstop entry point failed", e);
			}
		}
	}
}

namespace HalfGripTools.StandaloneHost
{
	internal static class Boot
	{
		private static string folder;

		private static bool started;

		/// <summary>Only mscorlib is touched here: Harmony and HalfGripTools are loaded later, from this assembly's own folder.</summary>
		internal static void Install()
		{
			folder = Path.GetDirectoryName(typeof(Boot).Assembly.Location);
			AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
			{
				string path = Path.Combine(folder, new AssemblyName(args.Name).Name + ".dll");
				return File.Exists(path) ? Assembly.LoadFrom(path) : null;
			};
			AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
			{
				if (args.LoadedAssembly.GetName().Name == "UnityEngine.CoreModule")
				{
					try
					{
						HookScenes();
					}
					catch (Exception e)
					{
						Crash("Could not hook the engine", e);
					}
				}
			};
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void HookScenes()
		{
			MethodBase target = AccessTools.Method("UnityEngine.SceneManagement.SceneManager:Internal_ActiveSceneChanged");
			if (target == null)
			{
				throw new MissingMethodException("SceneManager.Internal_ActiveSceneChanged was not found, so HalfGripTools cannot start");
			}
			new Harmony("halfgrip.tools.standalone").Patch(target, prefix: new HarmonyMethod(typeof(SceneHook).GetMethod(nameof(SceneHook.Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
		}

		internal static void StartTools()
		{
			if (started)
			{
				return;
			}
			started = true;
			try
			{
				StartInner();
			}
			catch (Exception e)
			{
				Crash("HalfGripTools could not start", e);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void StartInner()
		{
			ModRuntime.Start(new FileLogHost());
		}

		/// <summary>The engine is not up, or the loader itself failed: the only safe place to say so is a file in the temp folder.</summary>
		internal static void Crash(string what, Exception e)
		{
			try
			{
				File.AppendAllText(Path.Combine(Path.GetTempPath(), "HalfGripTools-startup.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {what}: {e}\r\n");
			}
			catch (Exception)
			{
			}
		}
	}

	internal static class SceneHook
	{
		internal static void Prefix()
		{
			Boot.StartTools();
		}
	}
}
