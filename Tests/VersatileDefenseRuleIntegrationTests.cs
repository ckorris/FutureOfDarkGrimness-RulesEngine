using System;
using System.Collections.Generic;
using System.Linq;
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
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197's Versatile Defense (21 refs), the facet P5a deferred:
    //
    //   "When a unit where all models have this rule is deployed or activated, pick one effect: when shot
    //    or charged from over 9in away, the unit either gets +1 to defense rolls, or enemy units get -1 to
    //    hit rolls against it. This effect lasts until the units' next activation."
    //
    // Three things separate it from the P5a rules that shipped, and each has tests below:
    //   * the LIFETIME. "Until the units' next activation" must survive the end of the activation that
    //     granted it AND the round boundary, because the whole point is to be live while the opponent
    //     shoots. ELifetime.UntilNextActivation, swept by ActivationStartStage.
    //   * TWO trigger hooks. Deployment as well as activation start - so the buff exists before the unit
    //     has ever activated. The deployment arm cannot use a once-per-X cost (the "used" marker is keyed
    //     on the RULE name, so it would still be closed at the unit's first activation), hence Cost.Free.
    //   * the ALL-MODELS gate on the CHOICE, not on the effect. Which only works because GatherOffers now
    //     hands the invocation its Definition; without that, Condition.AllModelsHaveThisRule silently
    //     evaluates to true and the gate is no gate at all.
    //
    // The two effects themselves are Sturdy's and Changebound's bodies verbatim - both already shipped and
    // covered - so what is asserted here is that the right one lands, at the right time, for the right long.
    [TestFixture]
    public class VersatileDefenseRuleIntegrationTests
    {
        private const string RuleName = "Versatile Defense";
        private const string GuardHelper = "Versatile Defense (Guard)";
        private const string EvasionHelper = "Versatile Defense (Evasion)";
        private const string GuardLabel = "+1 to defense rolls";
        private const string EvasionLabel = "-1 to enemy hit rolls";

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _player;
        private TeamData _team = null!;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = new RuleResolver();
            _resolver.Register(VersatileDefense());
            _resolver.Register(Guard());
            _resolver.Register(Evasion());

            _player = new PlayerID(Guid.NewGuid());
            _team = new TeamData(0, new List<PlayerID> { _player });
            var zone = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES);
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_team] = _store.GetDataBinding<RectangularZone>(_store.Create(zone)),
            };
        }

        // --- the rule under test, shaped exactly as the shipped supplement authors it ---

        private static SpecialRuleDefinition VersatileDefense() => new(RuleName,
            Array.Empty<HookEntry>(),
            new[]
            {
                SelfGrant(EHookID.Deployment_OnUnitDeployed, new Cost.Free(), GuardLabel, GuardHelper),
                SelfGrant(EHookID.Deployment_OnUnitDeployed, new Cost.Free(), EvasionLabel, EvasionHelper),
                SelfGrant(EHookID.Activation_OnActivationStart, new Cost.OncePerActivation(),
                    GuardLabel, GuardHelper),
                SelfGrant(EHookID.Activation_OnActivationStart, new Cost.OncePerActivation(),
                    EvasionLabel, EvasionHelper),
            });

        private static ActivatedAbility SelfGrant(EHookID hook, Cost cost, string label, string grantedRule) =>
            new(hook, cost,
                new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                    RequireLineOfSight: false),
                new Effect.AddRule(grantedRule, ELifetime.UntilNextActivation),
                new Condition.AllModelsHaveThisRule(),
                Label: label);

        /// <summary>Sturdy's body: +1 to defense rolls when shot or charged from over 9in away.</summary>
        private static SpecialRuleDefinition Guard() => new(GuardHelper,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.And(new Condition.AttackedFromOverInches(9f),
                        new Condition.AllModelsHaveThisRule()),
                    new Effect.RollModifier(ERollKind.Save, +1), ELifetime.ThisAttack, ERuleSeat.Subject),
            },
            Array.Empty<ActivatedAbility>());

        /// <summary>Changebound's body: enemies attacking from over 9in away get -1 to hit.</summary>
        private static SpecialRuleDefinition Evasion() => new(EvasionHelper,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.And(new Condition.AttackedFromOverInches(9f),
                        new Condition.AllModelsHaveThisRule()),
                    new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject),
            },
            Array.Empty<ActivatedAbility>());

        private TestRuleHarness Harness()
        {
            var harness = new TestRuleHarness();
            harness.Register(VersatileDefense());
            harness.Register(Guard());
            harness.Register(Evasion());
            return harness;
        }

        // --- the lifetime ---

        [Test]
        public void ChosenEffect_IsGrantedUntilTheUnitsNextActivation()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2, RuleName);
            GrantEffect(harness, unit, GuardLabel);

            Token grant = unit.Tokens.GetAllTokens(TokenType.RuleGrant).Single();
            var payload = (TokenPayload.RuleGrant)grant.Payload!;

            Assert.That(payload.RuleName, Is.EqualTo(GuardHelper));
            Assert.That(payload.Lifetime, Is.EqualTo(ELifetime.UntilNextActivation));
            Assert.That(grant.ClearTrigger,
                Is.EqualTo(new TokenClearTrigger.CustomHook(EHookID.Activation_OnActivationStart)),
                "the lifetime is realized as a clear at the unit's own next activation start - no new " +
                "TokenClearTrigger variant needed, CustomHook already says exactly this.");
        }

        [Test]
        public void TheGrant_SurvivesTheActivationThatMadeItAndTheRoundBoundary()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2, RuleName);
            GrantEffect(harness, unit, GuardLabel);

            var clearService = new TokenClearService();
            var containers = new List<ITokenContainer> { unit.Tokens };

            clearService.ClearForHook(EHookID.Activation_OnEndOfActivation, containers);
            Assert.That(unit.Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty,
                "ThisActivation would have died here - and the buff has to outlive its own activation, " +
                "or it is never live when the opponent shoots.");

            clearService.ClearForHook(EHookID.Round_OnRoundEnd, containers);
            Assert.That(unit.Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty,
                "ThisRound would have died here - and a unit activating late in round 1 must still be " +
                "buffed early in round 2, before it activates again.");

            clearService.ClearForHook(EHookID.Activation_OnActivationStart, containers);
            Assert.That(unit.Tokens.GetAllTokens(TokenType.RuleGrant), Is.Empty,
                "and it dies exactly at the next activation start, which is where the re-pick happens.");
        }

        // --- what the effects actually do ---

        [Test]
        public void Guard_GivesPlusOneDefense_OnlyBeyondNineInches()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2, RuleName);
            IUnit attacker = harness.BuildUnit("P2", 1);
            GrantEffect(harness, unit, GuardLabel);

            Assert.That(NetSave(harness, attacker, unit, distanceInches: 12f), Is.EqualTo(1),
                "the granted helper must be read back and fire - a grant nothing consumes is the Breath " +
                "Attack failure mode.");
            Assert.That(NetSave(harness, attacker, unit, distanceInches: 6f), Is.EqualTo(0),
                "'from over 9 inches away' - a close-range attack is not covered.");
        }

        [Test]
        public void Evasion_GivesMinusOneToEnemyHitRolls_OnlyBeyondNineInches()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2, RuleName);
            IUnit attacker = harness.BuildUnit("P2", 1);
            GrantEffect(harness, unit, EvasionLabel);

            Assert.That(NetHit(harness, attacker, unit, distanceInches: 12f), Is.EqualTo(-1));
            Assert.That(NetHit(harness, attacker, unit, distanceInches: 6f), Is.EqualTo(0));
        }

        [Test]
        public void PickingOneEffect_DoesNotConferTheOther()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2, RuleName);
            IUnit attacker = harness.BuildUnit("P2", 1);
            GrantEffect(harness, unit, GuardLabel);

            Assert.That(NetHit(harness, attacker, unit, distanceInches: 12f), Is.EqualTo(0),
                "choosing the defense-roll effect must not also confer the to-hit debuff.");
        }

        // --- the all-models gate on the choice ---

        [Test]
        public void TheChoice_IsOffered_WhenEveryModelHasTheRule()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 3, RuleName);

            Assert.That(harness.OfferAbilities(new ActivationStartContext(unit)).Select(o => o.Ability.Label),
                Is.EqualTo(new[] { GuardLabel, EvasionLabel }),
                "both labelled effects, in authored order.");
        }

        [Test]
        public void TheChoice_IsNotOffered_WhenOnlySomeModelsHaveTheRule()
        {
            // "a unit where ALL models have this rule". The rule is on one model of two, so the unit does
            // not qualify - but the offer is gathered from that model's carrier, so only the ability's
            // availability condition can stop it. Before GatherOffers passed the Definition through,
            // AllModelsHaveThisRule took its "no rule identity to check" arm and returned true, and this
            // unit was offered a buff it is not entitled to.
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2);
            ((ModelData)unit.Models[0]).AttachRuleDefinition(harness.Resolver.Resolve(RuleName));

            Assert.That(harness.OfferAbilities(new ActivationStartContext(unit)), Is.Empty);
        }

        [Test]
        public void TheChoice_IsOfferedAgain_OnceTheOddModelOutIsDead()
        {
            // AllModelsHaveThisRule counts only LIVING models, so a unit whose non-carrier casualty has
            // been removed does qualify. Pins that the gate is evaluated at offer time, not at army load.
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2);
            ((ModelData)unit.Models[0]).AttachRuleDefinition(harness.Resolver.Resolve(RuleName));

            Assert.That(harness.OfferAbilities(new ActivationStartContext(unit)), Is.Empty);

            ((ModelData)unit.Models[1]).DealWounds(((ModelData)unit.Models[1]).TotalWounds);

            Assert.That(harness.OfferAbilities(new ActivationStartContext(unit)), Has.Count.EqualTo(2));
        }

        // --- the once-per-activation gate, and why the deployment arm cannot share it ---

        [Test]
        public void TakingOneEffect_SpendsTheRulesGate_ForItsSibling()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2, RuleName);

            AbilityOffer first = harness.OfferAbilities(new ActivationStartContext(unit)).First();
            OperationApplier.ApplyTokenOperations(harness.Accept(first, unit));

            Assert.That(harness.OfferAbilities(new ActivationStartContext(unit)), Is.Empty,
                "the cost is keyed on the rule name, so choosing one effect closes the whole rule for the " +
                "activation - which is what 'pick one effect' means.");
        }

        [Test]
        public void TheDeploymentArmIsFree_SoPickingAtDeploymentLeavesTheActivationPickOpen()
        {
            // The reason the deployment abilities are Cost.Free. A OncePerActivation paid here would grant
            // an ActivationEnd-clearing "AbilityUsed:Versatile Defense" marker keyed on the RULE, and that
            // marker only clears at the END of the unit's first activation - so the unit would arrive at
            // that activation's start with its own gate already shut and never get to re-pick.
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 2, RuleName);

            AbilityOffer atDeployment = harness.OfferAbilities(new UnitDeployedContext(unit))
                .Single(o => o.Ability.Label == GuardLabel);
            OperationApplier.ApplyTokenOperations(harness.Accept(atDeployment, unit));

            Assert.That(unit.Tokens.HasToken(new TokenType("AbilityUsed:" + RuleName)), Is.False,
                "no gate marker is left behind at all.");
            Assert.That(harness.OfferAbilities(new ActivationStartContext(unit)), Has.Count.EqualTo(2),
                "so both effects are still on the table when the unit finally activates.");
        }

        // --- through the real stages ---

        [Test]
        public async Task ActivationStartStage_ClearsThePreviousChoice_BeforeTheUnitPicksAgain()
        {
            // The sweep has to run through the real stage, before it gathers: otherwise a unit that picked
            // Guard last activation and Evasion this one would be holding BOTH.
            DataBinding<UnitData> unit = MakeUnit("Havoc Brothers", RuleName);
            var requester = new EffectChoiceRequester(chooseIndex: 0);
            await RunActivationStart(requester, unit);

            Assert.That(HeldGrants(unit), Is.EqualTo(new[] { GuardHelper }));

            EndActivation(unit);

            requester = new EffectChoiceRequester(chooseIndex: 1);
            await RunActivationStart(requester, unit);

            Assert.That(HeldGrants(unit), Is.EqualTo(new[] { EvasionHelper }),
                "exactly the newly chosen effect - the previous activation's choice is swept first.");
        }

        [Test]
        public async Task ActivationStartStage_AsksOnceAndAppliesTheChoice()
        {
            DataBinding<UnitData> unit = MakeUnit("Havoc Brothers", RuleName);
            var requester = new EffectChoiceRequester(chooseIndex: 1);

            await RunActivationStart(requester, unit);

            Assert.That(requester.ChoiceCount, Is.EqualTo(1), "one rule, one pick.");
            Assert.That(requester.ChoiceRequest!.Options.Select(o => o.Label),
                Is.EqualTo(new[] { GuardLabel, EvasionLabel }));
            Assert.That(HeldGrants(unit), Is.EqualTo(new[] { EvasionHelper }));
        }

        [Test]
        public async Task DeployStage_AsksWhichEffect_RatherThanYesNo_AndGrantsTheChosenOne()
        {
            DataBinding<UnitData> unit = MakeUnit("Havoc Brothers", RuleName);
            MakeArmy(unit);

            var requester = new EffectChoiceRequester(chooseIndex: 1);
            await RunDeployUnit(requester, unit);

            Assert.That(requester.YesNoCount, Is.EqualTo(0),
                "'pick one effect' is mandatory - the deploy stage's Yes/No shape is for the 'you MAY' " +
                "rules (Vanguard, Fanatic) and must not be used here.");
            Assert.That(requester.ChoiceRequest, Is.Not.Null);
            Assert.That(requester.ChoiceRequest!.RuleName, Is.EqualTo(RuleName));
            Assert.That(requester.ChoiceRequest!.Options.Select(o => o.Label),
                Is.EqualTo(new[] { GuardLabel, EvasionLabel }),
                "both labelled effects reach the player, in authored order.");
            Assert.That(HeldGrants(unit), Is.EqualTo(new[] { EvasionHelper }),
                "the effect the player picked, and only that one, is live straight out of deployment.");
        }

        [Test]
        public async Task DeployStage_UnitWithoutTheRule_IsAskedNothingExtra()
        {
            DataBinding<UnitData> unit = MakeUnit("Grunts");
            MakeArmy(unit);

            var requester = new EffectChoiceRequester(chooseIndex: 0);
            await RunDeployUnit(requester, unit);

            Assert.That(requester.ChoiceRequest, Is.Null);
            Assert.That(requester.YesNoCount, Is.EqualTo(0));
            Assert.That(HeldGrants(unit), Is.Empty);
        }

        [Test]
        public async Task DeployedThenActivated_ThePickIsMadeTwice_AndOnlyTheLatestIsHeld()
        {
            // The end-to-end shape of "deployed OR activated": the unit is buffed from the moment it is on
            // the table, and re-chooses when it activates - it does not accumulate.
            DataBinding<UnitData> unit = MakeUnit("Havoc Brothers", RuleName);
            MakeArmy(unit);

            await RunDeployUnit(new EffectChoiceRequester(chooseIndex: 0), unit);
            Assert.That(HeldGrants(unit), Is.EqualTo(new[] { GuardHelper }));

            await RunActivationStart(new EffectChoiceRequester(chooseIndex: 1), unit);
            Assert.That(HeldGrants(unit), Is.EqualTo(new[] { EvasionHelper }));
        }

        // --- helpers ---

        private static void GrantEffect(TestRuleHarness harness, IUnit unit, string label)
        {
            AbilityOffer offer = harness.OfferAbilities(new ActivationStartContext(unit))
                .Single(o => o.Ability.Label == label);
            OperationApplier.ApplyTokenOperations(harness.Accept(offer, unit));
        }

        /// <summary>What ReconcileEndOfActivationStage does between two activations: retire the
        /// once-per-activation "used" markers. Note it does NOT touch the UntilNextActivation grant - that
        /// is the whole point of the lifetime, and ActivationStartStage is what sweeps it.</summary>
        private static void EndActivation(DataBinding<UnitData> unit)
        {
            var containers = new List<ITokenContainer> { unit.GetValue().Tokens };
            containers.AddRange(unit.GetValue().Models.Select(model => model.Tokens));
            new TokenClearService().ClearForHook(EHookID.Activation_OnEndOfActivation, containers);
        }

        private static string[] HeldGrants(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant)
                .Select(t => t.Payload).OfType<TokenPayload.RuleGrant>()
                .Select(g => g.RuleName).ToArray();

        private static int NetSave(TestRuleHarness harness, IUnit attacker, IUnit defender,
            float distanceInches)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject,
                new HitRollCompleteContext(attacker, defender, TestDice.Faces(4), distanceInches)));
            return sink.Net(ERollKind.Save);
        }

        private static int NetHit(TestRuleHarness harness, IUnit attacker, IUnit defender,
            float distanceInches)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject,
                new HitRollModifierContext(attacker, defender, distanceInches)));
            return sink.Net(ERollKind.Hit);
        }

        /// <summary>Drives the real ActivationStartStage for one unit.</summary>
        private async Task RunActivationStart(IPlayerRequestByID requester, DataBinding<UnitData> unit)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, ruleResolver: _resolver);
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);

            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("finish");
            await stage.Enter(unitContext);
        }

        /// <summary>Drives the real DeployUnitStage for one unit set as current deployer, so the deploy
        /// placement and the Deployment_OnUnitDeployed offers both run through production code.</summary>
        private async Task RunDeployUnit(IPlayerRequestByID requester, DataBinding<UnitData> unit)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, ruleResolver: _resolver);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones)
            {
                CurrentDeployingUnit = unit,
            };

            var stage = new DeployUnitStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            stage.OnFinish.Bind("finish");
            await stage.Enter(deployment);
        }

        private DataBinding<UnitData> MakeUnit(string name, params string[] ruleNames)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            foreach (string ruleName in ruleNames)
            {
                binding.GetValue().AttachRuleDefinition(_resolver.Resolve(ruleName));
            }
            return binding;
        }

        private void MakeArmy(params DataBinding<UnitData>[] units)
        {
            _store.Create(new ArmyData(_player, units.ToList()));
        }
    }

    /// <summary>Answers what the two stages under test ask: the mandatory effect choice (always the same
    /// index) and the deploy placement. Counts Yes/No requests so a test can prove the choice did NOT come
    /// through the optional-ability path, and captures the choice request to assert on its options.</summary>
    internal sealed class EffectChoiceRequester : IPlayerRequestByID
    {
        private readonly int _chooseIndex;

        public int YesNoCount { get; private set; }
        public int ChoiceCount { get; private set; }
        public ChooseAbilityEffectRequest? ChoiceRequest { get; private set; }

        public EffectChoiceRequester(int chooseIndex) => _chooseIndex = chooseIndex;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case ChooseAbilityEffectRequest choice:
                    ChoiceCount++;
                    ChoiceRequest = choice;
                    return Task.FromResult((TReply)(object)_chooseIndex);

                case YesNoRequest:
                    YesNoCount++;
                    return Task.FromResult((TReply)(object)false);

                case PlaceObjectsRequest<ModelData> place:
                    List<PlacedObjectEntry<ModelData>> entries = place.ModelsToPlace
                        .Select(m => new PlacedObjectEntry<ModelData>(m, new Position(10f, 10f)))
                        .ToList();
                    return Task.FromResult(
                        (TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
            }

            return Task.FromResult(default(TReply)!);
        }
    }
}
