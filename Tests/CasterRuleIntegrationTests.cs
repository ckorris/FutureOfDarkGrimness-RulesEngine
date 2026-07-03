using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
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
        public async Task RoundStart_JoinedCasterHero_FundsHostUnitPool()
        {
            // A Caster hero that joined a unit carries its Caster rule on its MODEL (the #006 hero-merge),
            // not the host unit. The round-start grant must still fund the host unit's spell pool so the
            // joined caster can actually cast (#093 joined-Caster corner).
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(m)));
            }

            var unit = new UnitData(_player, "Squad+Wizard", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            // Caster(3) lives on ONE model (the joined hero), not on the unit.
            modelBindings[0].GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(3) }));

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));

            await RunRoundStart(roundCount: 1);

            Assert.That(binding.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(3),
                "a joined Caster(3) hero must fund its host unit's spell pool at round start.");
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

        // #033 Slice 2 — Choose Action offers "Cast" to a caster with an affordable spell and routes it to
        // the cast stage.
        [Test]
        public async Task ChooseAction_CasterWithAffordableSpell_OffersCastAndRoutes()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedStringChoiceRequester("Cast"));
            // Friend-affinity spell so the caster is its own valid target — Cast requires a legal target.
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3,
                new[] { Spell("Zap", threshold: 1, ETargetAffinity.Friend) }, new Position(10f, 10f));
            UnitActionContext unitCtx = NewActivation(ctx, caster);

            bool routed = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToCast.Bind("ToCast");
            stage.ToCast.OnWillActivate += _ => routed = true;
            await stage.Enter(unitCtx);

            Assert.That(routed, Is.True, "a caster with a castable spell is offered Cast and routes to the cast stage");
        }

        // #033 Slice 2 — casting spends the spell's token cost (on the attempt) and loops back to Choose
        // Action without consuming the move/attack (layered). TriggeredMoveTestContext's FixedDiceRoller(4)
        // means the 4+ roll passes; the token spend is identical on a failed cast.
        [Test]
        public async Task CastSpellStage_SpendsTokens_AndLoopsBackLayered()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3,
                new[] { Spell("Zap", threshold: 2, ETargetAffinity.Foe) }, new Position(10f, 10f));
            MakeEnemyUnit(new PlayerID(System.Guid.NewGuid()), new Position(12f, 10f));
            UnitActionContext unitCtx = NewActivation(ctx, caster);

            bool finished = false;
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1),
                "casting spends the spell's threshold in tokens (3 - 2)");
            Assert.That(finished, Is.True, "casting loops back to Choose Action");
            Assert.That(unitCtx.HasMoved, Is.False, "casting is layered — it does not consume the move");
            Assert.That(unitCtx.HasAttacked, Is.False, "casting is layered — it does not consume the attack");
        }

        // #033 Slice 3 — a buff spell applies its effect (here AddRule) to the target as a RuleGrant token.
        // The caster is its own (sole) friendly target.
        [Test]
        public async Task CastSpellStage_BuffSpell_GrantsRuleTokenToTarget()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { BuffSpell("Bless", threshold: 1, grantedRule: "Furious") }, new Position(10f, 10f));
            UnitActionContext unitCtx = NewActivation(ctx, caster);

            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Token granted = caster.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant).FirstOrDefault();
            Assert.That(granted, Is.Not.Null, "a buff spell grants the target a RuleGrant token");
            Assert.That(((TokenPayload.RuleGrant)granted!.Payload!).RuleName, Is.EqualTo("Furious"),
                "the RuleGrant token carries the spell's granted rule");
        }

        // #033 Slice 3 — a damage spell runs the synthetic-hit save→wound pipeline against the target. AP(3)
        // pushes the save above 6, so the hit auto-fails and kills the 1-wound enemy; the cast then loops back.
        [Test]
        public async Task CastSpellStage_DamageSpell_AppliesWoundsThroughPipeline()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { DamageSpell("Bolt", threshold: 2, hits: 1, armorPenetration: 3) }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeEnemyUnit(new PlayerID(System.Guid.NewGuid()), new Position(12f, 10f));

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            bool finished = false;
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(enemy.GetValue().GetIsAlive(), Is.False,
                "an AP(3) spell hit auto-fails the 1-wound enemy's save and kills it through the real pipeline");
            Assert.That(finished, Is.True, "the damage pipeline returns to Choose Action");
            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "casting spent the spell's 2-token cost");
        }

        // #033 Slice A — pre-save hit rules now fire on spell damage. A Blast(3) spell multiplies its 2 base
        // hits to 6 (capped at the 6-model target), and AP(6) auto-fails every save, so the unit is wiped —
        // without the hit-complete fold only the 2 base hits would land (killing 2). Uses FixedFaceDiceRoller
        // so the multi-hit save rolls aren't collapsed to one (FixedDiceRoller's TotalRolls is always 1).
        [Test]
        public async Task CastSpellStage_DamageSpell_BlastMultipliesHits()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester(), new FixedFaceDiceRoller(4));
            RuntimeSpell nova = new RuntimeSpell(
                new SpellDefinition("Nova", 2,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.DealHits(2, new[] { "Blast" }, ArmorPenetration: 6)),
                new[] { new ResolvedRule("Blast", CoreRuleCatalog.Blast, new RuleArgument[] { new RuleArgument.Int(3) }) });
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3, new[] { nova }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeMultiModelEnemy(new PlayerID(System.Guid.NewGuid()),
                new Position(12f, 10f), modelCount: 6);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            int living = enemy.GetValue().Models.Count(m => m.GetIsAlive());
            Assert.That(living, Is.EqualTo(0),
                "Blast multiplied 2 hits to 6 (capped at model count); AP(6) auto-failed every save, wiping the unit");
        }

        // #032 — Rending on a DAMAGE SPELL applies AP per-hit, not to the whole attack. Every spell die
        // auto-hits; the ScriptedRoller makes the 3-hit roll come up {6, 5, 5}, so exactly ONE hit is a
        // natural 6 (Rending AP4 → save needed 6) and the other two are base AP (save needed 4). Saves all
        // roll a 5: the two base hits save (5 ≥ 4), the AP hit fails (5 < 6) → one model dies, two survive.
        // Under the old whole-attack shortcut all three hits would have carried AP4 and the unit would be
        // wiped, so the surviving count is exactly what distinguishes per-hit from the bug.
        [Test]
        public async Task CastSpellStage_DamageSpell_RendingAppliesApPerHit()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester(), new ScriptedRoller());
            RuntimeSpell shard = new RuntimeSpell(
                new SpellDefinition("Shard Storm", 2,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.DealHits(3, new[] { "Rending" }, ArmorPenetration: 0)),
                new[] { new ResolvedRule("Rending", CoreRuleCatalog.Rending) });
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3, new[] { shard }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeMultiModelEnemy(new PlayerID(System.Guid.NewGuid()),
                new Position(12f, 10f), modelCount: 3); // defense 4

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            int living = enemy.GetValue().Models.Count(m => m.GetIsAlive());
            Assert.That(living, Is.EqualTo(2),
                "only the single natural-6 hit carried AP4 and failed the save; the two base hits saved, so 2 of 3 survive");
        }

        // #034 single-model targeting — a damage spell flagged SingleModel resolves "as a unit of [1]": all
        // wounds funnel to one chosen model with no carry-over. 3 hits at AP(6) would wipe the 3-model unit
        // (cf. the Blast test above), but confinement kills only the picked model — the other two survive.
        // FixedFaceDiceRoller so the 3 hits actually roll (FixedDiceRoller collapses TotalRolls to 1).
        [Test]
        public async Task CastSpellStage_SingleModelDamageSpell_ConfinesWoundsToOneModel()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester(), new FixedFaceDiceRoller(4));
            RuntimeSpell smite = new RuntimeSpell(
                new SpellDefinition("Smite", 2,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false, SingleModel: true),
                    new Effect.DealHits(3, System.Array.Empty<string>(), ArmorPenetration: 6)),
                System.Array.Empty<ResolvedRule>());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3, new[] { smite }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeMultiModelEnemy(new PlayerID(System.Guid.NewGuid()),
                new Position(12f, 10f), modelCount: 3);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            int living = enemy.GetValue().Models.Count(m => m.GetIsAlive());
            Assert.That(living, Is.EqualTo(2),
                "single-model targeting confined all 3 lethal hits to one model; the other two survive (no carry-over)");
        }

        // #034 multi-unit damage — a damage spell with MaxCount > 1 runs the save→wound pipeline once per
        // chosen target (via the looped ResolveSpellDamageStage), so EVERY selected unit is hit, not just the
        // first. Two single-model enemies, 1 hit at AP(3) each → both die.
        [Test]
        public async Task CastSpellStage_MultiUnitDamageSpell_HitsEveryTarget()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            RuntimeSpell chainBolt = new RuntimeSpell(
                new SpellDefinition("Chain Bolt", 2,
                    new TargetSelector(18f, 1, 2, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.DealHits(1, System.Array.Empty<string>(), ArmorPenetration: 3)),
                System.Array.Empty<ResolvedRule>());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3, new[] { chainBolt }, new Position(10f, 10f));
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());
            (DataBinding<UnitData> enemyA, DataBinding<UnitData> enemyB) =
                MakeTwoEnemyUnits(enemyPlayer, new Position(12f, 10f), new Position(12f, 12f));

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(enemyA.GetValue().GetIsAlive(), Is.False, "the first chosen target took the spell's hit");
            Assert.That(enemyB.GetValue().GetIsAlive(), Is.False,
                "the second chosen target ALSO took the spell's hit — the pipeline runs per target, not just target[0]");
        }

        // #034 forced enemy movement (primitive #6) — a TriggeredMove spell repositions an ENEMY unit through
        // the cast path's non-damage seam (OperationExecutor runs the imperative InvokeTriggeredMove), and the
        // CASTER directs the displacement: the move-path request routes to the caster's player, not the victim.
        [Test]
        public async Task CastSpellStage_ForcedMoveSpell_CasterDirectsEnemyMove()
        {
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());
            var requester = new CannedForcedMoveRequester(dx: 3f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            RuntimeSpell shove = new RuntimeSpell(
                new SpellDefinition("Telekinetic Shove", 1,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.TriggeredMove(MaxInches: 6f, IsOptional: false)),
                System.Array.Empty<ResolvedRule>());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { shove }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeEnemyUnit(enemyPlayer, new Position(12f, 10f));

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(requester.Captured, Is.Not.Null,
                "the cast path issued a movement-path request for the forced move");
            Assert.That(requester.Captured!.TargetPlayerID, Is.EqualTo(_player),
                "the CASTER directs the forced move, not the enemy whose unit is displaced");
            Position moved = enemy.GetValue().ModelBindings[0].GetValue().PositionBinding.GetValue();
            Assert.That(moved.x, Is.EqualTo(15f).Within(0.001f), "the enemy model was displaced +3\" by the caster");
            Assert.That(moved.z, Is.EqualTo(10f).Within(0.001f));
            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1),
                "casting spent the spell's 1-token cost");
        }

        // #034 conditional/triggered (primitive #5) — Deep Hypnosis shape: the target takes a morale test and,
        // on a FAIL, the caster moves it. A Quality-6 target + a fixed face of 4 makes the cast succeed
        // (4 >= 4) while the morale test fails (4 < 6), so the on-fail TriggeredMove fires, caster-directed.
        [Test]
        public async Task CastSpellStage_ConditionalSpell_FailedMorale_CasterMovesEnemy()
        {
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());
            var requester = new CannedForcedMoveRequester(dx: 3f, dz: 0f);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedFaceDiceRoller(4));

            RuntimeSpell hypnosis = new RuntimeSpell(
                new SpellDefinition("Deep Hypnosis", 2,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.MoraleTestThen(new Effect.TriggeredMove(MaxInches: 6f, IsOptional: false))),
                System.Array.Empty<ResolvedRule>());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { hypnosis }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeEnemyUnit(enemyPlayer, new Position(12f, 10f), quality: 6);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(requester.Captured, Is.Not.Null,
                "the failed morale test triggered the on-fail forced move");
            Assert.That(requester.Captured!.TargetPlayerID, Is.EqualTo(_player),
                "the caster directs the on-fail move, not the displaced enemy");
            Position moved = enemy.GetValue().ModelBindings[0].GetValue().PositionBinding.GetValue();
            Assert.That(moved.x, Is.EqualTo(15f).Within(0.001f), "the enemy was displaced +3\" on a failed test");
        }

        // #034 conditional/triggered — Terrifying Fury shape: morale test; on a FAIL the target becomes
        // Fatigued (hits only on 6s in melee for the round). Same dice rig: cast passes, morale fails.
        [Test]
        public async Task CastSpellStage_ConditionalSpell_FailedMorale_AppliesFatigue()
        {
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester(), new FixedFaceDiceRoller(4));

            RuntimeSpell fury = new RuntimeSpell(
                new SpellDefinition("Terrifying Fury", 1,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.MoraleTestThen(new Effect.ApplyFatigue())),
                System.Array.Empty<ResolvedRule>());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { fury }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeEnemyUnit(enemyPlayer, new Position(12f, 10f), quality: 6);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(enemy.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True,
                "a failed morale test fatigues the target");
        }

        // The on-fail effect lands ONLY on a failure: with a fixed face of 6 the Quality-6 target passes its
        // morale test (cast also passes), so Terrifying Fury leaves it un-fatigued.
        [Test]
        public async Task CastSpellStage_ConditionalSpell_PassedMorale_NoEffect()
        {
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester(), new FixedFaceDiceRoller(6));

            RuntimeSpell fury = new RuntimeSpell(
                new SpellDefinition("Terrifying Fury", 1,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.MoraleTestThen(new Effect.ApplyFatigue())),
                System.Array.Empty<ResolvedRule>());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { fury }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeEnemyUnit(enemyPlayer, new Position(12f, 10f), quality: 6);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(enemy.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "a passed morale test applies no on-fail effect");
        }

        // #033 Slice B — a stat-modifier buff spell grants the target a numeric roll modifier; reading it for
        // the matching roll yields the delta and (for a "next time" grant) consumes it.
        [Test]
        public async Task CastSpellStage_StatModifierSpell_GrantsAndConsumesHitBuff()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            RuntimeSpell guidance = new RuntimeSpell(
                new SpellDefinition("Guidance", 1,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: false),
                    new Effect.StatModifier(ERollKind.Hit, 1, ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3,
                new[] { guidance }, new Position(10f, 10f));

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx); // caster is its own (sole) friendly target

            int firstRead = GrantedRollModifiers.ConsumeNet(caster.GetValue(), ERollKind.Hit);
            Assert.That(firstRead, Is.EqualTo(1), "the granted +1 to-hit applies to the next hit roll");
            int secondRead = GrantedRollModifiers.ConsumeNet(caster.GetValue(), ERollKind.Hit);
            Assert.That(secondRead, Is.EqualTo(0), "a 'next time' grant is consumed after one use");
        }

        // A duration (ThisRound) grant persists across reads — it's swept at round end, not consumed on use.
        [Test]
        public void GrantedRollModifiers_DurationGrant_NotConsumedOnUse()
        {
            DataBinding<UnitData> unit = MakeCasterUnit(casterRating: 1, tokens: 0,
                System.Array.Empty<RuntimeSpell>(), new Position(5f, 5f));
            unit.GetValue().Tokens.AddToken(new Token(TokenType.SaveRollModifier, 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(1)));

            Assert.That(GrantedRollModifiers.ConsumeNet(unit.GetValue(), ERollKind.Save), Is.EqualTo(1));
            Assert.That(GrantedRollModifiers.ConsumeNet(unit.GetValue(), ERollKind.Save), Is.EqualTo(1),
                "a duration grant persists across reads");
        }

        // #103 — a friendly Caster within 18" spends a token to add +1 to a cast, turning a would-fail roll
        // into a success. Self-buff spell so success is observable as a RuleGrant on the caster; a fixed face
        // of 3 fails the base 4+ but clears the assisted 3+.
        [Test]
        public async Task CastSpellStage_FriendlyCasterAssist_RescuesFailedCast()
        {
            var requester = new CannedAssistRequester(tokensPerAssister: 1);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(3));

            DataBinding<UnitData> caster = MakeCasterBinding(_player, casterRating: 3, tokens: 1, new Position(10f, 10f));
            DataBinding<UnitData> ally = MakeCasterBinding(_player, casterRating: 3, tokens: 2, new Position(12f, 10f));
            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { caster, ally });
            army.SetSpells(new[] { SelfBuffSpell("Bless", threshold: 1, grantedRule: "Furious") });
            _store.Create(army);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(caster.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Not.Empty,
                "the +1 friendly assist turned the failed cast (face 3 vs 4+) into a success (3+), applying the buff");
            Assert.That(ally.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1),
                "the assisting Caster spent one of its two spell tokens");
            Assert.That(requester.AssistPromptCount, Is.EqualTo(1),
                "the one eligible friendly Caster was offered the assist");
        }

        // #103 — an enemy Caster within 18" spends a token to subtract 1 from the cast, spoiling a roll that
        // would otherwise succeed. A fixed face of 4 clears the base 4+ but not the hindered 5+.
        [Test]
        public async Task CastSpellStage_EnemyCasterHinder_SpoilsSuccessfulCast()
        {
            var requester = new CannedAssistRequester(tokensPerAssister: 1);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(4));

            DataBinding<UnitData> caster = MakeCasterBinding(_player, casterRating: 3, tokens: 1, new Position(10f, 10f));
            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { caster });
            army.SetSpells(new[] { SelfBuffSpell("Bless", threshold: 1, grantedRule: "Furious") });
            _store.Create(army);

            var enemyPlayer = new PlayerID(System.Guid.NewGuid());
            DataBinding<UnitData> enemyCaster =
                MakeCasterBinding(enemyPlayer, casterRating: 3, tokens: 2, new Position(12f, 10f));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyCaster }));

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(caster.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Empty,
                "the -1 enemy hinder dropped the cast (face 4 vs hindered 5+) below success — no buff applied");
            Assert.That(enemyCaster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1),
                "the hindering enemy Caster spent one of its two spell tokens");
        }

        // #103 — a Caster beyond 18" is neither prompted nor able to sway the cast, so it resolves on the
        // base 4+ (and a fixed face of 3 fails). Guards the range gate and the no-nearby-Caster no-op.
        [Test]
        public async Task CastSpellStage_CasterBeyondRange_NotOfferedAssist()
        {
            var requester = new CannedAssistRequester(tokensPerAssister: 1);
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(3));

            DataBinding<UnitData> caster = MakeCasterBinding(_player, casterRating: 3, tokens: 1, new Position(10f, 10f));
            // 25" apart (base-to-base ~24") — outside the 18" assist range.
            DataBinding<UnitData> farAlly = MakeCasterBinding(_player, casterRating: 3, tokens: 2, new Position(35f, 10f));
            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { caster, farAlly });
            army.SetSpells(new[] { SelfBuffSpell("Bless", threshold: 1, grantedRule: "Furious") });
            _store.Create(army);

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(requester.AssistPromptCount, Is.EqualTo(0), "a Caster beyond 18\" is not offered the assist");
            Assert.That(farAlly.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2),
                "the out-of-range Caster spent nothing");
            Assert.That(caster.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant), Is.Empty,
                "with no assist the cast resolved on the base 4+ and failed on a 3");
        }

        private static RuntimeSpell SelfBuffSpell(string name, int threshold, string grantedRule) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Self, RequireLineOfSight: false),
                    new Effect.AddRule(grantedRule, ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());

        // A caster unit (Caster rating + optional spell tokens) with no army — the caller assembles the
        // ArmyData so multiple casters can share one army (needed for the #103 assist tests).
        private DataBinding<UnitData> MakeCasterBinding(PlayerID player, int casterRating, int tokens, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };

            var unit = new UnitData(player, "Wizards", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(casterRating) }));
            if (tokens > 0)
            {
                binding.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }
            return binding;
        }

        private static RuntimeSpell DamageSpell(string name, int threshold, int hits, int armorPenetration) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.DealHits(hits, System.Array.Empty<string>(), armorPenetration)),
                System.Array.Empty<ResolvedRule>());

        private static RuntimeSpell BuffSpell(string name, int threshold, string grantedRule) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: false),
                    new Effect.AddRule(grantedRule, ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());

        // #033 Slice 4 — the spell-menu subtext summarizes a spell's effect and target.
        [Test]
        public void SpellText_Describe_SummarizesEffectAndTarget()
        {
            string damage = SpellText.Describe(new SpellDefinition("Bolt", 2,
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                new Effect.DealHits(2, new[] { "Bane" }, ArmorPenetration: 1)));
            Assert.That(damage, Does.Contain("2 hits").And.Contain("AP(1)").And.Contain("Bane")
                .And.Contain("enemy").And.Contain("18"));

            string buff = SpellText.Describe(new SpellDefinition("Bless", 1,
                new TargetSelector(12f, 1, 2, ETargetAffinity.Friend, RequireLineOfSight: false),
                new Effect.AddRule("Furious", ELifetime.NextTrigger)));
            Assert.That(buff, Does.Contain("Furious").And.Contain("up to 2").And.Contain("friendly"));

            string statMod = SpellText.Describe(new SpellDefinition("Guidance", 1,
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: false),
                new Effect.StatModifier(ERollKind.Hit, 1, ELifetime.NextTrigger)));
            Assert.That(statMod, Does.Contain("+1 to hit rolls").And.Contain("friendly"));
        }

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            UnitActionContext unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private static RuntimeSpell Spell(string name, int threshold, ETargetAffinity affinity) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, affinity, RequireLineOfSight: false),
                    new Effect.DealHits(1, System.Array.Empty<string>())),
                System.Array.Empty<ResolvedRule>());

        private DataBinding<UnitData> MakeCasterUnit(int casterRating, int tokens,
            IReadOnlyList<RuntimeSpell> spells, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };

            var unit = new UnitData(_player, "Wizards", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(casterRating) }));
            if (tokens > 0)
            {
                binding.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }

            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { binding });
            army.SetSpells(spells);
            _store.Create(army);
            return binding;
        }

        private DataBinding<UnitData> MakeEnemyUnit(PlayerID enemyPlayer, Position pos, int quality = 4)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };

            var unit = new UnitData(enemyPlayer, "Grunts", quality: quality, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Two single-model enemy units owned by one enemy player, in one army — for multi-unit targeting.
        private (DataBinding<UnitData> a, DataBinding<UnitData> b) MakeTwoEnemyUnits(
            PlayerID enemyPlayer, Position posA, Position posB)
        {
            DataBinding<UnitData> a = MakeEnemyUnitBinding(enemyPlayer, "GruntsA", posA);
            DataBinding<UnitData> b = MakeEnemyUnitBinding(enemyPlayer, "GruntsB", posB);
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { a, b }));
            return (a, b);
        }

        private DataBinding<UnitData> MakeEnemyUnitBinding(PlayerID enemyPlayer, string name, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(enemyPlayer, name, quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private DataBinding<UnitData> MakeMultiModelEnemy(PlayerID enemyPlayer, Position pos, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(pos.x + i * 0.6f, pos.z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(enemyPlayer, "Grunts", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
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

    // Deterministic roller for the per-hit-AP spell test. The cast check (RollDecisive) always passes on
    // a 6; the FIRST Roll — the spell's hit roll — comes up one natural 6 and the rest 5s, so exactly one
    // hit triggers Rending; every later Roll (the save rolls) is all-5s, honouring rollCount so per-group
    // save counts stay right. Face 5 saves at threshold 4 (base hits) but fails at 6 (the AP hit).
    internal sealed class ScriptedRoller : IDiceRoller
    {
        private int _rollCalls;

        // Cast success check — a 6 clears the 4+ threshold without touching the hit/save script.
        public IDiceResults RollDecisive(int sideCount) => OnFace(sideCount, 6, 1f);

        public IDiceResults Roll(int sideCount, float rollCount)
        {
            _rollCalls++;
            if (_rollCalls == 1)
            {
                // The spell's hit roll: one natural 6 (Rending fires) plus two 5s (auto-hit, base AP).
                float[] perSide = new float[sideCount];
                perSide[6 - 1] = 1f;
                perSide[5 - 1] = 2f;
                return new DiceResults(perSide);
            }

            // Every save roll shows 5s: base hits (need 4) save, the AP hit (need 6) fails.
            return OnFace(sideCount, 5, rollCount);
        }

        private static IDiceResults OnFace(int sideCount, int face, float count)
        {
            float[] perSide = new float[sideCount];
            perSide[face - 1] = count;
            return new DiceResults(perSide);
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

    // Drives CastSpellStage by picking the first offered spell and the first eligible target.
    internal sealed class CannedCastRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case StringSelectionRequest spellPick:
                    return Task.FromResult((TReply)(object)spellPick.ValidOptions[0]);
                case SelectionRequest<UnitData> targetPick:
                    return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                case SelectionRequest<ModelData> modelPick:
                    return Task.FromResult((TReply)(object)modelPick.ValidOptions[0].Option);
                case AssignWoundsRequest woundPick:
                    // A partial kill (fewer wounds than models) lets the defender choose which models fall;
                    // for these tests the choice is immaterial, so auto-fill it the way EOF/CLI input does.
                    var wounds = new AssignWoundsResults(woundPick.UnitReceivingWounds, woundPick.TotalWoundsToAssign);
                    wounds.AutoFill();
                    return Task.FromResult((TReply)(object)wounds);
                default:
                    throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }

    // Drives a forced-move spell cast: picks the first spell and first target, then answers the resulting
    // movement-path request by translating the moved unit by a fixed delta — capturing the request so the
    // test can assert who was asked to direct the move.
    internal sealed class CannedForcedMoveRequester : IPlayerRequestByID
    {
        private readonly float _dx;
        private readonly float _dz;
        public DefineMovementPathRequest? Captured { get; private set; }

        public CannedForcedMoveRequester(float dx, float dz)
        {
            _dx = dx;
            _dz = dz;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case StringSelectionRequest spellPick:
                    return Task.FromResult((TReply)(object)spellPick.ValidOptions[0]);
                case SelectionRequest<UnitData> targetPick:
                    return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                case DefineMovementPathRequest moveRequest:
                {
                    Captured = moveRequest;
                    var entries = new List<ModelMoveEntry>();
                    foreach (DataBinding<ModelData> model in moveRequest.UnitDataBinding.GetValue().ModelBindings)
                    {
                        Position start = model.GetValue().PositionBinding.GetValue();
                        entries.Add(new ModelMoveEntry(model,
                            new List<Position> { new Position(start.x + _dx, start.z + _dz) }));
                    }
                    return Task.FromResult((TReply)(object)entries);
                }
                default:
                    throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }

    // Drives a cast that opens a #103 assist window: picks the first spell + target, and for each
    // CastAssistRequest spends a fixed number of tokens (clamped to what the assister actually has).
    // Counts the assist prompts so a test can assert who was (and wasn't) offered the choice.
    internal sealed class CannedAssistRequester : IPlayerRequestByID
    {
        private readonly int _tokensPerAssister;
        public int AssistPromptCount { get; private set; }

        public CannedAssistRequester(int tokensPerAssister)
        {
            _tokensPerAssister = tokensPerAssister;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case CastAssistRequest assist:
                    AssistPromptCount++;
                    return Task.FromResult((TReply)(object)System.Math.Min(_tokensPerAssister, assist.AvailableTokens));
                case StringSelectionRequest spellPick:
                    return Task.FromResult((TReply)(object)spellPick.ValidOptions[0]);
                case SelectionRequest<UnitData> targetPick:
                    return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                case SelectionRequest<ModelData> modelPick:
                    return Task.FromResult((TReply)(object)modelPick.ValidOptions[0].Option);
                default:
                    throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
