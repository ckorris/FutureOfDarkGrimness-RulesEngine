using FDG.Data;
using FDG.Players;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #209 - weapon-choice option order must not depend on how the weapon pool happens to hash:
    // the pool is a dictionary keyed by the Weapon REFERENCE type, so before the fix its
    // enumeration order was identity-hash-dependent and multi-weapon units swung/fired in a
    // different order per run, breaking same-seed replay (#193). The pin: the same weapon set
    // inserted in OPPOSITE orders yields the identical option sequence.
    [TestFixture]
    public class WeaponOrderDeterminismTests
    {
        [Test]
        public async Task MeleeWeaponOptions_HaveTheSameOrder_RegardlessOfInsertionOrder()
        {
            IReadOnlyList<string> forward = await MeleeOptionsFor(
                new Weapon("Spear", 0f, 1, 0), new Weapon("Blade", 0f, 1, 0), new Weapon("Claw", 0f, 1, 0));
            IReadOnlyList<string> reversed = await MeleeOptionsFor(
                new Weapon("Claw", 0f, 1, 0), new Weapon("Blade", 0f, 1, 0), new Weapon("Spear", 0f, 1, 0));

            Assert.That(forward, Is.EqualTo(reversed),
                "the offered melee weapon order must not depend on pool insertion/hash order");
            Assert.That(forward, Is.EqualTo(forward.OrderBy(label => label, StringComparer.Ordinal).ToList()),
                "options come out in a deterministic (ordinal) order");
        }

        [Test]
        public async Task RangedWeaponOptions_HaveTheSameOrder_RegardlessOfInsertionOrder()
        {
            IReadOnlyList<string> forward = await RangedOptionsFor(
                Gun("Rifle"), Gun("Pistol"), Gun("Cannon"));
            IReadOnlyList<string> reversed = await RangedOptionsFor(
                Gun("Cannon"), Gun("Pistol"), Gun("Rifle"));

            Assert.That(forward, Is.EqualTo(reversed),
                "the offered ranged weapon order must not depend on pool insertion/hash order");
            Assert.That(forward, Is.EqualTo(forward.OrderBy(name => name, StringComparer.Ordinal).ToList()),
                "options come out in a deterministic (ordinal) order");
        }

        // --- fixtures ---

        private static Weapon Gun(string name) => new Weapon(name, rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private static async Task<IReadOnlyList<string>> MeleeOptionsFor(params Weapon[] weapons)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new DeadlyWeaponPriorityTests.CapturingStringSelectionRequester();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);

            var model = new ModelData(0.5f, weapons.ToList(), new Position(0, 0, 0), store);
            var modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Attacker", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            var unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));

            var combatCtx = new CombatActionContext(ctx, unitBinding, isMelee: true);
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChosen.Bind("test-on-chosen");
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            return requester.Captured!.ValidOptions;
        }

        private static async Task<IReadOnlyList<string>> RangedOptionsFor(params Weapon[] weapons)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new ChooseRangedAttackStageTests.CapturingRangedRequester();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);

            var attackerPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { attackerPlayer }));
            store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            var attackerModel = new ModelData(0.5f, weapons.ToList(), new Position(0, 0, 0), store);
            var attackerModelBinding = store.GetDataBinding<ModelData>(store.Create(attackerModel));
            var attackerUnit = new UnitData(attackerPlayer, "Attacker", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { attackerModelBinding });
            var attackerBinding = store.GetDataBinding<UnitData>(store.Create(attackerUnit));
            store.Create(new ArmyData(attackerPlayer, new List<DataBinding<UnitData>> { attackerBinding }));

            var enemyModel = new ModelData(0.5f, new List<Weapon>(), new Position(10, 0, 0), store);
            var enemyModelBinding = store.GetDataBinding<ModelData>(store.Create(enemyModel));
            var enemyUnit = new UnitData(enemyPlayer, "Enemy", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { enemyModelBinding });
            var enemyBinding = store.GetDataBinding<UnitData>(store.Create(enemyUnit));
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyBinding }));

            var combatCtx = new CombatActionContext(ctx, attackerBinding, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChoseWeapon.Bind("test-chose");
            stage.BackToChooseAction.Bind("test-back");
            stage.OnNoValidShots.Bind("test-no-shots");
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            return requester.Captured!.WeaponOptions.Select(option => option.Weapon.Name).ToList();
        }
    }
}
