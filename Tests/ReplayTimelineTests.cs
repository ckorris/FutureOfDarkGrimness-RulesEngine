using FDG.Data;
using FDG.Network;
using FDG.Replay;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #454: the timeline over hand-built documents (plan 4.3 / 4.4). Requests are real request objects
    // serialized with the wire settings, exactly as RequestMessageSender records them, so the step filter
    // reads the same JSON shapes ($values-wrapped lists included) it meets in a real recording.
    [TestFixture]
    public class ReplayTimelineTests
    {
        private static readonly PlayerID PlayerA = new PlayerID(Guid.NewGuid());
        private static readonly PlayerID PlayerB = new PlayerID(Guid.NewGuid());

        private sealed class DocumentBuilder
        {
            private readonly List<ReplayEvent> _events = new List<ReplayEvent>();
            private long _seq;

            public GameDataStore Store { get; } = GameDataStore.GameDataStoreBuilder.GetDefault();

            public T Add<T>(T replayEvent) where T : ReplayEvent
            {
                T stamped = (T)(replayEvent with { Seq = _seq++ });
                _events.Add(stamped);
                return stamped;
            }

            public RequestEvent Ask<TReply>(IStageTaskRequest<TReply> request) =>
                Add(new RequestEvent(request.TaskID, request.TargetPlayerID, request.GetType().FullName!,
                    typeof(TReply).FullName!, request.DisplayName,
                    JsonConvert.SerializeObject(request, WireJsonSettings.For(Store))));

            public ReplyEvent Answer(RequestEvent request, string replyJson = "\"x\"") =>
                Add(new ReplyEvent(request.TaskId, replyJson));

            public RequestEvent Step(params string[] options)
            {
                RequestEvent request = Ask(Choice(PlayerA, options));
                Answer(request);
                return request;
            }

            public ReplayDocument Build() => new ReplayDocument(Header(), _events);
        }

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

        private static StringSelectionRequest Choice(PlayerID player, string[] options, bool allowCancel = false) =>
            new StringSelectionRequest(player, "Pick one", options, Array.Empty<StringSelectionRequest.InvalidOption>(),
                allowCancel: allowCancel, displayName: $"Choosing from {options.Length}");

        [Test]
        public void ARequestWithoutAReply_IsNotAStep_AndFoldsIntoThePreviousStep()
        {
            var doc = new DocumentBuilder();
            RequestEvent first = doc.Step("A", "B");
            RequestEvent unanswered = doc.Ask(Choice(PlayerA, new[] { "A", "B" }));
            RequestEvent failed = doc.Ask(Choice(PlayerB, new[] { "A", "B" }));
            doc.Add(new RequestErrorEvent(failed.TaskId, "resolver threw"));

            var timeline = new ReplayTimeline(doc.Build());

            Assert.That(timeline.Steps.Select(s => s.Decision.Request), Is.EqualTo(new[] { first }));
            Assert.That(timeline.Steps[0].Folded.Select(f => f.Request), Is.EqualTo(new[] { unanswered, failed }));
            Assert.That(timeline.Steps[0].Folded[1].Error, Is.Not.Null);
            Assert.That(timeline.Requests, Has.Count.EqualTo(3));
        }

        [Test]
        public void AOneOptionChoice_Folds_ButABackOptionCountsAsAChoice()
        {
            var doc = new DocumentBuilder();
            RequestEvent forcedFirst = doc.Step("Only");
            RequestEvent step = doc.Step("A", "B");
            RequestEvent forced = doc.Step("Only");

            RequestEvent orBack = doc.Ask(Choice(PlayerA, new[] { "Only" }, allowCancel: true));
            doc.Answer(orBack);

            var unit = new UnitData(PlayerB, "Grunts", quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>>());
            DataReference unitRef = doc.Store.Create(unit);
            var defender = new ChooseMeleeDefenderRequest(PlayerA, "Charge whom?",
                new[] { new CancellableSelectionRequest<UnitData>.ValidOption(doc.Store.GetDataBinding<UnitData>(unitRef), "Grunts") },
                Array.Empty<CancellableSelectionRequest<UnitData>.InvalidOption>());
            RequestEvent cancellable = doc.Ask(defender);
            doc.Answer(cancellable, "{}");

            RequestEvent yesNo = doc.Ask(new YesNoRequest(PlayerB, "Use it?"));
            doc.Answer(yesNo, "true");

            var timeline = new ReplayTimeline(doc.Build());

            Assert.That(timeline.FoldedBeforeFirstStep.Select(f => f.Request), Is.EqualTo(new[] { forcedFirst }));
            Assert.That(timeline.Steps.Select(s => s.Decision.Request), Is.EqualTo(new[] { step, orBack, cancellable, yesNo }));
            Assert.That(timeline.Steps[0].Folded.Select(f => f.Request), Is.EqualTo(new[] { forced }));
            Assert.That(DefaultStepFilter.CountOptions(cancellable), Is.EqualTo(2), "a CancellableResult reply offers Back");
            Assert.That(DefaultStepFilter.CountOptions(yesNo), Is.Null, "not a pick from a list");

            // The two lists named otherwise: an ability's effects, and the deployment zones.
            var effectRequest = new ChooseAbilityEffectRequest(PlayerA, "Pick an effect", "Rule", "Bearer",
                new[] { new ChooseAbilityEffectRequest.EffectOption("Only", "") });
            RequestEvent effect = new RequestEvent(effectRequest.TaskID, PlayerA, typeof(ChooseAbilityEffectRequest).FullName!,
                "System.Int32", "Choosing an Effect", JsonConvert.SerializeObject(effectRequest, WireJsonSettings.For(doc.Store)));
            Assert.That(DefaultStepFilter.CountOptions(effect), Is.EqualTo(1));
            RequestEvent zone = new RequestEvent(new TaskID(Guid.NewGuid()), PlayerA,
                "FDG.StageResolution.Requests.ChooseDeploymentZoneRequest", "x", "Choosing a Deployment Zone",
                "{\"AvailableZones\":{\"$type\":\"list\",\"$values\":[{\"TypeID\":{\"ID\":11},\"Index\":0,\"Generation\":1}]}}");
            Assert.That(DefaultStepFilter.CountOptions(zone), Is.EqualTo(1));
            Assert.That(timeline.Steps.Select(s => s.Index), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void RoundActivationAndUnit_ComeFromTheLatestMarkers()
        {
            var doc = new DocumentBuilder();
            DataReference unit = new DataReference { TypeID = new TypeID(8), Index = 3, Generation = 1 };

            doc.Add(new MarkerEvent(EReplayMarker.GameStart));
            doc.Step("deploy here", "or there");
            doc.Add(new MarkerEvent(EReplayMarker.RoundStart) { Round = 1 });
            doc.Add(new MarkerEvent(EReplayMarker.ActivationStart) { Round = 1, Activation = 1, PlayerId = PlayerA });
            doc.Step("Warriors", "Gunners");
            doc.Add(new MarkerEvent(EReplayMarker.ActivationStart) { Round = 1, Activation = 1, PlayerId = PlayerA, UnitRef = unit });
            doc.Step("Move", "Shoot");
            doc.Add(new MarkerEvent(EReplayMarker.ActivationStart) { Round = 1, Activation = 2, PlayerId = PlayerB });
            RequestEvent bravo = doc.Ask(Choice(PlayerB, new[] { "A", "B" }));
            doc.Answer(bravo);
            doc.Add(new MarkerEvent(EReplayMarker.RoundStart) { Round = 2 });
            doc.Step("A", "B");

            IReadOnlyList<ReplayStep> steps = new ReplayTimeline(doc.Build()).Steps;

            Assert.That(steps.Select(s => (s.Round, s.Activation)), Is.EqualTo(new (int?, int?)[]
            {
                (null, null), (1, 1), (1, 1), (1, 2), (2, null),
            }), "a repeat ActivationStart names the unit; it is not a new activation");
            Assert.That(steps.Select(s => s.Decision.ActivatingUnit), Is.EqualTo(new DataReference?[] { null, null, unit, null, null }));
            Assert.That(steps[3].PlayerId, Is.EqualTo(PlayerB));
            Assert.That(steps.Select(s => s.Controller), Is.EqualTo(new EReplayController?[]
            {
                EReplayController.Human, EReplayController.Human, EReplayController.Human, EReplayController.Ai, EReplayController.Human,
            }));
            Assert.That(steps[1].Summary, Is.EqualTo(steps[1].DisplayName), "undescribed steps read as their display name");
        }

        [Test]
        public void AResumedMarker_SplitsSnapshotEligibility()
        {
            var doc = new DocumentBuilder();
            doc.Add(new MarkerEvent(EReplayMarker.GameStart));
            SnapshotEvent first = doc.Add(new SnapshotEvent(0, "{}"));
            RequestEvent before = doc.Step("A", "B");
            doc.Add(new MarkerEvent(EReplayMarker.Resumed) { Round = 3 });
            SnapshotEvent resumed = doc.Add(new SnapshotEvent(1, "{}"));
            RequestEvent after = doc.Step("A", "B");
            doc.Add(new MarkerEvent(EReplayMarker.Resumed) { Round = 4 });
            RequestEvent orphaned = doc.Step("A", "B");

            var timeline = new ReplayTimeline(doc.Build());

            Assert.That(timeline.SnapshotFor(before.Seq), Is.SameAs(first));
            Assert.That(timeline.SnapshotFor(after.Seq), Is.SameAs(resumed), "the resume's own snapshot, never the one before it");
            Assert.That(timeline.SnapshotFor(orphaned.Seq), Is.Null, "a resume with no snapshot after it cuts off what came before");
            Assert.That(timeline.SnapshotFor(first.Seq - 1), Is.Null, "nothing to rebuild from before the first snapshot");
            Assert.That(timeline.Steps[1].Round, Is.EqualTo(3), "the Resumed marker carries the round");
        }

        [Test]
        public void EventsOutOfOrder_AreRefused()
        {
            var events = new List<ReplayEvent>
            {
                new LogEvent("one") { Seq = 5 },
                new LogEvent("two") { Seq = 4 },
            };

            Assert.That(() => new ReplayTimeline(new ReplayDocument(Header(), events)),
                Throws.TypeOf<ReplayFormatException>().With.Message.Contains("out of order"));
        }

        [Test]
        public void IndexOfSeq_FindsTheEvent_OrTheNextOne()
        {
            var events = new List<ReplayEvent>
            {
                new LogEvent("a") { Seq = 0 },
                new LogEvent("b") { Seq = 2 },
                new LogEvent("c") { Seq = 3 },
            };
            var timeline = new ReplayTimeline(new ReplayDocument(Header(), events));

            Assert.That(timeline.IndexOfSeq(2), Is.EqualTo(1));
            Assert.That(timeline.IndexOfSeq(1), Is.EqualTo(1));
            Assert.That(timeline.IndexOfSeq(9), Is.EqualTo(3));
        }

        // --- The reader's build stamp (#454) -------------------------------------------------------------

        private static ReplayDocument RoundTrip(ReplayHeader header)
        {
            using var stream = new MemoryStream();
            ReplayWriter.WriteGzip(header, new[] { new LogEvent("hello") { Seq = 0 } }, stream);
            stream.Position = 0;
            return ReplayReader.ReadGzip(stream);
        }

        [Test]
        public void ABuildMismatch_IsAWarning_NotAnError()
        {
            ReplayHeader other = Header();
            other.BuildVersion = "0.0.1+opr.3.5.1.git-abc1234-20260101";

            ReplayDocument read = RoundTrip(other);

            Assert.That(read.Events, Has.Count.EqualTo(1));
            Assert.That(read.Warnings, Has.Count.EqualTo(1));
            Assert.That(read.Warnings[0], Does.Contain("0.0.1+opr.3.5.1.git-abc1234-20260101").And.Contain(ReplayFormat.BuildVersion));
            Assert.That(read.Warnings[0].All(c => c < 128), Is.True, "player-facing text is ASCII");
            Assert.That(RoundTrip(Header()).Warnings, Is.Empty, "the same build reads its own replays silently");
        }

        [Test]
        public void DevBuilds_FromDifferentCommits_AreTheSameBuild()
        {
            // #455: the SDK appends the commit to every stamp; between dev builds it is not a mismatch.
            Assert.That(ReplayReader.SameBuild("0.3.0-dev+34060e7d", "0.3.0-dev+ede90fc2"), Is.True);
            Assert.That(ReplayReader.SameBuild("0.3.0-dev", "0.3.0-dev+ede90fc2"), Is.True);
            Assert.That(ReplayReader.SameBuild("0.2.0-dev+34060e7d", "0.3.0-dev+34060e7d"), Is.False, "a version bump still warns");
            Assert.That(ReplayReader.SameBuild("0.2.0+opr.3.5.1.git-abc-1", "0.2.0+opr.3.5.1.git-def-2"), Is.False, "release stamps match exactly");
            Assert.That(ReplayReader.SameBuild("", "0.3.0-dev+ede90fc2"), Is.False);
        }
    }
}
