using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace HalfGripTools.HarmonyHost
{
	/// <summary>
	/// Patches the game with Harmony (HarmonyX 2.9.0) so HalfGripTools gets the calls it needs from outside the game. Each patch below is one line
	/// that calls <see cref="GameHooks"/>: the day the game calls those itself, this assembly is not needed.
	/// </summary>
	public sealed class HarmonyProvider : IPatchProvider
	{
		private readonly Harmony harmony = new Harmony("halfgrip.tools");

		public string Name => "HarmonyX " + typeof(Harmony).Assembly.GetName().Version.ToString(3);

		public Assembly Api => typeof(Harmony).Assembly;

		public void InstallStartupHooks()
		{
			// These only react to speedrun mode and add the mod list to the run stats; they change nothing else in the game.
			Patch(typeof(SpeedrunModePatch));
			Patch(typeof(SpeedrunBeginRunPatch));
			Patch(typeof(RunStatsPatch));
		}

		public void InstallModHooks()
		{
			Patch(typeof(ControlsListPatch));
			GameHooks.ControlsRedirected = ControlsListPatch.Installed;
			Patch(typeof(UpgradeMapperAwakePatch));
			Patch(typeof(LoadoutApplyPatch));
			Patch(typeof(TrackIndexPatch));
			Patch(typeof(RaceBeginPatch));
			Patch(typeof(LanguageLoadedPatch));
		}

		public IModPatcher CreatePatcher(string ownerId)
		{
			return new HarmonyModPatcher(ownerId);
		}

		private void Patch(Type type)
		{
			try
			{
				harmony.CreateClassProcessor(type).Patch();
			}
			catch (Exception e)
			{
				ModRuntime.Log.LogError("Patch " + type.Name + " failed and is off: " + e.Message);
			}
		}
	}

	/// <summary>One mod's Harmony instance: <c>halfgrip.mod.&lt;id&gt;</c>. Everything patched with it is removed when the mod is switched off.</summary>
	internal sealed class HarmonyModPatcher : IModPatcher
	{
		private readonly Harmony harmony;

		internal HarmonyModPatcher(string modId)
		{
			harmony = new Harmony("halfgrip.mod." + modId);
		}

		public void PatchAll(Assembly mod, ModLog log)
		{
			foreach (Type type in mod.GetTypes())
			{
				if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
				{
					continue;
				}
				try
				{
					harmony.CreateClassProcessor(type).Patch();
				}
				catch (Exception e)
				{
					log.LogError("Patch " + type.Name + " failed and is off: " + e.Message);
				}
			}
		}

		public void UnpatchAll()
		{
			harmony.UnpatchSelf();
		}
	}

	[HarmonyPatch(typeof(Speedrun), nameof(Speedrun.Mode), MethodType.Setter)]
	internal static class SpeedrunModePatch
	{
		private static void Postfix()
		{
			GameHooks.SpeedrunModeChanged();
		}
	}

	[HarmonyPatch(typeof(Speedrun), nameof(Speedrun.BeginRun))]
	internal static class SpeedrunBeginRunPatch
	{
		private static void Prefix()
		{
			GameHooks.SpeedrunRunBeginning();
		}
	}

	[HarmonyPatch]
	internal static class RunStatsPatch
	{
		// Fit is private and takes a private nested type, so it is found by name.
		private static MethodBase TargetMethod()
		{
			return AccessTools.Method(typeof(RunStats), "Fit");
		}

		private static void Postfix(ref string __result)
		{
			__result = GameHooks.RunStatsFit(__result);
		}
	}

	[HarmonyPatch(typeof(MenuManager), "TrackIndex")]
	internal static class TrackIndexPatch
	{
		private static bool Prefix(ref int __result)
		{
			return !GameHooks.TrackIndex(ref __result);
		}
	}

	[HarmonyPatch(typeof(Loc), "Load")]
	internal static class LanguageLoadedPatch
	{
		private static void Postfix(string lang)
		{
			GameHooks.LanguageLoaded(lang);
		}
	}

	[HarmonyPatch(typeof(RunState), nameof(RunState.BeginRace))]
	internal static class RaceBeginPatch
	{
		private static void Prefix()
		{
			GameHooks.RaceBeginning();
		}
	}

	[HarmonyPatch(typeof(UpgradeMapper), "Awake")]
	internal static class UpgradeMapperAwakePatch
	{
		private static void Prefix()
		{
			GameHooks.CatalogAboutToBeRead();
		}
	}

	[HarmonyPatch(typeof(LoadoutProgress), nameof(LoadoutProgress.ApplyToNewRun))]
	internal static class LoadoutApplyPatch
	{
		private static void Prefix()
		{
			GameHooks.CatalogAboutToBeRead();
		}
	}

	/// <summary>Makes <c>ControlsSettingsUI.Fill</c> and <c>Controls.Holder</c> read <see cref="GameHooks.ControlRows"/> instead of <c>Controls.All</c>.</summary>
	[HarmonyPatch]
	internal static class ControlsListPatch
	{
		private static readonly FieldInfo All = AccessTools.Field(typeof(Controls), "All");

		private static readonly MethodInfo Rows = AccessTools.Method(typeof(GameHooks), nameof(GameHooks.ControlRows));

		private static int patched;

		private static int expected;

		internal static bool Installed => expected > 0 && patched == expected;

		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(ControlsSettingsUI), "Fill");
			yield return AccessTools.Method(typeof(Controls), "Holder");
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
		{
			expected++;
			bool found = false;
			foreach (CodeInstruction ins in code)
			{
				if (ins.opcode == OpCodes.Ldsfld && ins.operand as FieldInfo == All)
				{
					ins.opcode = OpCodes.Call;
					ins.operand = Rows;
					found = true;
				}
				yield return ins;
			}
			if (found)
			{
				patched++;
			}
		}
	}
}
