using FDG.Ai;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.EngineInterface;
using FDG.GameModel;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.Replay;
using FDG.SaveLoad;
using NUnit.Framework;
using static FDG.Tests.GameFingerprints;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    // #450: the recorder wired into a real in-process game (DeterminismTests' harness: AI on every slot, the
    // whole state machine, no front end). Pins the recording's structure - markers, snapshots, closed
    // requests, the header's seed and game ID - not its content: no golden files (plan rule 8), so OPR 3.6
    // rule changes can't turn these red.
    [TestFixture]
    public class ReplayRecorderGameTests
    {
        internal sealed class RecordedGame
        {
            public GameResult Result = null!;

            /// <summary>The live store's save at the moment the game completed - the recording is closed by
            /// then, so it is the state a full playback must reach (#454).</summary>
            public string FinalSave = "";
            public FDGServer Server = null!;
            public GameDataStore Store = null!;
            public ReplayRecorder Recorder = null!;
            public List<ReplayMarkerMessage> Broadcasts = new List<ReplayMarkerMessage>();
            public ReplayDocument Document = null!;
        }

        internal static async Task<RecordedGame> PlayFreshGame(int? seed, Action<ReplayRecorder>? configure = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var game = new RecordedGame { Store = store };
            bus.RegisterForMessageEvent<ReplayMarkerMessage>(m => { lock (game.Broadcasts) game.Broadcasts.Add(m); });

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Realistic;
            settings.DiceSeed = seed;
            settings.ObjectivePlacementMode = EObjectivePlacementMode.PlayerPlaced;

            game.Recorder = new ReplayRecorder();
            configure?.Invoke(game.Recorder);

            var completed = new TaskCompletionSource<GameResult>();
            game.Server = new FDGServer(store, bus, settings, slots, presentationClock: null, replay: game.Recorder);
            game.Server.OnGameCompleted += result =>
            {
                game.FinalSave = GameSaveSerializer.Save(store);
                completed.TrySetResult(result);
            };

            return await Finish(game, completed);
        }

        private static async Task<RecordedGame> Finish(RecordedGame game, TaskCompletionSource<GameResult> completed)
        {
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the recorded game must play to completion without hanging.");
            game.Result = await completed.Task;

            using MemoryStream stream = new MemoryStream(game.Recorder.ExportGzip());
            game.Document = ReplayReader.ReadGzip(stream);
            return game;
        }

        private static List<MarkerEvent> Markers(RecordedGame game, EReplayMarker kind) =>
            game.Document.Events.OfType<MarkerEvent>().Where(m => m.Marker == kind).ToList();

        [Test]
        [CancelAfter(180_000)]
        public async Task UnseededFreshGame_RecordsAStructurallyCompleteReplay()
        {
            RecordedGame game = await PlayFreshGame(seed: null);
            AssertReallyPlayed(game.Result);
            IReadOnlyList<ReplayEvent> events = game.Document.Events;
            GameProgressData progress = GameProgressUtilities.TryGetProgress(game.Store)!;

            // Header: D5's materialised seed and the game's identity, both the ones the save carries.
            ReplayHeader header = game.Document.Header;
            Assert.That(header.Seed, Is.Not.Null, "D5: an unseeded launch still records the seed it ran under");
            Assert.That(header.Seed, Is.EqualTo(progress.Settings.DiceSeed), "the replay's seed is the save's seed");
            Assert.That(header.Settings.DiceSeed, Is.EqualTo(header.Seed));
            Assert.That(header.GameId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(header.GameId, Is.EqualTo(progress.GameId).And.EqualTo(game.Server.GameId));
            Assert.That(header.RecordedBy, Is.EqualTo(EReplayRecordedBy.Host));
            Assert.That(header.RecordedByPlayerId, Is.Null, "no human at this machine");
            Assert.That(header.Slots.Select(s => (s.Name, s.Controller)),
                Is.EqualTo(new[] { ("AI 0", EReplayController.Ai), ("AI 1", EReplayController.Ai) }));
            Assert.That(header.Slots.Select(s => s.ArmyName), Has.None.Empty);
            Assert.That(header.FormatVersion, Is.EqualTo(ReplayFormat.CurrentVersion));

            // Seq dense, T monotonic: one consistent ordering across the engine and reply threads.
            Assert.That(events.Select(e => e.Seq), Is.EqualTo(Enumerable.Range(0, events.Count).Select(i => (long)i)));
            Assert.That(events.Select(e => e.T), Is.Ordered);

            // GameStart opens the recording, before map setup; GameEnded closes it, exactly once.
            var firstMarker = events.OfType<MarkerEvent>().First();
            Assert.That(firstMarker.Marker, Is.EqualTo(EReplayMarker.GameStart));
            Assert.That(firstMarker.Round, Is.Null, "a fresh game starts before round 1");
            Assert.That(events[0], Is.SameAs(firstMarker), "nothing is recorded before GameStart");
            List<MarkerEvent> ended = Markers(game, EReplayMarker.GameEnded);
            Assert.That(ended, Has.Count.EqualTo(1));
            Assert.That(events[^1], Is.EqualTo(ended[0]), "GameEnded is the last event");
            Assert.That(ended[0].Outcome, Is.EqualTo(game.Result.Outcome.ToString()));
            Assert.That(ended[0].Message, Is.EqualTo(game.Result.Message));

            // A RoundStart per round played, in order, each followed by a snapshot; snapshots = rounds + 1.
            List<MarkerEvent> rounds = Markers(game, EReplayMarker.RoundStart);
            Assert.That(rounds.Select(r => r.Round!.Value), Is.EqualTo(Enumerable.Range(1, game.Result.RoundsPlayed)));
            List<SnapshotEvent> snapshots = events.OfType<SnapshotEvent>().ToList();
            Assert.That(snapshots, Has.Count.EqualTo(rounds.Count + 1));
            Assert.That(snapshots.Select(s => s.Index), Is.EqualTo(Enumerable.Range(0, snapshots.Count)));
            foreach (SnapshotEvent snapshot in snapshots)
            {
                ReplayEvent before = events[(int)snapshot.Seq - 1];
                Assert.That(before, Is.InstanceOf<MarkerEvent>(), "every snapshot directly follows its marker");
                Assert.That(((MarkerEvent)before).Marker, Is.AnyOf(EReplayMarker.GameStart, EReplayMarker.RoundStart));
            }
            Assert.DoesNotThrow(() => GameSaveSerializer.Load(snapshots[^1].SaveJson));

            // Every request is closed by a reply or an error, and nothing answers an unknown request.
            var requested = events.OfType<RequestEvent>().ToDictionary(r => r.TaskId, r => r.Seq);
            var closed = events.OfType<ReplyEvent>().Select(r => (r.TaskId, r.Seq))
                .Concat(events.OfType<RequestErrorEvent>().Select(e => (e.TaskId, e.Seq))).ToList();
            Assert.That(requested, Is.Not.Empty);
            Assert.That(closed.Select(c => c.TaskId), Is.EquivalentTo(requested.Keys), "each request closed exactly once");
            Assert.That(closed.All(c => requested[c.TaskId] < c.Seq), Is.True, "a reply never precedes its request");
            Assert.That(game.Recorder.Summary.Steps, Is.EqualTo(events.OfType<ReplyEvent>().Count()));

            // Activations: numbered from 1 within each round; every unit-naming repeat follows its own boundary.
            List<MarkerEvent> activations = Markers(game, EReplayMarker.ActivationStart);
            Assert.That(activations, Is.Not.Empty);
            foreach (IGrouping<int?, MarkerEvent> round in activations.Where(a => a.UnitRef == null).GroupBy(a => a.Round))
            {
                Assert.That(round.Select(a => a.Activation!.Value), Is.EqualTo(Enumerable.Range(1, round.Count())),
                    $"round {round.Key}: boundaries number the activations 1..n");
            }
            MarkerEvent? open = null;
            foreach (MarkerEvent activation in activations)
            {
                if (activation.UnitRef == null)
                {
                    open = activation;
                    continue;
                }

                Assert.That(open, Is.Not.Null, "a unit is named only after its activation's boundary");
                Assert.That((activation.Round, activation.Activation, activation.PlayerId),
                    Is.EqualTo((open!.Round, open.Activation, open.PlayerId)), "the repeat names the open activation");
                Assert.That(game.Store.GetValue<UnitData>(activation.UnitRef!.Value).PlayerID, Is.EqualTo(activation.PlayerId),
                    "the named unit belongs to the acting player");
            }
            Assert.That(activations.Count(a => a.UnitRef != null), Is.GreaterThan(0));

            // The rest of the stream is there.
            Assert.That(events.OfType<BeatEvent>(), Is.Not.Empty, "beats are recorded even with no front end attached");
            Assert.That(events.OfType<LogEvent>(), Is.Not.Empty);
            Assert.That(events.OfType<StoreUpdateEvent>(), Is.Not.Empty);

            // Every marker and snapshot cue was broadcast to clients, in the same order.
            List<MarkerEvent> recordedMarkers = events.OfType<MarkerEvent>().ToList();
            List<MarkerEvent?> broadcastMarkers = game.Broadcasts.Where(b => b.Marker != null).Select(b => b.ToMarkerEvent()).ToList();
            Assert.That(broadcastMarkers, Is.EqualTo(recordedMarkers.Select(m => m with { Seq = 0, T = 0 })));
            Assert.That(game.Broadcasts.Where(b => b.SnapshotIndex != null).Select(b => b.SnapshotIndex!.Value),
                Is.EqualTo(snapshots.Select(s => s.Index)));

            // The recording is closed at game end, and the export is exactly what was recorded.
            Assert.That(game.Recorder.IsRecording, Is.False);
            Assert.That(events, Is.EqualTo(game.Recorder.EventsSnapshot()));
            Assert.That(game.Recorder.Summary.Outcome, Is.EqualTo(game.Result.Outcome.ToString()));
            Assert.That(game.Recorder.Summary.Rounds, Is.EqualTo(game.Result.RoundsPlayed));
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task SeededGame_RecordsTheGivenSeed()
        {
            RecordedGame game = await PlayFreshGame(seed: 4242);

            Assert.That(game.Document.Header.Seed, Is.EqualTo(4242));
            Assert.That(GameProgressUtilities.TryGetProgress(game.Store)!.Settings.DiceSeed, Is.EqualTo(4242));
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task ARecorderThatFails_DisablesItself_AndTheGameStillCompletes()
        {
            RecordedGame game = await PlayFreshGame(seed: 99, recorder =>
                recorder.BeatSerializerOverride = _ => throw new InvalidOperationException("simulated beat serializer failure"));

            AssertReallyPlayed(game.Result);
            Assert.That(game.Recorder.IsRecording, Is.False);
            Assert.That(game.Document.Events.OfType<BeatEvent>(), Is.Empty);
            Assert.That(Markers(game, EReplayMarker.GameStart), Has.Count.EqualTo(1), "what came before the failure is kept");
            Assert.That(Markers(game, EReplayMarker.GameEnded), Is.Empty, "nothing is recorded after the failure");
            Assert.That(game.Broadcasts.Count(b => b.Marker == EReplayMarker.GameEnded), Is.EqualTo(1),
                "clients still get the markers - their recordings don't depend on the host's");
        }

        // --- Resume / scenario launch ------------------------------------------------------------------

        private static async Task<RecordedGame> PlayResumedScenario(Guid presetGameId)
        {
            ScenarioFile scenario = new ScenarioFile
            {
                Name = "Replay resume scenario",
                Round = 2,
                ActivePlayer = 1,
                Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = null },
                Players = new()
                {
                    new ScenarioPlayer
                    {
                        Army = "shooters.fdgarmy",
                        Units = new() { new ScenarioUnit { Unit = "Warriors", Models = new() { new[] { 30f, 20f }, new[] { 31f, 20f }, new[] { 32f, 20f } } } },
                    },
                    new ScenarioPlayer
                    {
                        Army = "defenders.fdgarmy",
                        Units = new() { new ScenarioUnit { Unit = "Guards", Models = new() { new[] { 40f, 26f }, new[] { 41f, 26f }, new[] { 42f, 26f } } } },
                    },
                },
            };
            GameDataStore store = ScenarioCompiler.Compile(scenario, new[] { MakeShooterArmy(), MakeDefenderArmy() });

            GameProgressData compiled = GameProgressUtilities.TryGetProgress(store)!;
            Assert.That(compiled.Settings.DiceSeed, Is.Null, "precondition: the scenario names no seed");
            Assert.That(compiled.GameId, Is.EqualTo(Guid.Empty), "precondition: a compiled scenario has no game ID yet");
            if (presetGameId != Guid.Empty)
            {
                compiled.GameId = presetGameId;
                GameProgressUtilities.WriteProgress(store, compiled);
            }

            List<PlayerSlotInfo> savedInfos = store.GetAllValues<PlayerSlotInfo>().OrderBy(info => info.SlotID).ToList();
            foreach (DataReference oldInfo in store.GetAllDataReferences<PlayerSlotInfo>().ToList())
                store.Destroy(oldInfo);

            var bus = new InProcessBus();
            var slots = new PlayerSlot[savedInfos.Count];
            for (int i = 0; i < savedInfos.Count; i++)
            {
                slots[i] = new PlayerSlot(i, savedInfos[i].TeamNumber, savedInfos[i].PlayerID, new ArmyListFile(), store);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", savedInfos[i].PlayerID, new FDGGame_AsLocal(store, bus), null, slots[i].SlotID));
            }

            var game = new RecordedGame { Store = store, Recorder = new ReplayRecorder() };
            var completed = new TaskCompletionSource<GameResult>();
            game.Server = new FDGServer(store, bus, slots, replay: game.Recorder);
            game.Server.OnGameCompleted += result => completed.TrySetResult(result);

            // The resume constructor writes the materialised seed and the game ID back before it returns.
            GameProgressData resumed = GameProgressUtilities.TryGetProgress(store)!;
            Assert.That(resumed.Settings.DiceSeed, Is.Not.Null, "D5 on the resume path");
            Assert.That(resumed.GameId, Is.EqualTo(game.Server.GameId));
            Assert.That(game.Recorder.Header!.Seed, Is.EqualTo(resumed.Settings.DiceSeed));
            Assert.That(game.Recorder.Header.GameId, Is.EqualTo(game.Server.GameId));

            return await Finish(game, completed);
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task ResumedScenario_MintsAGameId_AndStartsTheRecordingInTheResumedRound()
        {
            RecordedGame game = await PlayResumedScenario(presetGameId: Guid.Empty);
            IReadOnlyList<ReplayEvent> events = game.Document.Events;

            Assert.That(game.Server.GameId, Is.Not.EqualTo(Guid.Empty), "a store with no game ID gets one on resume");
            Assert.That(GameProgressUtilities.TryGetProgress(game.Store)!.GameId, Is.EqualTo(game.Server.GameId));

            MarkerEvent start = (MarkerEvent)events[0];
            Assert.That(start.Marker, Is.EqualTo(EReplayMarker.GameStart));
            Assert.That(start.Round, Is.EqualTo(2), "a resumed recording starts in the round it re-enters");
            List<MarkerEvent> rounds = Markers(game, EReplayMarker.RoundStart);
            Assert.That(rounds.Select(r => r.Round!.Value), Is.EqualTo(Enumerable.Range(3, game.Result.RoundsPlayed - 2)),
                "the resumed round has no RoundStart of its own; every later round does");
            Assert.That(events.OfType<SnapshotEvent>().Count(), Is.EqualTo(rounds.Count + 1));
            MarkerEvent firstActivation = Markers(game, EReplayMarker.ActivationStart).First();
            Assert.That((firstActivation.Round, firstActivation.Activation), Is.EqualTo(((int?)2, (int?)1)),
                "activations after a resume are numbered from 1 within the resumed round");
            Assert.That(Markers(game, EReplayMarker.GameEnded), Has.Count.EqualTo(1));
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task ResumedGame_KeepsItsGameId()
        {
            Guid gameId = Guid.NewGuid();

            RecordedGame game = await PlayResumedScenario(presetGameId: gameId);

            Assert.That(game.Server.GameId, Is.EqualTo(gameId));
            Assert.That(game.Document.Header.GameId, Is.EqualTo(gameId));
            Assert.That(GameProgressUtilities.TryGetProgress(game.Store)!.GameId, Is.EqualTo(gameId),
                "every rolling snapshot after the resume keeps it");
        }

        [Test]
        public void WithConcreteSeed_KeepsAGivenSeed_AndPicksOneOtherwise()
        {
            GameSettings seeded = GameSettings.GetDefault();
            seeded.DiceSeed = 17;
            GameSettings random = GameSettings.GetDefault();

            Assert.That(seeded.WithConcreteSeed().DiceSeed, Is.EqualTo(17));
            Assert.That(random.WithConcreteSeed().DiceSeed, Is.Not.Null);
            Assert.That(random.DiceSeed, Is.Null, "a copy: the lobby's own setting stays random for the next game");
        }

        // --- The host lobby's real launch (the GUI's new-game path) --------------------------------------

        [Test]
        [CancelAfter(30_000)]
        public async Task HostLobbyLaunch_RecordsTheGame_UnderAConcreteSeed()
        {
            var hostVm = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
            hostVm.AddAiPlayer(EAiProfile.SoloRules);
            LobbyPlayerInfoSummary[] players = hostVm.PlayerInfos.ToArray();
            hostVm.UpdateArmyListFile(players[0].PlayerID, MakeShooterArmy());
            hostVm.UpdateArmyListFile(players[1].PlayerID, MakeDefenderArmy());
            var launched = new TaskCompletionSource<IFDGGame>();
            hostVm.OnLaunched += game => launched.TrySetResult(game);

            Assert.That(hostVm.Replay, Is.Null, "no replay before launch");
            Assert.That(hostVm.TryLaunchGame(out string? failReason), Is.True, failReason);
            IFDGGame localGame = await launched.Task;

            // The host's own slot is a local human whose front end never attaches here, so the state machine
            // stays parked at its ready gate: the recording holds GameStart + snapshot 0 and nothing else.
            Assert.That(hostVm.Replay, Is.Not.Null);
            Assert.That(localGame.Replay, Is.SameAs(hostVm.Replay), "the front end's game exposes the same recording");
            Assert.That(hostVm.Replay!.IsRecording, Is.True);
            ReplayDocument document;
            using (MemoryStream stream = new MemoryStream(hostVm.Replay.ExportGzip()))
                document = ReplayReader.ReadGzip(stream);

            Assert.That(document.Header.Seed, Is.Not.Null, "D5 on the lobby path: the lobby's seed setting was random");
            Assert.That(document.Header.Slots.Select(s => s.Controller), Is.EqualTo(new[] { EReplayController.Human, EReplayController.Ai }));
            Assert.That(document.Header.RecordedByPlayerId, Is.EqualTo(players[0].PlayerID), "the host's own player");
            Assert.That(((MarkerEvent)document.Events[0]).Marker, Is.EqualTo(EReplayMarker.GameStart));
            Assert.That(document.Events[1], Is.InstanceOf<SnapshotEvent>());

            hostVm.Dispose();
            Assert.That(hostVm.Replay.IsRecording, Is.False, "tearing the lobby down stops the recording");
            Assert.DoesNotThrow(() => hostVm.Replay.ExportGzip(), "and what was recorded stays exportable");
        }

        // Host-only no-op network double (the same shape as LobbyBotNamingTests'): no client ever joins.
        private sealed class NullNetworkHost : INetworkHost
        {
            public event Action<ConnectionID>? OnNewClientConnected { add { } remove { } }
            public event Action<ConnectionID>? OnClientDisconnected { add { } remove { } }
            public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived { add { } remove { } }

            public Task StartAsync() => Task.CompletedTask;
            public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled) => Task.CompletedTask;
            public Task SendCommandToSingleClientAsync(ConnectionID clientId, ArraySegment<byte> data, bool isPooled)
                => Task.CompletedTask;
            public void DisconnectClient(ConnectionID clientId) { }
            public void MarkClientAuthenticated(ConnectionID clientId) { }
            public void Stop() { }
        }

        // --- Wire --------------------------------------------------------------------------------------

        [Test]
        public void ReplayMarkerMessage_RoundTripsAcrossTheWire()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var sender = new MessageSerializer(store);
            var receiver = new MessageSerializer(store);
            receiver.RegisterMessageType<ReplayMarkerMessage>();
            var marker = new MarkerEvent(EReplayMarker.ActivationStart)
            {
                Round = 3,
                Activation = 2,
                PlayerId = new PlayerID(Guid.NewGuid()),
                UnitRef = new DataReference { TypeID = new TypeID(4), Index = 1, Generation = 2 },
            };

            var decodedMarker = (ReplayMarkerMessage?)receiver.DeserializeMessage(sender.SerializeMessage(ReplayMarkerMessage.For(marker)));
            var decodedCue = (ReplayMarkerMessage?)receiver.DeserializeMessage(sender.SerializeMessage(ReplayMarkerMessage.ForSnapshot(5)));

            Assert.That(decodedMarker!.ToMarkerEvent(), Is.EqualTo(marker));
            Assert.That(decodedMarker.SnapshotIndex, Is.Null);
            Assert.That(decodedCue!.SnapshotIndex, Is.EqualTo(5));
            Assert.That(decodedCue.ToMarkerEvent(), Is.Null, "a snapshot cue carries no marker");
        }
    }
}
