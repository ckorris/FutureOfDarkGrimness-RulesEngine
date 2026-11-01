using System.Reflection;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #459 - the assumed-token overlay: a read-only hypothetical a rule QUERY carries (AssumedTokens on the
    // RuleInvocation, accepted only by RuleEvaluator.EvaluateAllNamed's overlay overload), consulted by the
    // token-reading conditions before the live container. Built for the Tactician pricing the fire its own
    // Artillery piece takes AFTER the move it is weighing (#442 D9: the estimate kept the unmoved -1 the move
    // forfeits). Pinned here: the conditions read the overlay and never write the unit, a live evaluation is
    // unchanged, no live entry point accepts an overlay, CombatMath's DefenderMoved drops Artillery's -1 for a
    // moved defender and is a no-op for a plain one, and the planner's Score prices a move candidate as moved.
    [TestFixture]
    public class AssumedTokenOverlayTests
    {
        private static readonly Position NearPos = new Position(0f, 5f);
        private static readonly Position FarPos = new Position(15f, 5f); // over 12" base to base

        private GameDataStore _store = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
        }

        // --- The conditions read the overlay, never the unit ------------------------------------------------

        [Test]
        public void TokenPresent_AssumedPresent_IsTrue_AndTheUnitIsNotWritten()
        {
            UnitData unit = MakeUnit(NearPos).GetValue();
            var condition = new Condition.TokenPresent(TokenType.MovedThisRound);

            bool result = condition.Evaluate(Invocation(unit, AssumedTokens.Present(unit, TokenType.MovedThisRound)));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True, "assumed present reads as present");
                Assert.That(unit.Tokens.HasToken(TokenType.MovedThisRound), Is.False, "the live container is untouched");
            });
        }

        [Test]
        public void TokenPresent_AssumedAbsent_IsFalse_OverALiveToken()
        {
            UnitData unit = MakeUnit(NearPos).GetValue();
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));
            var condition = new Condition.TokenPresent(TokenType.MovedThisRound);

            bool result = condition.Evaluate(Invocation(unit, AssumedTokens.Absent(unit, TokenType.MovedThisRound)));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.False, "assumed absent hides the live token");
                Assert.That(unit.Tokens.HasToken(TokenType.MovedThisRound), Is.True, "the live token is still there");
            });
        }

        [Test]
        public void TokenPresent_WithoutAnOverlay_ReadsTheLiveTokens()
        {
            UnitData unit = MakeUnit(NearPos).GetValue();
            var condition = new Condition.TokenPresent(TokenType.MovedThisRound);

            Assert.That(condition.Evaluate(Invocation(unit, assumed: null)), Is.False);
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));
            Assert.That(condition.Evaluate(Invocation(unit, assumed: null)), Is.True);
        }

        [Test]
        public void TokenPresent_ReadsTheAssumedCount_AgainstMinCount()
        {
            UnitData unit = MakeUnit(NearPos).GetValue();
            AssumedTokens two = AssumedTokens.Present(unit, TokenType.Fatigued, count: 2);

            Assert.Multiple(() =>
            {
                Assert.That(new Condition.TokenPresent(TokenType.Fatigued, MinCount: 2).Evaluate(Invocation(unit, two)), Is.True);
                Assert.That(new Condition.TokenPresent(TokenType.Fatigued, MinCount: 3).Evaluate(Invocation(unit, two)), Is.False);
            });
        }

        [Test]
        public void Overlay_IsScopedToTheNamedUnit_AndTheNamedToken()
        {
            UnitData named = MakeUnit(NearPos).GetValue();
            UnitData other = MakeUnit(FarPos).GetValue();
            other.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));
            AssumedTokens assumed = AssumedTokens.Present(named, TokenType.MovedThisRound);

            Assert.Multiple(() =>
            {
                Assert.That(new Condition.TokenPresent(TokenType.MovedThisRound).Evaluate(Invocation(other, assumed)),
                    Is.True, "another unit reads its own live token");
                Assert.That(new Condition.TokenPresent(TokenType.Shaken).Evaluate(Invocation(named, assumed)),
                    Is.False, "another token on the named unit reads live");
                Assert.That(assumed.With(named, TokenType.MovedThisRound, 0).TryGetCount(named, TokenType.MovedThisRound,
                    out int refined) && refined == 0, Is.True, "a later entry for the same pair wins");
            });
        }

        // The seam generalises: "as if Shaken" through the two Fatigued/Shaken helpers.
        [Test]
        public void ShakenAssumption_ReachesCountsAsFatiguedInMelee_AndTargetShakenOrFatigued()
        {
            DataBinding<UnitData> charger = MakeUnit(NearPos);
            DataBinding<UnitData> target = MakeUnit(FarPos);
            UnitData targetUnit = target.GetValue();
            AssumedTokens asIfShaken = AssumedTokens.Present(targetUnit, TokenType.Shaken);
            var hitContext = new HitRollModifierContext(charger.GetValue(), targetUnit, 1f, IsMelee: true);

            Assert.Multiple(() =>
            {
                Assert.That(new Condition.CountsAsFatiguedInMelee().Evaluate(Invocation(targetUnit, asIfShaken)),
                    Is.True, "the bearer counts as Fatigued when assumed Shaken");
                Assert.That(new Condition.CountsAsFatiguedInMelee().Evaluate(Invocation(targetUnit, assumed: null)),
                    Is.False, "and not without the assumption");
                Assert.That(new Condition.TargetShakenOrFatigued().Evaluate(
                        new RuleInvocation(hitContext, charger.GetValue(), Array.Empty<RuleArgument>(), AssumedTokens: asIfShaken)),
                    Is.True, "the target-side reader sees the target's assumption");
                Assert.That(new Condition.TargetShakenOrFatigued().Evaluate(
                        new RuleInvocation(hitContext, charger.GetValue(), Array.Empty<RuleArgument>())),
                    Is.False);
                Assert.That(targetUnit.Tokens.HasToken(TokenType.Shaken), Is.False, "the target was never written");
                Assert.That(Condition.CountsAsFatiguedInMelee.Holds(targetUnit), Is.False,
                    "the stage layer's live predicate is unchanged");
            });
        }

        // --- Through the evaluator's query: Artillery's defensive arm ---------------------------------------

        [Test]
        public void Query_WithMovedAssumed_DropsArtillerysUnmovedMinusOne_AndTheLiveQueryKeepsIt()
        {
            DataBinding<UnitData> shooter = MakeUnit(NearPos);
            DataBinding<UnitData> artillery = MakeUnit(FarPos, artilleryModels: 1);
            UnitData def = artillery.GetValue();
            var context = new HitRollModifierContext(shooter.GetValue(), def, DistanceInches: 14f);
            RuleParticipant actor = RuleParticipant.Actor(shooter.GetValue());
            RuleParticipant subject = RuleParticipant.Subject(def, models: HeroStatRules.LivingModels(def));

            int live = NetHitModifier(_evaluator.EvaluateAllNamed(context, actor, subject));
            int asMoved = NetHitModifier(_evaluator.EvaluateAllNamed(context,
                AssumedTokens.Present(def, TokenType.MovedThisRound), actor, subject));
            int asUnmoved = NetHitModifier(_evaluator.EvaluateAllNamed(context,
                AssumedTokens.Absent(def, TokenType.MovedThisRound), actor, subject));
            int nullOverlay = NetHitModifier(_evaluator.EvaluateAllNamed(context, (AssumedTokens?)null, actor, subject));

            Assert.Multiple(() =>
            {
                Assert.That(live, Is.EqualTo(-1), "unmoved Artillery over 12\": -1 to the shooter");
                Assert.That(asMoved, Is.EqualTo(0), "as if it had moved: the -1 is gone");
                Assert.That(asUnmoved, Is.EqualTo(-1));
                Assert.That(nullOverlay, Is.EqualTo(live), "a null overlay is the plain query");
                Assert.That(def.Tokens.HasToken(TokenType.MovedThisRound), Is.False, "the query wrote nothing");
            });
        }

        [Test]
        public void OnlyTheReadOnlyNamedQuery_AcceptsAnOverlay()
        {
            // Structural guarantee: no live entry point (Evaluate, EvaluateAll, EvaluateAllNamedLive, the ability
            // paths) can be handed an overlay, so an assumption cannot reach a resolution that logs or spends grants.
            var accepting = typeof(RuleEvaluator)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(AssumedTokens)))
                .Select(m => m.Name)
                .ToList();

            Assert.That(accepting, Is.EqualTo(new[] { nameof(RuleEvaluator.EvaluateAllNamed) }));
        }

        // --- CombatMath: the defender's hypothetical moved state ---------------------------------------------

        [Test]
        public void EstimateShooting_DefenderMoved_PricesArtilleryAsAPlainUnit_AndHoldKeepsTheMinusOne()
        {
            DataBinding<UnitData> shooter = MakeUnit(NearPos, models: 3);
            DataBinding<UnitData> artillery = MakeUnit(FarPos, models: 3, artilleryModels: 3);
            DataBinding<UnitData> plain = MakeUnit(FarPos, models: 3);
            const float distance = 14f;

            float artilleryHold = Shoot(shooter, artillery, defenderMoved: null);
            float artilleryMoved = Shoot(shooter, artillery, defenderMoved: true);
            float artilleryUnmoved = Shoot(shooter, artillery, defenderMoved: false);
            float plainHold = Shoot(shooter, plain, defenderMoved: null);
            float plainMoved = Shoot(shooter, plain, defenderMoved: true);

            Assert.Multiple(() =>
            {
                // Quality 4 shooters: 3/6 hit a plain unit, 2/6 an unmoved Artillery unit.
                Assert.That(artilleryHold, Is.EqualTo(plainHold * 2f / 3f).Within(0.0001f),
                    "the hold estimate keeps the unmoved -1");
                Assert.That(artilleryMoved, Is.EqualTo(plainHold).Within(0.0001f),
                    "the move estimate loses it: priced exactly as a plain unit");
                Assert.That(artilleryUnmoved, Is.EqualTo(artilleryHold).Within(0.0001f),
                    "an explicit unmoved assumption matches the live (tokenless) read");
                Assert.That(plainMoved, Is.EqualTo(plainHold).Within(0.0001f),
                    "a non-Artillery defender's estimate is unchanged either way");
                Assert.That(artillery.GetValue().Tokens.HasToken(TokenType.MovedThisRound), Is.False,
                    "the estimate never stamped the token");
            });

            float Shoot(DataBinding<UnitData> atk, DataBinding<UnitData> def, bool? defenderMoved) =>
                CombatMath.EstimateShooting(_evaluator, atk, def,
                    new AttackContext(distance, AttackerMoved: true, DefenderMoved: defenderMoved)).ExpectedWounds;
        }

        [Test]
        public void EstimateShooting_DefenderUnmovedAssumption_RestoresTheMinusOne_OverALiveMovedToken()
        {
            DataBinding<UnitData> shooter = MakeUnit(NearPos, models: 3);
            DataBinding<UnitData> artillery = MakeUnit(FarPos, models: 3, artilleryModels: 3);
            DataBinding<UnitData> plain = MakeUnit(FarPos, models: 3);
            artillery.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));

            float live = TacticalAnalysis.ExpectedShootingAt(_evaluator, shooter, artillery, 14f, attackerMoved: true)
                .ExpectedWounds;
            float asUnmoved = TacticalAnalysis.ExpectedShootingAt(_evaluator, shooter, artillery, 14f,
                attackerMoved: true, defenderMoved: false).ExpectedWounds;
            float plainLive = TacticalAnalysis.ExpectedShootingAt(_evaluator, shooter, plain, 14f, attackerMoved: true)
                .ExpectedWounds;

            Assert.Multiple(() =>
            {
                Assert.That(live, Is.EqualTo(plainLive).Within(0.0001f), "a unit that really moved gives no -1");
                Assert.That(asUnmoved, Is.EqualTo(plainLive * 2f / 3f).Within(0.0001f),
                    "assumed unmoved: the -1 is back (a next-round what-if)");
                Assert.That(artillery.GetValue().Tokens.HasToken(TokenType.MovedThisRound), Is.True,
                    "the live token was not removed");
            });
        }

        // --- The planner: a move candidate is priced as moved, a Hold as it stands ---------------------------

        [Test]
        public void PlannerScore_MoveCandidate_PricesOurArtilleryAsMoved_HoldKeepsTheBonus()
        {
            // Two identical set-ups: our 3-model unit 30" from a 3-model enemy with 30" rifles, carrying a 12" pistol
            // that reaches nothing (so no offense term differs), once with Artillery on every model and once without.
            // The enemy's post-advance reach is 24" at the Hold and 18" at the move that closes 6" - both over 12", so
            // the -1 is in play for both candidates and the incoming-fire term is the only thing Artillery changes:
            // a Hold keeps its unmoved -1 (scores above the plain unit), a move forfeits it (scores exactly as the
            // plain unit). Negative control: with the planner passing DefenderMoved: null the Move line fails.
            (float Hold, float Move, EMacroIntent MoveIntent) artillery = ScoreHoldAndMove(artillery: true);
            (float Hold, float Move, EMacroIntent MoveIntent) plain = ScoreHoldAndMove(artillery: false);

            Assert.Multiple(() =>
            {
                Assert.That(artillery.MoveIntent, Is.EqualTo(plain.MoveIntent), "the same move candidate was compared");
                Assert.That(artillery.Hold, Is.GreaterThan(plain.Hold + 0.0001f),
                    $"Hold: Artillery's unmoved -1 lowers the incoming fire (artillery={artillery.Hold:F4} plain={plain.Hold:F4})");
                Assert.That(artillery.Move, Is.EqualTo(plain.Move).Within(0.0001f),
                    $"Move: priced as moved, no -1 (artillery={artillery.Move:F4} plain={plain.Move:F4})");
            });
        }

        private static (float Hold, float Move, EMacroIntent MoveIntent) ScoreHoldAndMove(bool artillery)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var us = new PlayerID(Guid.NewGuid());
            var them = new PlayerID(Guid.NewGuid());

            var ours = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
            {
                var model = new ModelData(0.5f,
                    new List<Weapon> { new Weapon("Pistol", rangeInches: 12f, attacks: 1, armorPenetration: 0) },
                    new Position(20f, 24f + i * 1.1f), store);
                if (artillery) model.AttachRuleDefinition(new ResolvedRule("Artillery", CoreRuleCatalog.Artillery));
                ours.Add(store.GetDataBinding<ModelData>(store.Create(model)));
            }
            var unit = store.GetDataBinding<UnitData>(store.Create(new UnitData(us, "Guns", 4, 4, modelBindings: ours)));
            store.Create(new ArmyData(us, new List<DataBinding<UnitData>> { unit }));

            var theirs = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
                theirs.Add(store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f,
                    new List<Weapon> { new Weapon("Rifle", rangeInches: 30f, attacks: 1, armorPenetration: 0) },
                    new Position(50f, 24f + i * 1.1f), store))));
            var foe = store.GetDataBinding<UnitData>(store.Create(new UnitData(them, "Foe", 4, 4, modelBindings: theirs)));
            store.Create(new ArmyData(them, new List<DataBinding<UnitData>> { foe }));

            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            var planner = new TacticianPlanner(tableState, evaluator);
            planner.BeginActivation(unit);
            List<MacroAction> candidates = MacroActionGenerator.Enumerate(evaluator, tableState, unit);
            MacroAction hold = candidates.First(c => c.Intent == EMacroIntent.Hold);
            Position now = hold.ProjectedCentroid;
            var enemyPos = new Position(50f, 25.1f);
            // The move that closes on the enemy the most (and really moves), so its endpoint stays inside their reach.
            MacroAction move = candidates
                .Where(c => c.Intent != EMacroIntent.Hold && Position.GetDistance2D(now, c.ProjectedCentroid) > 1f)
                .OrderBy(c => Position.GetDistance2D(c.ProjectedCentroid, enemyPos))
                .First();
            return (planner.Score(hold), planner.Score(move), move.Intent);
        }

        // --- Helpers -------------------------------------------------------------------------------------------

        private static RuleInvocation Invocation(UnitData bearer, AssumedTokens? assumed) =>
            new RuleInvocation(Hook: null, bearer, Array.Empty<RuleArgument>(), AssumedTokens: assumed);

        private static int NetHitModifier(IReadOnlyList<(RuleOperation Op, string RuleName)> ops) =>
            ops.Select(o => o.Op).OfType<RuleOperation.ApplyRollModifier>()
                .Where(m => m.Roll == ERollKind.Hit).Sum(m => m.Delta);

        // Artillery attached per MODEL (the ArtilleryRuleIntegrationTests convention); Quality 4 / Defense 4 units.
        private DataBinding<UnitData> MakeUnit(Position position, int models = 1, int artilleryModels = 0)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f,
                    new List<Weapon> { new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0) },
                    new Position(position.x, position.z + i * 1.1f), _store);
                if (i < artilleryModels)
                    model.AttachRuleDefinition(new ResolvedRule("Artillery", CoreRuleCatalog.Artillery));
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
