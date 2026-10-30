using System.Linq;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #432 - the OPR 3.6 range family: one RangeModifier effect carrying a shooting delta AND a charge delta with one
    // floor. Darkborn (and Shadowborn / Shrouded / Wild Veil) on the TARGET: "that enemy unit gets -4"/-2" range to a
    // min. of 6""; Highborn (and Extra Reach / Royal Legion) on the BEARER: "+4"/+2" range when shooting/charging".
    // Shooting through RangeRuleQueries.EffectiveRange and the real target-eligibility check; charging through the
    // charge budget every reader shares (MovementActionContext, its per-model budgets, the AI's ChargeBudget) and the
    // per-target query DefinePathStage applies (MovementRuleQueries.EffectiveChargeDistanceAgainst). Also Lustbound's
    // 3.6 movement numbers.
    [TestFixture]
    public class RangeFamily36RuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private PlayerID _attackerPlayer;
        private PlayerID _enemyPlayer;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _attackerPlayer = new PlayerID(System.Guid.NewGuid());
            _enemyPlayer = new PlayerID(System.Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _attackerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { _enemyPlayer }));
        }

        // --- Darkborn: the enemy's range shrinks ----------------------------------------------------------------

        [TestCase(24f, 20f)]
        [TestCase(12f, 8f)]
        [TestCase(9f, 6f)]
        [TestCase(4f, 4f)]
        public void Darkborn_ShrinksTheShootersRange_ToAMinimumOfSix(float gun, float expected)
        {
            DataBinding<UnitData> shooter = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> darkborn = MakeArmyUnit(_enemyPlayer, new Position(8, 0),
                rule: RangeConditional36Definitions.Shroud());

            Assert.That(RangeRuleQueries.EffectiveRange(shooter.GetValue(), Rifle(gun), darkborn.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(expected),
                gun == 9f ? "9 - 4 = 5, held at the 6\" minimum - not 5\"" :
                gun == 4f ? "the minimum only limits a reduction: a 4\" gun is not lengthened to 6\"" :
                $"{gun}\" - 4\"");
        }

        [Test]
        public void Darkborn_PushesA21InchTarget_OutOfA24InchGunsReach()
        {
            // 22" centre to centre = 21" base to base.
            DataBinding<UnitData> shooter = MakeArmyUnit(_attackerPlayer, new Position(0, 0), Rifle(24f));
            DataBinding<UnitData> target = MakeArmyUnit(_enemyPlayer, new Position(22, 0));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(shooter, _ctx), Is.True, "control: 21\" of 24\"");

            Attach(target, RangeConditional36Definitions.Shroud());
            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(shooter, _ctx), Is.False,
                "the gun reaches 20\" against a Darkborn unit");
        }

        [TestCase(12f, 10f)]
        [TestCase(7f, 6f)]
        [TestCase(5f, 5f)]
        public void Darkborn_ShrinksTheChargersReach_ToAMinimumOfSix(float baseCharge, float expected)
        {
            DataBinding<UnitData> charger = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> darkborn = MakeArmyUnit(_enemyPlayer, new Position(8, 0),
                rule: RangeConditional36Definitions.Shroud());

            Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(charger.GetValue(), darkborn.GetValue(),
                baseCharge, _ctx.RuleEvaluator), Is.EqualTo(expected));
        }

        [Test]
        public void Darkborn_OnAMinority_ChangesNothing()
        {
            DataBinding<UnitData> shooter = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> target = MakeArmyUnit(_enemyPlayer, new Position(8, 0), models: 3);
            SpecialRuleDefinition shroud = RangeConditional36Definitions.Shroud();
            target.GetValue().ModelBindings[0].GetValue().AttachRuleDefinition(new ResolvedRule(shroud.Name, shroud));

            Assert.That(RangeRuleQueries.EffectiveRange(shooter.GetValue(), Rifle(24f), target.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(24f), "1 of 3 models is not most");
            Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(shooter.GetValue(), target.GetValue(), 12f,
                _ctx.RuleEvaluator), Is.EqualTo(12f));
        }

        [Test]
        public void MeleeShrouding_TheChargeOnlyMember_LeavesShootingAlone()
        {
            DataBinding<UnitData> shooter = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> target = MakeArmyUnit(_enemyPlayer, new Position(8, 0));
            Attach(target, CoreRuleCatalog.MeleeShrouding);

            Assert.That(RangeRuleQueries.EffectiveRange(shooter.GetValue(), Rifle(9f), target.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(9f), "a zero shooting delta never floors a range");
            Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(shooter.GetValue(), target.GetValue(), 12f,
                _ctx.RuleEvaluator), Is.EqualTo(10f));
        }

        // --- Highborn: the bearer's own range grows -------------------------------------------------------------

        [Test]
        public void Highborn_AddsFourToShootingRange()
        {
            DataBinding<UnitData> elves = MakeArmyUnit(_attackerPlayer, new Position(0, 0),
                rule: RangeConditional36Definitions.Reach());
            DataBinding<UnitData> target = MakeArmyUnit(_enemyPlayer, new Position(8, 0));

            Assert.That(RangeRuleQueries.EffectiveRange(elves.GetValue(), Rifle(24f), target.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(28f));
        }

        [Test]
        public void Highborn_AddsTwoToTheChargeBudget_EveryReaderAgrees()
        {
            DataBinding<UnitData> plain = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> elves = MakeArmyUnit(_attackerPlayer, new Position(0, 5),
                rule: RangeConditional36Definitions.Reach());
            var baseline = new MovementActionContext(_ctx, plain);
            var context = new MovementActionContext(_ctx, elves);

            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 2f).Within(0.001f),
                "+2\" range when charging is +2\" of charge move");
            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "range is not movement: Advance untouched");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance).Within(0.001f),
                "and Rush untouched");

            IModel model = elves.GetValue().Models.Single();
            Assert.That(context.TryGetModelMoveBudget(model, out _, out _, out float modelCharge), Is.True);
            Assert.That(modelCharge, Is.EqualTo(context.MaxChargeDistance).Within(0.001f), "the per-model budget");
            Assert.That(MovementRuleQueries.PerModelMoveBudgets(elves.GetValue(), _ctx.RuleEvaluator)[model.ID].Charge,
                Is.EqualTo(context.MaxChargeDistance).Within(0.001f), "the planner's per-model budgets");
            Assert.That(TacticalAnalysis.ChargeBudget(elves.GetValue(), _ctx.RuleEvaluator),
                Is.EqualTo(context.MaxChargeDistance).Within(0.001f), "the AI's charge budget");
        }

        [Test]
        public void Highborn_ChargingDarkborn_NetsTwelve()
        {
            DataBinding<UnitData> elves = MakeArmyUnit(_attackerPlayer, new Position(0, 0),
                rule: RangeConditional36Definitions.Reach());
            DataBinding<UnitData> darkborn = MakeArmyUnit(_enemyPlayer, new Position(8, 0),
                rule: RangeConditional36Definitions.Shroud());
            var context = new MovementActionContext(_ctx, elves);

            Assert.That(MovementRuleQueries.EffectiveChargeDistanceAgainst(elves.GetValue(), darkborn.GetValue(),
                context.MaxChargeDistance, _ctx.RuleEvaluator), Is.EqualTo(12f), "12 + 2 - 2");
            Assert.That(TacticalAnalysis.ChargeDistanceAgainst(elves.GetValue(), darkborn.GetValue(),
                _ctx.RuleEvaluator), Is.EqualTo(12f), "the AI composes it the same way");
        }

        // --- Lustbound (and Scurry): the 3.6 movement numbers ---------------------------------------------------

        [Test]
        public void Lustbound_MovesPlusOneOnAdvance_PlusTwoOnRushAndCharge()
        {
            DataBinding<UnitData> plain = MakeArmyUnit(_attackerPlayer, new Position(0, 0));
            DataBinding<UnitData> daemons = MakeArmyUnit(_attackerPlayer, new Position(0, 5),
                rule: RangeConditional36Definitions.Move());
            var baseline = new MovementActionContext(_ctx, plain);
            var context = new MovementActionContext(_ctx, daemons);

            Assert.That(context.MaxAdvanceDistance - baseline.MaxAdvanceDistance, Is.EqualTo(1f).Within(0.001f));
            Assert.That(context.MaxRushDistance - baseline.MaxRushDistance, Is.EqualTo(2f).Within(0.001f));
            Assert.That(context.MaxChargeDistance - baseline.MaxChargeDistance, Is.EqualTo(2f).Within(0.001f));
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private static Weapon Rifle(float range) => new Weapon("Rifle", rangeInches: range, attacks: 1, armorPenetration: 0);

        private static void Attach(DataBinding<UnitData> unit, SpecialRuleDefinition rule) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));

        private DataBinding<UnitData> MakeArmyUnit(PlayerID player, Position at, Weapon? weapon = null,
            SpecialRuleDefinition? rule = null, int models = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var weapons = weapon != null && i == 0 ? new List<Weapon> { weapon } : new List<Weapon>();
                var model = new ModelData(0.5f, weapons, new Position(at.x, at.z + i * 1.5f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, "Unit", quality: 4, defense: 4, modelBindings: bindings);
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
