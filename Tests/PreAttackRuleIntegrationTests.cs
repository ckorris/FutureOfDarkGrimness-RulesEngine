using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
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
        private const string FriendRuleName = "Friend Buff";
        private const string FoeRuleName = "Foe Mark";

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

        // --- Slice 2b: cross-unit targeting ---

        [Test]
        public async Task PreAttackStage_FriendAbility_AppliesToTheChosenAlly_NotTheBearer()
        {
            (SpecialRuleDefinition friendDef, TokenType marker) = MakeFriendBuffRule();
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), friendDef);
            DataBinding<UnitData> ally = MakeUnitAt(new Position(5f, 0f), null); // friendly (same player), within 12"

            var ctx = new TriggeredMoveTestContext(_store, new CannedPreAttackRequester(FriendRuleName, ally));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);

            bool finished = false;
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Hold);
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(ally.GetValue().Tokens.HasToken(marker), Is.True, "the buff landed on the chosen ally");
            Assert.That(bearer.GetValue().Tokens.HasToken(marker), Is.False, "and not on the bearer");
            Assert.That(bearer.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:" + FriendRuleName)), Is.True,
                "the bearer paid the cost");
            Assert.That(finished, Is.True);
        }

        [Test]
        public async Task PreAttackStage_FoeAbilityWithNoEnemiesInRange_NotOffered()
        {
            // A "pick an enemy" ability with no enemy on the table has no eligible target, so it is filtered
            // out of the menu and no request is issued (NullPlayerRequester would throw if one were).
            (SpecialRuleDefinition foeDef, _) = MakeFoeMarkRule();
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), foeDef);

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            UnitActionContext unitCtx = NewActivation(ctx, bearer);

            bool finished = false;
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Charge);
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(finished, Is.True, "no valid targets → not offered → straight to the attack");
        }

        [Test]
        public async Task PreAttackStage_CancelTargetSelection_NothingApplied_StillProceeds()
        {
            (SpecialRuleDefinition friendDef, TokenType marker) = MakeFriendBuffRule();
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), friendDef);
            DataBinding<UnitData> ally = MakeUnitAt(new Position(5f, 0f), null);

            // Picks the ability, then cancels the target selection (target = null → Cancelled).
            var ctx = new TriggeredMoveTestContext(_store, new CannedPreAttackRequester(FriendRuleName, target: null));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);

            bool finished = false;
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Hold);
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(ally.GetValue().Tokens.HasToken(marker), Is.False, "cancelling applies nothing");
            Assert.That(bearer.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:" + FriendRuleName)), Is.False,
                "and pays no cost");
            Assert.That(finished, Is.True, "but the stage still proceeds to the attack");
        }

        // --- Slice 2e: representative catalog rules end-to-end ---

        // Furious Buff (real catalog rule): used before attacking, it grants the chosen ally a one-shot
        // Furious — a RuleGrant token with a FirstTrigger clear, which the read-back fires and 2c consumes.
        [Test]
        public async Task FuriousBuff_GrantsAOneShotFuriousToTheChosenAlly()
        {
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), CoreRuleCatalog.FuriousBuff);
            DataBinding<UnitData> ally = MakeUnitAt(new Position(3f, 0f), null);

            var ctx = new TriggeredMoveTestContext(_store, new CannedPreAttackRequester("Furious Buff", ally));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Charge);
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            bool oneShotFurious = ally.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant).Any(t =>
                t.Payload is TokenPayload.RuleGrant rg && rg.RuleName == "Furious"
                && t.ClearTrigger is TokenClearTrigger.FirstTrigger);
            Assert.That(oneShotFurious, Is.True, "the ally gained a consumable Furious grant");
        }

        // Mend (real catalog rule): heals the chosen wounded friendly model — exercising the Heal consumer
        // wired in OperationApplier. Under the fixed roller the D3 resolves to 4, clamped to the 2 wounds taken.
        [Test]
        public async Task Mend_HealsTheChosenWoundedFriendlyModel()
        {
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), CoreRuleCatalog.Mend);
            DataBinding<UnitData> ally = MakeUnitAt(new Position(2f, 0f), null);
            ally.GetValue().Models[0].SetMaxWounds(3); // Tough(3)
            ally.GetValue().Models[0].DealWounds(2);   // 2 of 3 taken
            Assert.That(ally.GetValue().Models[0].WoundsDealt, Is.EqualTo(2f), "precondition: 2 wounds taken");

            var ctx = new TriggeredMoveTestContext(_store, new CannedPreAttackRequester("Mend", ally));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Hold);
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(ally.GetValue().Models[0].WoundsDealt, Is.EqualTo(0f), "Mend healed the wounded ally");
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

        // A Friend-targeted pre-attack ability: pick one friendly unit within 12" and grant it a marker.
        private static (SpecialRuleDefinition def, TokenType marker) MakeFriendBuffRule()
        {
            var marker = new TokenType("FriendBuffFired");
            var ability = new ActivatedAbility(
                EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            var def = new SpecialRuleDefinition(FriendRuleName, Array.Empty<HookEntry>(), new[] { ability });
            return (def, marker);
        }

        // A Foe-targeted pre-attack ability: pick one enemy unit within 18" and mark it.
        private static (SpecialRuleDefinition def, TokenType marker) MakeFoeMarkRule()
        {
            var marker = new TokenType("FoeMarkFired");
            var ability = new ActivatedAbility(
                EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, false),
                new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            var def = new SpecialRuleDefinition(FoeRuleName, Array.Empty<HookEntry>(), new[] { ability });
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

        // Builds a unit at a specific position (so GetIsOnBattlefield sees it as placed), optionally with a
        // rule attached. All units share _player, so they're mutually friendly.
        private DataBinding<UnitData> MakeUnitAt(Position position, SpecialRuleDefinition? rule)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Test Unit", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (rule != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Answers the two requests a pre-attack ability resolution issues: the StringSelectionRequest menu
    // (returns the ability name) and the CancellableSelectionRequest for the target (returns the given
    // target as Selected, or Cancelled when target is null).
    internal sealed class CannedPreAttackRequester : IPlayerRequestByID
    {
        private readonly string _abilityChoice;
        private readonly DataBinding<UnitData>? _target;

        public CannedPreAttackRequester(string abilityChoice, DataBinding<UnitData>? target)
        {
            _abilityChoice = abilityChoice;
            _target = target;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is StringSelectionRequest)
            {
                return Task.FromResult((TReply)(object)_abilityChoice);
            }

            if (request is CancellableSelectionRequest<UnitData>)
            {
                CancellableResult<DataBinding<UnitData>> result = _target != null
                    ? new Selected<DataBinding<UnitData>>(_target)
                    : new Cancelled<DataBinding<UnitData>>();
                return Task.FromResult((TReply)(object)result);
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
