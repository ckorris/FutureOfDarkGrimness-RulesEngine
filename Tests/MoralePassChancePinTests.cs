using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #442 (#421 / #432 / #435): TacticalAnalysis.MoralePassChance is the REAL MoraleUtilities.TakeMoraleTest priced
    // instead of rolled. The engine's test rolls decisive dice (one concrete face each, even under the probabilistic
    // roller), so its exact pass probability is enumerated: every (first die, Fearless re-roll die) pair of faces
    // through the real test, at the Quality ResolveMoraleQuality gives it - 36 equally likely outcomes. The estimate
    // must equal that (plan G1: the engine is ground truth).
    [TestFixture]
    public class MoralePassChancePinTests
    {
        private const float BaseRadius = 0.5f;
        private GameDataStore _store = null!;
        private PlayerID _us;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(2)]
        public async Task PlainUnit_IsItsQuality(int quality)
        {
            DataBinding<UnitData> unit = MakeUnit("Warriors", quality, x: 10f);
            await AssertPinned(unit, EMoraleTestCause.Shooting, shotFrom: 12f, expected: (7 - quality) / 6f);
        }

        [Test]
        public async Task Fearless_AddsTheFourPlusSecondChance()
        {
            DataBinding<UnitData> unit = MakeUnit("Fanatics", 5, x: 10f);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fearless", CoreRuleCatalog.Fearless));
            await AssertPinned(unit, EMoraleTestCause.Shooting, shotFrom: 12f, expected: 2f / 6f + 4f / 6f * 3f / 6f);
        }

        // Hive Bond / Hold the Line / Valiant: "+1 to Morale Test rolls when shot/charged from over 9"/6" away".
        [TestCase(10f, 3f / 6f)]
        [TestCase(8f, 2f / 6f)]
        public async Task HiveBond_ShotFromOverNine_GetsPlusOne(float shotFrom, float expected)
        {
            DataBinding<UnitData> unit = MakeUnit("Grunts", 5, x: 10f);
            SpecialRuleDefinition bond = RangeConditional36Definitions.Morale();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(bond.Name, bond));
            await AssertPinned(unit, EMoraleTestCause.Shooting, shotFrom, expected);
        }

        // AoF Banner: "+1 to Morale Test rolls triggered by shooting or melee" - nothing for a rule's or spell's test.
        [TestCase(EMoraleTestCause.Shooting, 3f / 6f)]
        [TestCase(EMoraleTestCause.Other, 2f / 6f)]
        public async Task Banner_OnlyForAShootingOrMeleeTest(EMoraleTestCause cause, float expected)
        {
            DataBinding<UnitData> unit = MakeUnit("Spearmen", 5, x: 10f);
            SpecialRuleDefinition banner = AofSweep36Definitions.Banner();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(banner.Name, banner));
            await AssertPinned(unit, cause, shotFrom: 12f, expected);
        }

        // A friendly Tough(9)+ Hero within 6" lends its Quality (3.6 Hero), read through ResolveMoraleQuality.
        [TestCase(5f, 4f / 6f)]
        [TestCase(7f, 2f / 6f)]
        public async Task ToughNineHeroAura_LendsItsQuality_Within6Inches(float gap, float expected)
        {
            DataBinding<UnitData> unit = MakeUnit("Warriors", 5, x: 10f);
            DataBinding<UnitData> hero = MakeUnit("Lord", 3, x: 10f, z: 10f + gap + 2f * BaseRadius, models: 1);
            hero.GetValue().AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.GetValue().AttachRuleDefinition(new ResolvedRule("Tough", CoreRuleCatalog.Tough,
                new RuleArgument[] { new RuleArgument.Int(9) }));
            hero.GetValue().ModelBindings[0].GetValue().SetMaxWounds(9);
            await AssertPinned(unit, EMoraleTestCause.Shooting, shotFrom: 12f, expected);
        }

        [Test]
        public async Task Shaken_AlwaysFails()
        {
            DataBinding<UnitData> unit = MakeUnit("Warriors", 3, x: 10f);
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            await AssertPinned(unit, EMoraleTestCause.Shooting, shotFrom: 12f, expected: 0f);
        }

        // --- harness ----------------------------------------------------------------------------------------------

        private async Task AssertPinned(DataBinding<UnitData> unit, EMoraleTestCause cause, float shotFrom,
            float expected)
        {
            var roller = new TwoFaceRoller();
            var ctx = new TestGameContext(_store, roller);
            int passes = 0;
            for (int first = 1; first <= 6; first++)
            {
                for (int reroll = 1; reroll <= 6; reroll++)
                {
                    roller.Set(first, reroll);
                    MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(ctx,
                        unit.GetValue(), MoraleUtilities.ResolveMoraleQuality(ctx, unit.GetValue()),
                        failureCausesShakenOrRout: true, shotFromInches: shotFrom, cause: cause);
                    if (outcome.Passed) passes++;
                }
            }
            float engine = passes / 36f;

            float estimate = TacticalAnalysis.MoralePassChance(ctx.TableState, ctx.RuleEvaluator, unit.GetValue(),
                cause, shotFromInches: shotFrom);

            Assert.That(estimate, Is.EqualTo(engine).Within(0.0001f), "the estimate must equal the engine's test");
            Assert.That(estimate, Is.EqualTo(expected).Within(0.0001f));
        }

        private DataBinding<UnitData> MakeUnit(string name, int quality, float x, float z = 10f, int models = 4)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon> { new Weapon("Blade", 0f, 1, 0) },
                    new Position(x + i * 1.5f, z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(
                new UnitData(_us, name, quality: quality, defense: 4, modelBindings: bindings)));
            _store.Create(new ArmyData(_us, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // The morale test's decisive dice: the first call shows `first`, the next `reroll` (Fearless), cycling.
        private sealed class TwoFaceRoller : IDiceRoller
        {
            private int _first = 1, _reroll = 1, _calls;

            public void Set(int first, int reroll) { _first = first; _reroll = reroll; _calls = 0; }

            public IDiceResults RollDecisive(int sideCount) => Face(sideCount, _calls++ % 2 == 0 ? _first : _reroll, 1f);

            public IDiceResults Roll(int sideCount, float rollCount) => Face(sideCount, _first, rollCount);

            private static IDiceResults Face(int sideCount, int face, float count)
            {
                float[] perSide = new float[sideCount];
                perSide[face - 1] = count;
                return new DiceResults(perSide);
            }
        }
    }
}
