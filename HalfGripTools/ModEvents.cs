using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripTools
{
	/// <summary>
	/// Things that happen in the game, as plain C# events (<c>ctx.Events</c>), so a simple mod needs no Harmony patch to know about them.
	/// They are built on the game's own <c>Signals</c> and a light watch on the race, and they are raised on the main thread. A handler that throws
	/// is logged and does not stop the others. Everything a mod subscribed to is dropped when mods are switched off.
	/// </summary>
	public sealed class ModEvents
	{
		/// <summary>The race has started (the lights went out).</summary>
		public event Action RaceStarted;

		/// <summary>The race is over (finish, wreck or quit), once, as the game marks it over.</summary>
		public event Action RaceEnded;

		/// <summary>The player has started a new lap; the number is the lap they are now on (2 after the first line crossing).</summary>
		public event Action<int> LapCompleted;

		/// <summary>The player's car has no health left.</summary>
		public event Action PlayerWrecked;

		/// <summary>An enemy car was destroyed. The flag says whether the player did it.</summary>
		public event Action<bool> EnemyKilled;

		/// <summary>The player fired a gun.</summary>
		public event Action ShotFired;

		/// <summary>A scene has loaded and the other mods' scene callbacks have run.</summary>
		public event Action<Scene> SceneReady;

		private readonly ModContext ctx;

		internal ModEvents(ModContext ctx)
		{
			this.ctx = ctx;
		}

		internal void RemoveAll()
		{
			EventHub.Remove(this);
			RaceStarted = null;
			RaceEnded = null;
			LapCompleted = null;
			PlayerWrecked = null;
			EnemyKilled = null;
			ShotFired = null;
			SceneReady = null;
		}

		internal void Join()
		{
			EventHub.Add(this);
		}

		private void Run(Action call, string name)
		{
			try
			{
				call();
			}
			catch (Exception e)
			{
				ctx.Log.LogError("An " + name + " handler failed: " + e);
			}
		}

		internal void RaiseRaceStarted() { if (RaceStarted != null) Run(() => RaceStarted(), "RaceStarted"); }
		internal void RaiseRaceEnded() { if (RaceEnded != null) Run(() => RaceEnded(), "RaceEnded"); }
		internal void RaiseLap(int lap) { if (LapCompleted != null) Run(() => LapCompleted(lap), "LapCompleted"); }
		internal void RaiseWrecked() { if (PlayerWrecked != null) Run(() => PlayerWrecked(), "PlayerWrecked"); }
		internal void RaiseKilled(bool byPlayer) { if (EnemyKilled != null) Run(() => EnemyKilled(byPlayer), "EnemyKilled"); }
		internal void RaiseShot() { if (ShotFired != null) Run(() => ShotFired(), "ShotFired"); }
		internal void RaiseScene(Scene scene) { if (SceneReady != null) Run(() => SceneReady(scene), "SceneReady"); }
	}

	/// <summary>The one watcher behind every mod's <see cref="ModEvents"/>: it listens to the game's signals and watches the race.</summary>
	internal static class EventHub
	{
		private static readonly List<ModEvents> listeners = new List<ModEvents>();
		private static bool started;

		internal static void Add(ModEvents events)
		{
			if (!listeners.Contains(events))
			{
				listeners.Add(events);
			}
		}

		internal static void Remove(ModEvents events)
		{
			listeners.Remove(events);
		}

		/// <summary>Called once, with the loader's own object (it lives for the whole session, which is what <c>Signals</c> wants of an owner).</summary>
		internal static void Start(GameObject home)
		{
			if (started)
			{
				return;
			}
			started = true;
			Watcher watcher = home.AddComponent<Watcher>();
			Signals.On<EnemyKilled>(kill => { foreach (ModEvents e in listeners.ToArray()) e.RaiseKilled(kill.byPlayer); }, watcher);
			Signals.On<ShotFired>(shot => { foreach (ModEvents e in listeners.ToArray()) e.RaiseShot(); }, watcher);
		}

		internal static void SceneReady(Scene scene)
		{
			foreach (ModEvents e in listeners.ToArray())
			{
				e.RaiseScene(scene);
			}
		}

		private sealed class Watcher : MonoBehaviour
		{
			private TrackManager tm;
			private bool started, over, wrecked;
			private int lap;
			private int failures;

			private void Update()
			{
				if (listeners.Count == 0 || failures >= 3)
				{
					return;
				}
				try
				{
					Watch();
				}
				catch (Exception e)
				{
					// Said at most three times: a watcher that fails every frame would drown the log.
					failures++;
					ModRuntime.Log.LogError("ctx.Events: the race watch failed (" + failures + " of 3 before it stops): " + e.Message);
				}
			}

			private void Watch()
			{
				if (tm == null)
				{
					tm = FindFirstObjectByType<TrackManager>();
					started = over = wrecked = false;
					lap = 0;
					if (tm == null)
					{
						return;
					}
				}
				// The main menu has a TrackManager of its own, with no player car: not a race.
				if (tm.playerCarController == null)
				{
					return;
				}
				if (!started && tm.raceStarted)
				{
					started = true;
					lap = 1;
					foreach (ModEvents e in listeners.ToArray()) e.RaiseRaceStarted();
				}
				if (started)
				{
					int now = tm.lapManager != null ? tm.lapManager.GetPlayerCurrentLap() : lap;
					if (now > lap)
					{
						lap = now;
						foreach (ModEvents e in listeners.ToArray()) e.RaiseLap(now);
					}
					if (!wrecked && tm.playerCarlink != null && tm.playerCarlink.hp <= 0f)
					{
						wrecked = true;
						foreach (ModEvents e in listeners.ToArray()) e.RaiseWrecked();
					}
				}
				if (started && !over && GameManager.raceOver)
				{
					over = true;
					foreach (ModEvents e in listeners.ToArray()) e.RaiseRaceEnded();
				}
			}
		}
	}
}
