using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    // #033 Slice 0 — Caster(X) spell-token economy, driven through the real StartOfRoundExtraActionStage
    // (the same stage that grants tokens in a live game, every round including round 1):
    //  - A Caster(X) unit gains X SpellTokens at the start of a round.
    //  - Unspent tokens carry over between rounds, but the running total is clamped at MAX_SPELL_TOKENS.
    //  - A non-Caster unit gains nothing.
    [TestFixture]
    public class CasterRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task RoundStart_Caster_GrantsRatingTokens()
        {
            DataBinding<UnitData> caster = MakeUnit("Wizards", casterRating: 2);

            await RunRoundStart(roundCount: 1);

            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2),
                "a Caster(2) unit gains 2 spell tokens at the start of round 1");
        }

        [Test]
        public async Task SpellTokens_CarryOver_ClampedAtMax()
        {
            DataBinding<UnitData> caster = MakeUnit("Wizards", casterRating: 2);

            for (int round = 1; round <= 4; round++)
            {
                await RunRoundStart(round);
            }

            // 2 tokens/round across 4 rounds = 8 uncapped; the grant clamps the carried-over total to the cap.
            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens),
                Is.EqualTo(GameWideConstants.MAX_SPELL_TOKENS),
                "unspent tokens carry over but never exceed the cap");
        }

        [Test]
        public async Task RoundStart_NonCaster_GrantsNoTokens()
        {
            DataBinding<UnitData> plain = MakeUnit("Warriors", casterRating: null);

            await RunRoundStart(roundCount: 1);

            Assert.That(plain.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "a unit without the Caster rule gains no spell tokens");
        }

        // #033 Slice 1 — a damage spell's WithRules are pre-resolved to weapon-scoped ResolvedRules at army
        // load (where the resolver is live), so the cast stage can attach them without a resolver. A plain
        // (arg-less) weapon rule resolves directly.
        [Test]
        public void ResolveSpells_DamageSpell_PreResolvesPlainWeaponRule()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            var armyFile = new ArmyListFile
            {
                Spells = new()
                {
                    new SpellDefinition("Hex", 2,
                        new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                        new Effect.DealHits(2, new[] { "Bane" }, ArmorPenetration: 1)),
                },
            };

            IReadOnlyList<RuntimeSpell> spells = ArmyListSpellResolution.ResolveSpells(armyFile, resolver);

            Assert.That(spells, Has.Count.EqualTo(1));
            Assert.That(spells[0].Threshold, Is.EqualTo(2));
            Assert.That(spells[0].WeaponRules.Select(r => r.Definition.Name), Does.Contain("Bane"),
                "a damage spell's WithRules resolve to weapon-scoped ResolvedRules at load");
        }

        // A numeric weapon rule ("Blast(3)") parses its argument so the resolved rule carries Arg(0)=3.
        [Test]
        public void ResolveSpells_NumericWeaponRule_ParsesArgument()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            var armyFile = new ArmyListFile
            {
                Spells = new()
                {
                    new SpellDefinition("Boom", 1,
                        new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                        new Effect.DealHits(1, new[] { "Blast(3)" })),
                },
            };

            ResolvedRule blast = ArmyListSpellResolution.ResolveSpells(armyFile, resolver).Single().WeaponRules.Single();

            Assert.That(blast.Definition.Name, Is.EqualTo("Blast"));
            Assert.That(((RuleArgument.Int)blast.Arguments[0]).Value, Is.EqualTo(3));
        }

        private async Task RunRoundStart(int roundCount)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NoRequestsRequester());
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount));
        }

        private DataBinding<UnitData> MakeUnit(string name, int? casterRating)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                // Off-origin so the stage's reserve-arrival pass treats the unit as already deployed.
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (casterRating.HasValue)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                    new RuleArgument[] { new RuleArgument.Int(casterRating.Value) }));
            }

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // The round-start spell-token grant fires no player requests; any request signals a wiring bug.
    internal sealed class NoRequestsRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
            => throw new System.InvalidOperationException(
                "No player request expected during the round-start spell-token grant; got " + request.GetType());
    }
}
