using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #090 regression test for the Strafing fly-over fix: the WHOLE point is that a Strafing unit can again
    // path through an enemy in a real game. Before #090, DefinePathStage's #011 enemy-check rejected the
    // very move-through that triggers Strafing (the StrafingRuleIntegrationTests passed only because they
    // bypass DefinePathStage). This drives the real stage end-to-end.
    [TestFixture]
    public class MovementFlyOverIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task StrafingUnit_PathsThroughEnemy_DefinePathStageAccepts()
        {
            DataBinding<UnitData> mover = MakeUnit(withStrafing: true, new Position(0f, 0f));
            MakeEnemy(new Position(5f, 0f)); // directly on the (0,0)→(10,0) path

            // Should complete without throwing — the fly-over permission waives the pass-through block.
            await RunDefinePath(mover, destination: new Position(10f, 0f));
        }

        [Test]
        public void PlainUnit_PathsThroughEnemy_DefinePathStageRejects()
        {
            DataBinding<UnitData> mover = MakeUnit(withStrafing: false, new Position(0f, 0f));
            MakeEnemy(new Position(5f, 0f));

            Assert.That(async () => await RunDefinePath(mover, new Position(10f, 0f)),
                Throws.TypeOf<RequestResponseInvalidException>(),
                "Without fly-over, pathing through an enemy is rejected by the authoritative stage check.");
        }

        private async Task RunDefinePath(DataBinding<UnitData> mover, Position destination)
        {
            var move = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { destination })
            };
            var ctx = new WoundTestContext(_store, new FixedMoveRequester(move));
            var moveContext = new MovementActionContext(ctx, mover);

            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("done");
            await stage.Enter(moveContext);
        }

        private DataBinding<UnitData> MakeUnit(bool withStrafing, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
                modelBindings.Add(_store.GetDataBinding<ModelData>(
                    _store.Create(new ModelData(0.75f, new List<Weapon>(), pos, _store))));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Mover",
                quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (withStrafing)
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Strafing", CoreRuleCatalog.Strafing));
            _store.Create(new ArmyData(binding.GetValue().PlayerID, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private void MakeEnemy(Position pos)
        {
            var modelBinding = _store.GetDataBinding<ModelData>(
                _store.Create(new ModelData(0.75f, new List<Weapon>(), pos, _store)));
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Enemy",
                quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(binding.GetValue().PlayerID, new List<DataBinding<UnitData>> { binding }));
        }

        // Returns a preset move for the DefineMovementPathRequest DefinePathStage issues.
        private sealed class FixedMoveRequester : IPlayerRequestByID
        {
            private readonly List<ModelMoveEntry> _move;
            public FixedMoveRequester(List<ModelMoveEntry> move) => _move = move;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
                => Task.FromResult((TReply)(object)new Selected<List<ModelMoveEntry>>(_move));
        }
    }
}
