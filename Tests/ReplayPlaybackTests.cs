using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Replay;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.StageResolution;
using NUnit.Framework;
using static FDG.Tests.ReplayRecorderGameTests;

namespace FDG.Tests
{
    // #454: ReplayPlayer (plan 4.3). The scripted test records through the real recorder on a test store and
    // checks the state at every step against saves of the live store taken at the moment each decision was
    // asked. The real-game tests check the whole-game equalities plan rule 8 allows: played-back final state ==
    // live final state, and seeking to a step == walking to it. All comparisons are whole re-saved stores.
    [TestFixture]
    public class ReplayPlaybackTests
    {
        private static readonly PlayerID PlayerA = new PlayerID(Guid.NewGuid());
        private static readonly PlayerID PlayerB = new PlayerID(Guid.NewGuid());

        private static string Save(GameDataStore store) => GameSaveSerializer.Save(store);

        private static ReplayHeader Header() => new ReplayHeader
        {
            BuildVersion = ReplayFormat.BuildVersion,
            Settings = GameSettings.GetDefault(),
            Slots = new List<ReplaySlot>
            {
                new ReplaySlot { SlotId = 0, PlayerId = PlayerA, Name = "Alpha", Controller = EReplayController.Human },
                new ReplaySlot { SlotId = 1, PlayerId = PlayerB, Name = "Bravo", Controller = EReplayController.Ai },
            },
        };

        // An answered decision with no option list - always a step.
        private static void Decide(ReplayRecorder recorder, PlayerID player)
        {
            var task = new TaskID(Guid.NewGuid());
            recorder.OnRequested(task, player, "Test.Decision", "System.Boolean", "Deciding", "{}");
            recorder.OnReplied(task, "true");
        }

        private static ReplayDocument Read(ReplayRecorder recorder)
        {
            using var stream = new MemoryStream(recorder.ExportGzip());
            return ReplayReader.ReadGzip(stream);
        }

        [Test]
        public void ScriptedRecording_ShowsTheLiveStateAtEveryStep_AndOnlyReseeksRebuild()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(5, 5), gameDataStore: store);
            DataReference modelRef = store.Create(model);
            DataReference unitRef = store.Create(new UnitData(PlayerA, "Warriors", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(modelRef) }));
            DataReference note = store.Create("note");

            var recorder = new ReplayRecorder();
            recorder.Start(Header());
            recorder.AttachStore(store);
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.GameStart));
            recorder.Snapshot(store);

            store.GetValue<ModelData>(modelRef).SetPosition(new Position(10, 4));
            string atStep0 = Save(store);
            Decide(recorder, PlayerA);

            // Step 0's effects: an update, a remove, a beat and a log line.
            UnitData unit = store.GetValue<UnitData>(unitRef);
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.RoundEnd()));
            store.SetValue(unitRef, unit);
            store.Destroy(note);
            recorder.OnBeat(new UnitRoutedBeat(unit.ID, unit.Name, new List<RoutedModel>()));
            recorder.RecordLog("Warriors rout.", debug: false);
            string atStep1 = Save(store);
            Decide(recorder, PlayerB);

            // A new round's snapshot, then an add into the slot the remove above freed - the generation the
            // rebuilt store cannot know.
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.RoundStart) { Round = 1 });
            recorder.Snapshot(store);
            DataReference recycled = store.Create("recycled");
            Assert.That((recycled.Index, recycled.Generation), Is.EqualTo((note.Index, note.Generation + 1)), "precondition");
            string atStep2 = Save(store);
            Decide(recorder, PlayerA);

            store.GetValue<ModelData>(modelRef).SetPosition(new Position(20, 8));
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Tie" });
            string atEnd = Save(store);
            recorder.Dispose();

            var player = new ReplayPlayer(new ReplayTimeline(Read(recorder)));
            int replaced = 0;
            player.OnStoreReplaced += _ => replaced++;

            Assert.That(player.Steps, Has.Count.EqualTo(3));
            Assert.That(player.CurrentStep, Is.SameAs(player.Steps[0]));
            Assert.That(Save(player.Store), Is.EqualTo(atStep0), "the state a decision was asked in, before its effects");

            PlaybackSpan? span = player.StepForward();
            Assert.That(Save(player.Store), Is.EqualTo(atStep1));
            Assert.That(span!.StoreReplaced, Is.False);
            Assert.That(span.Beats.OfType<UnitRoutedBeat>().Single().UnitName, Is.EqualTo("Warriors"), "step 0's effects play on the way to step 1");
            Assert.That(span.LogLines.Select(l => l.Line), Is.EqualTo(new[] { "Warriors rout." }));
            Assert.That(span.Problems, Is.Empty);

            span = player.StepForward();
            Assert.That(Save(player.Store), Is.EqualTo(atStep2), "in place across a snapshot, adopting the recycled slot's generation");
            Assert.That(span!.Markers.Select(m => m.Marker), Is.EqualTo(new[] { EReplayMarker.RoundStart }));

            span = player.StepForward();
            Assert.That(Save(player.Store), Is.EqualTo(atEnd), "from the last step, forward is the end of the recording");
            Assert.That(player.IsAtEnd, Is.True);
            Assert.That(player.CurrentStep, Is.Null);
            Assert.That(span!.Markers.Last().Marker, Is.EqualTo(EReplayMarker.GameEnded));
            Assert.That(player.StepForward(), Is.Null);
            Assert.That(replaced, Is.EqualTo(0), "stepping forward never replaces the store");

            span = player.StepBack();
            Assert.That(player.CurrentStep, Is.SameAs(player.Steps[2]));
            Assert.That(Save(player.Store), Is.EqualTo(atStep2), "rebuilt from the round's snapshot");
            Assert.That(span!.StoreReplaced, Is.True);
            Assert.That(replaced, Is.EqualTo(1));

            player.StepBack();
            Assert.That(Save(player.Store), Is.EqualTo(atStep1));
            player.SeekToStep(0);
            Assert.That(Save(player.Store), Is.EqualTo(atStep0));
            Assert.That(player.StepBack(), Is.Null, "nothing before the first step");
            Assert.That(replaced, Is.EqualTo(3));

            player.SeekToEnd();
            Assert.That(Save(player.Store), Is.EqualTo(atEnd));
            Assert.That(player.TableState.DataStore, Is.SameAs(player.Store));
        }

        [Test]
        public void ARecordingWithNoSnapshot_IsRefusedClearly_AndOneWithNoSteps_StartsAtTheEnd()
        {
            var noSnapshot = new ReplayDocument(Header(), new ReplayEvent[] { new MarkerEvent(EReplayMarker.GameStart) { Seq = 0 } });
            Assert.That(() => new ReplayPlayer(new ReplayTimeline(noSnapshot)),
                Throws.TypeOf<ReplayPlaybackException>().With.Message.Contains("no snapshot"));

            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var noSteps = new ReplayDocument(Header(), new ReplayEvent[]
            {
                new MarkerEvent(EReplayMarker.GameStart) { Seq = 0 },
                new SnapshotEvent(0, Save(store)) { Seq = 1 },
            });
            var player = new ReplayPlayer(new ReplayTimeline(noSteps));
            Assert.That(player.IsAtEnd, Is.True);
            Assert.That(player.StepForward(), Is.Null);
            Assert.That(player.StepBack(), Is.Null);
        }

        // --- A real in-process game ------------------------------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task ARealGame_PlaysBackToItsLiveFinalState_AndSeekingEqualsWalking()
        {
            RecordedGame game = await PlayFreshGame(seed: 4242);
            var timeline = new ReplayTimeline(game.Document);
            Assert.That(timeline.Steps, Is.Not.Empty);

            var player = new ReplayPlayer(timeline);
            Assert.That(timeline.Steps.Select(s => s.Summary), Has.All.Match(@"^[ -~]+$"), "every step is described, in ASCII");
            Assert.That(timeline.Steps.Count(s => s.Summary != s.DisplayName), Is.GreaterThan(timeline.Steps.Count / 2),
                "most steps get a line of their own rather than the fallback");

            int replaced = 0;
            player.OnStoreReplaced += _ => replaced++;
            var walked = new List<string> { Save(player.Store) };
            int beats = 0;
            var problems = new List<string>();
            while (player.StepForward() is PlaybackSpan span)
            {
                beats += span.Beats.Count;
                problems.AddRange(span.Problems);
                if (player.IsAtEnd == false)
                {
                    walked.Add(Save(player.Store));
                }
            }

            Assert.That(Save(player.Store), Is.EqualTo(game.FinalSave), "every recorded delta, applied in order, reaches the live final state");
            Assert.That(replaced, Is.EqualTo(0));
            Assert.That(walked, Has.Count.EqualTo(timeline.Steps.Count));
            Assert.That(walked.Distinct().Count(), Is.GreaterThan(timeline.Steps.Count / 2), "the steps really move the game on");
            Assert.That(problems, Is.Empty, "every beat after step 0 reads back");

            // Seek to every step from a fresh player, last first, so each one is a real rebuild.
            var seeker = new ReplayPlayer(timeline);
            for (int k = timeline.Steps.Count - 1; k >= 0; k--)
            {
                seeker.SeekToStep(k);
                Assert.That(Save(seeker.Store), Is.EqualTo(walked[k]), $"seek == walk at step {k}");
            }
            Assert.That(beats, Is.GreaterThan(0));
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task EverySnapshot_ReloadsExactly_UnderThePlaybackLoad()
        {
            RecordedGame game = await PlayFreshGame(seed: 77);
            List<SnapshotEvent> snapshots = game.Document.Events.OfType<SnapshotEvent>().ToList();
            Assert.That(snapshots, Has.Count.GreaterThan(1));

            foreach (SnapshotEvent snapshot in snapshots)
            {
                Assert.That(Save(GameSaveSerializer.Load(snapshot.SaveJson, stampLegacyReserves: false)), Is.EqualTo(snapshot.SaveJson),
                    $"snapshot {snapshot.Index} comes back exactly as recorded");
            }

            // Why playback has its own load: snapshot 0 predates deployment, every model at the origin, and the
            // save path's legacy stamp would put each waiting unit in reserve.
            Assert.That(Save(GameSaveSerializer.Load(snapshots[0].SaveJson)), Is.Not.EqualTo(snapshots[0].SaveJson));
        }
    }
}
