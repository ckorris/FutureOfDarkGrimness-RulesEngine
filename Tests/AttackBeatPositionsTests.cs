using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // The attack visual must fire from the models that actually carry the weapon, not the whole
    // unit — otherwise a single rifle among five models would appear to come from the wrong place.
    [TestFixture]
    public class AttackBeatPositionsTests
    {
        private static GameDataStore NewStore() =>
            new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(16)
                .RegisterType<Position>(16)
                .RegisterType<ModelData>(16)
                .RegisterType<UnitData>(4)
                .RegisterType<Float2>(16)
                .Build();

        private static DataBinding<ModelData> MakeModel(GameDataStore store, List<Weapon> weapons, Position pos)
        {
            var model = new ModelData(0.5f, weapons, pos, store);
            DataReference reference = store.Create(model);
            return store.GetDataBinding<ModelData>(reference);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, List<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "U", 4, 4, models);
            DataReference reference = store.Create(unit);
            return store.GetDataBinding<UnitData>(reference);
        }

        private static Weapon W(string name) => new Weapon(name, 24f, 1, 0);

        [Test]
        public void FiringModels_ReturnsOnlyTheWeaponCarryingModels()
        {
            GameDataStore store = NewStore();
            Weapon rifle = W("Rifle");
            Weapon pistol = W("Pistol");

            // Five models; only one carries the rifle.
            var rifleModel = MakeModel(store, new List<Weapon> { rifle }, new Position(5f, 5f));
            var others = new List<DataBinding<ModelData>>
            {
                rifleModel,
                MakeModel(store, new List<Weapon> { pistol }, new Position(6f, 6f)),
                MakeModel(store, new List<Weapon> { pistol }, new Position(7f, 7f)),
                MakeModel(store, new List<Weapon> { pistol }, new Position(8f, 8f)),
                MakeModel(store, new List<Weapon> { pistol }, new Position(9f, 9f)),
            };
            var unit = MakeUnit(store, others);

            List<Position> firingRifle = AttackBeatPositions.FiringModels(unit, rifle);
            Assert.That(firingRifle, Has.Count.EqualTo(1), "only the rifle-carrying model fires the rifle");
            Assert.That(firingRifle[0].x, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(firingRifle[0].z, Is.EqualTo(5f).Within(0.0001f));

            List<Position> firingPistol = AttackBeatPositions.FiringModels(unit, pistol);
            Assert.That(firingPistol, Has.Count.EqualTo(4), "the four pistol models fire the pistol");
        }

        [Test]
        public void FiringModels_ExcludesUnplacedAndDeadModels()
        {
            GameDataStore store = NewStore();
            Weapon rifle = W("Rifle");

            var placed = MakeModel(store, new List<Weapon> { rifle }, new Position(5f, 5f));
            var unplaced = MakeModel(store, new List<Weapon> { rifle }, new Position()); // (0,0,0) reserve
            var dead = MakeModel(store, new List<Weapon> { rifle }, new Position(3f, 3f));
            dead.GetValue().DealWounds(dead.GetValue().TotalWounds);

            var unit = MakeUnit(store, new List<DataBinding<ModelData>> { placed, unplaced, dead });

            List<Position> firing = AttackBeatPositions.FiringModels(unit, rifle);
            Assert.That(firing, Has.Count.EqualTo(1), "unplaced (reserve) and dead carriers don't fire");
            Assert.That(firing[0].x, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void SelectBurstShooters_CountCoversList_PassesThrough()
        {
            var positions = new List<Position> { new Position(1f, 1f), new Position(2f, 2f) };
            Assert.That(AttackBeatPositions.SelectBurstShooters(positions, 2, 0), Is.SameAs(positions));
            Assert.That(AttackBeatPositions.SelectBurstShooters(positions, 5, 3), Is.SameAs(positions));
        }

        [Test]
        public void SelectBurstShooters_SplitBurst_RotatesAcrossCarriers()
        {
            var positions = new List<Position>
            {
                new Position(1f, 1f), new Position(2f, 2f), new Position(3f, 3f),
            };

            // A 3-copy Takedown volley split into single shots: shot k fires from carrier k.
            for (int shot = 0; shot < 3; shot++)
            {
                List<Position> selected = AttackBeatPositions.SelectBurstShooters(positions, 1, shot);
                Assert.That(selected, Has.Count.EqualTo(1), "a single-copy shot draws ONE beam");
                Assert.That(selected[0].x, Is.EqualTo(positions[shot].x).Within(0.0001f),
                    "each split shot fires from a different carrier");
            }
        }
    }

    // #276: the beat endpoints must depict only models that can actually strike — ranged carriers
    // need line of sight (unless the weapon ignores it) and range, melee carriers must be in melee
    // range — and a Takedown shot aims at its picked victim.
    [TestFixture]
    public class AttackBeatEndpointsTests
    {
        // Wall spanning x 8..12, z 3..7 — blocks the z=5 fire lane, leaves higher-z lanes clear.
        private static readonly RectangularZone WallRect = new RectangularZone(8, 12, 3, 7);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public void Ranged_OccludedCarrier_DoesNotAnimateAShot()
        {
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));
            Weapon rifle = Rifle();
            var blocked = MakeModel(new Position(1f, 5f), rifle);   // wall on its lane
            var clear = MakeModel(new Position(1f, 12f), rifle);    // sees past the wall
            var attacker = MakeUnit(blocked, clear);
            var defender = MakeUnit(MakeModel(new Position(21f, 5f)));

            (List<Position> from, List<Position> to) = AttackBeatPositions.Endpoints(_ctx.TableState,
                Metadata(attacker, defender, rifle, weaponCount: 1), _ctx.RuleEvaluator, seeThroughFriendlyUnits: false);

            Assert.That(from, Has.Count.EqualTo(1), "only the carrier with line of sight fires");
            Assert.That(from[0].z, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(to, Has.Count.EqualTo(1));
        }

        [Test]
        public void Ranged_OutOfRangeCarrier_DoesNotAnimateAShot()
        {
            Weapon rifle = Rifle(rangeInches: 24f);
            var near = MakeModel(new Position(1f, 5f), rifle);
            var far = MakeModel(new Position(1f, 60f), rifle); // ~57" from the target, rifle is 24"
            var attacker = MakeUnit(near, far);
            var defender = MakeUnit(MakeModel(new Position(21f, 5f)));

            (List<Position> from, _) = AttackBeatPositions.Endpoints(_ctx.TableState,
                Metadata(attacker, defender, rifle, weaponCount: 1), _ctx.RuleEvaluator, seeThroughFriendlyUnits: false);

            Assert.That(from, Has.Count.EqualTo(1), "only the carrier in range fires");
            Assert.That(from[0].z, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void Ranged_SplitBurst_EachShotFiresFromADifferentCarrier()
        {
            Weapon rifle = Rifle();
            var a = MakeModel(new Position(1f, 4f), rifle);
            var b = MakeModel(new Position(1f, 6f), rifle);
            var c = MakeModel(new Position(1f, 8f), rifle);
            var attacker = MakeUnit(a, b, c);
            var defender = MakeUnit(MakeModel(new Position(15f, 6f)));

            var fromZs = new List<float>();
            for (int shot = 0; shot < 3; shot++)
            {
                (List<Position> from, _) = AttackBeatPositions.Endpoints(_ctx.TableState,
                    Metadata(attacker, defender, rifle, weaponCount: 1, burstShotIndex: shot),
                    _ctx.RuleEvaluator, seeThroughFriendlyUnits: false);
                Assert.That(from, Has.Count.EqualTo(1), "a split single-copy shot draws ONE beam");
                fromZs.Add(from[0].z);
            }

            Assert.That(fromZs, Is.Unique, "the three split shots fire from three different snipers");
        }

        [Test]
        public void Ranged_TakedownPick_AimsAtThePickedModel()
        {
            Weapon rifle = Rifle();
            var attacker = MakeUnit(MakeModel(new Position(1f, 5f), rifle));
            var victim = MakeModel(new Position(21f, 9f));
            var defender = MakeUnit(MakeModel(new Position(21f, 5f)), victim);

            CombatMetadata metadata = Metadata(attacker, defender, rifle, weaponCount: 1);
            metadata.AddResult(new IndividualTargetResult(victim));

            (_, List<Position> to) = AttackBeatPositions.Endpoints(_ctx.TableState, metadata,
                _ctx.RuleEvaluator, seeThroughFriendlyUnits: false);

            Assert.That(to, Has.Count.EqualTo(1), "a Takedown shot aims at its one picked model");
            Assert.That(to[0].z, Is.EqualTo(9f).Within(0.0001f));
        }

        [Test]
        public void Melee_OutOfRangeCarrier_DoesNotAnimateASwing()
        {
            Weapon claws = new Weapon("Claws", 0f, 1, 0);
            var engaged = MakeModel(new Position(1f, 1f), claws);
            var standoff = MakeModel(new Position(10f, 1f), claws); // ~7" from the enemy, melee is 2"
            var attacker = MakeUnit(engaged, standoff);
            var defender = MakeUnit(MakeModel(new Position(2.5f, 1f)));

            (List<Position> from, _) = AttackBeatPositions.Endpoints(_ctx.TableState,
                Metadata(attacker, defender, claws, weaponCount: 1, isMelee: true), _ctx.RuleEvaluator, seeThroughFriendlyUnits: false);

            Assert.That(from, Has.Count.EqualTo(1), "only the model in melee range swings");
            Assert.That(from[0].x, Is.EqualTo(1f).Within(0.0001f));
        }

        private CombatMetadata Metadata(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, int weaponCount, bool isMelee = false, int burstShotIndex = 0) =>
            new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount,
                isMelee: isMelee, burstShotIndex: burstShotIndex);

        private static Weapon Rifle(float rangeInches = 24f) => new Weapon("Rifle", rangeInches, 1, 0);

        private DataBinding<ModelData> MakeModel(Position pos, params Weapon[] weapons)
        {
            var model = new ModelData(0.5f, weapons.ToList(), pos, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "U", 4, 4, models.ToList());
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
