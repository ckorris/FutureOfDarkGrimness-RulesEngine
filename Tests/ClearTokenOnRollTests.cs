using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
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
    // #376 (Vale Oath Boost) reshaped the mechanism: the effect emits a SINK op, TokenClearRollSink folds
    // every entry to the best (lowest) threshold per token type, and TokenClearRolls makes ONE decisive
    // roll per type through IOperationServices.ClearTokenOnRoll. A base 4+ plus a Boost 3+ is one roll at
    // 3+ (P 1/2 -> 2/3), never two chances (4+ twice would be 3/4).
    [TestFixture]
    public class ClearTokenOnRollTests
    {
        private static SpecialRuleDefinition Steadfast(string name = "Steadfast", int minRoll = 4) => new(name,
            new[]
            {
                new HookEntry(EHookID.Round_OnRoundStart,
                    new Condition.And(new Condition.TokenPresent(TokenType.Shaken),
                                      new Condition.AllModelsHaveThisRule()),
                    new Effect.ClearTokenOnRoll(TokenType.Shaken, MinRoll: minRoll),
                    ELifetime.ThisRound),
            },
            Array.Empty<ActivatedAbility>());

        // The Vale Oath Boost shape: gated on the unit carrying the base rule, authored as the FULL
        // boosted band (3) - the min-threshold convention (RerollSink doctrine), not an increment.
        private static SpecialRuleDefinition Boost(string baseName) => new(baseName + " Boost",
            new[]
            {
                new HookEntry(EHookID.Round_OnRoundStart,
                    new Condition.And(new Condition.TokenPresent(TokenType.Shaken),
                                      new Condition.UnitHasRule(baseName)),
                    new Effect.ClearTokenOnRoll(TokenType.Shaken, MinRoll: 3),
                    ELifetime.ThisRound),
            },
            Array.Empty<ActivatedAbility>());

        [Test]
        public void Effect_ProducesASinkOperation_OnlyWhileTheTokenIsHeld()
        {
            var harness = new TestRuleHarness();
            harness.Register(Steadfast());
            IUnit unit = harness.BuildUnit("P1", 1, "Steadfast");

            Assert.That(Ops(harness, unit), Is.Empty, "No Shaken token, no die rolled.");

            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(Ops(harness, unit).OfType<RuleOperation.ClearTokenOnRoll>().Single().MinRoll,
                Is.EqualTo(4));
        }

        // ---- The fold: base + Boost = one threshold, never a second chance --------------------------

        [Test]
        public void BaseAndBoost_FoldToTheBoostedThreshold()
        {
            var harness = new TestRuleHarness();
            harness.Register(Steadfast("Vale Oath"));
            harness.Register(Boost("Vale Oath"));
            IUnit unit = harness.BuildUnit("P1", 1, "Vale Oath", "Vale Oath Boost");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            var sink = new TokenClearRollSink();
            sink.ApplyFrom(Ops(harness, unit));

            Assert.That(sink.Entries, Has.Count.EqualTo(1),
                "two entries for one token type must fold to ONE roll - a second entry is a second chance.");
            Assert.That(sink.Entries.Single(), Is.EqualTo((TokenType.Shaken, 3)));
        }

        [Test]
        public void TwoDistinctBaseRules_AlsoFoldToOneRoll()
        {
            // Owner-ruled facet (#376): Steadfast + Battleborn on one unit is one roll at the best
            // threshold, not two chances. No shipped unit carries both; the fold decides the edge.
            var harness = new TestRuleHarness();
            harness.Register(Steadfast("Steadfast"));
            harness.Register(Steadfast("Battleborn"));
            IUnit unit = harness.BuildUnit("P1", 1, "Steadfast", "Battleborn");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            var sink = new TokenClearRollSink();
            sink.ApplyFrom(Ops(harness, unit));

            Assert.That(sink.Entries.Single(), Is.EqualTo((TokenType.Shaken, 4)));
        }

        [Test]
        public void Sink_ClampsOutOfRangeThresholds_AndKeepsTokenTypesSeparate()
        {
            var sink = new TokenClearRollSink();
            sink.ClearOn(TokenType.Shaken, 1);            // "always" must stay a real roll -> 2+
            sink.ClearOn(TokenType.Fatigued, 7);          // "never" must stay winnable -> 6+
            sink.ClearOn(TokenType.Fatigued, 5);

            Assert.That(sink.Entries, Is.EqualTo(new[]
            {
                (TokenType.Shaken, 2),
                (TokenType.Fatigued, 5),
            }), "thresholds clamp to [2,6] and fold per token type, not across types.");
        }

        // ---- The single decisive roll, through TokenClearRolls --------------------------------------

        [Test]
        public async Task FoldedRoll_AtTheBoostedThreshold_ClearsOnAThree()
        {
            var sink = new RecordingPresentationSink();
            (World world, IUnit unit) = ShakenUnit(new FixedDiceRoller(3), sink);

            await TokenClearRolls.ResolveAsync(unit, new RuleOperation[]
            {
                new RuleOperation.ClearTokenOnRoll(TokenType.Shaken, 4),
                new RuleOperation.ClearTokenOnRoll(TokenType.Shaken, 3),
            }, new GameOperationServices(world.Context));

            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False,
                "a 3 passes the folded 3+ threshold - the base 4+ entry must not shadow the Boost.");
            Assert.That(sink.Beats.OfType<DiceRolledBeat>().Count(), Is.EqualTo(1),
                "one token type folds to exactly one die, however many entries fired.");
        }

        [Test]
        public async Task ARollAtOrAboveTheThreshold_ClearsTheToken()
        {
            (World world, IUnit unit) = ShakenUnit(dieFace: 4);

            await new GameOperationServices(world.Context).ClearTokenOnRoll(unit, TokenType.Shaken, 4);

            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        [Test]
        public async Task ARollBelowTheThreshold_LeavesTheToken()
        {
            (World world, IUnit unit) = ShakenUnit(dieFace: 3);

            await new GameOperationServices(world.Context).ClearTokenOnRoll(unit, TokenType.Shaken, 4);

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

                await TokenClearRolls.ResolveAsync(unit,
                    new RuleOperation[] { new RuleOperation.ClearTokenOnRoll(TokenType.Shaken, 4) },
                    new GameOperationServices(world.Context));

                int remaining = unit.Tokens.GetTokenCount(TokenType.Shaken);
                Assert.That(remaining, Is.EqualTo(0).Or.EqualTo(1),
                    "The unit is Shaken or it is not; a fractional token means the roll was not decisive.");
                outcomes.Add(remaining);
            }

            Assert.That(outcomes, Is.EquivalentTo(new[] { 0, 1 }),
                "Across 40 seeds a 4+ recovery must sometimes pass and sometimes fail.");
        }

        // #278: a successful recovery announces itself with a Toast (tier-2) banner after the die beat.
        [Test]
        public async Task ASuccessfulClear_PresentsAToastBanner()
        {
            var sink = new RecordingPresentationSink();
            (World world, IUnit unit) = ShakenUnit(new FixedDiceRoller(4), sink);

            await new GameOperationServices(world.Context).ClearTokenOnRoll(unit, TokenType.Shaken, 4);

            BannerBeat? banner = sink.Beats.OfType<BannerBeat>().SingleOrDefault();
            Assert.That(banner, Is.Not.Null, "shedding Shaken presents a banner beat.");
            Assert.That(banner!.Tier, Is.EqualTo(EBannerTier.Toast), "the recovery banner is a Toast (tier 2).");
            Assert.That(banner.BannerText, Does.Contain("no longer Shaken"));
        }

        [Test]
        public async Task AFailedClear_PresentsNoBanner()
        {
            var sink = new RecordingPresentationSink();
            (World world, IUnit unit) = ShakenUnit(new FixedDiceRoller(3), sink);

            await new GameOperationServices(world.Context).ClearTokenOnRoll(unit, TokenType.Shaken, 4);

            Assert.That(sink.Beats.OfType<BannerBeat>().Any(), Is.False,
                "a failed recovery keeps its die beat but earns no banner.");
        }

        private static IReadOnlyList<RuleOperation> Ops(TestRuleHarness harness, IUnit unit) =>
            harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit));

        private static (World, IUnit) ShakenUnit(int dieFace) => ShakenUnit(new FixedDiceRoller(dieFace));

        private static (World, IUnit) ShakenUnit(IDiceRoller roller, IPresentationSink? sink = null)
        {
            var world = World.Build(roller, sink);
            IUnit unit = world.Unit.GetValue();
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
            return (world, unit);
        }

        /// <summary>A one-model army, registered so GameOperationServices can resolve the unit's binding.</summary>
        private sealed class World
        {
            public TestGameContext Context = null!;
            public DataBinding<UnitData> Unit = null!;

            public static World Build(IDiceRoller roller, IPresentationSink? sink = null)
            {
                var store = GameDataStore.GameDataStoreBuilder.GetDefault();
                var context = new TestGameContext(store, roller, presenter: sink == null
                    ? null : new LocalPresenter(sink, new InstantPresentationClock()));
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
