using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P5b — round-start Shaken recovery: "if a unit where all models have this rule is Shaken at the
    // beginning of the round, roll one die. On a 4+, it stops being Shaken." (Steadfast / Battleborn /
    // Honor Code, all identical.)
    //
    // Round_OnRoundStart is NOT a dormant hook, contrary to what #197's slice table said:
    // StartOfRoundExtraActionStage fires it every round for every living unit (Caster spell-token grants),
    // applying token operations and running executables. So this slice is only the effect.
    [TestFixture]
    public class ClearTokenOnRollTests
    {
        private static SpecialRuleDefinition Steadfast() => new("Steadfast",
            new[]
            {
                new HookEntry(EHookID.Round_OnRoundStart,
                    new Condition.And(new Condition.TokenPresent(TokenType.Shaken),
                                      new Condition.AllModelsHaveThisRule()),
                    new Effect.ClearTokenOnRoll(TokenType.Shaken, MinRoll: 4),
                    ELifetime.ThisRound),
            },
            Array.Empty<ActivatedAbility>());

        [Test]
        public void Effect_ProducesAnExecutableOperation_OnlyWhileTheTokenIsHeld()
        {
            var harness = new TestRuleHarness();
            harness.Register(Steadfast());
            IUnit unit = harness.BuildUnit("P1", 1, "Steadfast");

            Assert.That(Ops(harness, unit), Is.Empty, "No Shaken token, no die rolled.");

            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(Ops(harness, unit).OfType<RuleOperation.InvokeClearTokenOnRoll>().Single().MinRoll,
                Is.EqualTo(4));
        }

        [Test]
        public async Task ARollAtOrAboveTheThreshold_ClearsTheToken()
        {
            (World world, IUnit unit) = ShakenUnit(dieFace: 4);

            await OperationExecutor.Execute(
                new[] { new RuleOperation.InvokeClearTokenOnRoll(unit, TokenType.Shaken, 4) },
                new GameOperationServices(world.Context));

            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        [Test]
        public async Task ARollBelowTheThreshold_LeavesTheToken()
        {
            (World world, IUnit unit) = ShakenUnit(dieFace: 3);

            await OperationExecutor.Execute(
                new[] { new RuleOperation.InvokeClearTokenOnRoll(unit, TokenType.Shaken, 4) },
                new GameOperationServices(world.Context));

            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.True,
                "A 3 on a 4+ recovery must leave the unit Shaken.");
        }

        [Test]
        public async Task TheRollIsDecisive_UnderTheProbabilisticRoller_AndActuallyVaries()
        {
            // The dice invariant: a pass/fail test must ride RollDecisive, never a histogram. A plain Roll(1)
            // spreads 1/6 across every face, and "clear on 4+" would then want to remove a FRACTION of a
            // token. RollDecisive commits to one face.
            //
            // Asserting only "0 or 1 tokens" would also pass an implementation that silently always read face
            // 1, so this rolls across many seeds and requires BOTH outcomes to occur — the roll is decisive
            // and it is a real roll.
            var outcomes = new HashSet<int>();

            for (int seed = 0; seed < 40; seed++)
            {
                (World world, IUnit unit) = ShakenUnit(new ProbabilisticDiceRoller(seed));

                await OperationExecutor.Execute(
                    new[] { new RuleOperation.InvokeClearTokenOnRoll(unit, TokenType.Shaken, 4) },
                    new GameOperationServices(world.Context));

                int remaining = unit.Tokens.GetTokenCount(TokenType.Shaken);
                Assert.That(remaining, Is.EqualTo(0).Or.EqualTo(1),
                    "The unit is Shaken or it is not; a fractional token means the roll was not decisive.");
                outcomes.Add(remaining);
            }

            Assert.That(outcomes, Is.EquivalentTo(new[] { 0, 1 }),
                "Across 40 seeds a 4+ recovery must sometimes pass and sometimes fail.");
        }

        private static IReadOnlyList<RuleOperation> Ops(TestRuleHarness harness, IUnit unit) =>
            harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit));

        private static (World, IUnit) ShakenUnit(int dieFace) => ShakenUnit(new FixedDiceRoller(dieFace));

        private static (World, IUnit) ShakenUnit(IDiceRoller roller)
        {
            var world = World.Build(roller);
            IUnit unit = world.Unit.GetValue();
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
            return (world, unit);
        }

        /// <summary>A one-model army, registered so GameOperationServices can resolve the unit's binding.</summary>
        private sealed class World
        {
            public TestGameContext Context = null!;
            public DataBinding<UnitData> Unit = null!;

            public static World Build(IDiceRoller roller)
            {
                var store = GameDataStore.GameDataStoreBuilder.GetDefault();
                var context = new TestGameContext(store, roller);
                var player = new PlayerID(Guid.NewGuid());

                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(), gameDataStore: store);
                DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));

                var unit = new UnitData(player, "Steadfast Squad", quality: 4, defense: 4,
                    modelBindings: new List<DataBinding<ModelData>> { modelBinding });
                DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));

                store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unitBinding }));

                return new World { Context = context, Unit = unitBinding };
            }
        }
    }
}
