using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #433 - the OPR 3.6 "Enhanced X" family, Option B: every Enhanced X is the additive delta on top of X, folded by the
    // sinks the base already uses. For each family SHAPE: the Enhanced value lands exactly once with base + Enhanced (the
    // no-stack guard BoostRuleCompositionTests used to give the Boost family - an upgrade authored as the boosted total
    // and then ADDED to the base is what that test caught), and the Enhanced rule alone is inert (no X, nothing).
    // Through the same queries the stages and the AI read: movement budgets, effective range and charge reach.
    [TestFixture]
    public class EnhancedRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _attackerPlayer;
        private PlayerID _enemyPlayer;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = new RuleResolver();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), ruleResolver: _resolver);
            _attackerPlayer = new PlayerID(System.Guid.NewGuid());
            _enemyPlayer = new PlayerID(System.Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _attackerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { _enemyPlayer }));
        }

        // --- B1: Lustbound (and Scurry / Agile): +2" / +4" ------------------------------------------------------------

        [Test]
        public void EnhancedLustbound_WithLustbound_MovesPlusTwoOnAdvance_PlusFourOnRushAndCharge()
        {
            DataBinding<UnitData> plain = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> daemons = MakeArmyUnit(_attackerPlayer, new Position(0, 5),
                RangeConditional36Definitions.Move(), Enhanced36Definitions.Move());
            var baseline = new MovementActionContext(_ctx, plain);
            var context = new MovementActionContext(_ctx, daemons);

            // The no-stack guard: base (+1/+2/+2) plus the delta (+1/+2/+2) is exactly the Enhanced total, once.
            Assert.That(context.MaxAdvanceDistance - baseline.MaxAdvanceDistance, Is.EqualTo(2f).Within(0.001f));
            Assert.That(context.MaxRushDistance - baseline.MaxRushDistance, Is.EqualTo(4f).Within(0.001f));
            Assert.That(context.MaxChargeDistance - baseline.MaxChargeDistance, Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void EnhancedLustbound_WithoutLustbound_IsInert()
        {
            DataBinding<UnitData> plain = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> pretenders = MakeArmyUnit(_attackerPlayer, new Position(0, 5), Enhanced36Definitions.Move());
            var baseline = new MovementActionContext(_ctx, plain);
            var context = new MovementActionContext(_ctx, pretenders);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f));
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance).Within(0.001f));
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance).Within(0.001f));
        }

        [Test]
        public void EnhancedLustbound_ReadsALustboundThatArrivedByAura()
        {
            // The book shape: Lustbound Aura on a hero grants Lustbound as a RuleGrant token; the Enhanced gate must see it.
            SpecialRuleDefinition lustbound = RangeConditional36Definitions.Move();
            _resolver.Register(lustbound);
            DataBinding<UnitData> plain = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> daemons = MakeArmyUnit(_attackerPlayer, new Position(0, 5), Enhanced36Definitions.Move());
            daemons.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                new TokenPayload.RuleGrant(lustbound.Name, ELifetime.Aura)));
            var baseline = new MovementActionContext(_ctx, plain);
            var context = new MovementActionContext(_ctx, daemons);

            Assert.That(context.MaxAdvanceDistance - baseline.MaxAdvanceDistance, Is.EqualTo(2f).Within(0.001f),
                "the granted Lustbound's +1 and the Enhanced delta's +1");
            Assert.That(context.MaxRushDistance - baseline.MaxRushDistance, Is.EqualTo(4f).Within(0.001f));
        }

        // --- B1: Darkborn (and Shadowborn / Shrouded / Wild Veil): -8" / -4" to the 6" floor -------------------------

        [TestCase(24f, 16f)]
        [TestCase(12f, 6f)]
        [TestCase(9f, 6f)]
        [TestCase(4f, 4f)]
        public void EnhancedDarkborn_WithDarkborn_ShrinksTheShootersRangeByEight_ToTheSixInchFloor(float gun, float expected)
        {
            DataBinding<UnitData> shooter = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> darkborn = MakeArmyUnit(_enemyPlayer, new Position(8, 0),
                RangeConditional36Definitions.Shroud(), Enhanced36Definitions.Shrink());

            Assert.That(RangeRuleQueries.EffectiveRange(shooter.GetValue(), Rifle(gun), darkborn.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(expected),
                gun == 12f ? "12 - 8 = 4, held at the 6\" floor" :
                gun == 4f ? "the floor never lengthens a short gun" : $"{gun}\" - 8\"");
        }

        [TestCase(12f, 8f)]
        [TestCase(9f, 6f)]
        [TestCase(5f, 5f)]
        public void EnhancedDarkborn_WithDarkborn_ShrinksTheChargersReachByFour(float baseCharge, float expected)
        {
            DataBinding<UnitData> charger = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> darkborn = MakeArmyUnit(_enemyPlayer, new Position(8, 0),
                RangeConditional36Definitions.Shroud(), Enhanced36Definitions.Shrink());

            Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(charger.GetValue(), darkborn.GetValue(),
                baseCharge, _ctx.RuleEvaluator), Is.EqualTo(expected));
        }

        [Test]
        public void EnhancedDarkborn_WithoutDarkborn_IsInert()
        {
            DataBinding<UnitData> shooter = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> target = MakeArmyUnit(_enemyPlayer, new Position(8, 0), Enhanced36Definitions.Shrink());

            Assert.That(RangeRuleQueries.EffectiveRange(shooter.GetValue(), Rifle(24f), target.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(24f));
            Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(shooter.GetValue(), target.GetValue(), 12f,
                _ctx.RuleEvaluator), Is.EqualTo(12f));
        }

        // --- B1: Highborn (and Extra Reach / Royal Legion): +8" / +4" ---------------------------------------------------

        [Test]
        public void EnhancedHighborn_WithHighborn_AddsEightToRange_AndFourToTheChargeBudget()
        {
            DataBinding<UnitData> plain = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> elves = MakeArmyUnit(_attackerPlayer, new Position(0, 5),
                RangeConditional36Definitions.Reach(), Enhanced36Definitions.Reach());
            DataBinding<UnitData> target = MakeArmyUnit(_enemyPlayer, new Position(8, 0));

            Assert.That(RangeRuleQueries.EffectiveRange(elves.GetValue(), Rifle(24f), target.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(32f), "24 + 4 + 4, not 24 + 4 + 8");
            Assert.That(new MovementActionContext(_ctx, elves).MaxChargeDistance,
                Is.EqualTo(new MovementActionContext(_ctx, plain).MaxChargeDistance + 4f).Within(0.001f));
        }

        [Test]
        public void EnhancedHighborn_WithoutHighborn_IsInert()
        {
            DataBinding<UnitData> plain = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> pretenders = MakeArmyUnit(_attackerPlayer, new Position(0, 5), Enhanced36Definitions.Reach());
            DataBinding<UnitData> target = MakeArmyUnit(_enemyPlayer, new Position(8, 0));

            Assert.That(RangeRuleQueries.EffectiveRange(pretenders.GetValue(), Rifle(24f), target.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(24f));
            Assert.That(new MovementActionContext(_ctx, pretenders).MaxChargeDistance,
                Is.EqualTo(new MovementActionContext(_ctx, plain).MaxChargeDistance).Within(0.001f));
        }

        // --- every shape fires ----------------------------------------------------------------------------------------

        [Test]
        public void EveryEnhancedShape_LintsClean()
        {
            foreach (SpecialRuleDefinition rule in AllShapes())
            {
                Assert.That(RuleFireLint.Check(rule), Is.Empty, rule.Name);
            }
        }

        internal static IEnumerable<SpecialRuleDefinition> AllShapes()
        {
            yield return Enhanced36Definitions.Move();
            yield return Enhanced36Definitions.Shrink();
            yield return Enhanced36Definitions.Reach();
            yield return Enhanced36Definitions.ApMinusTwo();
            yield return Enhanced36Definitions.IgnoreFivePlus();
            yield return Enhanced36Definitions.RecoverThreePlus();
            yield return Enhanced36Definitions.RerollFivePlus();
            yield return Enhanced36Definitions.PostCombatSix();
            yield return Enhanced36Definitions.TwoD3();
            yield return Enhanced36Definitions.MinusHit();
            yield return Enhanced36Definitions.PlusDefense();
            yield return Enhanced36Definitions.Ward();
            yield return Enhanced36Definitions.Morale();
            yield return Enhanced36Definitions.ExtraHit();
            yield return Enhanced36Definitions.ApPlusOne();
            yield return Enhanced36Definitions.ExtraWound();
            yield return Enhanced36Definitions.Pick();
            yield return Enhanced36Definitions.PlusHit();
            yield return Enhanced36Definitions.HitsOnFive();
            yield return Enhanced36Definitions.Aura("Lustbound");
            yield return Enhanced36Definitions.Buff("Primal");
        }

        // --- helpers --------------------------------------------------------------------------------------------------

        private static Weapon Rifle(float range) => new Weapon("Rifle", rangeInches: range, attacks: 1, armorPenetration: 0);

        private DataBinding<UnitData> MakeArmyUnit(PlayerID player, Position at, params SpecialRuleDefinition[] rules)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(at.x, at.z), _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(player, "Unit", quality: 4, defense: 4, modelBindings: bindings);
            foreach (SpecialRuleDefinition rule in rules) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
