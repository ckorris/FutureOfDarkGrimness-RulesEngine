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
    // #433 - the OPR 3.6 "Enhanced X" family through the REAL stages: the keep-best folds (Enhanced Guardian's AP(-2),
    // Enhanced Plaguebound's 5-6 wound ignore, Enhanced Steadfast's 3+ recovery, Enhanced Mischievous' 5-6 re-roll,
    // Enhanced Harassing's 6" post-combat move), the activation move (Enhanced Wolfborn's 2D3"). For each shape: base +
    // Enhanced applies exactly the Enhanced value once (the no-stack guard), and Enhanced without its base is inert.
    [TestFixture]
    public class EnhancedCombatRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new ProbabilisticDiceRoller());
        }

        // --- the base rules the deltas sit on (the supplement's shapes) ----------------------------------------------

        private static class Bases
        {
            public static SpecialRuleDefinition Guardian => GdfSweep36Definitions.Guardian();

            /// <summary>Plaguebound / Self-Repair / Protected / Cursed Undead: "ignore wounds on 6+" on most models.</summary>
            public static SpecialRuleDefinition Plaguebound => new("Plaguebound",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.MostModelsHaveThisRule(),
                        new Effect.IgnoreWoundOnRoll(6), ELifetime.ThisAttack, ERuleSeat.Subject),
                },
                System.Array.Empty<ActivatedAbility>());

            /// <summary>Steadfast / Vale Oath: "On a 4+, it stops being Shaken."</summary>
            public static SpecialRuleDefinition Steadfast => new("Steadfast",
                new[]
                {
                    new HookEntry(EHookID.Round_OnRoundStart,
                        new Condition.And(new Condition.TokenPresent(TokenType.Shaken), new Condition.MostModelsHaveThisRule()),
                        new Effect.ClearTokenOnRoll(TokenType.Shaken, 4), ELifetime.ThisRound),
                },
                System.Array.Empty<ActivatedAbility>());

            /// <summary>Mischievous / Scrapper / Vicious / Bestial: a weapon rule, "re-roll unmodified Defense 6s".</summary>
            public static SpecialRuleDefinition Mischievous => new("Mischievous",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.Always(),
                        new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon);

            /// <summary>Wolfborn / Nimble / Rapid Blink / Wave-Step: the D3" activation move (#434 D8).</summary>
            public static SpecialRuleDefinition Wolfborn => new("Wolfborn",
                new[]
                {
                    new HookEntry(EHookID.Activation_OnActivationStart, new Condition.MostModelsHaveThisRule(),
                        new Effect.RepositionAtActivation(new DiceExpression.D3(), 0, Move: true), ELifetime.ThisActivation),
                },
                System.Array.Empty<ActivatedAbility>());
        }

        // --- B2: Enhanced Guardian (and Fortified / Ossified / Warden): AP(-2) ----------------------------------------

        [TestCase(1, 4)]
        [TestCase(2, 4)]
        [TestCase(3, 5)]
        public async Task EnhancedGuardian_WithGuardian_CountsHitsAsApMinusTwo_FlooredAtZero(int ap, int expectedSave)
        {
            DataBinding<UnitData> defender = MakeUnit("Custodians", 10f, 1, Bases.Guardian, Enhanced36Definitions.ApMinusTwo());

            Assert.That(await SaveNeeded(new Weapon("Gun", 24f, 1, ap), defender, 10f), Is.EqualTo(expectedSave),
                $"AP({ap}) less 2, never less 3 (the delta once) and never below AP(0)");
        }

        [Test]
        public async Task EnhancedGuardian_WithoutGuardian_IsInert()
        {
            DataBinding<UnitData> defender = MakeUnit("Pretenders", 10f, 1, Enhanced36Definitions.ApMinusTwo());

            Assert.That(await SaveNeeded(new Weapon("Gun", 24f, 1, 1), defender, 10f), Is.EqualTo(5), "AP(1) lands");
        }

        // --- B2: Enhanced Plaguebound (and Self-Repair / Protected / Cursed Undead): ignore on 5-6 -------------------

        [Test]
        public async Task EnhancedPlaguebound_WithPlaguebound_IgnoresWoundsOnFiveAndSix()
        {
            DataBinding<UnitData> defender = MakeUnit("Daemons", 10f, 1, Bases.Plaguebound, Enhanced36Definitions.IgnoreFivePlus());

            Assert.That(await WoundsFromOneFailedSave(defender), Is.EqualTo(4f / 6f).Within(0.0001f),
                "one roll per wound at the best threshold (5+), not a 6+ roll and then a 5+ roll");
        }

        [Test]
        public async Task EnhancedPlaguebound_WithoutPlaguebound_IsInert()
        {
            DataBinding<UnitData> defender = MakeUnit("Pretenders", 10f, 1, Enhanced36Definitions.IgnoreFivePlus());

            Assert.That(await WoundsFromOneFailedSave(defender), Is.EqualTo(1f).Within(0.0001f));
        }

        // --- B2: Enhanced Steadfast (and Vale Oath): recover on 3+ -------------------------------------------------------

        [Test]
        public void EnhancedSteadfast_WithSteadfast_IsOneRecoveryRoll_AtThreePlus()
        {
            var harness = new TestRuleHarness();
            harness.Register(Bases.Steadfast);
            harness.Register(Enhanced36Definitions.RecoverThreePlus());
            IUnit unit = harness.BuildUnit("P1", 3, "Steadfast", "Enhanced Steadfast");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            var sink = new TokenClearRollSink();
            sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit)));

            Assert.That(sink.Entries, Is.EqualTo(new[] { (TokenType.Shaken, 3) }),
                "the 4+ and the 3+ fold to ONE roll at 3+ - never a second chance");
        }

        [Test]
        public async Task EnhancedSteadfast_TheFoldedRoll_ClearsOnAThree()
        {
            var harness = new TestRuleHarness(new FixedDiceRoller(3));
            harness.Register(Bases.Steadfast);
            harness.Register(Enhanced36Definitions.RecoverThreePlus());
            IUnit unit = harness.BuildUnit("P1", 3, "Steadfast", "Enhanced Steadfast");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            await TokenClearRolls.ResolveAsync(unit, harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit)),
                new GameOperationServices(harness.GameContext));

            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False, "a 3 recovers an Enhanced Steadfast unit");
        }

        [Test]
        public void EnhancedSteadfast_WithoutSteadfast_RollsNothing()
        {
            var harness = new TestRuleHarness();
            harness.Register(Enhanced36Definitions.RecoverThreePlus());
            IUnit unit = harness.BuildUnit("P1", 3, "Enhanced Steadfast");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit)), Is.Empty);
        }

        // --- B2: Enhanced Mischievous (and Scrapper / Vicious / Bestial): re-roll 5-6 ---------------------------------

        [Test]
        public void EnhancedMischievous_WithMischievousOnTheWeapon_RerollsSavesOfFiveAndSix()
        {
            var harness = new TestRuleHarness();
            IUnit goblins = harness.BuildUnit("P1", 3);
            IUnit enemy = harness.BuildUnit("P2", 1);
            var blunderbuss = new Weapon("Blunderbuss", 12f, 1, 0);
            harness.AttachWeaponRule(blunderbuss, Bases.Mischievous);
            ((ModelData)goblins.Models[0]).Weapons.Add(blunderbuss);
            harness.AttachRule(goblins, Enhanced36Definitions.RerollFivePlus());

            var sink = new RerollSink();
            sink.ApplyFrom(harness.EvaluateAll(new SaveRollCompleteContext(goblins, enemy, TestDice.Faces(5, 6)),
                RuleParticipant.Actor(goblins, blunderbuss), RuleParticipant.Subject(enemy)));

            Assert.That(sink.RerollSavesAtOrAbove, Is.EqualTo(5), "the widest band wins: 5-6, one re-roll pass");
        }

        [Test]
        public void EnhancedMischievous_WithoutMischievous_IsInert()
        {
            var harness = new TestRuleHarness();
            IUnit goblins = harness.BuildUnit("P1", 3);
            IUnit enemy = harness.BuildUnit("P2", 1);
            harness.AttachRule(goblins, Enhanced36Definitions.RerollFivePlus());

            var sink = new RerollSink();
            sink.ApplyFrom(harness.EvaluateAll(new SaveRollCompleteContext(goblins, enemy, TestDice.Faces(5, 6)),
                RuleParticipant.Actor(goblins, new Weapon("Club", 0f, 1, 0)), RuleParticipant.Subject(enemy)));

            Assert.That(sink.RerollSavesAtOrAbove, Is.Null);
        }

        // --- B2: Enhanced Harassing (and Guerrilla / Hit & Run): 6" instead of 3" --------------------------------------

        [Test]
        public async Task EnhancedHarassing_WithHarassing_OffersOneSixInchMove()
        {
            var requester = new CapturingMoveRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = InArmy(MakeUnit("Raiders", 0f, 3, CoreRuleCatalog.Harassing,
                Enhanced36Definitions.PostCombatSix()));
            IUnit u = unit.GetValue();

            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u)));

            Assert.That(requester.Asked, Is.EqualTo(1), "the 3\" and the 6\" coalesce into ONE move");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(6f).Within(0.001f), "at the largest budget");
        }

        [Test]
        public async Task EnhancedHarassing_WithoutHarassing_OffersNothing()
        {
            var requester = new CapturingMoveRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = InArmy(MakeUnit("Pretenders", 0f, 3, Enhanced36Definitions.PostCombatSix()));
            IUnit u = unit.GetValue();

            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ctx.RuleEvaluator.EvaluateAll(
                new PostMeleeActionContext(u), RuleParticipant.Actor(u)));

            Assert.That(requester.Asked, Is.EqualTo(0));
        }

        // --- B3: Enhanced Wolfborn (and Nimble / Rapid Blink / Wave-Step): 2D3" ---------------------------------------

        [Test]
        public async Task EnhancedWolfborn_WithWolfborn_OffersOneMove_OfTwoD3()
        {
            DataBinding<UnitData> unit = MakeUnit("Hunters", 10f, 2, Bases.Wolfborn, Enhanced36Definitions.TwoD3());
            var requester = new CapturingMoveRequester();

            await RunActivationStart(unit, requester, new FixedDiceRoller(2));

            Assert.That(requester.Asked, Is.EqualTo(1), "the two D3 entries sum into ONE move");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(4f).Within(0.001f), "D3 + D3, both fixed to 2");
        }

        [Test]
        public async Task EnhancedWolfborn_WithoutWolfborn_OffersNothing()
        {
            DataBinding<UnitData> unit = MakeUnit("Pretenders", 10f, 2, Enhanced36Definitions.TwoD3());
            var requester = new CapturingMoveRequester();

            await RunActivationStart(unit, requester, new FixedDiceRoller(2));

            Assert.That(requester.Asked, Is.EqualTo(0));
        }

        // --- helpers --------------------------------------------------------------------------------------------------

        // The movement machinery looks a moving unit up through its army.
        private DataBinding<UnitData> InArmy(DataBinding<UnitData> unit)
        {
            _store.Create(new ArmyData(unit.GetValue().PlayerID, new List<DataBinding<UnitData>> { unit }));
            return unit;
        }

        private async Task RunActivationStart(DataBinding<UnitData> unit, IPlayerRequestByID requester, IDiceRoller dice)
        {
            InArmy(unit);
            var ctx = new TriggeredMoveTestContext(_store, requester, dice);
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);
            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(unitContext);
        }

        private async Task<int> SaveNeeded(Weapon weapon, DataBinding<UnitData> defender, float distance)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", 0f, 1);
            Place(defender, distance);

            var hitStage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            hitStage.NextStage.Bind("done");
            var hitMetadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            hitMetadata.AddResult(new DetermineHitRollResults(4, 1));
            await hitStage.Enter(hitMetadata);
            Assert.That(hitMetadata.QueryForResult(out RollToHitResults hits), Is.True);

            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Max(p => p.SaveNeeded);
        }

        private async Task<float> WoundsFromOneFailedSave(DataBinding<UnitData> defender)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", 0f, 1);
            Place(defender, 10f);

            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, new Weapon("Gun", 24f, 1, 0), weaponCount: 1);
            var failed = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(2), new PendingSaveRolls(TestDice.Faces(2), 4)),
            };
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failed));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            return result.TotalWoundsToAssign;
        }

        private DataBinding<UnitData> MakeUnit(string name, float z, int models, params SpecialRuleDefinition[] rules)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(i * 1.5f, z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: bindings);
            foreach (SpecialRuleDefinition rule in rules) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void Place(DataBinding<UnitData> unit, float edgeDistance)
        {
            List<DataBinding<ModelData>> models = unit.GetValue().ModelBindings;
            for (int i = 0; i < models.Count; i++)
            {
                models[i].GetValue().SetPosition(new Position(i * 1.5f, edgeDistance + 2f * BaseRadius));
            }
        }

        // Records every movement-path request (its budget) and declines it; answers any Yes/No with yes.
        private sealed class CapturingMoveRequester : IPlayerRequestByID
        {
            public int Asked;
            public DefineMovementPathRequest? Captured;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest:
                        return Task.FromResult((TReply)(object)true);
                    case DefineMovementPathRequest move:
                        Asked++;
                        Captured = move;
                        return Task.FromResult((TReply)(object)new Cancelled<List<ModelMoveEntry>>());
                    default:
                        throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
                }
            }
        }
    }
}
