using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #439 - the OPR 3.6.0 spell effects the re-import added to the cast path, driven through the real CastSpellStage
    // (CasterRuleIntegrationTests' harness):
    //  - the spell-token drain ("which loses 2 spell tokens. Note that the target may not spend those spell tokens to
    //    prevent this effect." - Shifting Form, Burn the Heretic, Aether-Cloud Rune): Effect.ConsumeToken on the target,
    //    and the target may not hinder the cast with the tokens it is about to lose;
    //  - the synthetic spell weapon carries the spell's range, so Pierce / Smash's "units over half of this weapon's
    //    range away" facet reads the spell's reach (owner-review D entry in WorkItems/439);
    //  - the once-per-round morale move of Deep Hypnosis / Seductive Invocation keys on the spell's own name, separate
    //    from the Mind Control rule's key (owner-review D entry).
    [TestFixture]
    public class Spells36RuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;
        private PlayerID _enemy;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
            _enemy = new PlayerID(System.Guid.NewGuid());
        }

        private static RuntimeSpell Drain(int count = 2) => new RuntimeSpell(
            new SpellDefinition("Burn the Heretic", 1,
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                new Effect.ConsumeToken(TokenType.SpellTokens, count)),
            System.Array.Empty<ResolvedRule>());

        [Test]
        public async Task DrainSpell_TheTargetLosesTwoSpellTokens()
        {
            var requester = new CannedAssistRequester(tokensPerAssister: 0);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f), Drain());
            DataBinding<UnitData> target = MakeCaster(_enemy, tokens: 5, new Position(14f, 10f));

            await Cast(ctx, caster);

            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0), "cast for 1");
            Assert.That(target.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(3),
                "the target lost exactly 2 of its 5 tokens");
        }

        [Test]
        public async Task DrainSpell_ATargetWithFewerTokens_LosesWhatItHas()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedAssistRequester(tokensPerAssister: 0),
                new FixedDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f), Drain());
            DataBinding<UnitData> target = MakeCaster(_enemy, tokens: 1, new Position(14f, 10f));

            await Cast(ctx, caster);

            Assert.That(target.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0));
        }

        [Test]
        public async Task DrainSpell_TheTargetMayNotHinderWithTheTokensItWouldLose()
        {
            // The target holds exactly the 2 tokens the spell takes: none of them is spendable, so it is never asked,
            // and the face-4 cast succeeds at the base 4+ instead of being hindered to 5+.
            var requester = new CannedAssistRequester(tokensPerAssister: 2);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f), Drain());
            DataBinding<UnitData> target = MakeCaster(_enemy, tokens: 2, new Position(14f, 10f));

            await Cast(ctx, caster);

            Assert.That(requester.AssistPromptCount, Is.EqualTo(0), "nothing it may spend - no hinder prompt");
            Assert.That(target.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "the unhindered cast succeeded and took both tokens");
        }

        [Test]
        public async Task DrainSpell_TheTargetMayStillHinderWithItsOtherTokens()
        {
            // 3 tokens, 2 reserved: it is offered 1, spends it (cast hindered to 5+, the face 4 fails), and keeps the
            // two the spell would have taken.
            int offered = -1;
            var requester = new CannedAssistRequester(tokensPerAssister: 0, perAssister: assist =>
            {
                offered = assist.AvailableTokens;
                return assist.AvailableTokens;
            });
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f), Drain());
            DataBinding<UnitData> target = MakeCaster(_enemy, tokens: 3, new Position(14f, 10f));

            await Cast(ctx, caster);

            Assert.That(offered, Is.EqualTo(1), "3 held - 2 the spell would take");
            Assert.That(target.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2),
                "it spent its one free token on the hinder; the failed cast took nothing");
        }

        [Test]
        public async Task DrainSpell_ANonTargetEnemyCaster_HindersWithEverything()
        {
            int offered = -1;
            var requester = new CannedAssistRequester(tokensPerAssister: 0, perAssister: assist =>
            {
                if (assist.AssistingUnit.GetValue().Name == "Bystanders") offered = assist.AvailableTokens;
                return 0;
            });
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f), Drain());
            DataBinding<UnitData> target = MakeCaster(_enemy, tokens: 2, new Position(14f, 10f));
            DataBinding<UnitData> bystander = MakeCaster(_enemy, tokens: 2, new Position(10f, 14f), name: "Bystanders");

            await Cast(ctx, caster);

            Assert.That(offered, Is.EqualTo(2), "only the picked target keeps tokens back");
            Assert.That(target.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0));
            Assert.That(bystander.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2));
        }

        [Test]
        public void DrainReservation_IsNullForEveryOtherEffect()
        {
            var targets = new[] { MakeCaster(_enemy, tokens: 3, new Position(14f, 10f)) };
            Assert.That(CastSpellStage.DrainReservation(new Effect.DealHits(1, System.Array.Empty<string>()), targets),
                Is.Null);
            Assert.That(CastSpellStage.DrainReservation(
                new Effect.ConsumeToken(new TokenType("Other"), 2), targets), Is.Null);
            Assert.That(CastSpellStage.DrainReservation(
                new Effect.ConsumeToken(TokenType.SpellTokens, 2), targets)!(targets[0].GetValue()), Is.EqualTo(2));
        }

        [Test]
        public void SpellText_DescribesTheDrain()
        {
            string text = SpellText.Describe(Drain().Definition);
            Assert.That(text, Does.StartWith("Loses 2 spell tokens - one enemy unit within 18\""));
        }

        // A Pierce spell (AP(0) + Pierce) at a Defense-4 target, every die a 4: Pierce's "over half of this weapon's
        // range away" AP(+1) makes the save a 5+ (the 4 fails, the 1-wound target dies) only when the target is beyond
        // half the SPELL's 18" range.
        [TestCase(12f, false)]
        [TestCase(6f, true)]
        public async Task PierceSpell_OverHalfTheSpellsRange_GetsAp(float gap, bool survives)
        {
            RuntimeSpell lance = new RuntimeSpell(
                new SpellDefinition("Cerebral Trauma", 1,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.DealHits(1, new[] { "Pierce" }, ArmorPenetration: 0)),
                new[] { new ResolvedRule("Pierce", WeaponRules36Definitions.Pierce) });
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester(), new FixedFaceDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f), lance);
            DataBinding<UnitData> target = MakeGrunts(_enemy, new Position(10f + gap + 1f, 10f));

            await Cast(ctx, caster);

            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0), "it was cast");
            Assert.That(target.GetValue().GetIsAlive(), Is.EqualTo(survives),
                survives ? "within 9\": no AP(+1), the 4 saves" : "beyond 9\": AP(+1), the 4 fails the 5+");
        }

        private static RuntimeSpell Invocation(string name) => new RuntimeSpell(
            new SpellDefinition(name, 1,
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                new Effect.MoraleTestThen(new Effect.TriggeredMove(6f, IsOptional: true, OncePerRoundKey: name))),
            System.Array.Empty<ResolvedRule>());

        [Test]
        public async Task OncePerRoundSpellMove_AUnitMovedByMindControl_CanStillBeMovedByTheSpell()
        {
            var requester = new CannedForcedMoveRequester(dx: 3f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f),
                Invocation("Seductive Invocation"));
            DataBinding<UnitData> victim = MakeGrunts(_enemy, new Position(12f, 10f), quality: 6);
            victim.GetValue().Tokens.AddToken(new Token(
                RuleOperation.InvokeTriggeredMove.OncePerRoundMoveToken("Mind Control"), 1,
                new TokenClearTrigger.RoundEnd()));

            await Cast(ctx, caster);

            Assert.That(requester.Captured, Is.Not.Null, "a different 'way' - the spell may still move it");
            Assert.That(victim.GetValue().Tokens.HasToken(
                RuleOperation.InvokeTriggeredMove.OncePerRoundMoveToken("Seductive Invocation")), Is.True);
        }

        [Test]
        public async Task OncePerRoundSpellMove_ASecondCastThisRound_CannotMoveTheSameUnit()
        {
            var requester = new CannedForcedMoveRequester(dx: 3f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(4));
            DataBinding<UnitData> caster = MakeCaster(_player, tokens: 1, new Position(10f, 10f),
                Invocation("Seductive Invocation"));
            DataBinding<UnitData> victim = MakeGrunts(_enemy, new Position(12f, 10f), quality: 6);
            victim.GetValue().Tokens.AddToken(new Token(
                RuleOperation.InvokeTriggeredMove.OncePerRoundMoveToken("Seductive Invocation"), 1,
                new TokenClearTrigger.RoundEnd()));

            await Cast(ctx, caster);

            Assert.That(requester.Captured, Is.Null, "already moved by Seductive Invocation this round");
            Assert.That(victim.GetValue().ModelBindings[0].GetValue().PositionBinding.GetValue().x,
                Is.EqualTo(12f).Within(0.001f));
        }

        private static async Task Cast(IGameContext ctx, DataBinding<UnitData> caster)
        {
            var unitCtx = new UnitActionContext(ctx, caster);
            unitCtx.Reset(caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private DataBinding<UnitData> MakeCaster(PlayerID owner, int tokens, Position pos, RuntimeSpell? spell = null,
            string name = "Wizards")
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            if (tokens > 0)
            {
                binding.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }

            var army = new ArmyData(owner, new List<DataBinding<UnitData>> { binding });
            if (spell != null) army.SetSpells(new[] { spell });
            _store.Create(army);
            return binding;
        }

        private DataBinding<UnitData> MakeGrunts(PlayerID owner, Position pos, int quality = 4)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, "Grunts", quality: quality, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
