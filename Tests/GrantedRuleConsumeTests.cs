using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    // #101 — guards the consume-on-fire behaviour added on top of #100's granted-rule read-back:
    //  * a one-shot (NextTrigger) grant is consumed directly by the evaluator on a live EvaluateAll, so it
    //    works even though the hit/save/melee pipeline applies ops through sinks, NOT OperationApplier (the
    //    earlier op-based consume was silently dropped at exactly those combat hooks);
    //  * the consume is gated to the live apply path — read-only named queries don't burn the buff;
    //  * duration (this-round) grants are NOT consumed on use (they're swept at round end).
    // Probe rule is Reliable, which fires at HitRollModifierContext in the Actor seat (QualityFloor op).
    [TestFixture]
    public class GrantedRuleConsumeTests
    {
        private static readonly Position ActorPos = new Position(0, 5);
        private static readonly Position TargetPos = new Position(10, 5);

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void NextTriggerGrant_ConsumedByLiveEvaluateAll_WithoutApplyingOps()
        {
            DataBinding<UnitData> unit = MakeUnit(ActorPos);
            DataBinding<UnitData> target = MakeUnit(TargetPos);
            GrantRule(unit, "Reliable", ELifetime.NextTrigger);

            RuleEvaluator evaluator = Evaluator();
            // Note: the caller never runs OperationApplier — exactly like the hit/save pipeline.
            evaluator.EvaluateAll(Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));
            IReadOnlyList<RuleOperation> second = evaluator.EvaluateAll(
                Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));

            Assert.That(second.OfType<RuleOperation.QualityFloor>(), Is.Empty,
                "a 'next time' grant is spent on its first occurrence and no longer fires");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.RuleGrant), Is.False,
                "the consumed grant token is removed");
        }

        [Test]
        public void ReadOnlyNamedQuery_DoesNotConsume()
        {
            DataBinding<UnitData> unit = MakeUnit(ActorPos);
            DataBinding<UnitData> target = MakeUnit(TargetPos);
            GrantRule(unit, "Reliable", ELifetime.NextTrigger);

            RuleEvaluator evaluator = Evaluator();
            evaluator.EvaluateAllNamed(Context(unit, target), (unit.GetValue(), ERuleSeat.Actor));

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.RuleGrant), Is.True,
                "a read-only query must not burn a one-shot buff");
            IReadOnlyList<RuleOperation> live = evaluator.EvaluateAll(
                Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));
            Assert.That(live.OfType<RuleOperation.QualityFloor>().Count(), Is.EqualTo(1),
                "the grant survived the read-only query, so the next live eval still fires it once");
        }

        [Test]
        public void ThisRoundGrant_NotConsumedOnUse()
        {
            DataBinding<UnitData> unit = MakeUnit(ActorPos);
            DataBinding<UnitData> target = MakeUnit(TargetPos);
            GrantRule(unit, "Reliable", ELifetime.ThisRound);

            RuleEvaluator evaluator = Evaluator();
            evaluator.EvaluateAll(Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));
            IReadOnlyList<RuleOperation> second = evaluator.EvaluateAll(
                Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));

            Assert.That(second.OfType<RuleOperation.QualityFloor>().Count(), Is.EqualTo(1),
                "a duration grant fires every time until swept at round end, not consumed on use");
        }

        private static RuleEvaluator Evaluator() =>
            new RuleEvaluator(new FixedDiceRoller(4), log: null, ruleResolver: CoreRuleCatalog.CreateResolver());

        private static HitRollModifierContext Context(DataBinding<UnitData> actor, DataBinding<UnitData> target) =>
            new HitRollModifierContext(actor.GetValue(), target.GetValue(), DistanceInches: 10f);

        private static void GrantRule(DataBinding<UnitData> unit, string ruleName, ELifetime lifetime)
        {
            // Mirror Effect.AddRule.Apply's lifetime → clear-trigger mapping: a "next time" grant clears on
            // FirstTrigger (and is consumed on fire); a this-round grant clears at RoundEnd (and isn't).
            TokenClearTrigger trigger = lifetime switch
            {
                ELifetime.NextTrigger => new TokenClearTrigger.FirstTrigger(),
                ELifetime.ThisActivation => new TokenClearTrigger.ActivationEnd(),
                ELifetime.ThisRound => new TokenClearTrigger.RoundEnd(),
                _ => new TokenClearTrigger.ManualOnly(),
            };
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1, trigger,
                Payload: new TokenPayload.RuleGrant(ruleName, lifetime)));
        }

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: position, gameDataStore: _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
