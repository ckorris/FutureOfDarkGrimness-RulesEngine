using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P11 reflect damage (Retaliate + Deathstrike + Self-Destruct), driven through
    // the real ResolveMeleeReflectStage and its save/wound child pipeline. A CombatActionContext is built and
    // its per-model start-wounds snapshot captured (as the melee flow does before the first swing); wounds are
    // then dealt to simulate the melee, and the stage reflects hits back at the enemy. Proves: Retaliate deals
    // X hits per wound taken, Deathstrike X hits per killed model, attribution is PER MODEL (a model-level rule
    // reflects only that model's wounds), and no reflect fires without wounds/kills or without the rule.
    // FixedFaceDiceRoller(1): every save die is a 1, so every reflected hit fails its save and becomes a wound,
    // making the wounds the attacker is assigned equal to the reflected hit count.
    [TestFixture]
    public class ReflectRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _attacker;
        private PlayerID _foe;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _attacker = new PlayerID(Guid.NewGuid());
            _foe = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task Retaliate_DealsXHitsPerWoundTaken_AtTheAttacker()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Berserkers", models: 10);
            DataBinding<UnitData> defender = MakeUnit(_foe, "Thornbeasts", models: 4,
                unitRule: (CoreRuleCatalog.Retaliate, 2));

            await RunReflect(ctx, attacker, defender,
                simulateMelee: () => DealWoundsAcrossModels(defender, 3)); // took 3 wounds in melee

            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Berserkers"), Is.EqualTo(6f).Within(0.001f),
                "Retaliate(2) x 3 wounds taken = 6 hits, all unsaved -> 6 wounds on the attacker.");
        }

        [Test]
        public async Task Deathstrike_DealsXHits_WhenARuleBearingModelIsKilled()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Raiders", models: 6);
            DataBinding<UnitData> defender = MakeUnit(_foe, "Bomb", models: 1,
                unitRule: (CoreRuleCatalog.Deathstrike, 3));

            await RunReflect(ctx, attacker, defender,
                simulateMelee: () => DealWoundsAcrossModels(defender, 1)); // the single model is killed

            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Raiders"), Is.EqualTo(3f).Within(0.001f),
                "the killed Deathstrike(3) model deals 3 hits back at the attacking unit.");
        }

        [Test]
        public async Task Deathstrike_ModelSurvives_NoReflect()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Raiders", models: 6);
            DataBinding<UnitData> defender = MakeUnit(_foe, "Ogre", models: 1,
                unitRule: (CoreRuleCatalog.Deathstrike, 3), woundsPerModel: 4);

            await RunReflect(ctx, attacker, defender,
                simulateMelee: () => DealWoundsAcrossModels(defender, 2)); // hurt but alive

            Assert.That(requester.WoundsByUnit, Is.Empty, "Deathstrike only fires on a kill, not on wounds.");
        }

        [Test]
        public async Task Retaliate_NoWoundsTaken_NoReflect()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Berserkers", models: 6);
            DataBinding<UnitData> defender = MakeUnit(_foe, "Thornbeasts", models: 4,
                unitRule: (CoreRuleCatalog.Retaliate, 2));

            await RunReflect(ctx, attacker, defender, simulateMelee: () => { /* took no wounds */ });

            Assert.That(requester.WoundsByUnit, Is.Empty, "no wounds taken -> nothing to retaliate.");
        }

        [Test]
        public async Task NoReflectRule_NoHits()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Berserkers", models: 6);
            DataBinding<UnitData> defender = MakeUnit(_foe, "Grunts", models: 4);

            await RunReflect(ctx, attacker, defender,
                simulateMelee: () => DealWoundsAcrossModels(defender, 3));

            Assert.That(requester.WoundsByUnit, Is.Empty, "a plain unit reflects nothing.");
        }

        [Test]
        public async Task Retaliate_PerModelAttribution_OnlyTheRuleBearingModelsWoundsCount()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Berserkers", models: 6);
            // Two-model unit; only model 0 carries Retaliate(2) (a champion), attached at MODEL scope.
            DataBinding<UnitData> defender = MakeUnit(_foe, "Champion+Grunt", models: 2);
            defender.GetValue().ModelBindings[0].GetValue()
                .AttachRuleDefinition(RuleWithArg(CoreRuleCatalog.Retaliate, 2));

            // Both models take exactly one wound. Only the champion's wound should reflect.
            await RunReflect(ctx, attacker, defender, simulateMelee: () =>
            {
                defender.GetValue().ModelBindings[0].GetValue().DealWounds(1);
                defender.GetValue().ModelBindings[1].GetValue().DealWounds(1);
            });

            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Berserkers"), Is.EqualTo(2f).Within(0.001f),
                "per-model attribution: Retaliate(2) x the champion's 1 wound = 2, not 4 (the grunt's wound is ignored).");
        }

        [Test]
        public async Task SelfDestruct_Survivor_IsKilled_AndDealsXHits()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Raiders", models: 6);
            DataBinding<UnitData> bomb = MakeUnit(_foe, "Suicide Drone", models: 1,
                unitRule: (CoreRuleCatalog.SelfDestruct, 3));

            // The drone survives the melee untouched - the survive-branch must then self-kill it.
            await RunReflect(ctx, attacker, bomb, simulateMelee: () => { /* no wounds */ });

            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Raiders"), Is.EqualTo(3f).Within(0.001f),
                "a surviving Self-Destruct(3) model deals 3 hits at the enemy.");
            Assert.That(bomb.GetValue().GetIsAlive(), Is.False,
                "and is immediately killed after the melee.");
        }

        [Test]
        public async Task SelfDestruct_KilledInMelee_DealsXHits_NotDoubled()
        {
            var requester = new ReflectRequester();
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeUnit(_attacker, "Raiders", models: 6);
            DataBinding<UnitData> bomb = MakeUnit(_foe, "Suicide Drone", models: 1,
                unitRule: (CoreRuleCatalog.SelfDestruct, 3));

            await RunReflect(ctx, attacker, bomb,
                simulateMelee: () => DealWoundsAcrossModels(bomb, 1)); // killed fighting

            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Raiders"), Is.EqualTo(3f).Within(0.001f),
                "killed in melee deals its 3 hits once; the self-kill branch doesn't double it.");
        }

        // --- helpers ---

        // Builds the combat context (snapshotting per-model start wounds, as the melee entry does), runs the
        // simulated melee damage, then drives the real reflect stage's per-bearer loop to completion.
        private async Task RunReflect(IGameContext ctx, DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Action simulateMelee)
        {
            var combat = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            combat.SetDefender(defender);

            simulateMelee();

            var stage = new ResolveMeleeReflectStage(ctx, new NoOpLayer<ICombatActionContext>());
            bool done = false;
            stage.OnBatchDone.Bind("batch"); // a terminal here; the real graph loops it back to the stage
            stage.OnReflectResolved.Bind("done");
            stage.OnReflectResolved.OnWillActivate += _ => done = true;

            int safety = 0;
            while (!done && safety++ < 20)
            {
                await stage.Enter(combat);
            }
            Assert.That(done, Is.True, "the per-bearer reflect loop terminated.");
        }

        private static void DealWoundsAcrossModels(DataBinding<UnitData> unit, int wounds)
        {
            // Spread wounds one model at a time, so a multi-model unit loses whole models in order.
            List<DataBinding<ModelData>> models = unit.GetValue().ModelBindings;
            int i = 0;
            for (int w = 0; w < wounds; w++)
            {
                models[i % models.Count].GetValue().DealWounds(1);
                i++;
            }
        }

        private static ResolvedRule RuleWithArg(SpecialRuleDefinition def, int x) =>
            new ResolvedRule(def.Name, def, new RuleArgument[] { new RuleArgument.Int(x) });

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, int models,
            (SpecialRuleDefinition Def, int X)? unitRule = null, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(i * 0.01f, 0f), _store);
                if (woundsPerModel != 1) model.SetMaxWounds(woundsPerModel);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (unitRule.HasValue)
            {
                binding.GetValue().AttachRuleDefinition(RuleWithArg(unitRule.Value.Def, unitRule.Value.X));
            }
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Auto-fills any AssignWoundsRequest and tallies the wounds each unit is assigned, so a test can read back
    // how many reflected hits landed (with saves failing, wounds == hits).
    internal sealed class ReflectRequester : IPlayerRequestByID
    {
        public Dictionary<string, float> WoundsByUnit { get; } = new();

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is AssignWoundsRequest wr)
            {
                string name = wr.UnitReceivingWounds.GetValue().Name;
                WoundsByUnit[name] = WoundsByUnit.GetValueOrDefault(name) + wr.TotalWoundsToAssign;
                var result = new AssignWoundsResults(wr.UnitReceivingWounds, wr.TotalWoundsToAssign);
                result.AutoFill();
                return Task.FromResult((TReply)(object)result);
            }
            throw new InvalidOperationException("Unexpected request: " + request.GetType());
        }
    }
}
