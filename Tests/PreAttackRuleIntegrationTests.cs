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

        // --- Targeting filters ---

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

            List<DataBinding<UnitData>> eligible = PreAttackTargeting.EligibleTargets(bearer, selector, ctx);

            Assert.That(eligible, Is.EqualTo(new[] { artilleryAlly }),
                "only the unit carrying the required rule is a candidate");
        }

        // #197 P6: a joined hero keeps its own rules on its MODEL (#006/#093), so a unit-only RequiredRule
        // scan is blind to the most common Caster in the corpus - a hero attached to a squad. Casting Debuff
        // ("pick one enemy within 18\" with Caster") would then never see a hero caster's unit.
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

            List<DataBinding<UnitData>> eligible = PreAttackTargeting.EligibleTargets(bearer, selector, ctx);

            Assert.That(eligible, Is.EqualTo(new[] { squadWithHero }),
                "a rule carried by a joined model qualifies its unit, and a unit with neither does not");
        }

        // --- Helpers ---

        // A self-targeted pre-attack ability, once per activation, granting the bearer a marker token.
        // Audit BUG-1 — a DealHits pre-attack ability (Breath Attack shape) must actually deal its hits
        // through the save->wound pipeline, not silently no-op after paying its cost. AP(6) pushes the
        // defense-4 save to 10, so the face-4 saves all fail: 3 hits kill the 3-model enemy.
        // (FixedFaceDiceRoller, not FixedDiceRoller — the latter collapses the 3 save rolls to one.)
        [Test]
        public async Task DealHitsAbility_ResolvesHitsThroughSaveAndWoundPipeline()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var requester = new DealHitsPreAttackRequester("Breath", enemy);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(4));

            var breath = new SpecialRuleDefinition("Breath", Array.Empty<HookEntry>(), new[]
            {
                new ActivatedAbility(
                    EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                    new TargetSelector(6f, 1, 1, ETargetAffinity.Foe, false),
                    new Effect.DealHits(3, Array.Empty<string>(), ArmorPenetration: 6),
                    new Condition.Always()),
            });
            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f), breath);

            UnitActionContext unitCtx = NewActivation(ctx, unit);
            bool finished = false;
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Hold);
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(enemy.GetValue().GetIsAlive(), Is.False,
                "3 hits at AP(6) auto-fail every save and kill the 3-model enemy - the ability must deal " +
                "real damage, not just log that it was used");
            Assert.That(finished, Is.True, "the pipeline hands off to the attack via OnFinished");
            Assert.That(unit.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:Breath")), Is.True,
                "the once-per-activation cost gate closed");
        }

        // #164 — a DealHits ability's WithRules must fold exactly as a fired volley's weapon rules do.
        // Blast(3) turns the ability's single hit into 3 (capped at the target's living-model count), so a
        // 3-model enemy is wiped; before this the names were dropped and only one model died. AP(6) pushes
        // the defense-4 save past 6 so every hit converts, isolating the hit COUNT as the thing under test.
        [Test]
        public async Task DealHitsAbility_WithBlast_MultipliesHitsThroughTheSharedFold()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Blast);
            var ctx = new TriggeredMoveTestContext(_store, new DealHitsPreAttackRequester("Breath", enemy),
                new FixedFaceDiceRoller(4), ruleResolver: resolver);

            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f),
                MakeDealHitsRule(baseHits: 1, withRules: new[] { "Blast(3)" }));

            await RunPreAttack(ctx, unit);

            Assert.That(LivingModels(enemy), Is.EqualTo(0),
                "Blast(3) must multiply the ability's 1 hit to 3 - the WithRules names have to reach the " +
                "synthetic weapon and fold at the hit-complete hook, not be dropped");
        }

        // The control that makes the test above mean something: the SAME ability without Blast kills exactly
        // one model. If both tests passed with WithRules ignored, the one above would be asserting nothing.
        [Test]
        public async Task DealHitsAbility_WithoutBlast_DealsOnlyItsBaseHits()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Blast);
            var ctx = new TriggeredMoveTestContext(_store, new DealHitsPreAttackRequester("Breath", enemy),
                new FixedFaceDiceRoller(4), ruleResolver: resolver);

            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f),
                MakeDealHitsRule(baseHits: 1, withRules: Array.Empty<string>()));

            await RunPreAttack(ctx, unit);

            Assert.That(LivingModels(enemy), Is.EqualTo(2),
                "with no WithRules the ability deals its bare 1 hit - the multiply must come from Blast, " +
                "not from the fold itself");
        }

        // A missing resolver (bare harness / pre-rehydration resume) must degrade to AP-only rather than
        // throw: the ability still deals its base hits, it just gets no weapon rules.
        [Test]
        public async Task DealHitsAbility_WithBlastButNoResolver_StillDealsBaseHits()
        {
            DataBinding<UnitData> enemy = MakeEnemyUnitAt(new Position(3f, 0f), modelCount: 3);
            var ctx = new TriggeredMoveTestContext(_store, new DealHitsPreAttackRequester("Breath", enemy),
                new FixedFaceDiceRoller(4));

            DataBinding<UnitData> unit = MakeUnitAt(new Position(0f, 0f),
                MakeDealHitsRule(baseHits: 1, withRules: new[] { "Blast(3)" }));

            await RunPreAttack(ctx, unit);

            Assert.That(LivingModels(enemy), Is.EqualTo(2),
                "no resolver - Blast cannot resolve, so the ability falls back to its base hits instead of " +
                "throwing mid-activation");
        }

        // A Breath-Attack-shaped Foe ability dealing baseHits at AP(6), optionally 'with' weapon rules.
        private static SpecialRuleDefinition MakeDealHitsRule(int baseHits, IReadOnlyList<string> withRules)
            => new SpecialRuleDefinition("Breath", Array.Empty<HookEntry>(), new[]
            {
                new ActivatedAbility(
                    EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                    new TargetSelector(6f, 1, 1, ETargetAffinity.Foe, false),
                    new Effect.DealHits(baseHits, withRules, ArmorPenetration: 6),
                    new Condition.Always()),
            });

        private static async Task RunPreAttack(TriggeredMoveTestContext ctx, DataBinding<UnitData> unit)
        {
            UnitActionContext unitCtx = NewActivation(ctx, unit);
            var stage = new PreAttackStage(ctx, new NoOpLayer<IUnitActionContext>(), EActionType.Hold);
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private static int LivingModels(DataBinding<UnitData> unit)
            => unit.GetValue().Models.Count(model => model.GetIsAlive());

        // An enemy-player unit within pre-attack targeting range.
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

    // CannedPreAttackRequester plus the wound-assignment half a DealHits ability's save->wound pipeline
    // raises (auto-filled, StrafeRequester-style).
    internal sealed class DealHitsPreAttackRequester : IPlayerRequestByID
    {
        private readonly string _abilityChoice;
        private readonly DataBinding<UnitData> _target;

        public DealHitsPreAttackRequester(string abilityChoice, DataBinding<UnitData> target)
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
