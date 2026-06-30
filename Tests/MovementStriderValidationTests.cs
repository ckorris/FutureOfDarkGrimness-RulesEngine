using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #102 — Strider waives the difficult-terrain move cap, and the capability query that drives it. A move
    // crossing Difficult terrain is normally limited to DIFFICULT_TERRAIN_MOVE_CAP_INCHES (6"); a Strider
    // unit ignores that cap. Mirrors MovementFlyOverValidationTests (the canMoveThroughEnemies precedent the
    // ignoresDifficultTerrain flag parallels). Models are radius 0.75".
    [TestFixture]
    public class MovementStriderValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // Difficult terrain spanning x=2..4; an 8" straight move crosses it, exceeding the 6" cap.
        private static List<ITerrain> Difficult()
            => new List<ITerrain> { new TerrainData(ETerrainType.Difficult, new RectangularZone(2, 4, -2, 2)) };

        [Test]
        public void DifficultCrossingBeyondCap_RejectedWithoutStrider()
        {
            // Control: 8" through difficult terrain, no Strider — capped, so rejected.
            ModelMoveEntry move = Move(new Position(0, 0), new Position(8, 0));

            bool ok = Charge(move, ignoresDifficultTerrain: false, out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit), Is.True);
        }

        [Test]
        public void DifficultCrossingBeyondCap_AllowedWithStrider()
        {
            // Same over-cap difficult crossing, but the mover is a Strider — the cap is waived.
            ModelMoveEntry move = Move(new Position(0, 0), new Position(8, 0));

            bool ok = Charge(move, ignoresDifficultTerrain: true, out var errors);

            Assert.That(ok, Is.True, Why(errors));
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit), Is.False);
        }

        [Test]
        public void NoChargeOverload_DifficultCrossing_HonorsStrider()
        {
            // The consolidation / executor overload (no charge semantics) applies the same waiver.
            ModelMoveEntry move = Move(new Position(0, 0), new Position(8, 0));

            bool capped = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, NoEnemies(), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false, Difficult(), out _);
            bool waived = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 12f, NoEnemies(), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: true, ignoresImpassibleTerrain: false, Difficult(), out var errors);

            Assert.That(capped, Is.False, "Without Strider the consolidation move over the cap is rejected.");
            Assert.That(waived, Is.True, Why(errors));
        }

        [Test]
        public void IgnoresDifficultTerrain_TrueForStriderUnit()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(withStrider: true);

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(unit.GetValue(), ctx.RuleEvaluator), Is.True);
        }

        [Test]
        public void IgnoresDifficultTerrain_FalseForPlainUnit()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(withStrider: false);

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(unit.GetValue(), ctx.RuleEvaluator), Is.False);
        }

        // The Army-Creator picker is derived from CoreRuleCatalog.All; before #102 Strider was uncatalogued
        // and so unpickable (and a no-op). Guards that it's now both registered and resolvable.
        [Test]
        public void Strider_IsCatalogued_AndResolvable()
        {
            Assert.That(CoreRuleCatalog.All.Any(r => r.Name == "Strider"), Is.True,
                "Strider must be in CoreRuleCatalog.All so the Army Creator can offer it.");

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            Assert.That(resolver.TryResolve("Strider", out ResolvedRule resolved), Is.True);
            Assert.That(resolved.Definition, Is.SameAs(CoreRuleCatalog.Strider));
        }

        private static List<EnemyModelFootprint> NoEnemies() => new List<EnemyModelFootprint>();

        private ModelMoveEntry Move(Position from, Position to)
            => new ModelMoveEntry(MakeModel(from), new List<Position> { to });

        // The full Move-action (charge) overload DefinePathStage uses; no enemies, so only the terrain cap bites.
        private bool Charge(ModelMoveEntry move, bool ignoresDifficultTerrain, out List<ReasonForInvalidMove> errors)
            => MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f, NoEnemies(),
                canMoveThroughEnemies: false, ignoresDifficultTerrain, ignoresImpassibleTerrain: false, Difficult(), out errors);

        private static string Why(List<ReasonForInvalidMove> errors)
            => "Unexpected errors: " + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            ModelData modelData = new ModelData(0.75f, new List<Weapon>(), initialPosition, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(modelData));
        }

        private DataBinding<UnitData> MakeUnit(bool withStrider)
        {
            var modelBindings = new List<DataBinding<ModelData>> { MakeModel(new Position(0, 0)) };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (withStrider)
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Strider", CoreRuleCatalog.Strider));
            return binding;
        }
    }
}
