using FDG.Data;
using FDG.GameModel;
using FDG.Network;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Replay;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    // #450: the host-side recorder, driven by hand - every Record* path, the export round trip, the summary,
    // the thread-safety contract and the disable-on-failure rule. The recorder wired into a real game is
    // ReplayRecorderGameTests.
    [TestFixture]
    public class ReplayRecorderTests
    {
        private sealed class CapturingOutput : ITextOutput
        {
            public readonly List<(string Line, bool Debug)> Lines = new List<(string, bool)>();

            public void Log(string message, TextColor? color = null) => Lines.Add((message, false));

            public void LogDebug(string message, TextColor? color = null) => Lines.Add((message, true));
        }

        private static ReplayHeader Header() => new ReplayHeader
        {
            BuildVersion = ReplayFormat.BuildVersion,
            EngineVersion = ReplayFormat.EngineVersion,
            GameId = Guid.NewGuid(),
            Seed = 7,
            RandomnessType = ERandomnessType.Realistic,
            Settings = GameSettings.GetDefault(),
            RecordedBy = EReplayRecordedBy.Host,
            StartedUtc = DateTime.UtcNow,
        };

        private static ReplayDocument ExportAndRead(ReplayRecorder recorder)
        {
            using MemoryStream stream = new MemoryStream(recorder.ExportGzip());
            return ReplayReader.ReadGzip(stream);
        }

        /// <summary>The value the store's own save writes for <paramref name="reference"/>.</summary>
        private static string SavedJsonFor(GameDataStore store, DataReference reference) =>
            SavedJsonFor(GameSaveSerializer.Save(store), reference);

        private static string SavedJsonFor(string saveJson, DataReference reference)
        {
            GameSaveFile save = JsonConvert.DeserializeObject<GameSaveFile>(saveJson)!;
            return save.Entries.Single(entry => entry.DataReference.Equals(reference)).JsonValue;
        }

        [Test]
        public void ScriptedGame_RecordsEveryKindInOrder_AndTheStoreDeltasMatchTheSave()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            CapturingOutput players = new CapturingOutput();
            using ReplayRecorder recorder = new ReplayRecorder();
            recorder.Start(Header(), players);
            recorder.AttachStore(store);
            // As FDGServer does, so an in-place token change rewrites its unit through the store.
            _ = new TokenChangeBroadcaster(store);

            // Add a unit (an army of them, via the real bootstrap).
            PlayerID player = new PlayerID(Guid.NewGuid());
            PlayerSlot slot = new PlayerSlot(0, 0, player, MakeShooterArmy(), store);
            GameBootstrap.CreateArmy(player, slot.ArmyListFile, store, GameBootstrap.BuildRuleResolver(new[] { slot }));
            int addsAfterSetup = recorder.EventsSnapshot().Count;
            Assert.That(addsAfterSetup, Is.GreaterThan(0), "creating the army must record its store adds");
            string setupSave = GameSaveSerializer.Save(store);

            DataBinding<UnitData> unit = store.GetAllDataBindings<UnitData>().First();
            ModelData model = (ModelData)unit.GetValue().Models.First();

            // Move a model, then update a token.
            model.SetPosition(new Position(12.5f, 7.25f));
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Fatigued));

            // A request / reply pair, by hand, with the wire body the sender would build.
            YesNoRequest question = new YesNoRequest(player, "Reactivate?", defaultAnswer: true, displayName: "Deciding");
            string requestJson = JsonConvert.SerializeObject(question, WireJsonSettings.For(store));
            TaskID task = new TaskID(Guid.NewGuid());
            recorder.OnRequested(task, player, typeof(YesNoRequest).FullName!, typeof(bool).FullName!,
                question.DisplayName, requestJson);
            recorder.OnReplied(task, "true");

            // Two beats and a log line (through the server's decorator), then a snapshot.
            recorder.OnBeat(new BannerBeat("Round 1", new TextColor(1, 2, 3, 255)));
            recorder.OnBeat(new UnitMovedBeat(new UnitID(Guid.NewGuid()), "Warriors",
                new List<ModelMove> { new ModelMove(new ModelID(Guid.NewGuid()), new List<Position> { new Position(1f, 2f), new Position(3f, 4f) }) },
                TimeSpan.FromMilliseconds(500)));
            new ReplayTextOutput(players, recorder).Log("Activating: Warriors.");
            string liveSave = GameSaveSerializer.Save(store);
            int snapshotIndex = recorder.Snapshot(store);

            ReplayDocument read = ExportAndRead(recorder);
            List<ReplayEvent> events = read.Events.ToList();

            // Order and counts.
            Assert.That(events.Take(addsAfterSetup), Has.All.InstanceOf<StoreAddEvent>(), "setup is all adds");
            Assert.That(events.Skip(addsAfterSetup).Select(e => e.Kind), Is.EqualTo(new[]
            {
                "StoreUpdate", "StoreUpdate", "Request", "Reply", "Beat", "Beat", "Log", "Snapshot",
            }), "model move, token update, request, reply, two beats, a log line, a snapshot - in that order");
            Assert.That(events.Select(e => e.Seq), Is.EqualTo(Enumerable.Range(0, events.Count).Select(i => (long)i)), "Seq is dense");
            Assert.That(events.Select(e => e.T), Is.Ordered, "T never goes backwards");

            // The recorded store JSON is exactly what the store's own save holds for that reference.
            var moved = (StoreUpdateEvent)events[addsAfterSetup];
            Assert.That(moved.Ref, Is.EqualTo(model.PositionBinding.Reference));
            Assert.That(moved.Json, Is.EqualTo(SavedJsonFor(store, moved.Ref)));
            var tokened = (StoreUpdateEvent)events[addsAfterSetup + 1];
            Assert.That(tokened.Ref, Is.EqualTo(unit.Reference));
            Assert.That(tokened.Json, Is.EqualTo(SavedJsonFor(store, unit.Reference)));
            Assert.That(tokened.Json, Does.Contain(nameof(TokenType.Fatigued)));
            // Every setup add, against a save taken right after setup. (Not against the final save: ModelData
            // also serializes a read-only Position derived from its PositionBinding, which drifts when the
            // Position entry moves without the ModelData entry itself being rewritten. Load ignores it.)
            Assert.That(events.OfType<StoreAddEvent>().Count(), Is.EqualTo(addsAfterSetup));
            foreach (StoreAddEvent add in events.OfType<StoreAddEvent>())
            {
                Assert.That(add.Json, Is.EqualTo(SavedJsonFor(setupSave, add.Ref)), $"add for {add.Ref.TypeID.ID}/{add.Ref.Index}");
            }

            // Request / reply carried verbatim.
            var request = (RequestEvent)events[addsAfterSetup + 2];
            Assert.That(request.TaskId, Is.EqualTo(task));
            Assert.That(request.PlayerId, Is.EqualTo(player));
            Assert.That(request.RequestType, Is.EqualTo(typeof(YesNoRequest).FullName));
            Assert.That(request.ReplyType, Is.EqualTo(typeof(bool).FullName));
            Assert.That(request.DisplayName, Is.EqualTo("Deciding"));
            Assert.That(request.RequestJson, Is.EqualTo(requestJson));
            Assert.That(((ReplyEvent)events[addsAfterSetup + 3]).ReplyJson, Is.EqualTo("true"));

            // Beats read back as their concrete types through the wire settings.
            var beats = events.OfType<BeatEvent>()
                .Select(b => JsonConvert.DeserializeObject<PresentationBeat>(b.BeatJson, WireJsonSettings.For(store)))
                .ToList();
            Assert.That(beats[0], Is.TypeOf<BannerBeat>());
            Assert.That(((BannerBeat)beats[0]!).BannerText, Is.EqualTo("Round 1"));
            Assert.That(beats[1], Is.TypeOf<UnitMovedBeat>());

            // The log line reached the players AND the recording.
            Assert.That(players.Lines, Does.Contain(("Activating: Warriors.", false)));
            Assert.That(((LogEvent)events[addsAfterSetup + 6]).Line, Is.EqualTo("Activating: Warriors."));

            // The snapshot is a full save of the store at that point.
            var snapshot = (SnapshotEvent)events.Last();
            Assert.That(snapshotIndex, Is.EqualTo(0));
            Assert.That(snapshot.Index, Is.EqualTo(0));
            Assert.That(snapshot.SaveJson, Is.EqualTo(liveSave));
            Assert.DoesNotThrow(() => GameSaveSerializer.Load(snapshot.SaveJson), "a snapshot loads like any save");

            ReplaySummary summary = recorder.Summary;
            Assert.That(summary.Steps, Is.EqualTo(1));
            Assert.That(summary.Rounds, Is.EqualTo(0));
            Assert.That(summary.Outcome, Is.Null);
            Assert.That(summary.SizeBytes, Is.GreaterThan(liveSave.Length));
            Assert.That(players.Lines.Where(l => l.Line.StartsWith("[replay]")), Is.Empty, "nothing failed");
        }

        [Test]
        public void Markers_AreStampedWithSeqAndT_AndFeedTheSummary()
        {
            using ReplayRecorder recorder = new ReplayRecorder();
            recorder.Start(Header());
            PlayerID player = new PlayerID(Guid.NewGuid());

            recorder.RecordMarker(new MarkerEvent(EReplayMarker.GameStart) { Seq = 99, T = 99 });
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.RoundStart) { Round = 1 });
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.ActivationStart) { Round = 1, Activation = 1, PlayerId = player });
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.RoundStart) { Round = 2 });
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Tie", Message = "It's a tie!" });

            ReplayDocument read = ExportAndRead(recorder);

            Assert.That(read.Events.Select(e => e.Seq), Is.EqualTo(new long[] { 0, 1, 2, 3, 4 }),
                "the recorder assigns Seq; a caller's value is ignored");
            Assert.That(((MarkerEvent)read.Events[2]).PlayerId, Is.EqualTo(player));
            Assert.That(recorder.Summary.Rounds, Is.EqualTo(2));
            Assert.That(recorder.Summary.Outcome, Is.EqualTo("Tie"));
        }

        [Test]
        public void BeforeStart_NothingIsRecorded_AndExportRefuses()
        {
            using ReplayRecorder recorder = new ReplayRecorder();

            recorder.RecordLog("too early", debug: false);
            recorder.OnReplied(new TaskID(Guid.NewGuid()), "true");

            Assert.That(recorder.IsRecording, Is.False);
            Assert.That(recorder.EventsSnapshot(), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => recorder.ExportGzip());
        }

        [Test]
        public void StartingTwice_IsAWiringError()
        {
            using ReplayRecorder recorder = new ReplayRecorder();
            recorder.Start(Header());

            Assert.Throws<InvalidOperationException>(() => recorder.Start(Header()));
        }

        [Test]
        public void FailingSnapshot_DisablesRecording_LogsOnce_AndKeepsWhatWasRecorded()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            CapturingOutput players = new CapturingOutput();
            using ReplayRecorder recorder = new ReplayRecorder
            {
                SnapshotSerializer = _ => throw new OutOfMemoryException("simulated"),
            };
            recorder.Start(Header(), players);
            recorder.RecordLog("before", debug: false);

            int first = recorder.Snapshot(store);
            int second = recorder.Snapshot(store);
            recorder.RecordLog("after", debug: false);

            Assert.That(recorder.IsRecording, Is.False);
            Assert.That((first, second), Is.EqualTo((0, 1)), "the snapshot index counts calls, recorded or not");
            List<string> failures = players.Lines.Select(l => l.Line).Where(l => l.StartsWith("[replay]")).ToList();
            Assert.That(failures, Has.Count.EqualTo(1), "a failure is logged exactly once");
            Assert.That(failures[0], Does.Contain("snapshot").And.Contain("OutOfMemoryException"));
            Assert.That(failures[0].All(c => c >= ' ' && c <= '~'), Is.True, "game text is ASCII");

            ReplayDocument read = ExportAndRead(recorder);
            Assert.That(read.Events.Select(e => ((LogEvent)e).Line), Is.EqualTo(new[] { "before" }),
                "what was recorded before the failure stays exportable; nothing after it is added");
        }

        [Test]
        public void FailingBeatSerializer_DisablesRecording_AndNeverThrows()
        {
            CapturingOutput players = new CapturingOutput();
            using ReplayRecorder recorder = new ReplayRecorder
            {
                BeatSerializerOverride = _ => throw new JsonSerializationException("simulated"),
            };
            recorder.Start(Header(), players);

            Assert.DoesNotThrow(() => recorder.OnBeat(new BannerBeat("x", new TextColor(0, 0, 0, 255))));
            Assert.DoesNotThrow(() => recorder.OnBeat(new BannerBeat("y", new TextColor(0, 0, 0, 255))));

            Assert.That(recorder.IsRecording, Is.False);
            Assert.That(players.Lines.Count(l => l.Line.StartsWith("[replay]")), Is.EqualTo(1));
        }

        [Test]
        public void AFailureOutputThatThrows_StillNeverReachesTheCaller()
        {
            using ReplayRecorder recorder = new ReplayRecorder
            {
                BeatSerializerOverride = _ => throw new InvalidOperationException("simulated"),
            };
            recorder.Start(Header(), new ThrowingOutput());

            Assert.DoesNotThrow(() => recorder.OnBeat(new BannerBeat("x", new TextColor(0, 0, 0, 255))));
            Assert.That(recorder.IsRecording, Is.False);
        }

        private sealed class ThrowingOutput : ITextOutput
        {
            public void Log(string message, TextColor? color = null) => throw new InvalidOperationException("log sink down");
        }

        [Test]
        public void Dispose_DetachesFromTheStore_ButTheRecordingStaysExportable()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ReplayRecorder recorder = new ReplayRecorder();
            recorder.Start(Header());
            recorder.AttachStore(store);
            PlayerID player = new PlayerID(Guid.NewGuid());
            _ = new PlayerSlot(0, 0, player, MakeShooterArmy(), store); // one store add
            int before = recorder.EventsSnapshot().Count;

            recorder.Dispose();
            _ = new PlayerSlot(1, 1, new PlayerID(Guid.NewGuid()), MakeDefenderArmy(), store);

            Assert.That(before, Is.GreaterThan(0));
            Assert.That(recorder.IsRecording, Is.False);
            Assert.That(ExportAndRead(recorder).Events, Has.Count.EqualTo(before));
        }

        [Test]
        public void ReplayTextOutput_ForwardsBothCategories_AndRecordsTheDebugFlag()
        {
            CapturingOutput players = new CapturingOutput();
            using ReplayRecorder recorder = new ReplayRecorder();
            recorder.Start(Header(), players);
            ReplayTextOutput output = new ReplayTextOutput(players, recorder);

            output.Log("shown");
            output.LogDebug("hidden unless Debug is on");

            Assert.That(players.Lines, Is.EqualTo(new[] { ("shown", false), ("hidden unless Debug is on", true) }));
            List<LogEvent> logged = ExportAndRead(recorder).Events.Cast<LogEvent>().ToList();
            Assert.That(logged.Select(l => (l.Line, l.Debug)), Is.EqualTo(new[] { ("shown", false), ("hidden unless Debug is on", true) }));
        }

        [Test]
        public void ConcurrentRecording_KeepsSeqDenseAndUnique()
        {
            // Replies arrive on the network read thread while the engine thread records everything else.
            using ReplayRecorder recorder = new ReplayRecorder();
            recorder.Start(Header());

            Parallel.For(0, 4000, i =>
            {
                if (i % 2 == 0) recorder.RecordLog($"line {i}", debug: false);
                else recorder.OnReplied(new TaskID(Guid.NewGuid()), "true");
            });

            IReadOnlyList<ReplayEvent> events = recorder.EventsSnapshot();
            Assert.That(events, Has.Count.EqualTo(4000));
            Assert.That(events.Select(e => e.Seq), Is.EqualTo(Enumerable.Range(0, 4000).Select(i => (long)i)));
            Assert.That(recorder.Summary.Steps, Is.EqualTo(2000));
        }
    }
}
