using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #341: the move beat carries the attitude at each waypoint so the front end can turn the model as it
    // glides, instead of snapping it to its final facing before the animation even starts. That is what makes
    // the placement rule watchable: a rotation dialled in for one node no longer applies to the ground before
    // it, so the turn has to happen visibly somewhere, and it happens across the leg into that node.
    //
    // The pairing is the thing worth pinning. Facings[0] is the model's PRE-MOVE resting attitude, matching
    // Waypoints[0] (its start position) - both have to be captured before MovementExecutor.CommitPositions
    // snaps the model to where it ended up.
    [TestFixture]
    public class MoveBeatFacingsTests
    {
        private GameDataStore _store = null!;
        private readonly PlayerID _player = new PlayerID(System.Guid.NewGuid());

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task Beat_PairsOneAttitudePerPolylinePoint_OpeningWithTheRestingFacing()
        {
            var resting = new Float2(0f, 1f);
            var atCorner = new Float2(1f, 0f);
            var atEnd = new Float2(0f, -1f);

            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), resting);
            DataBinding<ModelData> model = unit.GetValue().ModelBindings[0];

            UnitMovedBeat beat = await RunMove(unit, new ModelMoveEntry(model,
                new List<Position> { new Position(0f, 6f), new Position(6f, 6f) },
                new List<Float2> { atCorner, atEnd }));

            ModelMove move = beat.Moves.Single();
            Assert.That(move.Waypoints, Has.Count.EqualTo(3), "start + two waypoints");
            Assert.That(move.Facings, Is.Not.Null);
            Assert.That(move.Facings, Has.Count.EqualTo(3), "one attitude per polyline point");

            Assert.That(move.Facings![0], Is.EqualTo(resting),
                "the glide has to START from the pose the model was actually standing in - read before "
                + "CommitPositions overwrites it, or the model would jump to its final facing on frame one");
            Assert.That(move.Facings[1], Is.EqualTo(atCorner));
            Assert.That(move.Facings[2], Is.EqualTo(atEnd));
        }

        [Test]
        public async Task Beat_MoveWithNoPerWaypointFacings_CarriesNone()
        {
            // AI moves, aircraft and holds submit no facings: nothing turns, so there is nothing to
            // interpolate and the front end just draws the model's own facing for the whole glide.
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Float2(0f, 1f));
            DataBinding<ModelData> model = unit.GetValue().ModelBindings[0];

            UnitMovedBeat beat = await RunMove(unit,
                new ModelMoveEntry(model, new List<Position> { new Position(0f, 5f) }));

            Assert.That(beat.Moves.Single().Facings, Is.Null);
        }

        [Test]
        public async Task Beat_FewerFacingsThanWaypoints_PadsRatherThanMispairs()
        {
            // Defensive: the executor already tolerates a short facing list, and a beat that mispaired
            // facings with waypoints would spin the model on screen rather than merely under-rotate it.
            var resting = new Float2(0f, 1f);
            var turned = new Float2(1f, 0f);
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), resting);
            DataBinding<ModelData> model = unit.GetValue().ModelBindings[0];

            UnitMovedBeat beat = await RunMove(unit, new ModelMoveEntry(model,
                new List<Position> { new Position(0f, 4f), new Position(0f, 8f) },
                new List<Float2> { turned }));

            ModelMove move = beat.Moves.Single();
            Assert.That(move.Facings, Has.Count.EqualTo(move.Waypoints.Count));
            Assert.That(move.Facings![0], Is.EqualTo(resting));
            Assert.That(move.Facings[1], Is.EqualTo(turned));
            Assert.That(move.Facings[2], Is.EqualTo(turned), "padded with the last known attitude");
        }

        // Runs the real ExecuteMoveStage over one submitted path and returns the move beat it presented.
        private async Task<UnitMovedBeat> RunMove(DataBinding<UnitData> unit, ModelMoveEntry entry)
        {
            var sink = new RecordingPresentationSink();
            var ctx = new WoundTestContext(_store, new NoRequester(), presenter:
                new LocalPresenter(sink, new InstantPresentationClock()));

            var moveContext = new MovementActionContext(ctx, unit);
            moveContext.SubmitValidPathTemplate(new List<ModelMoveEntry> { entry });

            var stage = new ExecuteMoveStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnMoveExecuted.Bind("done");
            await stage.Enter(moveContext);

            return sink.Beats.OfType<UnitMovedBeat>().Single();
        }

        private DataBinding<UnitData> MakeUnit(Position at, Float2 facing)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
            model.SetFacing(facing);
            var modelBindings = new List<DataBinding<ModelData>>
                { _store.GetDataBinding<ModelData>(_store.Create(model)) };

            var unit = new UnitData(_player, "Bikers", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private sealed class NoRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : StageResolution.IStageTaskRequest<TReply>
                => throw new System.InvalidOperationException(
                    "ExecuteMoveStage asks the player nothing: " + request!.GetType().Name);
        }
    }
}
