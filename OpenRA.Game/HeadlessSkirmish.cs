#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA
{
	/// <summary>
	/// Creates and ticks a skirmish world without a renderer, window, or audio device. The lobby is
	/// populated with bot clients plus one admin observer, which the engine requires as the local
	/// client for <see cref="Game.IsHost"/> and host-side order processing. Used by the --simulate
	/// utility command for self-play, batch evaluation, and scenario testing.
	/// </summary>
	public static class HeadlessSkirmish
	{
		/// <summary>
		/// Hook for the host application to report whether a bot player's logic is enabled.
		/// When null, all bots are assumed enabled. The --simulate command sets this to a
		/// <c>ModularBot</c> enabled check.
		/// </summary>
		public static Func<Player, bool> IsBotEnabled;

		/// <summary>One lobby client entry as reported by a finished simulation.</summary>
		public sealed class ClientSummary
		{
			public int Index;
			public string Name;
			public string Faction;
			public int Team;
			public bool IsBot;
			public bool BotEnabled;
			public string Slot;
		}

		/// <summary>Outcome summary of a headless simulation.</summary>
		public sealed class Result
		{
			public int Ticks;
			public bool GameOver;
			public int ActorCount;
			public List<ClientSummary> Clients = [];
			public List<string> Winners = [];

			/// <summary>AI event counts parsed from the telemetry log (waves, feints, scouts, LLM calls...).</summary>
			public Dictionary<string, int> Events = [];
		}

		/// <summary>
		/// Runs a full skirmish simulation with every bot using the same bot type. The caller must
		/// have initialized settings (<see cref="Game.InitializeSettings"/>) and loaded the mod data
		/// beforehand.
		/// </summary>
		public static Result Run(ModData modData, Map map, string botType, int bots, int teams, int maxTicks, int seed)
		{
			var botTypes = new string[bots];
			Array.Fill(botTypes, botType);
			return Run(modData, map, botTypes, teams, maxTicks, seed);
		}

		/// <summary>
		/// Runs a full skirmish simulation where each bot may use a different bot type (mixed
		/// self-play, e.g. coalition "ai" versus a scripted "rush"/"turtle" opponent). The caller
		/// must have initialized settings (<see cref="Game.InitializeSettings"/>) and loaded the mod
		/// data beforehand.
		/// </summary>
		public static Result Run(ModData modData, Map map, IReadOnlyList<string> botTypes, int teams, int maxTicks, int seed)
		{
			var bots = botTypes.Count;
			if (bots < 2)
				throw new ArgumentException("At least two bots are required for a match.");
			if (teams < 1 || teams > 2)
				throw new ArgumentException("At most two teams are supported.");
			if ((bots + teams - 1) / teams > 8)
				throw new ArgumentException("At most 8 bots per team are supported.");

			ArgumentNullException.ThrowIfNull(modData);
			ArgumentNullException.ThrowIfNull(map);
			if (maxTicks < 1)
				throw new ArgumentException("Ticks must be positive.");

			var playable = new MapPlayers(map.PlayerDefinitions).Players.Values
				.Where(p => p.Playable && !p.Spectating).ToArray();
			if (playable.Length < bots)
				throw new ArgumentException($"Map '{map.Title}' only has {playable.Length} playable slots, but {bots} bots were requested.");

			// Global game state that world code expects to be set.
			Game.ModData = modData;
			Game.Sound = new Sound(new HeadlessPlatform(), Game.Settings.Sound);
			modData.InitializeLoaders(modData.DefaultFileSystem);

			var orderManager = new OrderManager(new EchoConnection());
			Game.OrderManager = orderManager;

			var lobby = orderManager.LobbyInfo;
			lobby.GlobalSettings.Map = map.Uid;
			lobby.GlobalSettings.RandomSeed = seed;
			lobby.GlobalSettings.ServerName = "Headless Skirmish";
			lobby.GlobalSettings.EnableSingleplayer = true;
			lobby.GlobalSettings.EnableSyncReports = true;

			// One lobby slot per playable map player, matching the keys of the map's player definitions.
			foreach (var pr in playable)
				lobby.Slots[pr.Name] = new Session.Slot
				{
					PlayerReference = pr.Name,
					AllowBots = true,
					LockFaction = pr.LockFaction,
					LockColor = pr.LockColor,
					LockTeam = pr.LockTeam,
					LockHandicap = pr.LockHandicap,
					LockSpawn = pr.LockSpawn,
					Required = false,
					Closed = false
				};

			// Admin observer: EchoConnection identifies the local client as index 1, which must be a
			// non-bot client for Game.IsHost and for the order packet queue StartGame sets up.
			// Only real factions are assigned to bots; the "Random" pseudo-faction would resolve differently per client.
			var factions = modData.DefaultRules.Actors[SystemActors.World].TraitInfos<FactionInfo>()
				.Where(f => f.RandomFactionMembers.Count == 0).ToArray();
			if (factions.Length == 0)
				throw new InvalidOperationException("No factions are defined for the world actor.");

			lobby.Clients.Add(new Session.Client
			{
				Index = 1,
				Name = "Headless",
				Faction = factions[0].InternalName,
				PreferredColor = Color.White,
				Color = Color.White,
				State = Session.ClientState.Ready,
				IsAdmin = true
			});

			// Bot clients: one per requested bot, alternating team assignment so adjacent slots
			// belong to different teams. BotControllerClientIndex names the admin observer (client 1)
			// as the host that added the bots: the engine's ValidateOrder drops bot orders unless the
			// issuing client matches the bot's controller.
			var slotKeys = lobby.Slots.Keys.ToArray();
			for (var i = 0; i < bots; i++)
			{
				var pr = playable[i];
				lobby.Clients.Add(new Session.Client
				{
					Index = i + 2,
					Name = $"Bot {i + 1}",
					Slot = slotKeys[i],
					Bot = botTypes[i],
					BotControllerClientIndex = 1,
					Faction = factions[i % factions.Length].InternalName,
					Team = 1 + i % teams,
					PreferredColor = pr.Color,
					Color = pr.Color,
					State = Session.ClientState.Ready
				});
			}

			// Create and load the world exactly like Game.StartGame, minus the renderer.
			modData.PrepareMap(map);
			orderManager.World = new World(map, modData, orderManager, WorldType.Regular);
			var world = orderManager.World;

			world.LoadComplete(null);
			orderManager.StartGame();
			world.PostLoadComplete(null);

			var result = new Result();
			while (result.Ticks < maxTicks && !world.IsGameOver)
			{
				// The game loop schedules end-of-game and other delayed actions outside the sync scope;
				// without this the game-over callback scheduled by MissionObjectives never fires.
				Game.PerformDelayedActions();

				Sync.RunUnsynced(world, orderManager.TickImmediate);
				if (orderManager.TryTick())
				{
					world.Tick();
					result.Ticks = world.WorldTick;
				}
			}

			// Capture the outcome before disposal.
			result.GameOver = world.IsGameOver;
			foreach (var a in world.Actors)
				result.ActorCount++;
			foreach (var c in lobby.Clients)
			{
				var player = world.Players.FirstOrDefault(p => p.ClientIndex == c.Index);
				result.Clients.Add(new ClientSummary
				{
					Index = c.Index,
					Name = player != null ? player.ResolvedPlayerName : c.Name,
					Faction = c.Faction,
					Team = c.Team,
					IsBot = c.IsBot,
					Slot = c.Slot,
					BotEnabled = c.IsBot && player != null && (IsBotEnabled?.Invoke(player) ?? true)
				});
			}

			foreach (var p in world.Players)
				if (p.WinState == WinState.Won)
					result.Winners.Add(p.ResolvedPlayerName);

			result.Events = SummarizeTelemetry();

			world.Dispose();
			orderManager.Dispose();
			return result;
		}

		/// <summary>Counts the AI's strategic events from the telemetry log for tuning and self-play.</summary>
		static Dictionary<string, int> SummarizeTelemetry()
		{
			var counts = new Dictionary<string, int>();
			try
			{
				var path = Path.Combine(Platform.SupportDir, "ai-telemetry.log");
				if (!File.Exists(path))
					return counts;

				foreach (var line in File.ReadLines(path))
				{
					var key = TelemetryEventKey(line);
					if (key == null)
						continue;

					counts.TryGetValue(key, out var n);
					counts[key] = n + 1;
				}
			}
			catch (IOException)
			{
				// Telemetry is best-effort.
			}

			return counts;
		}

		static string TelemetryEventKey(string line)
		{
			if (line.Contains("Wave of "))
				return "waves";
			if (line.Contains("Feint of "))
				return "feints";
			if (line.Contains("Recon probe"))
				return "recon";
			if (line.Contains("Bait placed"))
				return "bait";
			if (line.Contains("Counterattack"))
				return "counterattacks";
			if (line.Contains("Scout sent"))
				return "scouts";
			if (line.Contains("LLM plan received"))
				return "llm_plans";
			if (line.Contains("LLM intent applied"))
				return "llm_intents";
			if (line.Contains("Reserve committed"))
				return "reserve_commits";
			if (line.Contains("Prerequisite building ordered"))
				return "prereq_orders";
			if (line.Contains("Mission "))
				return "mission_events";
			return null;
		}

		/// <summary>Platform stub: headless runs only ever touch the sound device, through a no-op engine.</summary>
		sealed class HeadlessPlatform : IPlatform
		{
			public IPlatformWindow CreateWindow(Size size, WindowMode windowMode, float scaleModifier,
				int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
			{
				throw new NotSupportedException("Headless simulation does not create a window.");
			}

			public ISoundEngine CreateSound(string device)
			{
				return new NoOpSoundEngine();
			}

			public IFont CreateFont(byte[] data)
			{
				throw new NotSupportedException("Headless simulation does not create fonts.");
			}
		}

		/// <summary>Silent sound engine: keeps every Game.Sound call alive without an audio device.</summary>
		sealed class NoOpSoundEngine : ISoundEngine
		{
			public bool Dummy => true;
			public float Volume { get; set; }

			public SoundDevice[] AvailableDevices()
			{
				return [];
			}

			public ISoundSource AddSoundSourceFromMemory(byte[] data, int channels, int sampleBits, int sampleRate)
			{
				return null;
			}

			public ISound Play2D(ISoundSource sound, bool loop, bool relative, WPos pos, float volume, bool attenuateVolume)
			{
				return NoOpSound.Instance;
			}

			public ISound Play2DStream(Stream stream, int channels, int sampleBits, int sampleRate, bool loop, bool relative, WPos pos, float volume)
			{
				return NoOpSound.Instance;
			}

			public void PauseSound(ISound sound, bool paused) { }

			public void StopSound(ISound sound) { }

			public void SetAllSoundsPaused(bool paused) { }

			public void StopAllSounds() { }

			public void SetListenerPosition(WPos position) { }

			public void SetSoundVolume(float volume, ISound music, ISound video) { }

			public void SetSoundLooping(bool looping, ISound sound) { }

			public void SetSoundPosition(ISound sound, WPos position) { }

			public void Dispose() { }
		}

		/// <summary>Completes instantly so callers that poll sound state never block or crash.</summary>
		sealed class NoOpSound : ISound
		{
			public static readonly NoOpSound Instance = new();

			public float Volume { get; set; }

			public float SeekPosition => 0;

			public bool Complete => true;

			public void SetPosition(WPos pos) { }
		}
	}
}
