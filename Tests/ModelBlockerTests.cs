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

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);
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

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);
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

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);
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

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);

            Assert.That(blockers, Is.Empty, "Unplaced models (position 0,0) must not become blockers.");
        }

        [Test]
        public void OffSightLine_ThirdPartyModel_DoesNotBlock()
        {
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });
            MakeOrphanModel(new Position(10, 15)); // far off the line z=5

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.True,
                "A model that does not intersect the sight line must not block.");
        }

        [Test]
        public void AlliedUnitModel_OnSightLine_DoesNotBlock_UnderHouseRule()
        {
            // #384 house rule ON (the pre-#384 behavior, #044): same-team but
            // different-unit allied model on the line — must NOT block.
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var allyPlayer     = new PlayerID(Guid.NewGuid());
            var enemyPlayer    = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer, allyPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos }, attackerPlayer);
            MakeUnit(new[] { BlockerPos }, allyPlayer); // allied unit on the line
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos }, enemyPlayer);

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: true);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.True,
                "Models in allied units (same team as the attacker) must not block LoS.");
        }

        [Test]
        public void AlliedUnitModel_OnSightLine_Blocks_UnderOfficialRules()
        {
            // #384 default OFF (official rules): only the shooter's own unit and the target are
            // transparent - an allied unit's model on the line blocks like any other unit's.
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var allyPlayer     = new PlayerID(Guid.NewGuid());
            var enemyPlayer    = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer, allyPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos }, attackerPlayer);
            MakeUnit(new[] { BlockerPos }, allyPlayer); // allied unit on the line
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos }, enemyPlayer);

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.False,
                "Under the official rules an allied unit's model on the sight line must block LoS.");
        }

        [Test]
        public void SamePlayerOtherUnitModel_OnSightLine_Blocks_UnderOfficialRules()
        {
            // #384 default OFF, no teams registered: even the shooter's OWN PLAYER's other unit
            // blocks - the own-unit exception really is own-unit only.
            var player = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos }, player);
            MakeUnit(new[] { BlockerPos }, player); // the same player's OTHER unit on the line
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.False,
                "Under the official rules a same-player unit that is neither shooter nor target blocks LoS.");
        }

        [Test]
        public void FriendlySightBlockers_OtherAlliedUnitBlocks_OwnAndEnemyDoNot()
        {
            // #384 AI helper: only OTHER same-team units become blockers - never the active
            // unit's own models, and never enemy models (the lane approximation keeps #363).
            var activePlayer = new PlayerID(Guid.NewGuid());
            var allyPlayer   = new PlayerID(Guid.NewGuid());
            var enemyPlayer  = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { activePlayer, allyPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            DataBinding<UnitData> activeUnit = MakeUnit(new[] { AttackerPos, BlockerPos }, activePlayer);
            MakeUnit(new[] { new Position(5, 5) }, allyPlayer);   // ally's unit -> blocker
            MakeUnit(new[] { new Position(15, 5) }, enemyPlayer); // enemy unit -> not a blocker

            var blockers = LineOfSightUtilities.BuildFriendlySightBlockers(
                _ctx.TableState, activeUnit.GetValue());

            Assert.That(blockers, Has.Count.EqualTo(1),
                "Exactly the ally's model becomes a blocker - not the active unit's own models, not enemies.");
            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, blockers), Is.False,
                "The ally's base at (5,5) sits on the z=5 sight line and must cut it.");
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

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false);
            var allTerrain = blockers.Concat(new List<ITerrain>()).ToList();

            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, allTerrain), Is.False,
                "Models in enemy units that are neither attacker nor defender must still block LoS.");
        }

        [Test]
        public void RectangularBlocker_LongAxisAcrossSightLine_Blocks()
        {
            // Sight line runs along z=5. A 0.5"×6" base centred at (10, 7.5) — 2.5" off the line, far outside its
            // inscribed bounding circle (r=0.25). Facing +Z, its 6" length reaches down across the line (to z=4.5),
            // so its true footprint blocks where the bounding circle never would (#150).
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });
            MakeOrphanModel(new Position(10, 7.5f), new RectangleBase(0.5f, 6f), new Float2(0f, 1f));

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false).ToList();
            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, blockers), Is.False,
                "a long base reaching across the sight line blocks, though its bounding circle wouldn't.");
        }

        [Test]
        public void RectangularBlocker_ShortAxisAcrossSightLine_DoesNotBlock()
        {
            // Same base and centre, rotated to face +X: only its 0.5" width points at the sight line (reaching
            // z=7.25, short of z=5), so it no longer blocks. Orientation alone flips the outcome (#150).
            DataBinding<UnitData> attackerUnit = MakeUnit(new[] { AttackerPos });
            DataBinding<UnitData> defenderUnit = MakeUnit(new[] { TargetPos });
            MakeOrphanModel(new Position(10, 7.5f), new RectangleBase(0.5f, 6f), new Float2(1f, 0f));

            var blockers = LineOfSightUtilities.BuildModelBlockers(_ctx.TableState, attackerUnit, defenderUnit,
                seeThroughFriendlyUnits: false).ToList();
            Assert.That(LineOfSightUtilities.HasLineOfSight(AttackerPos, TargetPos, blockers), Is.True,
                "rotated so only its narrow width faces the line, the base no longer blocks.");
        }

        // Helpers

        private DataBinding<UnitData> MakeUnit(IEnumerable<Position> positions, PlayerID? playerID = null)
        {
            var models = positions.Select(pos =>
            {
                var md = new ModelData(
                    baseRadiusInches: BaseRadius,
                    weapons: new List<Weapon>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                return _store.GetDataBinding<ModelData>(_store.Create(md));
            }).ToList();

            var unit = new UnitData(playerID ?? new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Creates a ModelData that is registered in the store (and thus in TableState)
        // but does not belong to any unit.
        private void MakeOrphanModel(Position pos)
        {
            var md = new ModelData(
                baseRadiusInches: BaseRadius,
                weapons: new List<Weapon>(),
                initialPosition: pos,
                gameDataStore: _store);
            _store.Create(md);
        }

        // Orphan blocker with an explicit base shape + facing (#150 orientation-aware LoS).
        private void MakeOrphanModel(Position pos, IBaseShape shape, Float2 facing)
        {
            var md = new ModelData(shape, new List<Weapon>(), pos, _store);
            md.SetFacing(facing);
            _store.Create(md);
        }
    }
}
