using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.TempVisuals;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042 Phase 7h: proves movement is now an INVOCABLE
    // subsystem and that an InvokeTriggeredMove operation flows through the real MovementExecutor.
    //  - TryMove_* drive the movement primitive directly (commit on success, no mutation on a
    //    rejected over-budget move).
    //  - Vanguard_ThroughSeam drives the rule end-to-end: ResolveAbility -> OperationExecutor ->
    //    GameOperationServices -> MovementExecutor, faking only the player's destination choice via
    //    a canned path requester (mirrors how WoundRuleIntegrationTests fakes wound assignment).
    [TestFixture]
    public class TriggeredMoveRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void TryMove_WithinBudget_CommitsNewPositions()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            // Translate the whole unit +3" along x — within the 9" budget, coherency preserved.
            List<ModelMoveEntry> paths = TranslatePaths(unit, dx: 3f, dz: 0f);

            bool moved = MovementExecutor.TryMove(ctx, unit, paths, maxInches: 9f, out var errors);

            Assert.That(moved, Is.True, "a 3\" move is within the 9\" budget");
            Assert.That(errors, Is.Empty);
            AssertModelAt(unit, 0, 3f, 0f);
            AssertModelAt(unit, 1, 3f, 1f);
        }

        [Test]
        public void TryMove_OverBudget_RejectsAndLeavesPositionsUnchanged()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            List<ModelMoveEntry> paths = TranslatePaths(unit, dx: 20f, dz: 0f);

            bool moved = MovementExecutor.TryMove(ctx, unit, paths, maxInches: 9f, out var errors);

            Assert.That(moved, Is.False, "a 20\" move exceeds the 9\" budget");
            Assert.That(errors, Is.Not.Empty);
            AssertModelAt(unit, 0, 0f, 0f);
            AssertModelAt(unit, 1, 0f, 1f);
        }

        [Test]
        public async Task Vanguard_ThroughSeam_RepositionsTheUnit()
        {
            var requester = new CannedMovePathRequester(dx: 5f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f), new Position(0f, 1f));

            // Vanguard: after deploying, may move up to 9" once per game (test 17's ability shape).
            var ability = new ActivatedAbility(EHookID.Deployment_OnUnitDeployed, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.TriggeredMove(MaxInches: 9f, IsOptional: true), new Condition.Always());

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(
                new AbilityOffer(unit.GetValue(), "Vanguard", ability), new[] { unit.GetValue() });

            await OperationExecutor.Execute(ops, new GameOperationServices(ctx));

            Assert.That(requester.Captured, Is.Not.Null, "the seam issued a movement-path request");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(9f).Within(0.001f),
                "the 9\" triggered-move budget reaches the resolver");
            AssertModelAt(unit, 0, 5f, 0f);
            AssertModelAt(unit, 1, 5f, 1f);
        }

        private static List<ModelMoveEntry> TranslatePaths(DataBinding<UnitData> unit, float dx, float dz)
        {
            var entries = new List<ModelMoveEntry>();
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Position start = model.GetValue().PositionBinding.GetValue();
                entries.Add(new ModelMoveEntry(model,
                    new List<Position> { new Position(start.x + dx, start.z + dz) }));
            }
            return entries;
        }

        private static void AssertModelAt(DataBinding<UnitData> unit, int index, float x, float z)
        {
            Position pos = unit.GetValue().ModelBindings[index].GetValue().PositionBinding.GetValue();
            Assert.That(pos.x, Is.EqualTo(x).Within(0.001f), $"model {index} x");
            Assert.That(pos.z, Is.EqualTo(z).Within(0.001f), $"model {index} z");
        }

        private DataBinding<UnitData> MakeUnit(params Position[] modelPositions)
        {
            var playerID = new PlayerID(System.Guid.NewGuid());

            var modelBindings = new List<DataBinding<ModelData>>(modelPositions.Length);
            foreach (Position pos in modelPositions)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: new List<Weapon>(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(playerID, "Vanguards",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: modelBindings);
            DataBinding<UnitData> unitBinding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            // Wrap the unit in an army, as production always does — that's how GameOperationServices
            // resolves an IUnit back to its live data binding.
            var army = new ArmyData(playerID, new List<DataBinding<UnitData>> { unitBinding });
            _store.Create(army);

            return unitBinding;
        }
    }

    // Returns a canned move that translates every model in the requested unit by a fixed delta,
    // standing in for the player choosing a reposition destination.
    internal sealed class CannedMovePathRequester : IPlayerRequestByID
    {
        private readonly float _dx;
        private readonly float _dz;
        public DefineMovementPathRequest? Captured { get; private set; }

        public CannedMovePathRequester(float dx, float dz)
        {
            _dx = dx;
            _dz = dz;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest moveRequest)
            {
                Captured = moveRequest;
                var entries = new List<ModelMoveEntry>();
                foreach (DataBinding<ModelData> model in moveRequest.UnitDataBinding.GetValue().ModelBindings)
                {
                    Position start = model.GetValue().PositionBinding.GetValue();
                    entries.Add(new ModelMoveEntry(model,
                        new List<Position> { new Position(start.x + _dx, start.z + _dz) }));
                }
                return Task.FromResult((TReply)(object)entries);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Minimal IGameContext with a real RuleEvaluator and an injectable requester (mirrors WoundTestContext).
    internal sealed class TriggeredMoveTestContext : IGameContext
    {
        public ITextOutput TextOutput { get; } = new EmptyTextOutput();
        public IDiceRoller DiceRoller { get; }
        public RuleEvaluator RuleEvaluator { get; }
        public IPlayerRequestByID PlayerRequester { get; }
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public ITempVisualDrawer TempVisualDrawer { get; } = new NullTempVisualDrawer();
        public GameSettings Settings { get; } = GameSettings.GetDefault();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        public TriggeredMoveTestContext(GameDataStore store, IPlayerRequestByID requester)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            PlayerRequester = requester;
            DiceRoller = new FixedDiceRoller(4);
            RuleEvaluator = new RuleEvaluator(DiceRoller);
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
        public void NotifyGameEnded(string result) { }
    }
}
