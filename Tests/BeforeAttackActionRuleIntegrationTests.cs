using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // "Before attacking" activated abilities (authored at Activation_OnBeforeAttackAction) are menu actions:
    // ChooseActionStage lists each by name, and BeforeAttackActionStage resolves the ONE the player picked
    // (stashed on the context) - target select + effect - then loops back, layered, without consuming the
    // move/attack. This exercises the stage directly (a single stashed offer); the menu-side gating - offered
    // even when the unit cannot attack, filtered out when it has no eligible target - lives in
    // BeforeAttackActionMenuTests. Cross-unit (Friend/Foe) targeting and the DealHits (Breath Attack)
    // save->wound pipeline are covered here too.
    [TestFixture]
    public class BeforeAttackActionRuleIntegrationTests
    {
        private const string RuleName = "Self Buff";
        private const string FriendRuleName = "Friend Buff";

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task ResolvesSelfBuff_PaysCost_AndIsLayered()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            (_, TokenType marker) = MakeSelfBuffRule();
            DataBinding<UnitData> unit = MakeUnit(withBuff: true);
            UnitActionContext unitCtx = NewActivation(ctx, unit);
            StashOffer(ctx, unitCtx, unit);

            bool finished = await RunStage(ctx, unitCtx);

            Assert.That(unit.GetValue().Tokens.HasToken(marker), Is.True,
                "the chosen self-buff's effect (a token grant) was applied to the bearer");
            Assert.That(unit.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:" + RuleName)), Is.True,
                "the once-per-activation cost marker was paid");
            Assert.That(finished, Is.True, "the stage loops back via OnFinished");
            Assert.That(unitCtx.HasMoved, Is.False, "before-attack abilities are layered — no move consumed");
            Assert.That(unitCtx.HasAttacked, Is.False, "before-attack abilities are layered — no attack consumed");
            Assert.That(unitCtx.PendingCustomAction, Is.Null, "the pending offer is cleared after resolving");
        }

        [Test]
        public async Task NoPendingOffer_ReturnsToMenuWithoutPrompting()
        {
            // Defensive: entered with nothing stashed → straight back to Choose Action, no request issued.
            // The NullPlayerRequester would throw if the stage asked anything.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit(withBuff: false);
            UnitActionContext unitCtx = NewActivation(ctx, unit);

            bool finished = await RunStage(ctx, unitCtx);

            Assert.That(finished, Is.True, "no pending offer → straight back to the menu");
        }

        [Test]
        public async Task FriendAbility_AppliesToTheChosenAlly_NotTheBearer()
        {
            (SpecialRuleDefinition friendDef, TokenType marker) = MakeFriendBuffRule();
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), friendDef);
            DataBinding<UnitData> ally = MakeUnitAt(new Position(5f, 0f), null); // friendly (same player), within 12"

            var ctx = new TriggeredMoveTestContext(_store, new CannedTargetRequester(ally));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);
            StashOffer(ctx, unitCtx, bearer);

            bool finished = await RunStage(ctx, unitCtx);

            Assert.That(ally.GetValue().Tokens.HasToken(marker), Is.True, "the buff landed on the chosen ally");
            Assert.That(bearer.GetValue().Tokens.HasToken(marker), Is.False, "and not on the bearer");
            Assert.That(bearer.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:" + FriendRuleName)), Is.True,
                "the bearer paid the cost");
            Assert.That(finished, Is.True);
        }

        [Test]
        public async Task CancelTargetSelection_NothingApplied_StillReturnsToMenu()
        {
            (SpecialRuleDefinition friendDef, TokenType marker) = MakeFriendBuffRule();
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), friendDef);
            DataBinding<UnitData> ally = MakeUnitAt(new Position(5f, 0f), null);

            // Cancels the target selection (target = null → Cancelled).
            var ctx = new TriggeredMoveTestContext(_store, new CannedTargetRequester(target: null));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);
            StashOffer(ctx, unitCtx, bearer);

            bool finished = await RunStage(ctx, unitCtx);

            Assert.That(ally.GetValue().Tokens.HasToken(marker), Is.False, "cancelling applies nothing");
            Assert.That(bearer.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:" + FriendRuleName)), Is.False,
                "and pays no cost");
            Assert.That(finished, Is.True, "but the stage still loops back to the menu");
            Assert.That(unitCtx.PendingCustomAction, Is.Null, "the abandoned offer is cleared");
        }

        // Furious Buff (real catalog rule): used before attacking, it grants the chosen ally a one-shot
        // Furious — a RuleGrant token with a FirstTrigger clear, which the read-back fires and 2c consumes.
        [Test]
        public async Task FuriousBuff_GrantsAOneShotFuriousToTheChosenAlly()
        {
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), CoreRuleCatalog.FuriousBuff);
            DataBinding<UnitData> ally = MakeUnitAt(new Position(3f, 0f), null);

            var ctx = new TriggeredMoveTestContext(_store, new CannedTargetRequester(ally));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);
            StashOffer(ctx, unitCtx, bearer);
            await RunStage(ctx, unitCtx);

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

            var ctx = new TriggeredMoveTestContext(_store, new CannedTargetRequester(ally));
            UnitActionContext unitCtx = NewActivation(ctx, bearer);
            StashOffer(ctx, unitCtx, bearer);
            await RunStage(ctx, unitCtx);

            Assert.That(ally.GetValue().Models[0].WoundsDealt, Is.EqualTo(0f), "Mend healed the wounded ally");
        }

        // --- Targeting filters (AbilityTargeting) ---

        // #153 supplement: TargetSelector.RequiredRule restricts candidates to units carrying the named
        // rule (Re-Position Artillery's "pick one friendly within 6\" with Artillery").
        [Test]
        public void EligibleTargets_RequiredRule_FiltersToUnitsCarryingTheRule()
        {
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), null);
            DataBinding<UnitData> artilleryAlly = MakeUnitAt(new Position(3f, 0f), CoreRuleCatalog.Artillery);
            DataBinding<UnitData> plainAlly = MakeUnitAt(new Position(4f, 0f), null);

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var selector = new TargetSelector(6f, 1, 1, ETargetAffinity.Friend, false,
                RequiredRule: "artillery"); // case-insensitive, like Condition.UnitHasRule

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(bearer, selector, ctx);

            Assert.That(eligible, Is.EqualTo(new[] { artilleryAlly }),
                "only the unit carrying the required rule is a candidate");
        }

        // #197 P6: a joined hero keeps its own rules on its MODEL (#006/#093), so a unit-only RequiredRule
        // scan is blind to the most common Caster in the corpus - a hero attached to a squad.
        [Test]
        public void EligibleTargets_RequiredRule_MatchesARuleCarriedByAJoinedModel()
        {
            DataBinding<UnitData> bearer = MakeUnitAt(new Position(1f, 0f), null);
            DataBinding<UnitData> squadWithHero = MakeUnitAt(new Position(3f, 0f), null);
            DataBinding<UnitData> plainAlly = MakeUnitAt(new Position(4f, 0f), null);

            // The Caster rating lives on the joined hero's model, not on the host unit.
            ((ModelData)squadWithHero.GetValue().Models[0]).AttachRuleDefinition(new ResolvedRule("Caster",
                CoreRuleCatalog.Caster, new RuleArgument[] { new RuleArgument.Int(2) }));

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var selector = new TargetSelector(6f, 1, 1, ETargetAffinity.Friend, false, RequiredRule: "Caster");

            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(bearer, selector, ctx);

            Assert.That(eligible, Is.EqualTo(new[] { squadWithHero }),
                "a rule carried by a joined model qualifies its unit, and a unit with neither does not");
        }

        // --- DealHits (Breath Attack shape) resolves through the save->wound pipeline ---

        // Audit BUG-1 — a DealHits before-attack ability must actually deal its hits through the save->wound
        // pipeline, not silently no-op after paying its cost. AP(6) pushes the defense-4 save to 10, so the
        // face-4 saves all fail: 3 hits kill the 3-model enemy. (FixedFaceDiceRoller, not FixedDiceRoller.)
        [Test]
        public async Task DealHitsAbility_ResolvesHitsThroughSaveAndWoundPipeline()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var ctx = new TriggeredMoveTestContext(_store, new DealHitsTargetRequester(enemy),
                new FixedFaceDiceRoller(4));

            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f), MakeDealHitsRule(baseHits: 3,
                withRules: Array.Empty<string>()));
            UnitActionContext unitCtx = NewActivation(ctx, unit);
            StashOffer(ctx, unitCtx, unit);

            bool finished = await RunStage(ctx, unitCtx);

            Assert.That(enemy.GetValue().GetIsAlive(), Is.False,
                "3 hits at AP(6) auto-fail every save and kill the 3-model enemy - the ability must deal " +
                "real damage, not just log that it was used");
            Assert.That(finished, Is.True, "the pipeline loops back via OnFinished");
            Assert.That(unit.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:Breath")), Is.True,
                "the once-per-activation cost gate closed");
        }

        // #164 — a DealHits ability's WithRules must fold exactly as a fired volley's weapon rules do.
        // Blast(3) turns the ability's single hit into 3 (capped at the target's living-model count).
        [Test]
        public async Task DealHitsAbility_WithBlast_MultipliesHitsThroughTheSharedFold()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Blast);
            var ctx = new TriggeredMoveTestContext(_store, new DealHitsTargetRequester(enemy),
                new FixedFaceDiceRoller(4), ruleResolver: resolver);

            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f),
                MakeDealHitsRule(baseHits: 1, withRules: new[] { "Blast(3)" }));

            await RunDealHits(ctx, unit);

            Assert.That(LivingModels(enemy), Is.EqualTo(0),
                "Blast(3) must multiply the ability's 1 hit to 3 - the WithRules names have to reach the " +
                "synthetic weapon and fold at the hit-complete hook, not be dropped");
        }

        // The control: the SAME ability without Blast kills exactly one model.
        [Test]
        public async Task DealHitsAbility_WithoutBlast_DealsOnlyItsBaseHits()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Blast);
            var ctx = new TriggeredMoveTestContext(_store, new DealHitsTargetRequester(enemy),
                new FixedFaceDiceRoller(4), ruleResolver: resolver);

            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f),
                MakeDealHitsRule(baseHits: 1, withRules: Array.Empty<string>()));

            await RunDealHits(ctx, unit);

            Assert.That(LivingModels(enemy), Is.EqualTo(2),
                "with no WithRules the ability deals its bare 1 hit - the multiply must come from Blast, " +
                "not from the fold itself");
        }

        // A missing resolver (bare harness / pre-rehydration resume) must degrade to AP-only rather than throw.
        [Test]
        public async Task DealHitsAbility_WithBlastButNoResolver_StillDealsBaseHits()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var ctx = new TriggeredMoveTestContext(_store, new DealHitsTargetRequester(enemy),
                new FixedFaceDiceRoller(4));

            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f),
                MakeDealHitsRule(baseHits: 1, withRules: new[] { "Blast(3)" }));

            await RunDealHits(ctx, unit);

            Assert.That(LivingModels(enemy), Is.EqualTo(2),
                "no resolver - Blast cannot resolve, so the ability falls back to its base hits instead of " +
                "throwing mid-activation");
        }

        // --- Helpers ---

        // Gathers the (single) before-attack offer the unit carries and stashes it, exactly as
        // ChooseActionStage does when the player picks the ability from the menu.
        private static void StashOffer(IGameContext ctx, UnitActionContext unitCtx, DataBinding<UnitData> unit)
        {
            AbilityOffer offer = ctx.RuleEvaluator.GatherOffers(
                new BeforeAttackActionContext(unit.GetValue()))[0];
            unitCtx.SetPendingCustomAction(offer);
        }

        private static async Task<bool> RunStage(IGameContext ctx, UnitActionContext unitCtx)
        {
            bool finished = false;
            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);
            return finished;
        }

        private static async Task RunDealHits(TriggeredMoveTestContext ctx, DataBinding<UnitData> unit)
        {
            UnitActionContext unitCtx = NewActivation(ctx, unit);
            StashOffer(ctx, unitCtx, unit);
            await RunStage(ctx, unitCtx);
        }

        private static int LivingModels(DataBinding<UnitData> unit)
            => unit.GetValue().Models.Count(model => model.GetIsAlive());

        // A Breath-Attack-shaped Foe ability dealing baseHits at AP(6), optionally 'with' weapon rules.
        private static SpecialRuleDefinition MakeDealHitsRule(int baseHits, IReadOnlyList<string> withRules)
            => new SpecialRuleDefinition("Breath", Array.Empty<HookEntry>(), new[]
            {
                new ActivatedAbility(
                    EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                    new TargetSelector(6f, 1, 1, ETargetAffinity.Foe, false),
                    new Effect.DealHits(baseHits, withRules, ArmorPenetration: 6),
                    new Condition.Always()),
            });

        // An enemy-player unit within before-attack targeting range.
        private DataBinding<UnitData> MakeEnemyUnitAt(Position position, int modelCount)
        {
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(),
                    new Position(position.x + i * 0.6f, position.z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(enemyPlayer, "Enemy Grunts", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // A self-targeted before-attack ability, once per activation, granting the bearer a marker token.
        private static (SpecialRuleDefinition def, TokenType marker) MakeSelfBuffRule()
        {
            var marker = new TokenType("BeforeAttackBuffFired");
            var ability = new ActivatedAbility(
                EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            var def = new SpecialRuleDefinition(RuleName, Array.Empty<HookEntry>(), new[] { ability });
            return (def, marker);
        }

        // A Friend-targeted before-attack ability: pick one friendly unit within 12" and grant it a marker.
        private static (SpecialRuleDefinition def, TokenType marker) MakeFriendBuffRule()
        {
            var marker = new TokenType("FriendBuffFired");
            var ability = new ActivatedAbility(
                EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            var def = new SpecialRuleDefinition(FriendRuleName, Array.Empty<HookEntry>(), new[] { ability });
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

    // Answers the one request a stashed before-attack ability issues: the CancellableSelectionRequest for
    // the target (returns the given target as Selected, or Cancelled when target is null).
    internal sealed class CannedTargetRequester : IPlayerRequestByID
    {
        private readonly DataBinding<UnitData>? _target;

        public CannedTargetRequester(DataBinding<UnitData>? target) => _target = target;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
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

    // A DealHits before-attack ability issues the target selection plus the wound-assignment its save->wound
    // pipeline raises (auto-filled, StrafeRequester-style).
    internal sealed class DealHitsTargetRequester : IPlayerRequestByID
    {
        private readonly DataBinding<UnitData> _target;

        public DealHitsTargetRequester(DataBinding<UnitData> target) => _target = target;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is CancellableSelectionRequest<UnitData>)
            {
                CancellableResult<DataBinding<UnitData>> result = new Selected<DataBinding<UnitData>>(_target);
                return Task.FromResult((TReply)(object)result);
            }

            if (request is AssignWoundsRequest woundRequest)
            {
                var result = new AssignWoundsResults(woundRequest.UnitReceivingWounds, woundRequest.TotalWoundsToAssign);
                result.AutoFill();
                return Task.FromResult((TReply)(object)result);
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
