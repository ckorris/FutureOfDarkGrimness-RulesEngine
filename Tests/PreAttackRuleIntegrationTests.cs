using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #100 #2, slice 2a — the dedicated pre-attack stage. It fires Activation_OnPreAttack once the unit
    // has committed to an attack action, offers the SELF-targeted pre-attack abilities a rule contributes,
    // resolves the chosen one (paying its cost), and hands off to the real attack via OnFinished — layered,
    // so it never consumes the move/attack. Cross-unit (Friend/Foe) targeting is slice 2b. Probed with a
    // trivial once-per-activation self-buff that grants a marker token.
    [TestFixture]
    public class PreAttackRuleIntegrationTests
    {
        private const string RuleName = "Self Buff";

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task PreAttackStage_OffersSelfBuff_ResolvesAndPaysCost_Layered()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedStringChoiceRequester(RuleName));
            (_, TokenType marker) = MakeSelfBuffRule();
            DataBinding<UnitData> unit = MakeUnit(withBuff: true);
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            bool finished = false;
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Hold);
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(unit.GetValue().Tokens.HasToken(marker), Is.True,
                "the chosen self-buff's effect (a token grant) was applied to the bearer");
            Assert.That(unit.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:" + RuleName)), Is.True,
                "the once-per-activation cost marker was paid");
            Assert.That(finished, Is.True, "the stage hands off to the attack via OnFinished");
            Assert.That(unitCtx.HasMoved, Is.False, "pre-attack abilities are layered — no move consumed");
            Assert.That(unitCtx.HasAttacked, Is.False, "pre-attack abilities are layered — no attack consumed");
        }

        [Test]
        public async Task PreAttackStage_PlayerDeclines_NoEffect_StillProceeds()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedStringChoiceRequester(PreAttackStage.DONE_CHOICE));
            (_, TokenType marker) = MakeSelfBuffRule();
            DataBinding<UnitData> unit = MakeUnit(withBuff: true);
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            bool finished = false;
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Hold);
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(unit.GetValue().Tokens.HasToken(marker), Is.False, "declining applies nothing");
            Assert.That(finished, Is.True, "declining still hands off to the attack");
        }

        [Test]
        public async Task PreAttackStage_NoAbilities_ProceedsWithoutPrompting()
        {
            // No pre-attack ability on the unit → no offers → no request issued. The NullPlayerRequester
            // would throw if the stage asked anything, so reaching OnFinished proves it didn't.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(withBuff: false);
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            bool finished = false;
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Charge);
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(finished, Is.True, "no offers → straight to the attack");
        }

        // --- Helpers ---

        // A self-targeted pre-attack ability, once per activation, granting the bearer a marker token.
        private static (SpecialRuleDefinition def, TokenType marker) MakeSelfBuffRule()
        {
            var marker = new TokenType("PreAttackBuffFired");
            var ability = new ActivatedAbility(
                EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            var def = new SpecialRuleDefinition(RuleName, Array.Empty<HookEntry>(), new[] { ability });
            return (def, marker);
        }

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private DataBinding<UnitData> MakeUnit(bool withBuff)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Test Unit", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (withBuff)
            {
                (SpecialRuleDefinition def, _) = MakeSelfBuffRule();
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(RuleName, def));
            }

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
