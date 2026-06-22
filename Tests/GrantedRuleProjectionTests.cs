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
    // #101 keyword-buff bridge: a rule granted via a RuleGrant token (Effect.AddRule) is projected into
    // evaluation by name through the injected resolver, so it fires exactly like an innate rule. A
    // NextTrigger ("once / next time") grant is consumed when its hook+seat next fires on a real
    // (mutating) EvaluateAll — whether or not it changed the outcome — while read-only query paths and
    // duration grants never consume. The probe rule is Reliable, which fires at HitRollModifierContext in
    // the Actor seat producing a QualityFloor op (see WeaponScopedDispatchTests).
    [TestFixture]
    public class GrantedRuleProjectionTests
    {
        private static readonly Position ActorPos = new Position(0, 5);
        private static readonly Position TargetPos = new Position(10, 5);

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void GrantedRule_FiresAtItsHook_LikeAnInnateRule()
        {
            DataBinding<UnitData> unit = MakeUnit(ActorPos);
            DataBinding<UnitData> target = MakeUnit(TargetPos);
            GrantRule(unit, "Reliable", ELifetime.NextTrigger);

            IReadOnlyList<RuleOperation> ops = Evaluator().EvaluateAll(
                Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));

            Assert.That(ops.OfType<RuleOperation.QualityFloor>().Count(), Is.EqualTo(1),
                "the granted Reliable fires through the projected RuleGrant token");
        }

        [Test]
        public void GrantedRule_NextTrigger_ConsumedAfterItsHookFires()
        {
            DataBinding<UnitData> unit = MakeUnit(ActorPos);
            DataBinding<UnitData> target = MakeUnit(TargetPos);
            GrantRule(unit, "Reliable", ELifetime.NextTrigger);

            RuleEvaluator evaluator = Evaluator();
            evaluator.EvaluateAll(Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));
            IReadOnlyList<RuleOperation> second = evaluator.EvaluateAll(
                Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));

            Assert.That(second, Is.Empty, "a 'next time' grant is consumed on the occurrence and no longer fires");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.RuleGrant), Is.False,
                "the consumed grant token is removed");
        }

        [Test]
        public void GrantedRule_ReadOnlyQuery_ProjectsButDoesNotConsume()
        {
            DataBinding<UnitData> unit = MakeUnit(ActorPos);
            DataBinding<UnitData> target = MakeUnit(TargetPos);
            GrantRule(unit, "Reliable", ELifetime.NextTrigger);

            RuleEvaluator evaluator = Evaluator();
            var named = evaluator.EvaluateAllNamed(Context(unit, target), (unit.GetValue(), ERuleSeat.Actor));
            Assert.That(named.Count(t => t.Op is RuleOperation.QualityFloor), Is.EqualTo(1),
                "read-only queries see the granted rule (so UI can show it)");

            IReadOnlyList<RuleOperation> afterRead = evaluator.EvaluateAll(
                Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));
            Assert.That(afterRead.OfType<RuleOperation.QualityFloor>().Count(), Is.EqualTo(1),
                "the read-only query did not burn the buff, so it still fires once");
        }

        [Test]
        public void GrantedRule_ThisRound_NotConsumedOnUse()
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

        [Test]
        public void GrantedRule_WithoutResolver_IsInert()
        {
            DataBinding<UnitData> unit = MakeUnit(ActorPos);
            DataBinding<UnitData> target = MakeUnit(TargetPos);
            GrantRule(unit, "Reliable", ELifetime.NextTrigger);

            var evaluator = new RuleEvaluator(new FixedDiceRoller(4)); // no resolver injected
            IReadOnlyList<RuleOperation> ops = evaluator.EvaluateAll(
                Context(unit, target), (unit.GetValue(), ERuleSeat.Actor, null));

            Assert.That(ops, Is.Empty, "granted rules are inert when the evaluator has no resolver (test default)");
        }

        private static RuleEvaluator Evaluator() =>
            new RuleEvaluator(new FixedDiceRoller(4), log: null, ruleResolver: CoreRuleCatalog.CreateResolver());

        private static HitRollModifierContext Context(DataBinding<UnitData> actor, DataBinding<UnitData> target) =>
            new HitRollModifierContext(actor.GetValue(), target.GetValue(), DistanceInches: 10f);

        private static void GrantRule(DataBinding<UnitData> unit, string ruleName, ELifetime lifetime) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(), Payload: new TokenPayload.RuleGrant(ruleName, lifetime)));

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
