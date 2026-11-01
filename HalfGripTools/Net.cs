using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripTools
{
	/// <summary>Where the room is: there is no room, players are in the lobby, the race is loading or running, or the results are up.</summary>
	public enum NetPhase
	{
		None = 0,
		Lobby = 1,
		Loading = 2,
		Racing = 3,
		Results = 4
	}

	/// <summary>A player in the room.</summary>
	public struct NetPlayer
	{
		public ulong Id;
		public string Name;
		public bool IsHost;
		public bool IsLocal;
	}

	/// <summary>
	/// A game mode a mod adds to the room's MODE row. The host picks it like Race or Demolition; Multiplayer runs the race with the numbers below
	/// and the mod does the rest over <see cref="ModNet"/>.
	/// </summary>
	public sealed class NetMode
	{
		/// <summary>A short unique id, for example <c>"myMod.hotLap"</c>. Sent over the network; must be the same on every machine.</summary>
		public string Id;

		/// <summary>The name shown in the MODE row (capitals look best).</summary>
		public string Name;

		/// <summary>How many players the room takes in this mode, 2 to 64 (Multiplayer's limit). Default 20. More than about 20 is slow: every car is sent to every player.</summary>
		public int MaxPlayers = 20;

		/// <summary>The most laps the host can set, 1 to 100. Default 100.</summary>
		public int MaxLaps = 100;

		/// <summary>
		/// Optional. Where car number <c>i</c> (0 is the first place on the grid) starts, in world coordinates, or null to use the track's own grid
		/// (extra cars queue up behind it). Asked on every machine when the race loads, so it must give the same answer everywhere.
		/// Every mode is run as a race (laps, finish line, results); a mod that wants a different end calls <see cref="Net.EndRace"/> on the host.
		/// </summary>
		public Func<int, Pose?> SpawnPoint;
	}

	/// <summary>What Multiplayer (or another mod that carries messages between players) implements so <see cref="ModNet"/> can send.</summary>
	public interface INetTransport
	{
		/// <summary>Sends <paramref name="data"/> on <paramref name="channel"/> to one player, or to everybody else when <paramref name="to"/> is <see cref="Net.Everyone"/>.</summary>
		void Send(string channel, ulong to, byte[] data, bool reliable);

		/// <summary>Ends the race for everybody (the host's call; used by modes that have no finish line).</summary>
		void EndRace();
	}

	/// <summary>
	/// The room, as other mods see it. Nothing here needs the Multiplayer mod to be installed: without it <see cref="Available"/> is false,
	/// <see cref="InRoom"/> stays false and every send does nothing, so a mod can use it unconditionally.
	/// </summary>
	public static class Net
	{
		/// <summary>The value of <c>to</c> that means "every other player in the room".</summary>
		public const ulong Everyone = 0UL;

		/// <summary>The largest message that is sent reliably in one piece. Bigger ones are refused (send them in parts).</summary>
		public const int MaxReliable = 3800;

		/// <summary>The largest unreliable message (positions, effects). Bigger ones are refused.</summary>
		public const int MaxUnreliable = 1100;

		private static INetTransport transport;
		private static readonly List<NetPlayer> players = new List<NetPlayer>();
		private static readonly List<NetMode> modes = new List<NetMode>();
		private static readonly Dictionary<string, List<ModNet>> channels = new Dictionary<string, List<ModNet>>();

		/// <summary>True when a mod that carries messages (Multiplayer) is loaded.</summary>
		public static bool Available { get { return transport != null; } }

		public static bool InRoom { get; private set; }
		public static bool IsHost { get; private set; }
		public static ulong LocalId { get; private set; }
		public static ulong HostId { get; private set; }
		public static NetPhase Phase { get; private set; }

		/// <summary>The mode the room plays: <c>"race"</c>, <c>"demolition"</c>, <c>"tournament"</c>, <c>"custom"</c> or a registered mode's id.</summary>
		public static string ModeId { get; private set; }

		/// <summary>Everybody in the room, the host first. Empty outside a room.</summary>
		public static IList<NetPlayer> Players { get { return players.ToArray(); } }

		/// <summary>The modes mods have added (not the built-in ones).</summary>
		public static IList<NetMode> Modes { get { return modes.ToArray(); } }

		/// <summary>A player joined (also raised for each player already there when you join). Raised on the main thread.</summary>
		public static event Action<NetPlayer> PlayerJoined;

		public static event Action<NetPlayer> PlayerLeft;

		/// <summary>The room's phase changed (lobby, loading, racing, results) or the room was made or closed.</summary>
		public static event Action<NetPhase> PhaseChanged;

		/// <summary>The list of added modes changed. Multiplayer uses it to rebuild its MODE row.</summary>
		public static event Action ModesChanged;

		/// <summary>The host's call: ends the race for everybody and goes to the results. Does nothing for other players or outside a race.</summary>
		public static void EndRace()
		{
			if (transport != null && InRoom && IsHost && Phase == NetPhase.Racing)
			{
				transport.EndRace();
			}
		}

		// ------------------------------------------------------------ for the mod that carries the messages (Multiplayer)

		/// <summary>For the transport: installs it (null removes it).</summary>
		public static void SetTransport(INetTransport value)
		{
			transport = value;
			if (value == null)
			{
				SetRoom(false, false, 0UL, 0UL, null, NetPhase.None, null);
			}
		}

		/// <summary>For the transport: the room's state, whenever any of it changes.</summary>
		public static void SetRoom(bool inRoom, bool isHost, ulong localId, ulong hostId, IList<NetPlayer> now, NetPhase phase, string modeId)
		{
			List<NetPlayer> before = new List<NetPlayer>(players);
			List<NetPlayer> after = inRoom && now != null ? new List<NetPlayer>(now) : new List<NetPlayer>();
			NetPhase oldPhase = Phase;
			InRoom = inRoom;
			IsHost = inRoom && isHost;
			LocalId = inRoom ? localId : 0UL;
			HostId = inRoom ? hostId : 0UL;
			Phase = inRoom ? phase : NetPhase.None;
			ModeId = inRoom ? modeId : null;
			players.Clear();
			players.AddRange(after);
			foreach (NetPlayer p in before.Where(b => !after.Any(a => a.Id == b.Id)))
			{
				Raise(PlayerLeft, p);
			}
			foreach (NetPlayer p in after.Where(a => !before.Any(b => b.Id == a.Id)))
			{
				Raise(PlayerJoined, p);
			}
			if (oldPhase != Phase)
			{
				Raise(PhaseChanged, Phase);
			}
		}

		/// <summary>For the transport: a message arrived for <paramref name="channel"/> (the sending mod's id).</summary>
		public static void Deliver(string channel, ulong from, byte[] data)
		{
			List<ModNet> list;
			if (channel == null || !channels.TryGetValue(channel, out list))
			{
				return;
			}
			foreach (ModNet net in list.ToArray())
			{
				net.Raise(from, data);
			}
		}

		internal static bool Send(string channel, ulong to, byte[] data, bool reliable)
		{
			if (transport == null || !InRoom || data == null || data.Length > (reliable ? MaxReliable : MaxUnreliable))
			{
				return false;
			}
			try
			{
				transport.Send(channel, to, data, reliable);
				return true;
			}
			catch (Exception e)
			{
				Debug.LogWarning("HalfGripTools.Net: send failed: " + e.Message);
				return false;
			}
		}

		internal static void Join(string channel, ModNet net)
		{
			List<ModNet> list;
			if (!channels.TryGetValue(channel, out list))
			{
				channels[channel] = list = new List<ModNet>();
			}
			list.Add(net);
		}

		internal static void Leave(string channel, ModNet net)
		{
			List<ModNet> list;
			if (channels.TryGetValue(channel, out list))
			{
				list.Remove(net);
			}
		}

		internal static void AddMode(NetMode mode)
		{
			modes.RemoveAll(m => m.Id == mode.Id);
			modes.Add(mode);
			Raise(ModesChanged);
		}

		internal static void RemoveMode(NetMode mode)
		{
			if (modes.Remove(mode))
			{
				Raise(ModesChanged);
			}
		}

		private static void Raise(Action action)
		{
			if (action == null)
			{
				return;
			}
			foreach (Delegate d in action.GetInvocationList())
			{
				try { ((Action)d)(); }
				catch (Exception e) { Debug.LogWarning("HalfGripTools.Net: a handler failed: " + e); }
			}
		}

		private static void Raise<T>(Action<T> action, T value)
		{
			if (action == null)
			{
				return;
			}
			foreach (Delegate d in action.GetInvocationList())
			{
				try { ((Action<T>)d)(value); }
				catch (Exception e) { Debug.LogWarning("HalfGripTools.Net: a handler failed: " + e); }
			}
		}
	}

	/// <summary>
	/// One mod's line to the other players: <c>ctx.Net</c>. Messages go to the same mod on the other machines (the channel is the mod's id,
	/// and everybody in a room has the same mods). Handlers and modes are removed when mods are switched off.
	/// </summary>
	public sealed class ModNet
	{
		private readonly ModContext ctx;
		private readonly string channel;
		private readonly List<Action<ulong, byte[]>> handlers = new List<Action<ulong, byte[]>>();
		private readonly List<NetMode> myModes = new List<NetMode>();

		internal ModNet(ModContext ctx)
		{
			this.ctx = ctx;
			channel = ctx.Manifest.id;
			Net.Join(channel, this);
		}

		/// <summary>Sends to one player, or to everybody else with <see cref="Net.Everyone"/>. False when it was not sent (no room, too big).</summary>
		public bool Send(byte[] data, ulong to = Net.Everyone, bool reliable = true)
		{
			return ctx.Active && Net.Send(channel, to, data, reliable);
		}

		/// <summary>Sends to the host. When this machine is the host nothing is sent (use your own code path).</summary>
		public bool SendToHost(byte[] data, bool reliable = true)
		{
			return ctx.Active && !Net.IsHost && Net.Send(channel, Net.HostId, data, reliable);
		}

		/// <summary>Calls <paramref name="handler"/> on the main thread for every message another player's copy of this mod sends.</summary>
		public void OnReceive(Action<ulong, byte[]> handler)
		{
			handlers.Add(handler);
		}

		/// <summary>Adds a game mode to the MODE row of Multiplayer's race settings.</summary>
		public void RegisterMode(NetMode mode)
		{
			if (mode == null || string.IsNullOrEmpty(mode.Id) || string.IsNullOrEmpty(mode.Name))
			{
				ctx.Log.LogWarning("RegisterMode needs an Id and a Name.");
				return;
			}
			mode.MaxPlayers = Mathf.Clamp(mode.MaxPlayers, 2, 64);
			mode.MaxLaps = Mathf.Clamp(mode.MaxLaps, 1, 100);
			myModes.Add(mode);
			Net.AddMode(mode);
		}

		internal void Raise(ulong from, byte[] data)
		{
			if (!ctx.Active)
			{
				return;
			}
			foreach (Action<ulong, byte[]> handler in handlers.ToArray())
			{
				try
				{
					handler(from, data);
				}
				catch (Exception e)
				{
					ctx.Log.LogError("A network handler failed: " + e);
				}
			}
		}

		internal void Shutdown()
		{
			handlers.Clear();
			Net.Leave(channel, this);
			foreach (NetMode mode in myModes)
			{
				Net.RemoveMode(mode);
			}
			myModes.Clear();
		}
	}
}
