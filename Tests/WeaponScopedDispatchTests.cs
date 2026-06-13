using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #027 slice 2: dispatch is weapon-aware. A weapon-attached rule fires only when the event's
    // participant carries that weapon — proven both at queue level (the evaluator's weapon-aware
    // participant form) and through the REAL RollToHitStage with a mixed-armament attacker: the
    // Blast cannon's volley multiplies, the plain rifle's doesn't. Identical argument-less rules
    // reached through several carriers (unit + weapon) fire once, per the rulebook's
    // "effects from multiple instances of the same special rule don't stack".
    [TestFixture]
    public class WeaponScopedDispatchTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position TargetPos   = new Position(10, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows 6
        }

        [Test]
        public async Task BlastOnOneWeapon_MultipliesOnlyThatWeaponsVolley()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(TargetPos, modelCount: 5);

            Weapon cannon = MakeWeapon("Cannon");
            cannon.AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            Weapon rifle = MakeWeapon("Rifle");

            Assert.That(TotalHits(await RunRollToHit(attacker, defender, cannon)), Is.EqualTo(3f),
                "The Blast(3) cannon's 1 base hit multiplies to 3.");
            Assert.That(TotalHits(await RunRollToHit(attacker, defender, rifle)), Is.EqualTo(1f),
                "The same unit's plain rifle is untouched by the cannon's Blast.");
        }

        [Test]
        public void WeaponRule_FiresOnlyWhenItsWeaponIsInScope()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(TargetPos);
            Weapon reliableGun = MakeWeapon("Reliable Gun");
            reliableGun.AttachRuleDefinition(new ResolvedRule("Reliable", CoreRuleCatalog.Reliable));

            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            var context = new HitRollModifierContext(attacker.GetValue(), defender.GetValue(), DistanceInches: 10f);

            IReadOnlyList<RuleOperation> withWeapon = evaluator.EvaluateAll(context,
                (attacker.GetValue(), ERuleSeat.Actor, reliableGun));
            IReadOnlyList<RuleOperation> withoutWeapon = evaluator.EvaluateAll(context,
                (attacker.GetValue(), ERuleSeat.Actor, null));

            Assert.That(withWeapon.OfType<RuleOperation.QualityFloor>().Count(), Is.EqualTo(1),
                "Reliable fires when its carrying weapon is the one attacking.");
            Assert.That(withoutWeapon, Is.Empty,
                "Reliable does not fire for attacks made without its weapon.");
        }

        [Test]
        public void SameRuleOnUnitAndWeapon_FiresOnce()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(TargetPos);

            Weapon gun = MakeWeapon("Gun");
            gun.AttachRuleDefinition(new ResolvedRule("Reliable", CoreRuleCatalog.Reliable));
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Reliable", CoreRuleCatalog.Reliable));

            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            IReadOnlyList<RuleOperation> operations = evaluator.EvaluateAll(
                new HitRollModifierContext(attacker.GetValue(), defender.GetValue(), DistanceInches: 10f),
                (attacker.GetValue(), ERuleSeat.Actor, gun));

            Assert.That(operations.OfType<RuleOperation.QualityFloor>().Count(), Is.EqualTo(1),
                "Multiple instances of the same argument-less rule don't stack.");
        }

        private async Task<RollToHitResults> RunRollToHit(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Weapon weapon)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(new DetermineHitRollNeededResults(4)); // a 6 clears a 4+ threshold

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private static Weapon MakeWeapon(string name) =>
            new Weapon(name, rangeInches: 48f, attacks: 1, armorPenetration: 0);

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList)
            {
                total += hit.HitCount;
            }
            return total;
        }

        private DataBinding<UnitData> MakeUnit(Position position, int modelCount = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: position,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
