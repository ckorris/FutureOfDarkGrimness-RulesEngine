using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #419 - Core Rules v3.6.0, Melee Results: "Units in melee don't take Morale Tests from wounds at the end of
    // an activation. Instead, they compare the number of wounds each unit took during this activation, and the
    // unit with the highest total loses, and must take a Morale Test." Pinned through the real stages:
    //  - the item's acceptance case end to end (the REAL MeleeStage graph): a charge whose Impact kills two
    //    models and whose strikes are even is lost by the defender, and every wound is counted exactly once
    //    even though the activation's wound ledger also sees the melee's own wounds;
    //  - wounds taken BEFORE the melee in the same activation count: a Dangerous Terrain test on the charge move
    //    (the REAL MovementExecutor) and a Breath-Attack-shaped ability (the REAL BeforeAttackActionStage);
    //  - the ledger is swept table-wide when an activation starts and ends, so an earlier activation's wounds
    //    never reach the comparison.
    // DetermineMeleeWinnerStageTests / FearRuleIntegrationTests / ScourgeRuleIntegrationTests cover the stage's
    // arithmetic in isolation; CounterRuleIntegrationTests pins that the snapshots swap with the roles.
    [TestFixture]
    public class MeleeResultRuleIntegrationTests
    {
        private static readonly RectangularZone DangerZone = new RectangularZone(3, 5, -2, 2);

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // --- the acceptance case, end to end ------------------------------------------------------------------

        // Every die is a 4. The charger (one Tough(9) model, Quality 4, Defense 5, a 2-attack weapon, Impact(2))
        // charges six 1-wound models (Quality 4, Defense 5, one attack each). Impact: 2 dice hit on 2+ and both
        // saves fail (4 < 5+) - two models die. The charger's 2 swings hit and wound - two more. The 2 survivors
        // strike back for 2 wounds. The strikes are even (2 vs 2); with Impact the defender took 4, so it loses
        // and tests morale (a 4 passes at Quality 4, so the units stay locked and nothing else changes).
        [Test]
        public async Task ImpactKillsTwo_StrikesEven_TheDefenderTookMoreAndLoses()
        {
            var log = new RecordingTextOutput();
            var (ctx, charger, defender) = BuildCharge(log, impact: 2);

            await RunMelee(ctx, charger);

            Assert.That(LivingModels(defender), Is.EqualTo(2), "premise: Impact killed 2, the swings 2 more");
            Assert.That(charger.GetValue().RemainingWounds, Is.EqualTo(7f), "premise: 2 survivors struck back for 2");
            Assert.That(Lines(log), Has.Member(
                "Melee result: Charger took 2 wounds, Defender took 4 wounds - Defender took more and loses the melee."),
                "Impact wounds are wounds the defender took this activation - and each wound counts once, though " +
                "the activation's wound ledger also saw it land");
            Assert.That(Lines(log), Has.Member("Morale test passed (needed 4)."), "the loser takes the Morale Test");
        }

        // The control: the same charge without Impact. 2 swings kill 2 of 6, the 4 survivors strike back for 4 -
        // the charger took more. Pins that the acceptance case's result comes from the Impact wounds.
        [Test]
        public async Task SameChargeWithoutImpact_TheChargerTookMoreAndLoses()
        {
            var log = new RecordingTextOutput();
            var (ctx, charger, _) = BuildCharge(log, impact: 0);

            await RunMelee(ctx, charger);

            Assert.That(Lines(log), Has.Member(
                "Melee result: Charger took 4 wounds, Defender took 2 wounds - Charger took more and loses the melee."));
        }

        // --- wounds taken before the melee in the same activation ----------------------------------------------

        // "during this activation": the charger loses a model to a Dangerous Terrain test on its charge move,
        // then the strikes are even (1 vs 1). It took 2 wounds this activation to the defender's 1, so it loses.
        [Test]
        public async Task ADangerousTerrainWoundOnTheChargeMove_CountsForTheCharger()
        {
            var log = new RecordingTextOutput();
            var ctx = new TestGameContext(_store, new FixedOnesDiceRoller(1), textOutput: log);
            DataBinding<UnitData> charger = MakeUnit("Charger", models: 3, x: 0f);
            DataBinding<UnitData> defender = MakeUnit("Defender", models: 3, x: 12f);

            await CrossDangerousTerrain(ctx, charger);
            Assert.That(WoundsTakenThisActivation.Peek(charger.GetValue()), Is.EqualTo(1f),
                "premise: the Dangerous Terrain wound landed through CasualtyPresentation and was ledgered");

            var combat = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            combat.SetDefender(defender);
            Wound(defender, 1f); // the charger's strikes
            Wound(charger, 1f);  // the strike-back

            DetermineMeleeWinnerResults result = await RunDetermineWinner(ctx, combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon),
                "2 (1 terrain + 1 struck) vs 1 - the charger took more");
            Assert.That(Lines(log), Has.Member("Charger already took 1 wound earlier this activation - they count."));
        }

        // "during this activation", the other combatant: a Breath-Attack-shaped before-attack ability (1 hit at
        // AP(6), so the save fails) kills one enemy model, then the unit charges it and the strikes are even.
        [Test]
        public async Task ABeforeAttackAbilityWound_CountsForTheUnitItStruck()
        {
            DataBinding<UnitData> enemy = MakeUnit("Enemy", models: 3, x: 3f);
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(4),
                playerRequester: new DealHitsTargetRequester(enemy));
            DataBinding<UnitData> breather = MakeUnit("Breather", models: 3, x: 0f, rule: BreathRule());

            await UseBreath(ctx, breather);
            Assert.That(LivingModels(enemy), Is.EqualTo(2), "premise: the breath killed one model");
            Assert.That(WoundsTakenThisActivation.Peek(enemy.GetValue()), Is.EqualTo(1f),
                "premise: the ability's wound landed through ApplyWoundsStage and was ledgered");

            var combat = new CombatActionContext(ctx, breather, isMelee: true, isCharging: true);
            combat.SetDefender(enemy);
            Wound(enemy, 1f);
            Wound(breather, 1f);

            DetermineMeleeWinnerResults result = await RunDetermineWinner(ctx, combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon),
                "the enemy took 2 (1 breath + 1 struck) vs 1 - it loses");
        }

        // --- the ledger's scope is one activation ---------------------------------------------------------------

        // The end of the activation clears the ledger on EVERY unit (the struck unit is not the activated one), so
        // a later activation's melee starts from zero: here the same even exchange is a tie.
        [Test]
        public async Task WoundsFromAnEarlierActivation_DoNotCount()
        {
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(4));
            DataBinding<UnitData> activated = MakeUnit("Charger", models: 3, x: 0f);
            DataBinding<UnitData> struck = MakeUnit("Defender", models: 3, x: 12f);
            WoundsTakenThisActivation.Add(struck.GetValue(), 2f); // shot last activation

            var turn = new SingleTurnContext(ctx, activated.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { activated });
            turn.ChooseUnitToActivate(activated);
            var reconcile = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            reconcile.OnFinished.Bind("done");
            await reconcile.Enter(turn);

            Assert.That(WoundsTakenThisActivation.Peek(struck.GetValue()), Is.EqualTo(0f),
                "the end-of-activation sweep reaches the struck unit too");

            var combat = new CombatActionContext(ctx, activated, isMelee: true, isCharging: true);
            combat.SetDefender(struck);
            Wound(struck, 1f);
            Wound(activated, 1f);

            DetermineMeleeWinnerResults result = await RunDetermineWinner(ctx, combat);

            Assert.That(result.Winner, Is.EqualTo(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
        }

        // Wounds taken between activations (a round-start effect, deployment) must not reach the next activation's
        // melee either, so the START of an activation clears every unit's ledger as well. Drives the real
        // MainUnitActionStage through a pristine back-out (ActivationBackOutTests' shape).
        [Test]
        public async Task TheStartOfAnActivation_ClearsEveryUnitsLedger()
        {
            var requester = new ActivationBackOutTests.ActionMenuRequester { ReplyCancel = true };
            var ctx = new WoundTestContext(_store, requester);
            DataBinding<UnitData> unit = MakeUnit("Activating", models: 1, x: 0f);
            DataBinding<UnitData> bystander = MakeUnit("Bystander", models: 3, x: 12f);
            WoundsTakenThisActivation.Add(bystander.GetValue(), 2f);

            var turn = new SingleTurnContext(ctx, unit.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { unit });
            turn.ChooseUnitToActivate(unit);
            var main = new MainUnitActionStage(ctx, new NoOpLayer<ISingleTurnContext>());
            main.OnBackedOut.Bind("backedOut");
            main.ToReconcileEndOfActivation.Bind("reconcile");
            await main.Enter(turn);

            Assert.That(WoundsTakenThisActivation.Peek(bystander.GetValue()), Is.EqualTo(0f));
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private static IReadOnlyList<string> Lines(RecordingTextOutput log) =>
            log.Entries.Select(entry => entry.Message).ToList();

        private static int LivingModels(DataBinding<UnitData> unit) =>
            unit.GetValue().Models.Count(model => model.GetIsAlive());

        // A wound the melee's own exchange dealt, landed the way ApplyWoundsStage lands it: on the model AND on the
        // activation's wound ledger - so the stage is proven not to count it twice.
        private static void Wound(DataBinding<UnitData> unit, float wounds)
        {
            unit.GetValue().Models.First(model => model.GetIsAlive()).DealWounds(wounds);
            WoundsTakenThisActivation.Add(unit.GetValue(), wounds);
        }

        private static async Task<DetermineMeleeWinnerResults> RunDetermineWinner(IGameContext ctx,
            CombatActionContext combat)
        {
            var stage = new DetermineMeleeWinnerStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnNeedsRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION);
            stage.OnDoesntNeedRollToDecide.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION);
            await stage.Enter(combat);

            combat.QueryForResult(out DetermineMeleeWinnerResults result);
            return result;
        }

        // Every model walks a path through the dangerous zone (DangerousTerrainDiceRuleIntegrationTests' shape) and
        // the roll lands through MovementExecutor.ResolveDangerousTerrain - the move's real wound seam.
        private static async Task CrossDangerousTerrain(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var paths = unit.GetValue().ModelBindings
                .Select(model =>
                {
                    model.GetValue().SetPosition(new Position(0f, 0f));
                    return new ModelMoveEntry(model, new List<Position> { new Position(8f, 0f) });
                })
                .ToList();
            MovementExecutor.DangerousTerrainResult rolled = MovementExecutor.RollDangerousTerrain(ctx, paths,
                new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) }, unit.GetValue());
            await MovementExecutor.ResolveDangerousTerrain(ctx, rolled);
        }

        // Stashes the bearer's (single) before-attack offer and resolves it, as ChooseActionStage would on a pick.
        private static async Task UseBreath(IGameContext ctx, DataBinding<UnitData> bearer)
        {
            var unitCtx = new UnitActionContext(ctx, bearer);
            unitCtx.Reset(bearer);
            unitCtx.SetPendingCustomAction(ctx.RuleEvaluator.GatherOffers(
                new BeforeAttackActionContext(bearer.GetValue()))[0]);

            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        // A Breath-Attack-shaped Foe ability: one hit at AP(6) (BeforeAttackActionRuleIntegrationTests' shape).
        private static SpecialRuleDefinition BreathRule() =>
            new SpecialRuleDefinition("Breath", Array.Empty<HookEntry>(), new[]
            {
                new ActivatedAbility(
                    EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                    new TargetSelector(6f, 1, 1, ETargetAffinity.Foe, false),
                    new Effect.DealHits(1, Array.Empty<string>(), ArmorPenetration: 6),
                    new Condition.Always()),
            });

        private async Task RunMelee(IGameContext ctx, DataBinding<UnitData> charger)
        {
            var melee = new MeleeStage(ctx, new NoOpLayer<IUnitActionContext>());
            melee.OnFinishedMelee.Bind("finishedMelee");
            melee.BackToChooseAction.Bind("backToChooseAction");
            await melee.Enter(new UnitActionContext(ctx, charger));
        }

        // The acceptance-case table: the charger at (20, 20) and the six defenders in a 2 x 3 block in front of it,
        // every one within the 3" strike range, on their own teams and armies so the defender pick can see them.
        private (TestGameContext Ctx, DataBinding<UnitData> Charger, DataBinding<UnitData> Defender) BuildCharge(
            RecordingTextOutput log, int impact)
        {
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(4), textOutput: log,
                playerRequester: new MeleeRequester());

            var chargerPlayer = new PlayerID(Guid.NewGuid());
            var defenderPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { chargerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { defenderPlayer }));

            var claws = new Weapon("Claws", rangeInches: 0f, attacks: 2, armorPenetration: 0);
            var chargerModel = new ModelData(0.75f, new List<Weapon> { claws }, new Position(20f, 20f), _store);
            chargerModel.SetMaxWounds(9);
            var chargerUnit = new UnitData(chargerPlayer, "Charger", quality: 4, defense: 5,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(_store.Create(chargerModel)),
                });
            DataBinding<UnitData> charger = _store.GetDataBinding<UnitData>(_store.Create(chargerUnit));
            if (impact > 0)
            {
                charger.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                    new RuleArgument[] { new RuleArgument.Int(impact) }));
            }

            var spear = new Weapon("Spear", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            var defenderModels = new List<DataBinding<ModelData>>();
            foreach (float x in new[] { 21.5f, 22.6f })
            {
                foreach (float z in new[] { 18.9f, 20f, 21.1f })
                {
                    var model = new ModelData(0.5f, new List<Weapon> { spear }, new Position(x, z), _store);
                    defenderModels.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
                }
            }
            var defenderUnit = new UnitData(defenderPlayer, "Defender", quality: 4, defense: 5,
                modelBindings: defenderModels);
            DataBinding<UnitData> defender = _store.GetDataBinding<UnitData>(_store.Create(defenderUnit));

            _store.Create(new ArmyData(chargerPlayer, new List<DataBinding<UnitData>> { charger }));
            _store.Create(new ArmyData(defenderPlayer, new List<DataBinding<UnitData>> { defender }));
            return (ctx, charger, defender);
        }

        // 1-wound models in a row along z = 0 starting at x, on their own player and army.
        private DataBinding<UnitData> MakeUnit(string name, int models, float x, SpecialRuleDefinition? rule = null)
        {
            var player = new PlayerID(Guid.NewGuid());
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(x + i * 1.2f, 0f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (rule != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Answers every prompt the melee poses: the first defender, the first (swing) weapon, always strike back,
        // auto-filled wound assignment, and a consolidation that stays put.
        private sealed class MeleeRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case CancellableSelectionRequest<UnitData> defenderPick:
                        return Reply<TReply>(new Selected<DataBinding<UnitData>>(defenderPick.ValidOptions[0].Option));

                    case StringSelectionRequest weaponPick:
                        return Reply<TReply>(weaponPick.ValidOptions[0]);

                    case YesNoRequest:
                        return Reply<TReply>(true);

                    case AssignWoundsRequest woundRequest:
                        var assigned = new AssignWoundsResults(woundRequest.UnitReceivingWounds, woundRequest.Packets);
                        assigned.AutoFill();
                        return Reply<TReply>(assigned);

                    case ConsolidationMoveRequest:
                        return Reply<TReply>(new List<ModelMoveEntry>());
                }

                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }

            private static Task<TReply> Reply<TReply>(object reply) => Task.FromResult((TReply)reply);
        }
    }
}
