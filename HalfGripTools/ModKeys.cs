using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>A key a mod added to the game's Settings > Controls list. The player can rebind it there.</summary>
	public sealed class ModKey
	{
		/// <summary>The key it has until the player moves it.</summary>
		public KeyCode Key { get; }

		public string Label { get; }

		internal ModKey(KeyCode key, string label)
		{
			Key = key;
			Label = label;
		}

		/// <summary>The key it is on now (the default, or what the player chose).</summary>
		public KeyCode Physical => Controls.Physical(Key);

		/// <summary>The name of the key as the game prints it, for hints: "Z", "Tab", "L Shift".</summary>
		public string Name => Controls.Name(Physical);

		/// <summary>True on the frame the key goes down. False while the game has input muted (a popup, the pause menu).</summary>
		public bool Down => GameInput.KeyDown(Key);

		/// <summary>True while the key is held. False while input is muted.</summary>
		public bool Held => GameInput.Key(Key);

		/// <summary>Like <see cref="Down"/>, but also true while the game has input muted. For a mod that mutes input itself.</summary>
		public bool DownRaw => Input.GetKeyDown(Physical);
	}

	/// <summary>Makes <c>ControlsSettingsUI.Fill</c> and <c>Controls.Holder</c> read <see cref="ModKeys.Rows"/> instead of <c>Controls.All</c>.</summary>
	[HarmonyPatch]
	internal static class ControlsListPatch
	{
		private static readonly System.Reflection.FieldInfo All = AccessTools.Field(typeof(Controls), "All");

		private static readonly System.Reflection.MethodInfo Rows = AccessTools.Method(typeof(ModKeys), "Rows");

		private static int patched;

		private static int expected;

		internal static bool Installed => expected > 0 && patched == expected;

		private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(ControlsSettingsUI), "Fill");
			yield return AccessTools.Method(typeof(Controls), "Holder");
		}

		private static IEnumerable<HarmonyLib.CodeInstruction> Transpiler(IEnumerable<HarmonyLib.CodeInstruction> code)
		{
			expected++;
			bool found = false;
			foreach (HarmonyLib.CodeInstruction ins in code)
			{
				if (ins.opcode == System.Reflection.Emit.OpCodes.Ldsfld && ins.operand as System.Reflection.FieldInfo == All)
				{
					ins.opcode = System.Reflection.Emit.OpCodes.Call;
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

	/// <summary>
	/// Adds keys to the game's own control list (<c>Controls.All</c>), so they appear in Settings > Controls with the game's rebind,
	/// "Default" and swap-when-taken behaviour. The list is rebuilt from the game's original rows plus every active mod's keys.
	/// </summary>
	public sealed class ModKeys
	{
		private static Controls.Action[] original;

		private static readonly List<KeyEntry> active = new List<KeyEntry>();

		private readonly ModContext owner;

		private readonly List<KeyEntry> mine = new List<KeyEntry>();

		private sealed class KeyEntry
		{
			internal ModKey Key;
			internal Controls.Action Row;
		}

		internal ModKeys(ModContext owner)
		{
			this.owner = owner;
		}

		/// <summary>
		/// Lists a key in Settings > Controls. Returns the key to read, or null (with a warning in the log) if the default key is already
		/// used by the game or another mod. <paramref name="section"/> is where it is listed; avoid Cheats, which only shows with cheats on.
		/// </summary>
		public ModKey Add(KeyCode key, string label, Controls.Section section = Controls.Section.Race, Func<bool> shown = null)
		{
			ManualLogSource log = owner.Log;
			Controls.Action[] game = Original();
			if (game.Any(a => a.key == key) || active.Any(e => e.Key.Key == key))
			{
				log.LogWarning($"Controls: {key} is already a control, so '{label}' was not added.");
				return null;
			}
			KeyEntry entry = new KeyEntry { Key = new ModKey(key, label), Row = new Controls.Action(key, section, () => label, shown) };
			mine.Add(entry);
			active.Add(entry);
			if (Rebuild())
			{
				log.LogInfo($"Controls: added '{label}' ({key}) to Settings > Controls");
			}
			else
			{
				log.LogWarning($"Controls: '{label}' works on {key} but could not be listed in Settings > Controls.");
			}
			return entry.Key;
		}

		internal void RemoveAll()
		{
			if (mine.Count == 0)
			{
				return;
			}
			foreach (KeyEntry entry in mine)
			{
				active.Remove(entry);
			}
			mine.Clear();
			Rebuild();
		}

		private static Controls.Action[] Original()
		{
			if (original == null)
			{
				original = Controls.All;
			}
			return original;
		}

		private static bool Rebuild()
		{
			// Controls.All is static readonly: the JIT bakes its array into compiled code, so replacing it left patched methods
			// pointing at a freed array. It stays untouched; the game's readers are redirected to Rows() instead.
			rows = Original().Concat(active.Select(e => e.Row)).ToArray();
			return ControlsListPatch.Installed;
		}

		private static Controls.Action[] rows;

		/// <summary>The game's controls plus every active mod's keys. Called from the game's code by <see cref="ControlsListPatch"/>.</summary>
		public static Controls.Action[] Rows()
		{
			return rows ?? Controls.All;
		}
	}
}
