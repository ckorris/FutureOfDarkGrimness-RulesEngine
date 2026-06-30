using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029 — Flying ignores ALL terrain (impassible + difficult cap + dangerous) and moves through units. These
    // tests pin the impassible-terrain waiver (the new validation flag) and the IgnoresAllTerrain query that
    // distinguishes Flying (AllTerrain scope) from Strider (DifficultOnly). Models are radius 0.75".
    [TestFixture]
    public class MovementFlyingValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // Impassible terrain x=4..6; a straight (0,0)->(10,0) move sweeps its base through it.
        private static List<ITerrain> Impassible()
            => new List<ITerrain> { new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2)) };

        [Test]
        public void ImpassibleCrossing_BlockedWithoutFlying()
        {
            ModelMoveEntry move = Move(new Position(0, 0), new Position(10, 0));

            bool ok = Validate(move, ignoresImpassibleTerrain: false, out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain), Is.True);
        }

        [Test]
        public void ImpassibleCrossing_AllowedWithFlying()
        {
            ModelMoveEntry move = Move(new Position(0, 0), new Position(10, 0));

            bool ok = Validate(move, ignoresImpassibleTerrain: true, out var errors);

            Assert.That(ok, Is.True, Why(errors));
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughImpassibleTerrain), Is.False);
        }

        [Test]
        public void IgnoresAllTerrain_TrueForFlying_FalseForStriderAndPlain()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());

            Assert.That(MovementRuleQueries.IgnoresAllTerrain(MakeUnit("Flying", CoreRuleCatalog.Flying).GetValue(), ctx.RuleEvaluator),
                Is.True);
            Assert.That(MovementRuleQueries.IgnoresAllTerrain(MakeUnit("Strider", CoreRuleCatalog.Strider).GetValue(), ctx.RuleEvaluator),
                Is.False, "Strider is DifficultOnly — it does not ignore all terrain.");
            Assert.That(MovementRuleQueries.IgnoresAllTerrain(MakeUnit(null, null).GetValue(), ctx.RuleEvaluator),
                Is.False);
        }

        [Test]
        public void Flying_AlsoSatisfiesDifficultCapAndMoveThroughQueries()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> flyer = MakeUnit("Flying", CoreRuleCatalog.Flying);

            // AllTerrain implies the difficult-cap waiver; the second passive grants the move-through-units permission.
            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(flyer.GetValue(), ctx.RuleEvaluator), Is.True);
            Assert.That(MovementRuleQueries.CanMoveThroughEnemies(flyer.GetValue(), ctx.RuleEvaluator), Is.True);
        }

        [Test]
        public void Flying_IsCatalogued_AndResolvable()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            Assert.That(CoreRuleCatalog.All.Any(r => r.Name == "Flying"), Is.True, "Flying must be in All.");
            Assert.That(resolver.TryResolve("Flying", out _), Is.True);
        }

        private static List<EnemyModelFootprint> NoEnemies() => new List<EnemyModelFootprint>();

        private ModelMoveEntry Move(Position from, Position to)
            => new ModelMoveEntry(MakeModel(from), new List<Position> { to });

        // The enemy-aware no-charge overload; no enemies and no difficult terrain, so only the impassible check bites.
        private bool Validate(ModelMoveEntry move, bool ignoresImpassibleTerrain, out List<ReasonForInvalidMove> errors)
            => MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxDistanceInches: 20f, NoEnemies(), canMoveThroughEnemies: false,
                ignoresDifficultTerrain: false, ignoresImpassibleTerrain, Impassible(), out errors);

        private static string Why(List<ReasonForInvalidMove> errors)
            => "Unexpected errors: " + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

        private DataBinding<ModelData> MakeModel(Position position)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), position, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(string? ruleName, SpecialRuleDefinition? def)
        {
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { MakeModel(new Position(0, 0)) });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (ruleName != null && def != null)
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(ruleName, def));
            return binding;
        }
    }
}
