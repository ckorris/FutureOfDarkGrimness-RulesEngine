using System.Linq;
using FDG.Data;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #017: proves the real DetermineInRange{Attackers,Defenders}
    // stages gate melee participation by position. Only models within MELEE_RANGE_INCHES_HORIZONTAL (2")
    // base-to-base AND MELEE_RANGE_INCHES_VERTICAL (4") vertically of an enemy model may strike; an
    // out-of-range model in the same unit contributes none of its weapons to the swing pool.
    [TestFixture]
    public class MeleeInRangeIntegrationTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester());
        }

        [Test]
        public async Task OutOfRangeAttacker_ContributesNoWeaponsToThePool()
        {
            // Defender at origin. One attacker in base contact (in range), one 8" away (out of range).
            DataBinding<UnitData> attacker = MakeUnit(new Position(1f, 0f), new Position(8f, 0f));
            DataBinding<UnitData> defender = MakeUnit(new Position(0f, 0f));

            CombatActionContext context = await RunAttackersStage(attacker, defender);

            Assert.That(context.InRangeAttackingModels, Has.Count.EqualTo(1),
                "Only the in-base-contact model is within melee range.");
            Assert.That(context.AvailableWeapons.Values.Sum(), Is.EqualTo(1),
                "The 8\"-away model's melee weapon must not enter the swing pool.");
        }

        [Test]
        public async Task VerticallyDistantAttacker_IsOutOfRange()
        {
            // Both attackers are horizontally in base contact, but one is 5" up (beyond the 4" vertical reach).
            DataBinding<UnitData> attacker = MakeUnit(new Position(1f, 2f, 0f), new Position(1f, 5f, 0f));
            DataBinding<UnitData> defender = MakeUnit(new Position(0f, 0f, 0f));

            CombatActionContext context = await RunAttackersStage(attacker, defender);

            Assert.That(context.InRangeAttackingModels, Has.Count.EqualTo(1),
                "The model 5\" above the enemy is out of vertical melee range; the one 2\" up is in range.");
            Assert.That(context.AvailableWeapons.Values.Sum(), Is.EqualTo(1));
        }

        [Test]
        public async Task AllAttackersInRange_AllContribute()
        {
            DataBinding<UnitData> attacker = MakeUnit(new Position(1f, 0f), new Position(1.5f, 0f));
            DataBinding<UnitData> defender = MakeUnit(new Position(0f, 0f));

            CombatActionContext context = await RunAttackersStage(attacker, defender);

            Assert.That(context.InRangeAttackingModels, Has.Count.EqualTo(2));
            Assert.That(context.AvailableWeapons.Values.Sum(), Is.EqualTo(2));
        }

        [Test]
        public async Task OutOfRangeDefender_ExcludedFromStrikeBackSet()
        {
            // Attacker single model at origin. Defender has one model in range and one far away.
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f));
            DataBinding<UnitData> defender = MakeUnit(new Position(1f, 0f), new Position(8f, 0f));

            CombatActionContext context = await RunAttackersThenDefendersStage(attacker, defender);

            Assert.That(context.InRangeDefendingModels, Has.Count.EqualTo(1),
                "Only the in-range defending model is eligible to strike back.");
        }

        [Test]
        public async Task NoAttackerInRange_FizzlesWithoutEmptyPoolCrash()
        {
            // Attacker and defender 20" apart: no model is within melee range. The guard must route to the
            // melee-fizzle path rather than entering ChooseMeleeWeaponStage with an empty pool (#017).
            DataBinding<UnitData> attacker = MakeUnit(new Position(0f, 0f));
            DataBinding<UnitData> defender = MakeUnit(new Position(20f, 0f));

            bool noneInRangeFired = false;
            bool proceededToDefenders = false;

            CombatActionContext context = new CombatActionContext(_ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            DetermineInRangeAttackersStage stage =
                new DetermineInRangeAttackersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToDetermineDefenders.Bind("done");
            stage.OnNoAttackersInRange.Bind("done");
            stage.ToDetermineDefenders.OnWillActivate += _ => proceededToDefenders = true;
            stage.OnNoAttackersInRange.OnWillActivate += _ => noneInRangeFired = true;

            await stage.Enter(context);

            Assert.That(noneInRangeFired, Is.True, "With nobody in range the melee fizzles instead of swinging.");
            Assert.That(proceededToDefenders, Is.False);
            Assert.That(context.AvailableWeapons, Is.Empty, "No in-range attacker → empty swing pool.");
        }

        [Test]
        public async Task InRangeButOnlyRangedWeapon_FizzlesWithoutEmptyPoolCrash()
        {
            // The attacking model is in base contact (positionally in melee range) but carries only a ranged
            // weapon — the dead-units case: the melee-armed models died and a ranged-only survivor remains in
            // range. inRange.Count > 0, but the swing pool is empty, so the guard must route to the fizzle path
            // rather than entering ChooseMeleeWeaponStage with an empty pool.
            DataBinding<UnitData> attacker = MakeRangedUnit(new Position(1f, 0f));
            DataBinding<UnitData> defender = MakeUnit(new Position(0f, 0f));

            bool noneInRangeFired = false;
            bool proceededToDefenders = false;

            CombatActionContext context = new CombatActionContext(_ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            DetermineInRangeAttackersStage stage =
                new DetermineInRangeAttackersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToDetermineDefenders.Bind("done");
            stage.OnNoAttackersInRange.Bind("done");
            stage.ToDetermineDefenders.OnWillActivate += _ => proceededToDefenders = true;
            stage.OnNoAttackersInRange.OnWillActivate += _ => noneInRangeFired = true;

            await stage.Enter(context);

            Assert.That(context.InRangeAttackingModels, Has.Count.EqualTo(1),
                "The model is within melee range positionally.");
            Assert.That(context.AvailableWeapons, Is.Empty,
                "Its only weapon is ranged, so the melee swing pool is empty.");
            Assert.That(noneInRangeFired, Is.True,
                "An in-range but melee-weaponless attacker fizzles instead of crashing ChooseMeleeWeaponStage.");
            Assert.That(proceededToDefenders, Is.False);
        }

        [Test]
        public void MinDistanceBetweenUnits_IgnoresDeadModels()
        {
            // A corpse sits at base contact; the only living model is 20" away. Distance must reflect the
            // living model so a wiped/out-of-reach unit can't appear in melee range (#017 root-cause fix).
            DataBinding<UnitData> unitA = MakeUnit(new Position(0f, 0f));
            DataBinding<UnitData> unitB = MakeUnit(new Position(1f, 0f), new Position(20f, 0f));

            // Kill the near model at (1,0); leave the far one at (20,0) alive.
            ModelData nearModel = unitB.GetValue().ModelBindings[0].GetValue();
            nearModel.DealWounds(nearModel.TotalWounds);

            float dist = UnitCompareUtilities.MinDistanceBetweenUnits(
                unitA.GetValue(), unitB.GetValue(), out _, out _, includeVertical: false);

            // Nearest living pair: (0,0) r0.75 vs (20,0) r0.75 → b2b = 20 - 1.5 = 18.5".
            Assert.That(dist, Is.EqualTo(18.5f).Within(0.01f),
                "The dead model at base contact must be ignored; distance is to the living model.");
        }

        private async Task<CombatActionContext> RunAttackersStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            CombatActionContext context = new CombatActionContext(_ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            DetermineInRangeAttackersStage stage =
                new DetermineInRangeAttackersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToDetermineDefenders.Bind("done");
            await stage.Enter(context);
            return context;
        }

        private async Task<CombatActionContext> RunAttackersThenDefendersStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            CombatActionContext context = await RunAttackersStage(attacker, defender);

            DetermineInRangeDefendersStage stage =
                new DetermineInRangeDefendersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToChooseMeleeWeapons.Bind("done");
            await stage.Enter(context);
            return context;
        }

        // Each model carries a single melee weapon (RangeInches 0) so the pool size equals the in-range model count.
        private DataBinding<UnitData> MakeUnit(params Position[] modelPositions)
            => MakeUnit(() => new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0), modelPositions);

        // Each model carries a single ranged weapon (RangeInches > 0) and no melee weapon, so it never
        // contributes to the melee swing pool even when positionally in range.
        private DataBinding<UnitData> MakeRangedUnit(params Position[] modelPositions)
            => MakeUnit(() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0), modelPositions);

        private DataBinding<UnitData> MakeUnit(System.Func<Weapon> weaponFactory, params Position[] modelPositions)
        {
            List<DataBinding<ModelData>> modelBindings = new List<DataBinding<ModelData>>(modelPositions.Length);
            foreach (Position position in modelPositions)
            {
                List<Weapon> weapons = new List<Weapon> { weaponFactory() };
                ModelData model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: weapons,
                    initialPosition: position,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            UnitData unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
