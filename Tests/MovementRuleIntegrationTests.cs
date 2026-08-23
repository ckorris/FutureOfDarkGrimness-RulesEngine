using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Fast/Slow flow through the REAL
    // MovementActionContext. Its constructor fires the Movement_OnMoveActionDeclared "when"
    // once per action type, the RuleEvaluator evaluates the Actor seat, and the
    // MovementModifierSink folds the result into each budget — none of it interpreted by the
    // context. Baselines are taken from a no-rule unit so the test survives constant changes.
    [TestFixture]
    public class MovementRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public void FastUnit_AddsTwoToAdvance_FourToRushAndCharge()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> fast = MakeUnit();
            AttachFast(fast);
            var context = new MovementActionContext(_ctx, fast);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 2f).Within(0.001f),
                "Fast adds +2\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 4f).Within(0.001f),
                "Fast adds +4\" to Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 4f).Within(0.001f),
                "Fast adds +4\" to Charge.");
        }

        [Test]
        public void VeryFastUnit_DoublesFastsBonuses()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> veryFast = MakeUnit();
            veryFast.GetValue().AttachRuleDefinition(new ResolvedRule("Very Fast", CoreRuleCatalog.VeryFast));
            var context = new MovementActionContext(_ctx, veryFast);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 4f).Within(0.001f),
                "Very Fast adds +4\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 8f).Within(0.001f),
                "Very Fast adds +8\" to Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 8f).Within(0.001f),
                "Very Fast adds +8\" to Charge.");
        }

        [Test]
        public void SlowUnit_ReducesAllThreeBudgets()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> slow = MakeUnit();
            AttachSlow(slow);
            var context = new MovementActionContext(_ctx, slow);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance - 2f).Within(0.001f),
                "Slow subtracts 2\" from Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance - 4f).Within(0.001f),
                "Slow subtracts 4\" from Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance - 4f).Within(0.001f),
                "Slow subtracts 4\" from Charge.");
        }

        [Test]
        public void AgileUnit_AddsOneToAdvance_TwoToRushAndCharge()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> agile = MakeUnit();
            agile.GetValue().AttachRuleDefinition(new ResolvedRule("Agile", CoreRuleCatalog.Agile));
            var context = new MovementActionContext(_ctx, agile);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 1f).Within(0.001f),
                "Agile adds +1\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 2f).Within(0.001f),
                "Agile adds +2\" to Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 2f).Within(0.001f),
                "Agile adds +2\" to Charge.");
        }

        [Test]
        public void QuickUnit_AddsTwoToAllThreeBudgets()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> quick = MakeUnit();
            quick.GetValue().AttachRuleDefinition(new ResolvedRule("Quick", CoreRuleCatalog.Quick));
            var context = new MovementActionContext(_ctx, quick);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 2f).Within(0.001f),
                "Quick adds +2\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 2f).Within(0.001f),
                "Quick adds +2\" to Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 2f).Within(0.001f),
                "Quick adds +2\" to Charge.");
        }

        [Test]
        public void RapidAdvance_AddsFourToAdvanceOnly()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rapid Advance", CoreRuleCatalog.RapidAdvance));
            var context = new MovementActionContext(_ctx, unit);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 4f).Within(0.001f),
                "Rapid Advance adds +4\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance).Within(0.001f),
                "Rapid Advance is Advance-only; Rush untouched.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance).Within(0.001f),
                "Rapid Advance is Advance-only; Charge untouched.");
        }

        [Test]
        public void RapidRush_AddsSixToRushOnly()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rapid Rush", CoreRuleCatalog.RapidRush));
            var context = new MovementActionContext(_ctx, unit);

            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 6f).Within(0.001f),
                "Rapid Rush adds +6\" to Rush.");
            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "Rapid Rush is Rush-only; Advance untouched.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance).Within(0.001f),
                "Rapid Rush is Rush-only; Charge untouched.");
        }

        [Test]
        public void RapidCharge_AddsFourToChargeOnly()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rapid Charge", CoreRuleCatalog.RapidCharge));
            var context = new MovementActionContext(_ctx, unit);

            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 4f).Within(0.001f),
                "Rapid Charge adds +4\" to Charge.");
            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "Rapid Charge is Charge-only; Advance untouched.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance).Within(0.001f),
                "Rapid Charge is Charge-only; Rush untouched.");
        }

        // The shoot-after-advance gate (ChooseActionStage.GetCanShoot) must use the SAME advance distance the
        // move resolver grants — otherwise a Fast unit that legally advances past the base 6" is wrongly
        // blocked from shooting. MovementRuleQueries.EffectiveMoveShootDistance is that shared value.
        [Test]
        public void EffectiveMoveShootDistance_NoRules_IsBaseDistance()
        {
            float advance = MovementRuleQueries.EffectiveMoveShootDistance(MakeUnit().GetValue(), _ctx.RuleEvaluator);
            Assert.That(advance, Is.EqualTo(GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES).Within(0.001f),
                "a unit with no movement rules advances-and-shoots the base distance");
        }

        [Test]
        public void EffectiveMoveShootDistance_FastUnit_MatchesMoveResolverAdvance()
        {
            DataBinding<UnitData> fast = MakeUnit();
            AttachFast(fast);

            float gateAdvance = MovementRuleQueries.EffectiveMoveShootDistance(fast.GetValue(), _ctx.RuleEvaluator);
            float resolverAdvance = new MovementActionContext(_ctx, fast).MaxAdvanceDistance;

            Assert.That(gateAdvance, Is.EqualTo(GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES + 2f).Within(0.001f),
                "Fast raises the advance-and-shoot distance by +2\"");
            Assert.That(gateAdvance, Is.EqualTo(resolverAdvance).Within(0.001f),
                "the shoot gate's advance distance must equal what the move resolver actually allows");
        }

        // ── #153: one-shot movement grants + counts-as-terrain ─────────────────────────────────────────

        // A one-shot ("once, next time it would apply") granted movement rule must contribute to ALL
        // THREE action budgets, and re-projecting must not spend it — context init is read-only; the
        // grant is spent only when the move resolves (ExecuteMoveStage).
        [Test]
        public void OneShotGrantedMovementRule_BoostsAllBudgets_AndProjectionDoesNotSpendIt()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            var baseline = new MovementActionContext(ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            GrantOnce(unit, "Quick"); // +2" to Advance/Rush/Charge, once
            var first = new MovementActionContext(ctx, unit);
            var second = new MovementActionContext(ctx, unit);

            foreach ((MovementActionContext context, string label) in
                     new[] { (first, "first projection"), (second, "second projection") })
            {
                Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 2f).Within(0.001f),
                    $"{label}: granted Quick boosts Advance");
                Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 2f).Within(0.001f),
                    $"{label}: granted Quick boosts Rush");
                Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 2f).Within(0.001f),
                    $"{label}: granted Quick boosts Charge");
            }
        }

        [Test]
        public async Task OneShotGrantedMovementRule_IsSpentWhenTheMoveResolves()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            var baseline = new MovementActionContext(ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            GrantOnce(unit, "Quick");
            var context = new MovementActionContext(ctx, unit);
            context.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(unit.GetValue().ModelBindings[0],
                    new List<Position> { new Position(1f, 0f) }),
            });

            var stage = new ExecuteMoveStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnMoveExecuted.Bind("done");
            await stage.Enter(context);

            Assert.That(unit.GetValue().Tokens.GetTokenCount(Rules.Foundation.TokenType.RuleGrant),
                Is.EqualTo(0), "the one-shot grant is spent when the move resolves");
            var after = new MovementActionContext(ctx, unit);
            Assert.That(after.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "the next move projects without the spent grant");
        }

        // #377 — the spell-granted counts-as-terrain shape, exactly as the importer synthesizes it
        // ("Desert Storm Effect": Movement_OnMoveThroughTerrain / always / countAsInTerrain / Actor,
        // granted NextTrigger by the spell's addRule). The rule reaches the unit as a RuleGrant token,
        // not an attachment, so this pins the granted read-back at the terrain hook: projection caps the
        // budgets without spending, and the real move spends the grant so "once" means once.
        [Test]
        public async Task SpellGrantedCountAsDifficult_CapsTheMove_AndTheMoveSpendsIt()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            resolver.RegisterOrReplace(SynthesizedDesertStorm);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), ruleResolver: resolver);
            var baseline = new MovementActionContext(ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            GrantOnce(unit, "Desert Storm Effect");

            float cap = GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES;
            var first = new MovementActionContext(ctx, unit);
            var second = new MovementActionContext(ctx, unit);
            Assert.That(first.MaxAdvanceDistance, Is.EqualTo(cap).Within(0.001f),
                "the granted counts-as-difficult caps the projected Advance");
            Assert.That(first.MaxRushDistance, Is.EqualTo(cap).Within(0.001f),
                "the granted counts-as-difficult caps the projected Rush");
            Assert.That(second.MaxAdvanceDistance, Is.EqualTo(cap).Within(0.001f),
                "re-projection is read-only and must not spend the grant");

            var context = new MovementActionContext(ctx, unit);
            context.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(unit.GetValue().ModelBindings[0],
                    new List<Position> { new Position(1f, 0f) }),
            });
            var stage = new ExecuteMoveStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnMoveExecuted.Bind("done");
            await stage.Enter(context);

            Assert.That(unit.GetValue().Tokens.GetTokenCount(Rules.Foundation.TokenType.RuleGrant),
                Is.EqualTo(0), "the one-shot grant is spent by the move it capped");
            var after = new MovementActionContext(ctx, unit);
            Assert.That(after.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "'once' means once - the next move projects uncapped");
        }

        private static readonly SpecialRuleDefinition SynthesizedDesertStorm = new("Desert Storm Effect",
            new List<HookEntry>
            {
                new HookEntry(EHookID.Movement_OnMoveThroughTerrain, new Condition.Always(),
                    new Effect.CountAsInTerrain(ECountAsTerrain.Difficult), ELifetime.ThisActivation),
            },
            new List<ActivatedAbility>());

        // "Counts as being in Difficult Terrain": the whole move is capped at the difficult-terrain
        // limit, after bonuses — unless the unit ignores difficult terrain (Strider).
        [Test]
        public void CountsAsDifficultTerrain_CapsAllThreeBudgets()
        {
            DataBinding<UnitData> unit = MakeUnit();
            AttachFast(unit); // proves the cap applies AFTER bonuses
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Slowing Curse", CountsAsDifficultRule));
            var context = new MovementActionContext(_ctx, unit);

            float cap = GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES;
            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(cap).Within(0.001f));
            Assert.That(context.MaxRushDistance, Is.EqualTo(cap).Within(0.001f));
            Assert.That(context.MaxChargeDistance, Is.EqualTo(cap).Within(0.001f));
        }

        [Test]
        public void CountsAsDifficultTerrain_StriderIgnoresTheCap()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Slowing Curse", CountsAsDifficultRule));
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Strider", CoreRuleCatalog.Strider));
            var context = new MovementActionContext(_ctx, unit);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "ignoring difficult terrain waives the counted-as cap too");
        }

        private static readonly SpecialRuleDefinition CountsAsDifficultRule = new("Slowing Curse",
            new List<HookEntry>
            {
                new HookEntry(EHookID.Movement_OnMoveThroughTerrain, new Condition.Always(),
                    new Effect.CountAsInTerrain(ECountAsTerrain.Difficult), ELifetime.ThisActivation),
            },
            new List<ActivatedAbility>());

        private static void GrantOnce(DataBinding<UnitData> unit, string ruleName) =>
            unit.GetValue().Tokens.AddToken(new Rules.Tokens.Token(Rules.Foundation.TokenType.RuleGrant, 1,
                new Rules.Foundation.TokenClearTrigger.FirstTrigger(),
                Payload: new Rules.Tokens.TokenPayload.RuleGrant(ruleName, ELifetime.NextTrigger)));

        private static void AttachFast(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fast", CoreRuleCatalog.Fast));

        private static void AttachSlow(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Slow", CoreRuleCatalog.Slow));

        private DataBinding<UnitData> MakeUnit()
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0),
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
