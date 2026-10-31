using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Network;
using FDG.Network.Messages;
using FDG.Players;
using FDG.Replay;
using FDG.SaveLoad;
using FDG.StageResolution;
using Newtonsoft.Json;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    // #453: a save carries the replay so far, and resuming it continues the same recording. Real in-process games (AI
    // on every slot, the whole state machine): one saved at round 2's first activation boundary - from inside the
    // marker broadcast, so on the engine thread exactly there - then loaded and played to the end. Pins structure
    // and state equalities, never content (plan rule 8).
    [TestFixture]
    public class ReplayResumeTests
    {
        private const int Seed = 4530;

        /// <summary>The store part of a save - what a save held before #453 - for tests that compare game state.</summary>
        internal static string StoreOnly(string saveJson)
        {
            GameSaveFile file = JsonConvert.DeserializeObject<GameSaveFile>(saveJson)!;
            file.Replay = null;
            file.ReplaySummary = null;
            return JsonConvert.SerializeObject(file, Formatting.Indented);
        }

        internal static ReplayDocument Read(byte[] gzip)
        {
            using var stream = new MemoryStream(gzip);
            return ReplayReader.ReadGzip(stream);
        }

        internal sealed class SavedGame
        {
            /// <summary>The save taken at round 2's first activation boundary, replay embedded.</summary>
            public string Save = "";
            public ReplayDocument Live = null!;
            public List<ReplayMarkerMessage> Broadcasts = new List<ReplayMarkerMessage>();
        }

        /// <summary>Plays a fresh recorded game to its end, saving it through <see cref="FDGServer.SaveGame"/> as round
        /// 2's first activation begins.</summary>
        internal static async Task<SavedGame> PlayAndSaveInRoundTwo()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var game = new SavedGame();
            FDGServer? server = null;
            bus.RegisterForMessageEvent<ReplayMarkerMessage>(message =>
            {
                lock (game.Broadcasts) game.Broadcasts.Add(message);
                if (message.Marker == EReplayMarker.ActivationStart && message.Round == 2 && message.UnitRef == null
                    && game.Save.Length == 0)
                {
                    game.Save = server!.SaveGame();
                }
            });

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, new FDGGame_AsLocal(store, bus), Seed, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Realistic;
            settings.DiceSeed = Seed;
            settings.ObjectivePlacementMode = EObjectivePlacementMode.PlayerPlaced;

            var recorder = new ReplayRecorder();
            var completed = new TaskCompletionSource<GameResult>();
            server = new FDGServer(store, bus, settings, slots, presentationClock: null, replay: recorder);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the recorded game must play to completion without hanging.");
            Assert.That(game.Save, Is.Not.Empty, "precondition: the game reached round 2's first activation");
            game.Live = Read(recorder.ExportGzip());
            return game;
        }

        internal sealed class ResumedGame
        {
            public GameResult Result = null!;
            public ReplayRecorder Recorder = null!;
            public ReplayDocument Document = null!;

            /// <summary>The resumed game's store when it completed - what a full playback must reach.</summary>
            public string FinalSave = "";

            /// <summary>A save taken after the end, the way #187's recovery save is.</summary>
            public string SaveAfterTheEnd = "";
        }

        /// <summary>Loads <paramref name="saveJson"/> as the app's load paths do and plays it to the end, AI on every
        /// slot, continuing the save's replay.</summary>
        internal static async Task<ResumedGame> ResumeAndFinish(string saveJson)
        {
            (GameDataStore store, byte[]? prior) = GameSaveSerializer.LoadWithReplay(saveJson);
            List<PlayerSlotInfo> savedInfos = store.GetAllValues<PlayerSlotInfo>().OrderBy(info => info.SlotID).ToList();
            foreach (DataReference oldInfo in store.GetAllDataReferences<PlayerSlotInfo>().ToList())
                store.Destroy(oldInfo);

            var bus = new InProcessBus();
            var slots = new PlayerSlot[savedInfos.Count];
            for (int i = 0; i < savedInfos.Count; i++)
            {
                slots[i] = new PlayerSlot(i, savedInfos[i].TeamNumber, savedInfos[i].PlayerID, new ArmyListFile(), store);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", savedInfos[i].PlayerID, new FDGGame_AsLocal(store, bus), Seed, slots[i].SlotID));
            }

            var game = new ResumedGame { Recorder = ReplayRecorder.Continue(prior) };
            var completed = new TaskCompletionSource<GameResult>();
            FDGServer? server = null;
            server = new FDGServer(store, bus, slots, replay: game.Recorder);
            server.OnGameCompleted += result =>
            {
                game.FinalSave = GameSaveSerializer.Save(store);
                game.SaveAfterTheEnd = server!.SaveGame();
                completed.TrySetResult(result);
            };

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the resumed game must play to completion without hanging.");
            game.Result = await completed.Task;
            game.Document = Read(game.Recorder.ExportGzip());
            return game;
        }

        private static string FinalPlayback(ReplayDocument document)
        {
            var player = new ReplayPlayer(new ReplayTimeline(document));
            player.SeekToEnd();
            return GameSaveSerializer.Save(player.Store);
        }

        private static List<EReplayMarker> MarkerKinds(ReplayDocument document) =>
            document.Events.OfType<MarkerEvent>().Select(m => m.Marker).Where(m => m != EReplayMarker.ActivationStart).ToList();

        // --- Saving ------------------------------------------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task ASave_EmbedsTheReplaySoFar_EndingWithItsSavedMarker()
        {
            SavedGame game = await PlayAndSaveInRoundTwo();

            GameSaveFile file = JsonConvert.DeserializeObject<GameSaveFile>(game.Save)!;
            (GameDataStore loaded, byte[]? replay) = GameSaveSerializer.LoadWithReplay(game.Save);
            Assert.That(replay, Is.Not.Null.And.Not.Empty);
            Assert.That(GameSaveSerializer.Load(game.Save).GetAllValues<UnitData>().Count(), Is.EqualTo(loaded.GetAllValues<UnitData>().Count()),
                "Load still reads a save that carries a replay");

            ReplayDocument embedded = Read(replay!);
            var saved = (MarkerEvent)embedded.Events[^1];
            Assert.That(saved.Marker, Is.EqualTo(EReplayMarker.Saved));
            Assert.That(saved.Round, Is.EqualTo(2));
            Assert.That(embedded.Header.GameId, Is.EqualTo(game.Live.Header.GameId));
            Assert.That(file.ReplaySummary!.Steps, Is.EqualTo(new ReplayTimeline(embedded).Steps.Count),
                "the summary describes the embedded replay");
            Assert.That(file.ReplaySummary.Rounds, Is.EqualTo(2));
            Assert.That(file.ReplaySummary.Outcome, Is.Null, "saved mid-game");

            // The live recording carries the same marker at the same place and plays on past it.
            Assert.That(embedded.Events.Select(e => (e.Seq, e.Kind)), Is.EqualTo(game.Live.Events.Take(embedded.Events.Count).Select(e => (e.Seq, e.Kind))),
                "the embedded replay is the live recording up to the save");
            Assert.That(game.Live.Events.OfType<MarkerEvent>().Count(m => m.Marker == EReplayMarker.Saved), Is.EqualTo(1));
            Assert.That(game.Live.Events[^1], Is.InstanceOf<MarkerEvent>().With.Property(nameof(MarkerEvent.Marker)).EqualTo(EReplayMarker.GameEnded));
            Assert.That(game.Broadcasts.Count(b => b.Marker == EReplayMarker.Saved), Is.EqualTo(1), "clients record the save too");
            Assert.That(ReplayVerifier.Verify(new ReplayTimeline(game.Live)).Passed, Is.True);
        }

        // --- Resuming ----------------------------------------------------------------------------------

        [Test]
        [CancelAfter(240_000)]
        public async Task ResumingASave_ContinuesOneReplay_ThatPlaysBackToTheLiveEnd()
        {
            SavedGame saved = await PlayAndSaveInRoundTwo();
            ReplayDocument prior = Read(GameSaveSerializer.LoadWithReplay(saved.Save).ReplayGzip!);

            ResumedGame resumed = await ResumeAndFinish(saved.Save);
            ReplayDocument document = resumed.Document;
            IReadOnlyList<ReplayEvent> events = document.Events;

            Assert.That(document.Header.GameId, Is.EqualTo(prior.Header.GameId), "one game, one ID");
            Assert.That(document.Header.StartedUtc, Is.EqualTo(prior.Header.StartedUtc), "the game began when it first began");
            Assert.That(document.Header.ResumedUtc, Has.Count.EqualTo(1));
            Assert.That(document.Header.Seed, Is.EqualTo(prior.Header.Seed));

            Assert.That(events.Take(prior.Events.Count).Select(e => (e.Seq, e.Kind)), Is.EqualTo(prior.Events.Select(e => (e.Seq, e.Kind))),
                "the saved history is kept as it was");
            Assert.That(events.Select(e => e.Seq), Is.EqualTo(Enumerable.Range(0, events.Count).Select(i => (long)i)),
                "Seq counts on across the resume");
            Assert.That(events.Zip(events.Skip(1)).All(pair => pair.First.T <= pair.Second.T), Is.True, "T never runs backwards");

            var resumedMarker = (MarkerEvent)events[prior.Events.Count];
            Assert.That(resumedMarker.Marker, Is.EqualTo(EReplayMarker.Resumed));
            Assert.That(resumedMarker.Round, Is.EqualTo(2));
            var snapshot = (SnapshotEvent)events[prior.Events.Count + 1];
            Assert.That(snapshot.Index, Is.EqualTo(prior.Events.OfType<SnapshotEvent>().Count()), "the snapshot index counts on");

            List<EReplayMarker> kinds = MarkerKinds(document);
            int savedAt = kinds.IndexOf(EReplayMarker.Saved);
            Assert.That(kinds.Skip(savedAt).Take(2), Is.EqualTo(new[] { EReplayMarker.Saved, EReplayMarker.Resumed }));
            Assert.That(kinds[^1], Is.EqualTo(EReplayMarker.GameEnded));
            Assert.That(kinds.Count(k => k == EReplayMarker.GameEnded), Is.EqualTo(1));
            Assert.That(events.OfType<SnapshotEvent>().Count(), Is.EqualTo(resumed.Result.RoundsPlayed + 2),
                "game start, every round start, and the resume");

            Assert.That(FinalPlayback(document), Is.EqualTo(resumed.FinalSave), "played back, the one replay ends where the game did");
            ReplayVerification verification = ReplayVerifier.Verify(new ReplayTimeline(document));
            Assert.That(verification.Passed, Is.True, string.Join("\n", verification.Failures.Select(f => f.Message)));
            Assert.That(verification.Notes.Select(n => n.Message), Has.Some.Contains("follows a resume"));

            ReplaySummary summary = resumed.Recorder.Summary;
            Assert.That(summary.Steps, Is.EqualTo(new ReplayTimeline(document).Steps.Count), "the summary counts both sessions");
            Assert.That(summary.Outcome, Is.EqualTo(resumed.Result.Outcome.ToString()));
            Assert.That(summary.DurationMs, Is.EqualTo(events[^1].T));
        }

        // A #187 recovery save is written after the game ended: its replay ends with GameEnded and no Saved marker (the
        // recording is closed). Resumed and played on, the one replay keeps that end, then the resume (Chris, #453).
        [Test]
        [CancelAfter(300_000)]
        public async Task ResumingASaveWrittenAfterTheEnd_KeepsThatEnd_ThenTheResume()
        {
            SavedGame saved = await PlayAndSaveInRoundTwo();
            ResumedGame first = await ResumeAndFinish(saved.Save);
            ReplayDocument recovery = Read(GameSaveSerializer.LoadWithReplay(first.SaveAfterTheEnd).ReplayGzip!);
            Assert.That(((MarkerEvent)recovery.Events[^1]).Marker, Is.EqualTo(EReplayMarker.GameEnded),
                "a save after the end embeds the closed recording as it stands");

            ResumedGame again = await ResumeAndFinish(first.SaveAfterTheEnd);
            ReplayTimeline timeline = new ReplayTimeline(again.Document);

            List<EReplayMarker> kinds = MarkerKinds(again.Document);
            Assert.That(kinds.Count(k => k == EReplayMarker.Resumed), Is.EqualTo(2));
            Assert.That(kinds.Count(k => k == EReplayMarker.GameEnded), Is.EqualTo(2));
            int firstEnd = kinds.IndexOf(EReplayMarker.GameEnded);
            Assert.That(kinds[firstEnd + 1], Is.EqualTo(EReplayMarker.Resumed));
            Assert.That(again.Document.Header.ResumedUtc, Has.Count.EqualTo(2));

            Assert.That(timeline.GameEndedMarker!.Seq, Is.EqualTo(again.Document.Events[^1].Seq), "the game's end is the last one");
            Assert.That(again.Recorder.Summary.Outcome, Is.EqualTo(again.Result.Outcome.ToString()));
            ReplayVerification verification = ReplayVerifier.Verify(timeline);
            Assert.That(verification.Passed, Is.True, string.Join("\n", verification.Failures.Select(f => f.Message)));
            Assert.That(verification.Notes.Select(n => n.Message), Has.Some.Contains("and was resumed from a save"));
            Assert.That(FinalPlayback(again.Document), Is.EqualTo(again.FinalSave));
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task ASaveWithoutAReplay_ResumesAFreshRecording_FromGameStart()
        {
            SavedGame saved = await PlayAndSaveInRoundTwo();

            ResumedGame resumed = await ResumeAndFinish(StoreOnly(saved.Save));

            var start = (MarkerEvent)resumed.Document.Events[0];
            Assert.That(start.Marker, Is.EqualTo(EReplayMarker.GameStart), "nothing to continue: the recording begins at the resume, as before #453");
            Assert.That(start.Round, Is.EqualTo(2));
            Assert.That(resumed.Document.Header.ResumedUtc, Is.Empty);
            Assert.That(resumed.Document.Header.GameId, Is.EqualTo(saved.Live.Header.GameId), "the store still names the game");
        }

        // --- Recorder, scripted ------------------------------------------------------------------------

        private sealed class Lines : ITextOutput
        {
            public readonly List<string> Logged = new List<string>();

            public void Log(string message, TextColor? color = null) => Logged.Add(message);

            public void LogDebug(string message, TextColor? color = null) => Logged.Add(message);
        }

        private static ReplayHeader Header(Guid gameId) => new ReplayHeader { GameId = gameId, StartedUtc = new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc) };

        private static byte[] Recording(ReplayHeader header, params ReplayEvent[] events)
        {
            using var stream = new MemoryStream();
            ReplayWriter.WriteGzip(header, events.Select((e, i) => e with { Seq = i, T = 100 * i }), stream);
            return stream.ToArray();
        }

        [Test]
        public void Continue_SeedsTheRecording_AndCountsOnFromIt()
        {
            Guid gameId = Guid.NewGuid();
            var task = new TaskID(Guid.NewGuid());
            byte[] prior = Recording(Header(gameId),
                new MarkerEvent(EReplayMarker.GameStart),
                new SnapshotEvent(0, "{}"),
                new MarkerEvent(EReplayMarker.RoundStart) { Round = 1 },
                new SnapshotEvent(1, "{}"),
                new RequestEvent(task, new PlayerID(Guid.NewGuid()), "Req", "Rep", "Pick", "{}"),
                new ReplyEvent(task, "{}"),
                new MarkerEvent(EReplayMarker.Saved) { Round = 1 });

            ReplayRecorder recorder = ReplayRecorder.Continue(prior);
            Assert.That(recorder.ContinuesASave, Is.True);
            Assert.That(recorder.Summary.Steps, Is.EqualTo(1), "the history counts before the resume records anything");

            var log = new Lines();
            recorder.Start(Header(gameId), log);
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.Resumed) { Round = 1 });
            Assert.That(recorder.Snapshot(GameDataStore.GameDataStoreBuilder.GetDefault()), Is.EqualTo(2), "snapshot indices count on");

            List<ReplayEvent> events = Read(recorder.ExportGzip()).Events.ToList();
            Assert.That(events.Select(e => e.Seq), Is.EqualTo(Enumerable.Range(0, 9).Select(i => (long)i)));
            Assert.That(events[7].T, Is.GreaterThanOrEqualTo(600), "T counts on from the history's last event");
            Assert.That(recorder.Header!.StartedUtc, Is.EqualTo(Header(gameId).StartedUtc), "the history's header is kept");
            Assert.That(recorder.Header.ResumedUtc, Has.Count.EqualTo(1));
            Assert.That(log.Logged, Is.Empty);
        }

        [Test]
        public void Continue_UnreadableBytes_StartAFreshRecordingAtTheResume_AndSaySo()
        {
            ReplayRecorder recorder = ReplayRecorder.Continue(new byte[] { 1, 2, 3 });
            var log = new Lines();
            recorder.Start(Header(Guid.NewGuid()), log);

            Assert.That(recorder.ContinuesASave, Is.True, "the resume still records Resumed, not GameStart");
            Assert.That(recorder.Summary.Steps, Is.Zero);
            Assert.That(recorder.Header!.ResumedUtc, Has.Count.EqualTo(1));
            Assert.That(log.Logged.Single(), Does.StartWith("[replay] The save's replay could not be read"));
        }

        [Test]
        public void Continue_AnotherGamesReplay_IsDropped_AndSaysSo()
        {
            byte[] prior = Recording(Header(Guid.NewGuid()), new MarkerEvent(EReplayMarker.GameStart), new SnapshotEvent(0, "{}"));
            ReplayRecorder recorder = ReplayRecorder.Continue(prior);
            var log = new Lines();
            Guid thisGame = Guid.NewGuid();
            recorder.Start(Header(thisGame), log);
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.Resumed) { Round = 2 });

            List<ReplayEvent> events = Read(recorder.ExportGzip()).Events.ToList();
            Assert.That(events.Single(), Is.InstanceOf<MarkerEvent>());
            Assert.That(events[0].Seq, Is.Zero);
            Assert.That(recorder.Header!.GameId, Is.EqualTo(thisGame));
            Assert.That(log.Logged.Single(), Does.StartWith("[replay] The save's replay is of another game"));
        }

        [Test]
        public void Continue_Null_IsAPlainRecorder()
        {
            Assert.That(ReplayRecorder.Continue(null).ContinuesASave, Is.False);
        }

        [Test]
        public void CutForSave_KeepsWhatCameBefore_AndClosesWithSaved()
        {
            var task = new TaskID(Guid.NewGuid());
            byte[] bytes = Recording(Header(Guid.NewGuid()),
                new MarkerEvent(EReplayMarker.GameStart),
                new SnapshotEvent(0, "{}"),
                new MarkerEvent(EReplayMarker.RoundStart) { Round = 3 },
                new RequestEvent(task, new PlayerID(Guid.NewGuid()), "Req", "Rep", "Pick", "{}"),
                new ReplyEvent(task, "{}"),
                new RequestEvent(new TaskID(Guid.NewGuid()), new PlayerID(Guid.NewGuid()), "Req", "Rep", "Pick", "{}"));

            ReplayRecorder cut = ReplayRecorder.CutForSave(Read(bytes), beforeSeq: 5);

            List<ReplayEvent> events = Read(cut.ExportGzip()).Events.ToList();
            Assert.That(events.Select(e => e.Kind), Is.EqualTo(new[] { "Marker", "Snapshot", "Marker", "Request", "Reply", "Marker" }));
            var saved = (MarkerEvent)events[^1];
            Assert.That((saved.Marker, saved.Round, saved.Seq, saved.T), Is.EqualTo((EReplayMarker.Saved, (int?)3, 5L, 400L)));
            Assert.That(cut.IsRecording, Is.False);
            Assert.That(cut.Summary.Steps, Is.EqualTo(1));
        }

        [Test]
        public void ExportThroughResume_IsTheHistoryUpToTheResume_OrNull()
        {
            byte[] prior = Recording(Header(Guid.NewGuid()), new MarkerEvent(EReplayMarker.GameStart), new SnapshotEvent(0, "{}"));
            ReplayDocument priorDocument = Read(prior);
            ReplayRecorder recorder = ReplayRecorder.Continue(prior);
            recorder.Start(Header(priorDocument.Header.GameId));
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.Resumed) { Round = 2 });
            recorder.RecordLog("after the resume", debug: false);

            List<ReplayEvent> history = Read(recorder.ExportThroughResume()!).Events.ToList();
            Assert.That(history.Select(e => e.Kind), Is.EqualTo(new[] { "Marker", "Snapshot", "Marker" }));
            Assert.That(((MarkerEvent)history[^1]).Marker, Is.EqualTo(EReplayMarker.Resumed));

            var fresh = new ReplayRecorder();
            fresh.Start(Header(Guid.NewGuid()));
            fresh.RecordMarker(new MarkerEvent(EReplayMarker.GameStart));
            Assert.That(fresh.ExportThroughResume(), Is.Null, "a recording that did not begin at a resume has no history to hand on");
        }

        // --- Verifier, scripted ------------------------------------------------------------------------

        private static ReplayVerification Verify(params ReplayEvent[] events) =>
            ReplayVerifier.Verify(new ReplayTimeline(Read(Recording(Header(Guid.NewGuid()), events))));

        [Test]
        public void Verifier_ARequestOpenAtTheSave_IsANote_WhenTheResumedGameAsksAgain()
        {
            var player = new PlayerID(Guid.NewGuid());
            TaskID abandoned = new TaskID(Guid.NewGuid()), askedAgain = new TaskID(Guid.NewGuid());
            string store = GameSaveSerializer.Save(GameDataStore.GameDataStoreBuilder.GetDefault());

            ReplayVerification verification = Verify(
                new MarkerEvent(EReplayMarker.GameStart),
                new SnapshotEvent(0, store),
                new RequestEvent(abandoned, player, "Req", "Rep", "Pick", "{}"),
                new MarkerEvent(EReplayMarker.Saved),
                new MarkerEvent(EReplayMarker.Resumed) { Round = 1 },
                new SnapshotEvent(1, store),
                new RequestEvent(askedAgain, player, "Req", "Rep", "Pick", "{}"),
                new ReplyEvent(askedAgain, "{}"),
                new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Win" });

            Assert.That(verification.Passed, Is.True, string.Join("\n", verification.Failures.Select(f => f.Message)));
            Assert.That(verification.Notes.Select(n => n.Message), Has.Some.EqualTo("1 request open when the game was saved, asked again after the resume."));
        }

        [Test]
        public void Verifier_AnUnansweredRequest_ThatThePlayerAnsweredPastInTheSameSession_StillFails()
        {
            var player = new PlayerID(Guid.NewGuid());
            TaskID skipped = new TaskID(Guid.NewGuid()), later = new TaskID(Guid.NewGuid());
            string store = GameSaveSerializer.Save(GameDataStore.GameDataStoreBuilder.GetDefault());

            ReplayVerification verification = Verify(
                new MarkerEvent(EReplayMarker.GameStart),
                new SnapshotEvent(0, store),
                new RequestEvent(skipped, player, "Req", "Rep", "Pick", "{}"),
                new RequestEvent(later, player, "Req", "Rep", "Pick", "{}"),
                new ReplyEvent(later, "{}"),
                new MarkerEvent(EReplayMarker.Saved),
                new MarkerEvent(EReplayMarker.Resumed) { Round = 1 },
                new SnapshotEvent(1, store));

            Assert.That(verification.Failures.Select(f => f.Message), Has.Some.Contains("was never answered, but the same player answered a later request (Seq 3)"));
        }

        [Test]
        public void Verifier_TwoGameEndsInOneSession_StillFail()
        {
            string store = GameSaveSerializer.Save(GameDataStore.GameDataStoreBuilder.GetDefault());

            ReplayVerification verification = Verify(
                new MarkerEvent(EReplayMarker.GameStart),
                new SnapshotEvent(0, store),
                new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Disconnect" },
                new MarkerEvent(EReplayMarker.Resumed) { Round = 1 },
                new SnapshotEvent(1, store),
                new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Win" },
                new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Win" });

            Assert.That(verification.Failures.Select(f => f.Message), Is.EqualTo(new[] { "A second GameEnded marker at Seq 6 (the first is at Seq 5)." }));
            Assert.That(verification.Notes.Select(n => n.Message), Has.Some.EqualTo("The game ended (Disconnect) at Seq 2 and was resumed from a save at Seq 3."));
        }
    }
}
