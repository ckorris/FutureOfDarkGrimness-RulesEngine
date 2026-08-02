using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Tests
{
    // #314: ShotEligibility is the single answer to "which defender can this shooter actually hit",
    // shared by the rules (ChooseRangedAttackStage), the attack animation (AttackBeatPositions) and the
    // targeting previews. The bug that motivated it: the shoot panel aimed its fire lines at the NEAREST
    // defender by raw distance, so a line was drawn straight through a wall while the volley itself
    // resolved against a model the shooter could see.
    [TestFixture]
    public class ShotEligibilityTests
    {
        // Wall spanning x 8..12, z 3..7 — blocks the z=5 fire lane, leaves higher-z lanes clear.
        private static readonly RectangularZone WallRect = new RectangularZone(8, 12, 3, 7);

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void NearestVisibleModel_SkipsANearerBlockedModel()
        {
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));
            IModel shooter = Model(new Position(1f, 5f));
            IModel blockedNearer = Model(new Position(21f, 5f));  // behind the wall, but closest
            IModel visibleFarther = Model(new Position(30f, 5f)); // ALSO behind the wall
            IModel visible = Model(new Position(25f, 12f));       // clear lane, farthest of all

            IModel? aimed = ShotEligibility.NearestVisibleModel(shooter.Position, shooter.BaseShape,
                shooter.Facing, new List<IModel> { blockedNearer, visibleFarther, visible }, Blockers());

            Assert.That(aimed, Is.SameAs(visible),
                "the shot is aimed at the nearest model the shooter can SEE, never at a closer blocked one");
        }

        [Test]
        public void NearestVisibleModel_EverythingBlocked_ReturnsNull()
        {
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));
            IModel shooter = Model(new Position(1f, 5f));
            IModel blocked = Model(new Position(21f, 5f));

            Assert.That(ShotEligibility.NearestVisibleModel(shooter.Position, shooter.BaseShape,
                shooter.Facing, new List<IModel> { blocked }, Blockers()), Is.Null);
        }

        [Test]
        public void NearestVisibleModel_NullBlockers_IgnoresLineOfSight()
        {
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));
            IModel shooter = Model(new Position(1f, 5f));
            IModel blockedNearer = Model(new Position(21f, 5f));
            IModel visible = Model(new Position(25f, 12f));

            IModel? aimed = ShotEligibility.NearestVisibleModel(shooter.Position, shooter.BaseShape,
                shooter.Facing, new List<IModel> { blockedNearer, visible }, blockers: null);

            Assert.That(aimed, Is.SameAs(blockedNearer),
                "an Indirect/Takedown shot lobs at what it can't see, so the nearest model wins");
        }

        [Test]
        public void NearestVisibleModel_HonoursRangeAndSkipsCorpsesAndUnplacedModels()
        {
            IModel shooter = Model(new Position(1f, 5f));
            IModel dead = Model(new Position(3f, 5f));
            IModel unplaced = Model(new Position(0f, 0f));
            IModel alive = Model(new Position(11f, 5f));
            IModel outOfRange = Model(new Position(60f, 5f));
            dead.DealWounds(dead.TotalWounds - dead.WoundsDealt);

            IModel? aimed = ShotEligibility.NearestVisibleModel(shooter.Position, shooter.BaseShape,
                shooter.Facing, new List<IModel> { dead, unplaced, alive, outOfRange },
                blockers: null, maxRangeInches: 24f);

            Assert.That(aimed, Is.SameAs(alive),
                "a corpse, a model still in reserve at the origin, and an out-of-range model are all no targets");
        }

        // The seam's whole point: whatever the rules count as a shooter, the preview must find a model
        // for it to aim at - and never the blocked one. Runs the REAL targeting stage and cross-checks
        // its modelsThatCanShoot against the helper.
        [Test]
        public async Task ModelsTheStageCallsShooters_AllHaveAVisibleModelToAimAt()
        {
            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));

            // One shooter, firing down the z=12 lane. The enemy's nearest model sits behind the wall.
            DataBinding<ModelData> shooter = ModelBinding(new Position(1f, 12f), new Weapon("Rifle", 48f, 1, 0));
            DataBinding<UnitData> attackerUnit = Unit(attackerPlayer, "Squad", shooter);
            _store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerUnit }));

            // (21,1) is NEARER (~21.8" base to base) but its lane from (1,12) cuts the wall's z 3..7 band;
            // (25,12) is farther (~23") down a clear horizontal lane.
            DataBinding<ModelData> blocked = ModelBinding(new Position(21f, 1f));
            DataBinding<ModelData> visible = ModelBinding(new Position(25f, 12f));
            DataBinding<UnitData> enemyUnit = Unit(enemyPlayer, "Enemy", blocked, visible);
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit }));

            var requester = new ChooseRangedAttackStageTests.CapturingRangedRequester();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(_store, requester);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChoseWeapon.Bind("test-on-chose-weapon");
            stage.BackToChooseAction.Bind("test-back-to-choose-action");
            stage.OnNoValidShots.Bind("test-on-no-valid-shots");
            await stage.Enter(new CombatActionContext(ctx, attackerUnit, isMelee: false));

            ChooseRangedAttackRequest? request = requester.Captured;
            Assert.That(request, Is.Not.Null, "the stage offered the shot");

            WeaponOption option = request!.WeaponOptions.Single();
            WeaponTargetStats stats = option.WeaponTargetStats.Single(t => t.TargetUnit == enemyUnit);
            Assert.That(stats.modelsThatCanShoot, Is.Not.Empty, "the clear-lane shooter can fire");

            IReadOnlyList<ITerrain> blockers =
                ShotEligibility.BuildBlockers(ctx.TableState, attackerUnit, enemyUnit);
            var targets = enemyUnit.GetValue().Models.ToList();
            foreach (DataBinding<ModelData> shooterBinding in stats.modelsThatCanShoot)
            {
                ModelData from = shooterBinding.GetValue();
                IModel? aimed = ShotEligibility.NearestVisibleModel(from.Position, from.BaseShape,
                    from.Facing, targets, blockers);
                Assert.That(aimed, Is.Not.Null,
                    "every model the rules call a shooter must have a defender the preview can point at");
                Assert.That(aimed, Is.SameAs(visible.GetValue()),
                    "and it must be the one it can see, not the nearer model behind the wall");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private IReadOnlyList<ITerrain> Blockers() => new TableState(_store).Terrain.Objects.ToList();

        private IModel Model(Position position) => ModelBinding(position).GetValue();

        private DataBinding<ModelData> ModelBinding(Position position, params Weapon[] weapons)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: weapons.ToList(),
                initialPosition: position, gameDataStore: _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> Unit(PlayerID player, string name,
            params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: models.ToList());
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
