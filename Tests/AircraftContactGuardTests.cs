using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #427 - OPR 3.6 Aircraft: "[...] can't be moved in contact with or charged, and units targeting them get
    // -12" range and -1 to hit rolls." The charge half and the range half were already tested; this file pins
    // the other two clauses that had no test of their own:
    //
    //   * "can't be moved in contact with" on the two moves #417's 3" melee range stretched - the pile-in a
    //     defender makes toward its charger, and the consolidation the survivor makes after the melee. Neither
    //     may END within the 1" standoff of an Aircraft (the band the charge validator already enforces), where
    //     a normal enemy is only a base-contact obstacle;
    //   * "-1 to hit rolls" - through the REAL DetermineHitRollStage, not just the catalog entry.
    [TestFixture]
    public class AircraftContactGuardTests
    {
        private const float Standoff = GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES;

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // --- pile-in --------------------------------------------------------------------------------------------

        [Test]
        public void PileIn_StopsShortOfAnAircraftStandingInTheLane()
        {
            // The defender wants to pile the full 3" toward a charger on its right; an Aircraft's base sits in
            // the lane. A third-party ground unit would stop it at contact (PileInTests); an Aircraft must
            // keep the 1" standoff - "can't be moved in contact with".
            DataBinding<ModelData> defender = MakeModel(new Position(0f, 0f), 0.5f);
            DataBinding<ModelData> charger = MakeModel(new Position(10f, 0f), 0.5f);
            var aircraft = new EnemyModelFootprint(new Position(3f, 0f), 1.0f, unitKey: 0, uncontactable: true,
                baseShape: new CircleBase(1.0f));

            var moves = PileInUtilities.ComputePileInMoves(new[] { charger }, new[] { defender }, terrain: null,
                otherEnemyModels: new[] { aircraft });

            Position end = moves.Count > 0 ? moves[0].NewPosition : defender.GetValue().Position;
            float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                end, new Position(3f, 0f), new CircleBase(0.5f), new CircleBase(1.0f));

            Assert.That(end.x, Is.GreaterThan(0.3f), "it still piles in toward the charger as far as it may");
            Assert.That(gap, Is.GreaterThanOrEqualTo(Standoff - 0.02f),
                $"it must end at least the {Standoff}\" standoff from the Aircraft, not in contact (gap {gap:F3})");
        }

        [Test]
        public void PileIn_AGroundUnitInTheSameLane_IsStillOnlyAContactObstacle()
        {
            // Control: the same lane with an ordinary enemy base - the defender runs up to contact with it.
            DataBinding<ModelData> defender = MakeModel(new Position(0f, 0f), 0.5f);
            DataBinding<ModelData> charger = MakeModel(new Position(10f, 0f), 0.5f);
            var ground = new EnemyModelFootprint(new Position(3f, 0f), 1.0f, unitKey: 0, uncontactable: false,
                baseShape: new CircleBase(1.0f));

            var moves = PileInUtilities.ComputePileInMoves(new[] { charger }, new[] { defender }, terrain: null,
                otherEnemyModels: new[] { ground });

            Position end = moves.Single().NewPosition;
            float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                end, new Position(3f, 0f), new CircleBase(0.5f), new CircleBase(1.0f));
            Assert.That(gap, Is.LessThan(0.1f), $"stops at contact with a ground unit (gap {gap:F3})");
        }

        [Test]
        public void PileIn_AnAircraftOutsideTheLane_DoesNotHoldTheDefenderBack()
        {
            DataBinding<ModelData> defender = MakeModel(new Position(0f, 0f), 0.5f);
            DataBinding<ModelData> charger = MakeModel(new Position(2f, 0f), 0.5f); // 1" b2b: one short step
            var farAircraft = new EnemyModelFootprint(new Position(0f, 9f), 1.0f, unitKey: 0, uncontactable: true,
                baseShape: new CircleBase(1.0f));

            var moves = PileInUtilities.ComputePileInMoves(new[] { charger }, new[] { defender }, terrain: null,
                otherEnemyModels: new[] { farAircraft });

            float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(moves.Single().NewPosition,
                charger.GetValue().Position, 0.5f, 0.5f);
            Assert.That(b2b, Is.LessThanOrEqualTo(0.01f), "base contact with the charger, unhindered");
        }

        // --- consolidation --------------------------------------------------------------------------------------

        [Test]
        public void Consolidation_EndingWithinTheStandoffOfAnAircraft_IsRejected()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), 0.5f);
            var move = new ModelMoveEntry(model, new List<Position> { new Position(3f, 0f) });

            // Aircraft at x=5 (r 0.75): the mover ends 0.75" from it - inside the band.
            bool inside = MovementUtilities.ValidateConsolidationPaths(new List<ModelMoveEntry> { move }, 3f,
                new[] { new EnemyModelFootprint(new Position(5f, 0f), 0.75f, 0, uncontactable: true) },
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false,
                terrain: null, out var errors);

            Assert.That(inside, Is.False, "'can't be moved in contact with' - consolidation may not close on it");
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedTooCloseToEnemy), Is.True);
        }

        [Test]
        public void Consolidation_EndingClearOfTheAircraft_OrNextToAGroundEnemy_IsLegal()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f), 0.5f);
            var move = new ModelMoveEntry(model, new List<Position> { new Position(3f, 0f) });
            var noFriends = (IEnumerable<EnemyModelFootprint>?)null;

            bool clear = MovementUtilities.ValidateConsolidationPaths(new List<ModelMoveEntry> { move }, 3f,
                new[] { new EnemyModelFootprint(new Position(6f, 0f), 0.75f, 0, uncontactable: true) },
                false, false, false, terrain: null, out var clearErrors, noFriends);
            Assert.That(clear, Is.True, "1.75\" clear of the Aircraft is fine: " +
                string.Join(", ", clearErrors.Select(e => e.ErrorReasonType)));

            bool ground = MovementUtilities.ValidateConsolidationPaths(new List<ModelMoveEntry> { move }, 3f,
                new[] { new EnemyModelFootprint(new Position(5f, 0f), 0.75f, 0, uncontactable: false) },
                false, false, false, terrain: null, out var groundErrors, noFriends);
            Assert.That(ground, Is.True, "control: the identical move beside a ground unit is legal: " +
                string.Join(", ", groundErrors.Select(e => e.ErrorReasonType)));
        }

        // --- the footprints the stages build --------------------------------------------------------------------

        [Test]
        public void TheEnemyFootprintsAStageBuilds_TagAnAircraftUncontactable_AndAGroundUnitNot()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> mover = MakeUnit(new Position(0f, 0f), withAircraft: false);
            DataBinding<UnitData> aircraft = MakeUnit(new Position(20f, 0f), withAircraft: true);
            DataBinding<UnitData> ground = MakeUnit(new Position(30f, 0f), withAircraft: false);
            _store.Create(new ArmyData(mover.GetValue().PlayerID, new List<DataBinding<UnitData>> { mover }));
            _store.Create(new ArmyData(new PlayerID(System.Guid.NewGuid()),
                new List<DataBinding<UnitData>> { aircraft, ground }));

            List<EnemyModelFootprint> footprints = MovementUtilities.GetEnemyModelFootprints(mover, ctx);

            Assert.That(footprints.Count(f => f.Uncontactable), Is.EqualTo(1), "only the Aircraft's footprint");
            Assert.That(footprints.Single(f => f.Uncontactable).Center.x, Is.EqualTo(20f));
        }

        // --- -1 to hit ------------------------------------------------------------------------------------------

        [Test]
        public async Task ShootingAnAircraft_IsMinusOneToHit_ThroughTheRealHitRollStage()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(4));
            DataBinding<UnitData> shooter = MakeUnit(new Position(0f, 0f), withAircraft: false);
            DataBinding<UnitData> aircraft = MakeUnit(new Position(0f, 6f), withAircraft: true);
            DataBinding<UnitData> ground = MakeUnit(new Position(0f, 6f), withAircraft: false);
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

            async Task<int> Needed(DataBinding<UnitData> target)
            {
                var stage = new DetermineHitRollStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
                stage.NextStage.Bind("done");
                var metadata = new CombatMetadata(ctx, shooter, target, rifle, weaponCount: 1);
                await stage.Enter(metadata);
                Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
                return result.HitRollNeeded;
            }

            Assert.That(await Needed(ground), Is.EqualTo(4), "control: Quality 4");
            Assert.That(await Needed(aircraft), Is.EqualTo(5), "Aircraft: -1 to hit rolls, at any distance");
        }

        [Test]
        public void TheAircraftDescription_IsTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.Aircraft.Description, Is.EqualTo(
                "May only use Advance actions, moving in a straight line, and adding 30\" to its total move " +
                "(even if Shaken). Aircraft ignore all units and terrain when moving and stopping, can't seize " +
                "or contest objectives, can't be moved in contact with or charged, and units targeting them " +
                "get -12\" range and -1 to hit rolls."));
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private DataBinding<ModelData> MakeModel(Position position, float radius) =>
            _store.GetDataBinding<ModelData>(_store.Create(
                new ModelData(radius, new List<Weapon>(), position, _store)));

        private DataBinding<UnitData> MakeUnit(Position position, bool withAircraft)
        {
            var bindings = new List<DataBinding<ModelData>> { MakeModel(position, 0.75f) };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), withAircraft ? "Flyer" : "Walker",
                quality: 4, defense: 4, modelBindings: bindings);
            if (withAircraft) unit.AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
