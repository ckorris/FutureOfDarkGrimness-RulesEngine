using System;
using System.Collections.Generic;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #021 / GF v3.5.1 — "Shaken units always fail morale tests." TakeMoraleTest must short-circuit to a
    // failure for an already-Shaken unit without rolling, which is what escalates a Shaken unit that
    // loses a later melee at half strength into a Rout. Quality is 4; FixedDiceRoller(6) would pass a
    // real roll, so a failure here proves the auto-fail fired rather than the die.
    [TestFixture]
    public class ShakenAlwaysFailsMoraleTests
    {
        [Test]
        public async Task ShakenUnit_AutoFailsMoraleTest_WithoutRolling()
        {
            var (ctx, unit) = Build(dieValue: 6);
            MoraleUtilities.ApplyShaken(unit);

            MoraleUtilities.MoraleTestOutcome outcome =
                await MoraleUtilities.TakeMoraleTest(ctx, unit.GetValue(), baseRollNeeded: 4);

            Assert.That(outcome.Passed, Is.False,
                "a Shaken unit always fails its morale test, even when a 6 would otherwise pass.");
        }

        [Test]
        public async Task UnshakenUnit_WithSameRoll_Passes()
        {
            // Control: identical context/roll on an un-Shaken unit passes, proving the failure above is the
            // Shaken auto-fail and not the die.
            var (ctx, unit) = Build(dieValue: 6);

            MoraleUtilities.MoraleTestOutcome outcome =
                await MoraleUtilities.TakeMoraleTest(ctx, unit.GetValue(), baseRollNeeded: 4);

            Assert.That(outcome.Passed, Is.True, "an un-Shaken unit rolling a 6 against Quality 4 passes.");
        }

        private static (TestGameContext Ctx, DataBinding<UnitData> Unit) Build(int dieValue)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(dieValue));

            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            };
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Tester", quality: 4, defense: 4,
                modelBindings: modelBindings);
            return (ctx, store.GetDataBinding<UnitData>(store.Create(unit)));
        }
    }
}
