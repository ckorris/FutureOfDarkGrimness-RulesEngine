using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class DangerousTerrainWoundTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public async Task RollOf1_CrossesDangerous_DealsOneWound()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2), from: new Position(0, 0), to: new Position(8, 0));
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 1, terrain: dangerous, paths: paths);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore + 1));
        }

        [Test]
        public async Task RollOf2_CrossesDangerous_NoWound()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2), from: new Position(0, 0), to: new Position(8, 0));
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 2, terrain: dangerous, paths: paths);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        [Test]
        public async Task PathMissesDangerous_NoWound()
        {
            // Model moves at z=5, well above the dangerous zone (z -2..2).
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            var move = new ModelMoveEntry(model, new List<Position> { new Position(8, 5) });
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 1, terrain: dangerous, paths: new List<ModelMoveEntry> { move });

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        [Test]
        public async Task NoDangerousTerrain_NoWound()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2), from: new Position(0, 0), to: new Position(8, 0));
            var noDangerous = new List<ITerrain> { new TerrainData(ETerrainType.Cover, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 1, terrain: noDangerous, paths: paths);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        [Test]
        public async Task NoPaths_NoWound()
        {
            var model = MakeModel(new Position(0, 0));
            var unit = MakeUnit(new List<DataBinding<ModelData>> { model });
            float woundsBefore = model.GetValue().WoundsDealt;
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            var ctx = new TestGameContext(_store, new FixedDiceRoller(1));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var movCtx = new StubMovementContext(ctx, unit, paths: null, terrain: dangerous);
            await stage.Enter(movCtx);
            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        // ── #299: a dangerous-terrain casualty animates its death ───────────────────────────────────────

        // The bug: the batch dealt its wounds and presented only the dice row, so a model killed by
        // dangerous terrain went straight from alive to hidden - the front-end drops any model that is
        // dead in state with no death beat registered, so it simply vanished mid-move.
        [Test]
        public async Task DangerousTerrainKill_PresentsDeathBeat()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2),
                from: new Position(0, 0), to: new Position(8, 0)); // 1-wound model
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            RecordingPresentationSink sink = await RunStage(diceValue: 1, terrain: dangerous, paths: paths);

            Assert.That(model.GetValue().GetIsDead(), Is.True, "a 1 takes the 1-wound model's last wound.");
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Count(), Is.EqualTo(1),
                "a model killed by dangerous terrain animates its death exactly once.");
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Single().Model.ID, Is.EqualTo(model.GetValue().ID.ID));
        }

        // The dice row is read BEFORE the casualty drops - the player sees what killed it.
        [Test]
        public async Task DangerousTerrainKill_DiceBeatPrecedesTheDeath()
        {
            var (_, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2),
                from: new Position(0, 0), to: new Position(8, 0));
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            RecordingPresentationSink sink = await RunStage(diceValue: 1, terrain: dangerous, paths: paths);

            int dice = sink.Beats.FindIndex(b => b is DiceRolledBeat);
            int death = sink.Beats.FindIndex(b => b is ModelDiedBeat);
            Assert.That(dice, Is.GreaterThanOrEqualTo(0), "the batched dangerous-terrain roll is presented.");
            Assert.That(death, Is.GreaterThan(dice), "the roll is read before its casualty falls.");
        }

        // A model that survives its wound flinches instead of dying - and is NOT reported as a death.
        [Test]
        public async Task DangerousTerrainWound_OnSurvivor_PresentsFlinchNotDeath()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0), tough: 3);
            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(model, new List<Position> { new Position(8, 0) }),
            };
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            RecordingPresentationSink sink = await RunStage(diceValue: 1, terrain: dangerous, paths: paths);

            Assert.That(model.GetValue().GetIsDead(), Is.False);
            Assert.That(sink.Beats.OfType<ModelWoundedBeat>().Count(), Is.EqualTo(1), "a survivor flinches.");
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Any(), Is.False);
        }

        // A safe roll animates nothing at all - only the dice row is presented.
        [Test]
        public async Task DangerousTerrain_SafeRoll_PresentsNoCasualtyBeats()
        {
            var (_, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2),
                from: new Position(0, 0), to: new Position(8, 0));
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            RecordingPresentationSink sink = await RunStage(diceValue: 4, terrain: dangerous, paths: paths);

            Assert.That(sink.Beats.OfType<DiceRolledBeat>().Count(), Is.EqualTo(1));
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Any(), Is.False);
            Assert.That(sink.Beats.OfType<ModelWoundedBeat>().Any(), Is.False);
        }

        // The roll stage must leave every model untouched: the wounds land only once, at ExecuteMoveStage,
        // so a model is never dead in state while the front-end has no death beat for it (and, because the
        // roll stage no longer applies anything, no wound can be dealt twice).
        [Test]
        public async Task RollStage_LeavesWoundsPending_UntilResolved()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2),
                from: new Position(0, 0), to: new Position(8, 0));
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            var ctx = new TestGameContext(_store, new FixedDiceRoller(1));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var unit = MakeUnit(paths.Select(p => p.Model).ToList());
            var movCtx = new StubMovementContext(ctx, unit, paths, dangerous);

            await stage.Enter(movCtx);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(0f),
                "the roll stage rolls but does not wound - the model is still standing for its move.");
            Assert.That(movCtx.PendingDangerousTerrain.PendingWounds.Count, Is.EqualTo(1),
                "the wound it rolled is held pending.");

            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(1f), "resolving lands it exactly once.");
        }

        // ── #153: "counts as being in Dangerous Terrain" ────────────────────────────────────────────────

        // The granted rule forces every moving model to test, even with no dangerous terrain on the table.
        [Test]
        public async Task CountsAsDangerous_NoDangerousOnTable_MovingModelStillTests()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(model, new List<Position> { new Position(8, 0) }),
            };
            var unit = MakeUnit(new List<DataBinding<ModelData>> { model });
            unit.GetValue().AttachRuleDefinition(new Rules.Dispatch.ResolvedRule("Cursed Ground", CountsAsDangerousRule));
            float woundsBefore = model.GetValue().WoundsDealt;

            var ctx = new TestGameContext(_store, new FixedDiceRoller(1));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var movCtx = new StubMovementContext(ctx, unit, paths, new List<ITerrain>());
            await stage.Enter(movCtx);
            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore + 1),
                "counts-as-dangerous forces the test on a roll of 1, even with no dangerous terrain");
        }

        // Ignoring all terrain (Flying) waives the counted-as effect like the real one.
        [Test]
        public async Task CountsAsDangerous_FlyingIgnoresIt()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(model, new List<Position> { new Position(8, 0) }),
            };
            var unit = MakeUnit(new List<DataBinding<ModelData>> { model });
            unit.GetValue().AttachRuleDefinition(new Rules.Dispatch.ResolvedRule("Cursed Ground", CountsAsDangerousRule));
            unit.GetValue().AttachRuleDefinition(new Rules.Dispatch.ResolvedRule("Flying", Rules.Dispatch.CoreRuleCatalog.Flying));
            float woundsBefore = model.GetValue().WoundsDealt;

            var ctx = new TestGameContext(_store, new FixedDiceRoller(1));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var movCtx = new StubMovementContext(ctx, unit, paths, new List<ITerrain>());
            await stage.Enter(movCtx);
            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        private static readonly Rules.Definitions.SpecialRuleDefinition CountsAsDangerousRule =
            new("Cursed Ground",
                new List<Rules.Definitions.HookEntry>
                {
                    new Rules.Definitions.HookEntry(Rules.Foundation.EHookID.Movement_OnMoveThroughTerrain,
                        new Rules.Definitions.Condition.Always(),
                        new Rules.Definitions.Effect.CountAsInTerrain(Rules.Definitions.ECountAsTerrain.Dangerous),
                        Rules.Foundation.ELifetime.ThisActivation),
                },
                new List<Rules.Definitions.ActivatedAbility>());

        // Helpers

        // The stage only ROLLS the test; the wounds are landed after the move beat by ExecuteMoveStage.
        // These tests exercise both halves through the same seam the real flow uses, so the assertions
        // stay about "does crossing dangerous terrain wound the model".
        private async Task<RecordingPresentationSink> RunStage(int diceValue, List<ITerrain> terrain,
            List<ModelMoveEntry> paths)
        {
            var sink = new RecordingPresentationSink();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(diceValue),
                presenter: new LocalPresenter(sink, new Presentation.InstantPresentationClock()));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var unit = MakeUnit(paths.Select(p => p.Model).ToList());
            var movCtx = new StubMovementContext(ctx, unit, paths, terrain);
            await stage.Enter(movCtx);
            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);
            return sink;
        }

        private (DataBinding<ModelData> model, List<ModelMoveEntry> paths) MakePathThrough(
            RectangularZone dangerousZone, Position from, Position to)
        {
            DataBinding<ModelData> model = MakeModel(from);
            var move = new ModelMoveEntry(model, new List<Position> { to });
            return (model, new List<ModelMoveEntry> { move });
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition, int tough = 1)
        {
            var modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            if (tough > 1) modelData.SetMaxWounds(tough);
            var reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }

        private DataBinding<UnitData> MakeUnit(List<DataBinding<ModelData>> modelBindings)
        {
            var playerID = new PlayerID(Guid.NewGuid());
            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            var reference = _store.Create(unit);
            return _store.GetDataBinding<UnitData>(reference);
        }
    }

    // Stub IMovementActionContext for DangerousTerrainWoundTests.
    internal class StubMovementContext : IMovementActionContext
    {
        private readonly IGameContext _gameContext;
        private readonly DataBinding<UnitData> _unit;
        private readonly List<ModelMoveEntry>? _paths;
        private readonly List<ITerrain> _terrain;

        public IGameContext GameContext => _gameContext;
        public DataBinding<UnitData> MovingUnit => _unit;
        public float MaxAdvanceDistance => 12f;
        public float MaxRushDistance => 12f;
        public float MaxChargeDistance => 12f;
        public float MaxModelAdvanceDistance => MaxAdvanceDistance;
        public bool TryGetModelMoveBudget(IModel model, out float advance, out float rush, out float charge)
        {
            advance = MaxAdvanceDistance; rush = MaxRushDistance; charge = MaxChargeDistance; return true;
        }
        public List<ITerrain> RelevantTerrain => _terrain;

        public StubMovementContext(IGameContext ctx, DataBinding<UnitData> unit,
            List<ModelMoveEntry>? paths, List<ITerrain> terrain)
        {
            _gameContext = ctx;
            _unit = unit;
            _paths = paths;
            _terrain = terrain;
        }

        public bool TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths)
        {
            if (_paths == null) { paths = null!; return false; }
            paths = _paths;
            return true;
        }

        public bool TryGetMovementDistance(out float distance) { distance = 0f; return false; }
        public void SubmitValidPathTemplate(List<ModelMoveEntry> paths) { }
        public bool MoveCancelled { get; private set; }
        public void RegisterMoveCancelled() => MoveCancelled = true;

        public MovementExecutor.DangerousTerrainResult PendingDangerousTerrain { get; private set; }
            = MovementExecutor.DangerousTerrainResult.None;

        public IReadOnlyList<ModelMoveEntry>? PlannedMove => null;

        public string? MustEndAbleToAttackRule => null;
        public void RegisterDangerousTerrainRoll(MovementExecutor.DangerousTerrainResult result)
            => PendingDangerousTerrain = result;
    }
}
