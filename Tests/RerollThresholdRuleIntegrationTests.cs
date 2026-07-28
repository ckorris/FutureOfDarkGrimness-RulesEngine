using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197: RerollCondition.OnUnmodifiedValue gained a MinValue so Mischievous/Scrapper Boost can widen
    // the save re-roll from unmodified 6 to unmodified 5-6. Proven through the REAL AssignWoundsStage,
    // beside BaneRuleIntegrationTests, whose setup this reuses.
    //
    // The composition pin is the point of this file. Save re-rolls fold by MINIMUM threshold, not by sum,
    // so a weapon carrying both a base (6) and its Boost (5-6) re-rolls at 5+ - the wider of the two -
    // rather than double-counting. That is the opposite of the additive sinks, where #196 shipped three
    // defect classes precisely because a Boost was authored as the full band and then ADDED to its base.
    // Here the full band is correct, and a test proves it rather than a comment claiming it.
    //
    // ProbabilisticDiceRoller makes the re-roll deterministic: re-rolling N dice at save-needed 4 yields
    // N x P(below 4) = N x 3/6 new wounds.
    [TestFixture]
    public class RerollThresholdRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
        }

        // The distance gate the shipped Boosts also carry is authored data, pinned app-side by
        // MischievousScrapperBoostShippedDataTests; what varies here is only the threshold.
        private static SpecialRuleDefinition RerollFrom(string name, int minValue) => new(name,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.Always(),
                    new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue(minValue)),
                    ELifetime.ThisAttack, ERuleSeat.Actor),
            },
            System.Array.Empty<ActivatedAbility>());

        [Test]
        public async Task TheDefaultThreshold_RerollsOnlyUnmodifiedSixes()
        {
            DataBinding<UnitData> attacker = MakeUnit(1);
            Attach(attacker, RerollFrom("Mischievous", 6));

            await RunStage(attacker, MakeUnit(5));

            // Saved dice are a 5 and a 6; only the 6 qualifies. 1 original wound + 1 x 3/6.
            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1.5f).Within(0.0001f),
                "the unboosted rule leaves the saved 5 alone");
        }

        [Test]
        public async Task AWidenedThreshold_RerollsFivesAndSixes()
        {
            DataBinding<UnitData> attacker = MakeUnit(1);
            Attach(attacker, RerollFrom("Mischievous Boost", 5));

            await RunStage(attacker, MakeUnit(5));

            // Both saved dice qualify. 1 original wound + 2 x 3/6.
            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f).Within(0.0001f),
                "'re-roll successful unmodified defense results of 5-6'");
        }

        [Test]
        public async Task ABaseAndItsBoostTogether_ComposeToTheWiderBand_NotToBoth()
        {
            DataBinding<UnitData> attacker = MakeUnit(1);
            Attach(attacker, RerollFrom("Mischievous", 6));
            Attach(attacker, RerollFrom("Mischievous Boost", 5));

            await RunStage(attacker, MakeUnit(5));

            // The corpus puts the Boost on units that already have the base, so this is the ordinary case,
            // not an edge one. Summing the two would re-roll the 6 twice (2.25 wounds); taking the wider
            // band re-rolls each qualifying die once.
            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f).Within(0.0001f),
                "identical to the Boost alone - the minimum threshold wins, nothing double-counts");
        }

        // Found by mutation-testing this file: widening the DEFAULT from 6 to 5 left every test above
        // green, because they all pass their threshold explicitly and Bane's own fixture uses two 6s,
        // which cannot tell the two bands apart. The default is load-bearing for compatibility - every
        // authoring that predates the field, core Bane included, serializes as a bare
        // {"kind":"onUnmodifiedValue"} and must keep meaning "the unmodified maximum".
        [Test]
        public void TheUnspecifiedThreshold_IsTheUnmodifiedMaximum()
        {
            Assert.That(new RerollCondition.OnUnmodifiedValue().MinValue, Is.EqualTo(6));
        }

        [Test]
        public async Task ARuleAuthoredWithoutAThreshold_RerollsOnlySixes()
        {
            DataBinding<UnitData> attacker = MakeUnit(1);
            Attach(attacker, new SpecialRuleDefinition("Bane-shaped",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.Always(),
                        new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()),
                        ELifetime.ThisAttack, ERuleSeat.Actor),
                },
                System.Array.Empty<ActivatedAbility>()));

            await RunStage(attacker, MakeUnit(5));

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1.5f).Within(0.0001f),
                "the saved 5 stands - a widened default would silently boost every shipped Bane");
        }

        [Test]
        public async Task NoRerollRule_LeavesEverySavedDieStanding()
        {
            await RunStage(MakeUnit(1), MakeUnit(5));

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f).Within(0.0001f),
                "only the original failed save lands");
        }

        // The shipped Boosts carry attackedFromOverInches(9). That gate is only meaningful if the stage
        // actually threads the attacker-to-defender distance into SaveRollCompleteContext - which the
        // app-side data test cannot prove, because it never runs a stage.
        [Test]
        public async Task TheDistanceGate_ReadsTheRealAttackerToDefenderDistance()
        {
            DataBinding<UnitData> attacker = MakeUnit(1);
            Attach(attacker, new SpecialRuleDefinition("Mischievous Boost",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                        new Condition.AttackedFromOverInches(9f),
                        new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue(5)),
                        ELifetime.ThisAttack, ERuleSeat.Actor),
                },
                System.Array.Empty<ActivatedAbility>()));

            await RunStage(attacker, MakeUnit(5, atZ: 20f));

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f).Within(0.0001f),
                "20in apart is 'over 9\" away' - both saved dice are re-rolled");
        }

        [Test]
        public async Task TheDistanceGate_DoesNotFireInsideNineInches()
        {
            DataBinding<UnitData> attacker = MakeUnit(1);
            Attach(attacker, new SpecialRuleDefinition("Mischievous Boost",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                        new Condition.AttackedFromOverInches(9f),
                        new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue(5)),
                        ELifetime.ThisAttack, ERuleSeat.Actor),
                },
                System.Array.Empty<ActivatedAbility>()));

            await RunStage(attacker, MakeUnit(5, atZ: 4f));

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f).Within(0.0001f),
                "inside 9in the Boost does not apply and no die is re-rolled");
        }

        private async Task RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: false);

            // One failed save (1 wound) plus two successful ones - a natural 5 and a natural 6 - so the
            // threshold decides how many are re-rolled. Bane's fixture uses two 6s, which cannot tell the
            // two thresholds apart.
            var failed = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)),
            };
            var successful = new List<SuccessfulSaveInfo>
            {
                new SuccessfulSaveInfo(TestDice.Faces(5, 6), new PendingSaveRolls(TestDice.Faces(5, 6), 4)),
            };
            metadata.AddResult(new RollToSaveResults(successful, failed));

            await stage.Enter(metadata);
        }

        private static void Attach(DataBinding<UnitData> unit, SpecialRuleDefinition definition) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(definition.Name, definition));

        private DataBinding<UnitData> MakeUnit(int modelCount, float atZ = 0f)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                    initialPosition: new Position(i * 2f, atZ), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
