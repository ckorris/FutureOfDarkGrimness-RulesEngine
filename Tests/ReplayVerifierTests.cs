using FDG.Data;
using FDG.Replay;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using NUnit.Framework;
using static FDG.Tests.ReplayRecorderGameTests;

namespace FDG.Tests
{
    // #455: ReplayVerifier. A scripted recording on a test store pins each check against a known tampering; the
    // real-game test pins "a clean recording verifies clean" (plan rule 8: no golden files).
    [TestFixture]
    public class ReplayVerifierTests
    {
        private static readonly PlayerID PlayerA = new PlayerID(Guid.NewGuid());
        private static readonly PlayerID PlayerB = new PlayerID(Guid.NewGuid());

        private sealed class Scripted
        {
            public ReplayDocument Document = null!;
            public DataReference UnitRef;
        }

        // Snapshot 0, a unit update + a model move + a decision, snapshot 1, another decision, the end.
        private static Scripted Record(Action<ReplayRecorder>? beforeEnd = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataReference modelRef = store.Create(new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(5, 5), gameDataStore: store));
            DataReference unitRef = store.Create(new UnitData(PlayerA, "Warriors", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(modelRef) }));

            var recorder = new ReplayRecorder();
            recorder.Start(new ReplayHeader
            {
                BuildVersion = ReplayFormat.BuildVersion,
                Settings = GameSettings.GetDefault(),
                Slots = new List<ReplaySlot>
                {
                    new ReplaySlot { SlotId = 0, PlayerId = PlayerA, Controller = EReplayController.Human },
                    new ReplaySlot { SlotId = 1, PlayerId = PlayerB, Controller = EReplayController.Ai },
                },
            });
            recorder.AttachStore(store);
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.GameStart));
            recorder.Snapshot(store);

            UnitData unit = store.GetValue<UnitData>(unitRef);
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.RoundEnd()));
            store.SetValue(unitRef, unit);
            store.GetValue<ModelData>(modelRef).SetPosition(new Position(9, 4));
            Decide(recorder, PlayerA);

            recorder.RecordMarker(new MarkerEvent(EReplayMarker.RoundStart) { Round = 1 });
            recorder.Snapshot(store);
            store.GetValue<ModelData>(modelRef).SetPosition(new Position(12, 6));
            Decide(recorder, PlayerB);

            beforeEnd?.Invoke(recorder);
            recorder.RecordMarker(new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Tie" });
            recorder.Dispose();

            using var stream = new MemoryStream(recorder.ExportGzip());
            return new Scripted { Document = ReplayReader.ReadGzip(stream), UnitRef = unitRef };
        }

        private static void Decide(ReplayRecorder recorder, PlayerID player)
        {
            var task = new TaskID(Guid.NewGuid());
            recorder.OnRequested(task, player, "Test.Decision", "System.Boolean", "Deciding", "{}");
            recorder.OnReplied(task, "true");
        }

        private static ReplayVerification Verify(ReplayDocument document) => ReplayVerifier.Verify(new ReplayTimeline(document));

        private static ReplayDocument Without(ReplayDocument document, Func<ReplayEvent, bool> drop) =>
            new ReplayDocument(document.Header, document.Events.Where(e => drop(e) == false).ToList());

        private static StoreUpdateEvent UnitUpdate(Scripted scripted) =>
            scripted.Document.Events.OfType<StoreUpdateEvent>().Single(u => u.Ref.Equals(scripted.UnitRef));

        [Test]
        public void ACleanRecording_Verifies()
        {
            ReplayVerification result = Verify(Record().Document);

            Assert.That(result.Failures, Is.Empty, string.Join("\n", result.Failures.Select(f => f.Message)));
            Assert.That(result.Notes, Is.Empty);
            Assert.That(result.SnapshotsCompared, Is.EqualTo(1));
            Assert.That(result.DeltasApplied, Is.EqualTo(3), "the unit update and the two moves");
        }

        [Test]
        public void ARemovedUpdate_IsAGapAtItsSeq_AndTheDivergenceItCauses()
        {
            Scripted scripted = Record();
            StoreUpdateEvent removed = UnitUpdate(scripted);

            ReplayVerification result = Verify(Without(scripted.Document, e => e.Seq == removed.Seq));

            Assert.That(result.Passed, Is.False);
            ReplayVerifyIssue gap = result.Failures.Single(f => f.Check == "seq");
            Assert.That(gap.Seq, Is.EqualTo(removed.Seq));
            Assert.That(gap.Message, Does.Contain($"Event {removed.Seq} is missing"));

            ReplayVerifyIssue divergence = result.Failures.Single(f => f.Check == "snapshot");
            Assert.That(divergence.Message, Does.StartWith("Snapshot 1 "));
            Assert.That(divergence.Details.Single(), Does.StartWith($"UnitData[{scripted.UnitRef.Index}]: _tokens"));
            Assert.That(divergence.Details.Single(), Does.Contain("no change recorded since snapshot 0"));
        }

        [Test]
        public void ACorruptedUpdate_IsAnchoredToItsSeq_WithTheFieldItGotWrong()
        {
            Scripted scripted = Record();
            StoreUpdateEvent update = UnitUpdate(scripted);
            StoreUpdateEvent corrupted = update with { Json = update.Json.Replace("\"Warriors\"", "\"Wariors\"") };
            Assert.That(corrupted.Json, Is.Not.EqualTo(update.Json), "precondition");

            var events = scripted.Document.Events.Select(e => e.Seq == update.Seq ? corrupted : e).ToList();
            ReplayVerification result = Verify(new ReplayDocument(scripted.Document.Header, events));

            ReplayVerifyIssue divergence = result.Failures.Single();
            Assert.That(divergence.Check, Is.EqualTo("snapshot"));
            Assert.That(divergence.Seq, Is.EqualTo(update.Seq));
            Assert.That(divergence.Details.Single(), Is.EqualTo($"UnitData[{scripted.UnitRef.Index}]: Name (last change Seq {update.Seq})"));
        }

        [Test]
        public void RequestAndMarkerChecks()
        {
            var openMidway = new TaskID(Guid.NewGuid());
            var openAtEnd = new TaskID(Guid.NewGuid());
            var failed = new TaskID(Guid.NewGuid());
            Scripted scripted = Record(recorder =>
            {
                recorder.OnRequested(openMidway, PlayerA, "Test.Lost", "System.Boolean", "Lost", "{}");
                recorder.OnRequested(failed, PlayerA, "Test.Failing", "System.Boolean", "Failing", "{}");
                recorder.OnFailed(failed, "resolver threw");
                Decide(recorder, PlayerA); // A moved on: the open request before this is a hole
                recorder.OnRequested(openAtEnd, PlayerB, "Test.Pending", "System.Boolean", "Pending", "{}");
            });

            ReplayVerification result = Verify(scripted.Document);
            Assert.That(result.Failures.Select(f => f.Message),
                Has.Exactly(1).Matches<string>(m => m.StartsWith("Lost (Seq") && m.Contains("was never answered")));
            Assert.That(result.Notes.Select(n => n.Message), Has.Exactly(1).EqualTo("Failing (Seq " +
                scripted.Document.Events.OfType<RequestEvent>().Single(r => r.TaskId == failed).Seq + ") failed: resolver threw"));
            Assert.That(result.Notes.Select(n => n.Message), Has.Exactly(1).EqualTo("1 request still open when the recording stopped."));

            // No GameEnded is a note; a second one, and an answer to nothing, are failures.
            ReplayDocument document = scripted.Document;
            MarkerEvent end = document.Events.OfType<MarkerEvent>().Single(m => m.Marker == EReplayMarker.GameEnded);
            ReplayVerification abandoned = Verify(Without(document, e => e.Seq == end.Seq));
            Assert.That(abandoned.Notes.Select(n => n.Check), Does.Contain("marker"));
            Assert.That(abandoned.Failures.Where(f => f.Check == "marker"), Is.Empty);

            long next = document.Events[^1].Seq + 1;
            var tampered = document.Events.Concat(new ReplayEvent[]
            {
                new MarkerEvent(EReplayMarker.GameEnded) { Seq = next, Outcome = "Tie" },
                new ReplyEvent(new TaskID(Guid.NewGuid()), "true") { Seq = next + 1 },
            }).ToList();
            ReplayVerification doubled = Verify(new ReplayDocument(document.Header, tampered));
            Assert.That(doubled.Failures.Single(f => f.Check == "marker").Seq, Is.EqualTo(next));
            Assert.That(doubled.Failures.Single(f => f.Message.Contains("never requested")).Seq, Is.EqualTo(next + 1));
        }

        [Test]
        public void NoSnapshot_Fails()
        {
            Scripted scripted = Record();
            ReplayVerification result = Verify(Without(scripted.Document, e => e is SnapshotEvent));
            Assert.That(result.Failures.Select(f => f.Check), Does.Contain("snapshot"));
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task ARealGame_VerifiesClean()
        {
            RecordedGame game = await PlayFreshGame(seed: 4551);
            ReplayTimeline timeline = new ReplayTimeline(game.Document);

            ReplayVerification result = ReplayVerifier.Verify(timeline);

            Assert.That(result.Failures, Is.Empty, string.Join("\n", result.Failures.Select(f => $"{f.Seq} {f.Message} {string.Join(" | ", f.Details)}")));
            Assert.That(result.SnapshotsCompared, Is.EqualTo(timeline.Snapshots.Count - 1));
            Assert.That(result.SnapshotsCompared, Is.GreaterThan(0));
            Assert.That(result.DeltasApplied, Is.EqualTo(game.Document.Events.Count(e => e is StoreAddEvent or StoreUpdateEvent or StoreRemoveEvent)));
            Assert.That(result.BeatsRead, Is.EqualTo(game.Document.Events.OfType<BeatEvent>().Count()));
        }

        [Test]
        public void JsonDiff_NamesTheTopLevelPropertiesThatDiffer()
        {
            Assert.That(ReplayJsonDiff.ChangedProperties("{\"a\":1,\"b\":{\"c\":2},\"d\":3}", "{\"b\":{\"c\":5},\"a\":1,\"e\":4}"),
                Is.EqualTo(new[] { "b", "e", "d" }), "after's order, then the removed ones");
            Assert.That(ReplayJsonDiff.ChangedProperties("{\"X\":0.0,\"Y\":1.0}", "{\"Y\":1.0,\"X\":0.0}"), Is.Empty);
            Assert.That(ReplayJsonDiff.ChangedProperties("\"note\"", "\"other\""), Is.EqualTo(new[] { ReplayJsonDiff.WholeValue }));
            Assert.That(ReplayJsonDiff.ChangedProperties(null, "{}"), Is.EqualTo(new[] { ReplayJsonDiff.WholeValue }));
        }
    }
}
