using System.Net;
using System.Net.Sockets;
using FDG.Ai;
using FDG.Data;
using FDG.EngineInterface;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Network.Messages.DataMessages;
using FDG.Network.Messages.StageRequestMessages;
using FDG.Players;
using FDG.Presentation;
using FDG.Replay;
using FDG.SaveLoad;
using FDG.StageResolution;
using Newtonsoft.Json;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    // #451: a remote client's own replay recording, checked against the host's recording of the same game. A real
    // host lobby and client lobby - over the in-process loopback transport (LoopbackNetwork), and once over real
    // sockets - each side's decisions answered by the solo-rules AI, the whole state machine in between. Pins
    // structure and state equalities, never content (plan rule 8: no golden files).
    [TestFixture]
    public class ClientReplayRecordingTests
    {
        private static readonly TimeSpan GameTimeout = TimeSpan.FromSeconds(120);

        private sealed class NetworkedGame
        {
            public LobbyViewModel_Host HostVm = null!;
            public LobbyViewModel_Client ClientVm = null!;
            public PlayerID HostPlayer;
            public PlayerID ClientPlayer;

            /// <summary>The host's live store when the game completed - what a full playback must reach.</summary>
            public string HostFinalSave = "";
            public ReplayDocument Host = null!;
            public ReplayDocument Client = null!;
        }

        private static async Task<LobbyViewModel_Client> Join(INetworkClient transport, string name)
        {
            var clientVm = new LobbyViewModel_Client(name, transport, "");
            Task<string?> joined = clientVm.JoinResultTask;
            Assert.That(await Task.WhenAny(joined, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(joined), "join handshake");
            Assert.That(await joined, Is.Null, "the client must be accepted");
            return clientVm;
        }

        /// <summary>Wires both front ends to the AI, launches through <paramref name="launch"/>, and waits for the
        /// game to end on both sides.</summary>
        private static async Task<NetworkedGame> Play(NetworkedGame game, Func<LobbyViewModel_Host, bool> launch)
        {
            var hostEnded = new TaskCompletionSource();
            var clientEnded = new TaskCompletionSource();
            game.HostVm.OnLaunched += local => local.AssignInterfaces(null, null,
                AiResolverRegistryFactory.BuildSoloRules(local.TableState, game.HostPlayer, slotID: 0), null, null);
            game.ClientVm.OnLaunched += remote => remote.AssignInterfaces(null, null,
                AiResolverRegistryFactory.BuildSoloRules(remote.TableState, game.ClientPlayer, slotID: 1), null, null);
            game.HostVm.OnGameCompleted += _ =>
            {
                game.HostFinalSave = ReplayResumeTests.StoreOnly(game.HostVm.SaveGameToJson()!); // #453: a save embeds the replay
                hostEnded.TrySetResult();
            };
            game.ClientVm.OnGameEnded += _ => clientEnded.TrySetResult();

            Assert.That(launch(game.HostVm), Is.True, "launch");
            Task both = Task.WhenAll(hostEnded.Task, clientEnded.Task);
            Assert.That(await Task.WhenAny(both, Task.Delay(GameTimeout)), Is.SameAs(both), "the networked game must end on both sides");

            game.Host = Read(game.HostVm.Replay!);
            game.Client = Read(game.ClientVm.Replay!);
            return game;
        }

        /// <param name="connect">Connects the client's transport, once the host lobby is listening.</param>
        private static async Task<NetworkedGame> PlayFreshGame(NetworkedGame game, INetworkHost net, Func<Task<INetworkClient>> connect)
        {
            game.HostVm = new LobbyViewModel_Host("Host", "The Table", "", net);
            game.HostVm.PresentationClockFactory = () => new InstantPresentationClock();
            game.ClientVm = await Join(await connect(), "Client");
            game.HostPlayer = game.HostVm.PlayerInfos.Single(p => p.PlayerType == EPlayerType.Local).PlayerID;
            game.ClientPlayer = (await HostRosterRow(game.HostVm, p => p.PlayerType == EPlayerType.Network)).PlayerID;
            game.HostVm.UpdateArmyListFile(game.HostPlayer, MakeShooterArmy());
            game.ClientVm.UpdateArmyListFile(game.ClientPlayer, MakeDefenderArmy());
            await HostRosterRow(game.HostVm, p => p.PlayerID == game.ClientPlayer && p.ArmyListSummary.IsAssigned);

            return await Play(game, host => host.TryLaunchGame(out string? reason) || Fail(reason));
        }

        // Over real sockets the host's roster catches up on its read thread; the loopback transport has it at once.
        private static async Task<LobbyPlayerInfoSummary> HostRosterRow(LobbyViewModel_Host hostVm, Func<LobbyPlayerInfoSummary, bool> match)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                LobbyPlayerInfoSummary? row = hostVm.PlayerInfos.FirstOrDefault(match);
                if (row != null) return row;
                await Task.Delay(50);
            }

            Assert.Fail("the host's roster never caught up with the client");
            throw new InvalidOperationException("unreachable");
        }

        private static bool Fail(string? reason)
        {
            Assert.Fail(reason);
            return false;
        }

        private static ReplayDocument Read(IReplaySource replay)
        {
            using var stream = new MemoryStream(replay.ExportGzip());
            return ReplayReader.ReadGzip(stream);
        }

        private static List<MarkerEvent> Markers(ReplayDocument document) =>
            document.Events.OfType<MarkerEvent>().Select(m => m with { Seq = 0, T = 0 }).ToList();

        private static string FinalSave(ReplayDocument document)
        {
            var player = new ReplayPlayer(new ReplayTimeline(document));
            player.SeekToEnd();
            return GameSaveSerializer.Save(player.Store);
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task NetworkedGame_TheClientsRecording_MatchesTheHosts()
        {
            var net = new LoopbackNetworkHost();
            NetworkedGame game = await PlayFreshGame(new NetworkedGame(), net, () => Task.FromResult<INetworkClient>(net.Connect("Client")));

            AssertTheClientsRecordingMatchesTheHosts(game);

            Assert.That(game.ClientVm.Replay!.IsRecording, Is.False, "GameEnded closes the client's recording");
            game.ClientVm.Dispose();
            game.HostVm.Dispose();
            Assert.DoesNotThrow(() => game.ClientVm.Replay.ExportGzip(), "what was recorded stays exportable");
        }

        // The loopback transport delivers every frame inline on the sender's thread. Real sockets add what the
        // arrival-order finding rests on: one read thread on the client, #086's per-connection write lock on the host,
        // and the host's engine and read threads both sending.
        [Test]
        [CancelAfter(180_000)]
        public async Task NetworkedGame_OverRealSockets_TheClientsRecording_MatchesTheHosts()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var host = new FDGHost(port: port);
            var client = new FDGClient();
            var game = new NetworkedGame();
            try
            {
                Task listening = host.StartAsync(); // the accept loop - runs until the host stops
                Assert.That(listening.IsFaulted, Is.False, $"host failed to bind: {listening.Exception?.GetBaseException().Message}");
                await PlayFreshGame(game, host, async () =>
                {
                    Assert.That(await client.ConnectAsync(IPAddress.Loopback, port), Is.True, "connect");
                    return client;
                });

                AssertTheClientsRecordingMatchesTheHosts(game);
            }
            finally
            {
                // Sockets outlive a failed assertion; a listener left running would make later port picks flaky.
                foreach (Action teardown in new Action[] { () => game.ClientVm?.Dispose(), () => game.HostVm?.Dispose(), client.Disconnect, host.Stop })
                {
                    try { teardown(); } catch { /* teardown must not mask the real failure */ }
                }
            }
        }

        private static void AssertTheClientsRecordingMatchesTheHosts(NetworkedGame game)
        {
            ReplayDocument host = game.Host, client = game.Client;

            // Header: the host's identity, this client's point of view.
            Assert.That(client.Header.RecordedBy, Is.EqualTo(EReplayRecordedBy.Client));
            Assert.That(client.Header.RecordedByPlayerId, Is.EqualTo(game.ClientPlayer));
            Assert.That(client.Header.GameId, Is.EqualTo(host.Header.GameId).And.Not.EqualTo(Guid.Empty));
            Assert.That(client.Header.Seed, Is.EqualTo(host.Header.Seed).And.Not.Null);
            Assert.That(client.Header.Slots.Select(s => (s.PlayerId, s.Controller)), Is.EqualTo(new[]
            {
                (game.HostPlayer, EReplayController.Remote), (game.ClientPlayer, EReplayController.Human),
            }));

            // The same markers, less the host's GameStart (sent during the host's launch, before this client had a
            // game to record it in - its join snapshot stands in for it).
            Assert.That(Markers(host)[0].Marker, Is.EqualTo(EReplayMarker.GameStart));
            Assert.That(Markers(client), Is.EqualTo(Markers(host).Skip(1)));
            Assert.That(Markers(client).Last().Marker, Is.EqualTo(EReplayMarker.GameEnded));

            // Every decision of every player, each closed.
            var hostTasks = host.Events.OfType<RequestEvent>().Select(r => r.TaskId).ToList();
            var clientTasks = client.Events.OfType<RequestEvent>().Select(r => r.TaskId).ToList();
            Assert.That(clientTasks, Is.EquivalentTo(hostTasks));
            Assert.That(clientTasks, Is.Unique);
            Assert.That(client.Events.OfType<ReplyEvent>().Select(r => r.TaskId), Is.EquivalentTo(host.Events.OfType<ReplyEvent>().Select(r => r.TaskId)));
            Assert.That(clientTasks.Distinct().Count(), Is.GreaterThan(10), "precondition: a game with decisions on both sides");
            Assert.That(new ReplayTimeline(client).Steps.Count, Is.EqualTo(new ReplayTimeline(host).Steps.Count));

            // What the players saw and read: every beat, every log line (Debug ones included), in the same order.
            Assert.That(client.Events.OfType<BeatEvent>().Select(b => b.BeatJson), Is.EqualTo(host.Events.OfType<BeatEvent>().Select(b => b.BeatJson)));
            Assert.That(client.Events.OfType<LogEvent>().Select(l => (l.Line, l.Debug)), Is.EqualTo(host.Events.OfType<LogEvent>().Select(l => (l.Line, l.Debug))));
            Assert.That(client.Events.OfType<BeatEvent>().Count(), Is.GreaterThan(0));

            // Snapshots: the join, then one per round, numbered and taken like the host's - the same states.
            List<SnapshotEvent> hostSnapshots = host.Events.OfType<SnapshotEvent>().ToList();
            List<SnapshotEvent> clientSnapshots = client.Events.OfType<SnapshotEvent>().ToList();
            int rounds = Markers(host).Count(m => m.Marker == EReplayMarker.RoundStart);
            Assert.That(rounds, Is.GreaterThan(0));
            Assert.That(clientSnapshots, Has.Count.EqualTo(rounds + 1));
            Assert.That(clientSnapshots.Select(s => s.Index), Is.EqualTo(hostSnapshots.Select(s => s.Index)));
            for (int i = 0; i < clientSnapshots.Count; i++)
            {
                Assert.That(clientSnapshots[i].SaveJson, Is.EqualTo(hostSnapshots[i].SaveJson), $"snapshot {i}");
            }

            // Played back, the client's recording ends where the host's game did.
            Assert.That(FinalSave(client), Is.EqualTo(game.HostFinalSave));
            ReplayVerification verification = ReplayVerifier.Verify(new ReplayTimeline(client));
            Assert.That(verification.Passed, Is.True, string.Join("\n", verification.Failures.Select(f => f.Message)));
        }

        // A client that joins a game already in progress - a resumed save it rejoins (#187) - records from its join
        // snapshot: the resumed state, round 2 of a compiled scenario, with the game ID the host minted for it.
        [Test]
        [CancelAfter(180_000)]
        public async Task ResumedGame_ARejoiningClient_RecordsFromItsJoinSnapshot()
        {
            GameDataStore compiled = ScenarioCompiler.Compile(MakeRejoinScenario(), MakeRejoinArmies());
            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(compiled));
            PlayerID[] savedPlayers = loaded.GetAllValues<PlayerSlotInfo>().OrderBy(s => s.SlotID).Select(s => s.PlayerID).ToArray();

            var net = new LoopbackNetworkHost();
            var game = new NetworkedGame { HostVm = new LobbyViewModel_Host("Host", "Recovered Game", "", net, loaded) };
            game.HostVm.PresentationClockFactory = () => new InstantPresentationClock();
            game.ClientVm = await Join(net.Connect("Client"), "Client");
            game.HostPlayer = game.HostVm.PlayerInfos.Single(p => p.PlayerType == EPlayerType.Local).PlayerID;
            game.ClientPlayer = game.HostVm.PlayerInfos.Single(p => p.PlayerType == EPlayerType.Network).PlayerID;
            Assert.That(new[] { game.HostPlayer, game.ClientPlayer }, Is.EqualTo(savedPlayers), "precondition: both sides took their saved slots");

            await Play(game, host => host.TryResumeGame(out string? reason) || Fail(reason));
            ReplayDocument host = game.Host, client = game.Client;

            Assert.That(client.Events[0], Is.InstanceOf<SnapshotEvent>(), "the recording starts with the join snapshot");
            Assert.That(((SnapshotEvent)client.Events[0]).Index, Is.EqualTo(0));
            Assert.That(((SnapshotEvent)client.Events[0]).SaveJson, Is.EqualTo(host.Events.OfType<SnapshotEvent>().First().SaveJson),
                "joined mid-game, the client's first snapshot is the resumed state the host's starts from");
            Assert.That(client.Header.GameId, Is.EqualTo(host.Header.GameId).And.Not.EqualTo(Guid.Empty));
            Assert.That(Markers(host)[0], Is.EqualTo(new MarkerEvent(EReplayMarker.GameStart) { Round = 2 }));
            Assert.That(Markers(client), Is.EqualTo(Markers(host).Skip(1)));
            Assert.That(FinalSave(client), Is.EqualTo(game.HostFinalSave));
            Assert.That(ReplayVerifier.Verify(new ReplayTimeline(client)).Passed, Is.True);

            game.ClientVm.Dispose();
            game.HostVm.Dispose();
        }

        // #453: a resumed save that carries a replay hands it to the client with the launch message. The client's file
        // begins with the host's, event for event, through the host's Resumed marker; its join snapshot is the one after
        // the resume (same index, same state); the rest is its own recording, as in any game.
        [Test]
        [CancelAfter(240_000)]
        public async Task ResumedSave_TheClientsRecording_ContinuesTheSavedHistory()
        {
            ReplayResumeTests.SavedGame saved = await ReplayResumeTests.PlayAndSaveInRoundTwo();
            (GameDataStore loaded, byte[]? prior) = GameSaveSerializer.LoadWithReplay(saved.Save);
            int historyLength = ReplayResumeTests.Read(prior!).Events.Count;

            var net = new LoopbackNetworkHost();
            var game = new NetworkedGame { HostVm = new LobbyViewModel_Host("Host", "Saved Game", "", net, loaded, prior) };
            game.HostVm.PresentationClockFactory = () => new InstantPresentationClock();
            game.ClientVm = await Join(net.Connect("Client"), "Client");
            game.HostPlayer = game.HostVm.PlayerInfos.Single(p => p.PlayerType == EPlayerType.Local).PlayerID;
            game.ClientPlayer = game.HostVm.PlayerInfos.Single(p => p.PlayerType == EPlayerType.Network).PlayerID;

            await Play(game, host => host.TryResumeGame(out string? reason) || Fail(reason));
            ReplayDocument host = game.Host, client = game.Client;

            var resumed = (MarkerEvent)host.Events[historyLength];
            Assert.That(resumed.Marker, Is.EqualTo(EReplayMarker.Resumed), "precondition: the host continued the save's replay");
            Assert.That(Lines(client).Take(historyLength + 1), Is.EqualTo(Lines(host).Take(historyLength + 1)),
                "the client's recording begins with the host's, verbatim, through the resume");

            var hostSnapshot = (SnapshotEvent)host.Events[historyLength + 1];
            SnapshotEvent joinSnapshot = client.Events.Skip(historyLength + 1).OfType<SnapshotEvent>().First();
            Assert.That(client.Events[historyLength + 1], Is.SameAs(joinSnapshot), "nothing recorded between the resume and the join");
            Assert.That(joinSnapshot.Index, Is.EqualTo(hostSnapshot.Index));
            Assert.That(joinSnapshot.SaveJson, Is.EqualTo(hostSnapshot.SaveJson), "the join snapshot is the resumed state");

            Assert.That(client.Header.RecordedBy, Is.EqualTo(EReplayRecordedBy.Client));
            Assert.That(client.Header.GameId, Is.EqualTo(host.Header.GameId));
            Assert.That(client.Header.StartedUtc, Is.EqualTo(host.Header.StartedUtc), "the client's file begins when the game did");
            Assert.That(client.Header.ResumedUtc, Is.EqualTo(host.Header.ResumedUtc).And.Count.EqualTo(1));
            Assert.That(Markers(client).Skip(Markers(host).IndexOf(Markers(host).First(m => m.Marker == EReplayMarker.Resumed))),
                Is.EqualTo(Markers(host).SkipWhile(m => m.Marker != EReplayMarker.Resumed)));

            Assert.That(FinalSave(client), Is.EqualTo(game.HostFinalSave));
            foreach (ReplayDocument recording in new[] { host, client })
            {
                ReplayVerification verification = ReplayVerifier.Verify(new ReplayTimeline(recording));
                Assert.That(verification.Passed, Is.True, string.Join("\n", verification.Failures.Select(f => f.Message)));
            }

            game.ClientVm.Dispose();
            game.HostVm.Dispose();
        }

        private static List<string> Lines(ReplayDocument document) =>
            document.Events.Select(e => JsonConvert.SerializeObject(e, ReplayFormat.JsonSettings)).ToList();

        // The bookkeeping a real game rarely or never exercises, as a scripted message sequence: a request arrives
        // once however it is learnt, a duplicate notice is ignored, an error closes a request, a snapshot cue before
        // the join has nothing to save, and the host's GameEnded closes the recording.
        [Test]
        public void ScriptedMessages_EachDecisionOnce_ErrorsClose_GameEndedStops()
        {
            var bus = new InProcessBus();
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(Guid.NewGuid());
            var them = new PlayerID(Guid.NewGuid());
            var recorder = new ClientReplayRecorder(bus, store, me, hostHeader: null);
            TaskID mine = new TaskID(Guid.NewGuid()), theirs = new TaskID(Guid.NewGuid()), failed = new TaskID(Guid.NewGuid());
            StageTaskNotifyResolvedMessage Notice(TaskID task, PlayerID player, string? reply, string? error) =>
                new StageTaskNotifyResolvedMessage(task, player, "Req", "Rep", "Notice name", "{\"r\":1}", reply, error);

            bus.SendCommandToAllAsync(ReplayMarkerMessage.ForSnapshot(0)); // before the join: nothing to save yet
            recorder.OnFullStateApplied();
            bus.SendCommandToAllAsync(new StageTaskNotifyAwaitingMessage(mine, new PlayerSlotInfo(me, 1, 1, "Me", true), "Deploy"));
            bus.SendCommandToAllAsync(new StageTaskRequestMessage(me, mine, "Req", "Rep", "{\"r\":1}"));
            bus.SendCommandToAllAsync(Notice(mine, me, "\"a\"", null));
            bus.SendCommandToAllAsync(Notice(mine, me, "\"a\"", null)); // a duplicate notice
            bus.SendCommandToAllAsync(Notice(theirs, them, "\"b\"", null));
            bus.SendCommandToAllAsync(Notice(failed, them, null, "Player disconnected."));
            bus.SendCommandToAllAsync(ReplayMarkerMessage.For(new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Win" }));
            bus.SendCommandToAllAsync(Notice(new TaskID(Guid.NewGuid()), them, "\"late\"", null)); // after the end

            Assert.That(recorder.IsRecording, Is.False, "GameEnded closes the recording");
            List<ReplayEvent> events = Read(recorder).Events.ToList();
            Assert.That(events.Select(e => e.Kind), Is.EqualTo(new[]
            {
                "Snapshot", "Request", "Reply", "Request", "Reply", "Request", "RequestError", "Marker",
            }));
            Assert.That(events.OfType<RequestEvent>().Select(r => (r.TaskId, r.DisplayName)), Is.EqualTo(new[]
            {
                (mine, "Deploy"), (theirs, "Notice name"), (failed, "Notice name"),
            }), "an own request takes the awaiting notice's name; the others the resolved notice's");
            Assert.That(events.OfType<RequestErrorEvent>().Single().Message, Is.EqualTo("Player disconnected."));
            Assert.That(recorder.Header!.GameId, Is.EqualTo(Guid.Empty), "no host header: no game ID");
            Assert.That(recorder.Header.Settings, Is.Null);
        }

        // #453, scripted: a seeded recording leaves out a delta that arrives before its join snapshot (playback could
        // not place it after the history's Resumed), and history it cannot read is reported and dropped.
        [Test]
        public void ScriptedHistory_SeedsTheRecording_DropsDeltasBeforeTheJoin_AndReportsBadBytes()
        {
            var host = new ReplayHeader { GameId = Guid.NewGuid(), StartedUtc = new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc) };
            host.ResumedUtc.Add(new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc));
            byte[] history;
            using (var stream = new MemoryStream())
            {
                ReplayWriter.WriteGzip(host, new ReplayEvent[]
                {
                    new MarkerEvent(EReplayMarker.GameStart) { Seq = 0 },
                    new SnapshotEvent(0, "{}") { Seq = 1 },
                    new MarkerEvent(EReplayMarker.Saved) { Seq = 2, T = 50 },
                    new MarkerEvent(EReplayMarker.Resumed) { Seq = 3, T = 50, Round = 2 },
                }, stream);
                history = stream.ToArray();
            }

            var bus = new InProcessBus();
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(Guid.NewGuid());
            var recorder = new ClientReplayRecorder(bus, store, me, host, history);
            var early = new DataReference { TypeID = new TypeID(1), Index = 0, Generation = 1 };
            bus.SendCommandToAllAsync(new AddSingleDataMessage(early, "{}")); // ahead of the join: left out
            recorder.OnFullStateApplied();
            bus.SendCommandToAllAsync(new AddSingleDataMessage(early, "{}"));

            List<ReplayEvent> events = Read(recorder).Events.ToList();
            Assert.That(events.Select(e => e.Kind), Is.EqualTo(new[] { "Marker", "Snapshot", "Marker", "Marker", "Snapshot", "StoreAdd" }));
            Assert.That(events.Select(e => e.Seq), Is.EqualTo(new long[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(((SnapshotEvent)events[4]).Index, Is.EqualTo(1), "the join snapshot counts on from the history's");
            Assert.That(recorder.Header!.StartedUtc, Is.EqualTo(host.StartedUtc));
            Assert.That(recorder.Header.ResumedUtc, Is.EqualTo(host.ResumedUtc));
            Assert.That(recorder.Header.RecordedBy, Is.EqualTo(EReplayRecordedBy.Client));

            var lines = new List<string>();
            var unread = new ClientReplayRecorder(new InProcessBus(), store, me, host, new byte[] { 1, 2, 3 }, new LinesOutput(lines));
            Assert.That(lines.Single(), Does.StartWith("[replay] The game's replay so far could not be read"));
            Assert.That(unread.Summary.SizeBytes, Is.Zero, "nothing seeded");
            Assert.That(unread.Header!.ResumedUtc, Is.Empty, "without the history the recording is not a continuation");
        }

        private sealed class LinesOutput : ITextOutput
        {
            private readonly List<string> _lines;

            public LinesOutput(List<string> lines) => _lines = lines;

            public void Log(string message, TextColor? color = null) => _lines.Add(message);

            public void LogDebug(string message, TextColor? color = null) => _lines.Add(message);
        }

        private static ArmyListFile[] MakeRejoinArmies() =>
            Enumerable.Range(0, 2).Select(i => new ArmyListFile
            {
                Name = $"Army {i}",
                Units = new()
                {
                    new UnitFileEntry
                    {
                        Name = "Troops", ModelCount = 3, Quality = 4, Defense = 4,
                        Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                    },
                },
            }).ToArray();

        private static ScenarioFile MakeRejoinScenario() => new ScenarioFile
        {
            Name = "Rejoin and record",
            Round = 2,
            ActivePlayer = 0,
            Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 4510 },
            Players = Enumerable.Range(0, 2).Select(i => new ScenarioPlayer
            {
                Army = $"army{i}.fdgarmy",
                Units = new()
                {
                    new ScenarioUnit
                    {
                        Unit = "Troops",
                        Models = new() { new[] { 20f + i * 10, 20f }, new[] { 21f + i * 10, 20f }, new[] { 22f + i * 10, 20f } },
                    },
                },
            }).ToList(),
        };

        // --- Wire --------------------------------------------------------------------------------------

        [Test]
        public void ResolvedNotice_CarriesTheWholeDecision_AcrossTheWire()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var sender = new MessageSerializer(store);
            var receiver = new MessageSerializer(store);
            receiver.RegisterMessageType<StageTaskNotifyResolvedMessage>();

            var answered = new StageTaskNotifyResolvedMessage(new TaskID(Guid.NewGuid()), new PlayerID(Guid.NewGuid()),
                "FDG.StageResolution.Requests.YesNoRequest", "System.Boolean", "Use Martial Prowess?",
                "{\"Question\":\"Reactivate?\",\"$type\":\"x\"}", ReplyJson: "true", ErrorMessage: null);
            var failed = answered with { TaskID = new TaskID(Guid.NewGuid()), ReplyJson = null, ErrorMessage = "Player disconnected." };

            Assert.That(receiver.DeserializeMessage(sender.SerializeMessage(answered)), Is.EqualTo(answered));
            Assert.That(receiver.DeserializeMessage(sender.SerializeMessage(failed)), Is.EqualTo(failed));
        }
    }
}
