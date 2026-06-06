using FDG.Data;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Verifies that BuildModelBlockers produces circular Blocking terrain for every
    // placed model that belongs to neither the attacking unit nor the defending unit,
    // and that the resulting blockers actually affect LoS evaluation.
    //
    // Geometry:
    //   Attacker at (0, 5) → target at (20, 5).
    //   A model centered at (10, 5) with radius 0.75" lies directly on the sight line.
    [TestFixture]
    public class ModelBlockerTests
    {
        private static readonly Position AttackerPos  = new Position(0, 5);
        private static readonly Position TargetPos    = new Position(20, 5);
        private static readonly Position BlockerPos   = new Position(10, 5);
        private const float BaseRadius = 0.75f;

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public void ThirdPartyModel_OnSightLine_Blocks()
        {
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });
            MakeOrphanModel(BlockerPos); // belongs to no unit — should block

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.False,
                "Third-party model on sight line should block LoS.");
        }

        [Test]
        public void AttackingUnitModel_OnSightLine_DoesNotBlock()
        {
            // Put one of the attacker's own models right on the line — it must be excluded.
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos, BlockerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.True,
                "Models in the attacking unit must not block LoS to the enemy.");
        }

        [Test]
        public void DefendingUnitModel_OnSightLine_DoesNotBlock()
        {
            // A defending model on the path — the shooter should see through their own targets.
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { BlockerPos, TargetPos });

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.True,
                "Models in the defending unit must not block LoS to their own unit.");
        }

        [Test]
        public void UnplacedModel_OnSightLine_DoesNotBlock()
        {
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });
            MakeOrphanModel(new Position(0, 0)); // position (0,0) means unplaced

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit);

            Assert.That(blockers, Is.Empty, "Unplaced models (position 0,0) must not become blockers.");
        }

        [Test]
        public void OffSightLine_ThirdPartyModel_DoesNotBlock()
        {
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });
            MakeOrphanModel(new Position(10, 15)); // far off the line z=5

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.True,
                "A model that does not intersect the sight line must not block.");
        }

        [Test]
        public void AlliedUnitModel_OnSightLine_DoesNotBlock()
        {
            // Same-team but different-unit allied model on the line — must NOT block.
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var allyPlayer     = new PlayerID(Guid.NewGuid());
            var enemyPlayer    = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer, allyPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos }, attackerPlayer);
            MakeUnit(new[] { BlockerPos }, allyPlayer); // allied unit on the line
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos }, enemyPlayer);

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.True,
                "Models in allied units (same team as the attacker) must not block LoS.");
        }

        [Test]
        public void ThirdPartyEnemyUnitModel_OnSightLine_StillBlocks()
        {
            // Different-team unit on the line that is neither attacker nor defender — must block.
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyAPlayer   = new PlayerID(Guid.NewGuid());
            var enemyBPlayer   = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyAPlayer, enemyBPlayer }));

            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos }, attackerPlayer);
            MakeUnit(new[] { BlockerPos }, enemyAPlayer); // some other enemy unit on the line
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos }, enemyBPlayer);

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.False,
                "Models in enemy units that are neither attacker nor defender must still block LoS.");
        }

        // Helpers

        private DataBinding<UnitData> MakeUnit(IEnumerable<Position> positions, PlayerID? playerID = null)
        {
            var models = positions.Select(pos =>
            {
                var md = new ModelData(
                    baseRadiusInches: BaseRadius,
                    weapons: new List<Weapon>(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                return _store.GetDataBinding<ModelData>(_store.Create(md));
            }).ToList();

            var unit = new UnitData(playerID ?? new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(), modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Creates a ModelData that is registered in the store (and thus in TableState)
        // but does not belong to any unit.
        private void MakeOrphanModel(Position pos)
        {
            var md = new ModelData(
                baseRadiusInches: BaseRadius,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: pos,
                gameDataStore: _store);
            _store.Create(md);
        }
    }
}
