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
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #100 #13 — marker-scaled magnitude (the growth/frenzy family): rules accumulate marker tokens
    // (on destroying an enemy / at round start / at round end) and read them back as a scaled roll
    // modifier or AP reduction. Four engine pieces under test:
    //   - Effect.TokenScaledRollModifier / TokenScaledReduceArmorPenetration (count -> magnitude);
    //   - GrantToken.MaxTotal (the "up to a max. of X markers" clamp, at grant time);
    //   - ReconcileObjectivesStage firing Round_OnRoundEnd rules before the token sweep
    //     (Fortified Growth's accumulation trigger);
    //   - the became-Shaken token clear (Fortified Growth's "if ever Shaken, loses all markers").
    [TestFixture]
    public class TokenScaledMarkerTests
    {
        private static readonly TokenType Marker = new("TestFrenzyMarker");

        // --- TokenScaledRollModifier ---

        private static SpecialRuleDefinition FrenzyReader() => new("Test Frenzy",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.TokenPresent(Marker),
                    new Effect.TokenScaledRollModifier(Marker, ERollKind.Hit, Delta: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        [Test]
        public void RollModifier_ScalesWithTheBearersMarkerCount()
        {
            var harness = new TestRuleHarness();
            harness.Register(FrenzyReader());
            IUnit unit = harness.BuildUnit("P1", 1, "Test Frenzy");

            Assert.That(HitDeltas(harness, unit), Is.Empty, "No markers, no modifier.");

            unit.Tokens.AddToken(new Token(Marker, 1, new TokenClearTrigger.ManualOnly()));
            Assert.That(HitDeltas(harness, unit).Single(), Is.EqualTo(1));

            unit.Tokens.AddToken(new Token(Marker, 1, new TokenClearTrigger.ManualOnly()));
            Assert.That(HitDeltas(harness, unit).Single(), Is.EqualTo(2),
                "Two markers must double the per-marker delta.");
        }

        private static SpecialRuleDefinition GrowthReader() => new("Test Growth",
            new[]
            {
                // Growth reads "for every two markers"; the TokenPresent MinCount mirrors PerMarkers so
                // the rule stays silent below one full step (and so RuleFireLint seeds enough markers).
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.TokenPresent(Marker, MinCount: 2),
                    new Effect.TokenScaledRollModifier(Marker, ERollKind.Hit, Delta: 1, PerMarkers: 2),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        [Test]
        public void RollModifier_PerMarkersTwo_UsesWholeStepsOnly()
        {
            var harness = new TestRuleHarness();
            harness.Register(GrowthReader());
            IUnit unit = harness.BuildUnit("P1", 1, "Test Growth");

            unit.Tokens.AddToken(new Token(Marker, 3, new TokenClearTrigger.ManualOnly()));
            Assert.That(HitDeltas(harness, unit).Single(), Is.EqualTo(1),
                "Three markers at per-two scaling is one whole step.");

            unit.Tokens.AddToken(new Token(Marker, 1, new TokenClearTrigger.ManualOnly()));
            Assert.That(HitDeltas(harness, unit).Single(), Is.EqualTo(2),
                "Four markers is two steps.");
        }

        // --- TokenScaledReduceArmorPenetration ---

        private static SpecialRuleDefinition FortifiedGrowthReader() => new("Test Fortified Growth",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.TokenPresent(Marker),
                    new Effect.TokenScaledReduceArmorPenetration(Marker, PerMarkers: 1, MaxReduction: 2),
                    ELifetime.ThisAttack,
                    ERuleSeat.Subject),
            },
            Array.Empty<ActivatedAbility>());

        [Test]
        public void ApReduction_ScalesPerMarker_AndCapsAtMaxReduction()
        {
            var harness = new TestRuleHarness();
            harness.Register(FortifiedGrowthReader());
            IUnit unit = harness.BuildUnit("P1", 1, "Test Fortified Growth");

            unit.Tokens.AddToken(new Token(Marker, 1, new TokenClearTrigger.ManualOnly()));
            Assert.That(ApReductions(harness, unit).Single(), Is.EqualTo(1));

            // Four markers, capped at 2 — Fortified Growth accumulates to four but reads at most -2.
            unit.Tokens.AddToken(new Token(Marker, 3, new TokenClearTrigger.ManualOnly()));
            Assert.That(ApReductions(harness, unit).Single(), Is.EqualTo(2),
                "The read-side cap must clamp four markers to a reduction of 2.");
        }

        // --- GrantToken.MaxTotal ---

        private static SpecialRuleDefinition CappedGranter() => new("Test Granter",
            new[]
            {
                new HookEntry(EHookID.Round_OnRoundStart,
                    new Condition.Always(),
                    new Effect.GrantToken(Marker, new ValueSource.Literal(1),
                        new TokenClearTrigger.ManualOnly(), MaxTotal: 2),
                    ELifetime.ThisRound),
            },
            Array.Empty<ActivatedAbility>());

        [Test]
        public void GrantToken_MaxTotal_StopsGrantingAtTheCap()
        {
            var harness = new TestRuleHarness();
            harness.Register(CappedGranter());
            IUnit unit = harness.BuildUnit("P1", 1, "Test Granter");

            for (int round = 0; round < 3; round++)
            {
                IReadOnlyList<RuleOperation> ops =
                    harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit));
                OperationApplier.ApplyTokenOperations(ops);
            }

            Assert.That(unit.Tokens.GetTokenCount(Marker), Is.EqualTo(2),
                "Three capped grants of 1 with MaxTotal 2 must stop at 2.");
        }

        [Test]
        public void GrantToken_MaxTotal_ClampsAnOverCapGrantPartially()
        {
            var harness = new TestRuleHarness();
            var granter = new SpecialRuleDefinition("Test Big Granter",
                new[]
                {
                    new HookEntry(EHookID.Round_OnRoundStart,
                        new Condition.Always(),
                        new Effect.GrantToken(Marker, new ValueSource.Literal(5),
                            new TokenClearTrigger.ManualOnly(), MaxTotal: 4),
                        ELifetime.ThisRound),
                },
                Array.Empty<ActivatedAbility>());
            harness.Register(granter);
            IUnit unit = harness.BuildUnit("P1", 1, "Test Big Granter");
            unit.Tokens.AddToken(new Token(Marker, 3, new TokenClearTrigger.ManualOnly()));

            IReadOnlyList<RuleOperation> ops =
                harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit));
            OperationApplier.ApplyTokenOperations(ops);

            Assert.That(unit.Tokens.GetTokenCount(Marker), Is.EqualTo(4),
                "A grant of 5 onto 3 existing markers with MaxTotal 4 must add exactly 1.");
        }

        // --- Round-end firing (ReconcileObjectivesStage) ---

        // The #196 lesson: check where an effect is CONSUMED, not just that the hook accepts it. This
        // drives the REAL stage and asserts the marker landed — an unfired Round_OnRoundEnd loop, or one
        // that runs after the token sweep, fails here.
        [Test]
        public async Task RoundEndRules_FireThroughTheRealStage_BeforeTheSweep()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4));

            var roundEndGranter = new SpecialRuleDefinition("Test Round End Granter",
                new[]
                {
                    new HookEntry(EHookID.Round_OnRoundEnd,
                        new Condition.Always(),
                        new Effect.GrantToken(Marker, new ValueSource.Literal(1),
                            new TokenClearTrigger.ManualOnly(), MaxTotal: 4),
                        ELifetime.ThisRound),
                },
                Array.Empty<ActivatedAbility>());

            DataBinding<UnitData> unit = MakeUnit(store, "Grower", 1);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(
                roundEndGranter.Name, roundEndGranter));

            var stage = new ReconcileObjectivesStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.ToReconcileEndOfTurn.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN);
            stage.ToVictoryCalculation.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION);
            await stage.Enter(new StubMainPhaseContext(ctx));

            Assert.That(unit.GetValue().Tokens.GetTokenCount(Marker), Is.EqualTo(1),
                "The round-end rule pass must run and its ManualOnly marker must survive the sweep.");
        }

        // --- Became-Shaken clear ---

        [Test]
        public void ApplyShaken_ClearsShakenTriggeredTokens()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "Loser", 2);
            unit.GetValue().Tokens.AddToken(new Token(Marker, 3,
                new TokenClearTrigger.CustomHook(EHookID.Morale_OnShakenApplied)));

            MoraleUtilities.ApplyShaken(unit);

            Assert.That(unit.GetValue().Tokens.HasToken(Marker), Is.False,
                "Becoming Shaken must clear every CustomHook(Morale_OnShakenApplied) token.");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        [Test]
        public void ApplyShaken_LeavesOtherTokensAlone()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "Loser", 1);
            unit.GetValue().Tokens.AddToken(new Token(Marker, 2, new TokenClearTrigger.ManualOnly()));

            MoraleUtilities.ApplyShaken(unit);

            Assert.That(unit.GetValue().Tokens.GetTokenCount(Marker), Is.EqualTo(2),
                "A ManualOnly marker (the Frenzy family) survives becoming Shaken.");
        }

        [Test]
        public void SpilloutShaken_AlsoClearsShakenTriggeredTokens()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            DataBinding<UnitData> unit = MakeUnit(store, "Occupant", 1);
            unit.GetValue().Tokens.AddToken(new Token(Marker, 2,
                new TokenClearTrigger.CustomHook(EHookID.Morale_OnShakenApplied)));

            TransportUtilities.ApplySpilloutEffects(unit.GetValue(), new FixedDiceRoller(6));

            Assert.That(unit.GetValue().Tokens.HasToken(Marker), Is.False,
                "The transport-spillout Shaken path must clear the same tokens as ApplyShaken.");
        }

        // --- Helpers ---

        private static IEnumerable<int> HitDeltas(TestRuleHarness harness, IUnit unit) =>
            harness.Evaluate(unit, ERuleSeat.Actor,
                    new HitRollModifierContext(unit, unit, DistanceInches: 12f))
                .OfType<RuleOperation.ApplyRollModifier>()
                .Where(op => op.Roll == ERollKind.Hit)
                .Select(op => op.Delta);

        private static IEnumerable<int> ApReductions(TestRuleHarness harness, IUnit unit) =>
            harness.Evaluate(unit, ERuleSeat.Subject,
                    new HitRollCompleteContext(unit, unit,
                        new FixedDiceRoller(4).Roll(6, 1), DistanceInches: 12f))
                .OfType<RuleOperation.ReduceArmorPenetration>()
                .Select(op => op.Amount);

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0), gameDataStore: store);
                modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }
    }
}
