using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #027 slice 3: weapon-scoped rules on the DEFENDER's weapons, and per-weapon sight queries.
    // Counter is a weapon rule ("strikes first with this weapon when charged"), so the strike-order
    // and charge-contact evaluations hand the defender's melee weapons to the evaluator as carriers
    // — a Counter spear triggers the swap, a Counter rule on a ranged weapon doesn't, and the
    // impact-dice reduction counts only the models actually carrying a Counter weapon.
    // SightRuleQueries now evaluates the queried weapon's own rules, so an Indirect mortar reports
    // LoS-ignore while the same unit's rifle doesn't.
    [TestFixture]
    public class WeaponScopedCounterAndSightTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester());
        }

        [Test]
        public async Task CounterOnMeleeWeapon_ChargedUnitStrikesFirst()
        {
            DataBinding<UnitData> charger = MakeUnit(MakeBlade());

            Weapon counterSpear = MakeBlade("Spear");
            counterSpear.AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));
            DataBinding<UnitData> defender = MakeUnit(counterSpear);

            CombatActionContext context = await RunStrikeOrderStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(defender.GetValue()),
                "Counter on the defender's melee weapon swaps the roles — it strikes first.");
        }

        [Test]
        public async Task CounterOnRangedWeapon_DoesNotTriggerStrikeFirst()
        {
            DataBinding<UnitData> charger = MakeUnit(MakeBlade());

            Weapon counterRifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0,
                specialRules: new HashSet<ISpecialRule_Weapon>());
            counterRifle.AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));
            DataBinding<UnitData> defender = MakeUnit(MakeBlade(), counterRifle);

            CombatActionContext context = await RunStrikeOrderStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "Counter on a ranged weapon is not in melee scope — no strike-first.");
        }

        [Test]
        public void CounterImpactReduction_CountsOnlyModelsCarryingACounterWeapon()
        {
            DataBinding<UnitData> charger = MakeUnit(MakeBlade());

            Weapon counterSpear = MakeBlade("Spear");
            counterSpear.AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));

            // 3 models: only the first carries the Counter spear; the other two have plain blades.
            DataBinding<UnitData> defender = MakeUnitPerModelWeapons(
                new[] { counterSpear }, new[] { MakeBlade() }, new[] { MakeBlade() });

            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            IReadOnlyList<RuleOperation> operations = evaluator.EvaluateAll(
                new ChargeContactContext(charger.GetValue(), defender.GetValue()),
                DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender.GetValue()));

            RuleOperation.ChargeImpactHits reduction =
                operations.OfType<RuleOperation.ChargeImpactHits>().Single();
            Assert.That(reduction.DiceCount, Is.EqualTo(-1),
                "only the one model carrying the Counter spear reduces the charger's impact dice.");
        }

        [Test]
        public void SightQueries_ReportPerWeapon()
        {
            Weapon mortar = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0,
                specialRules: new HashSet<ISpecialRule_Weapon>());
            mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            Weapon rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0,
                specialRules: new HashSet<ISpecialRule_Weapon>());

            DataBinding<UnitData> attacker = MakeUnit(mortar, rifle);
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));

            Assert.That(SightRuleQueries.IgnoresTerrain(attacker.GetValue(), mortar, evaluator), Is.True,
                "the Indirect mortar ignores line-of-sight terrain.");
            Assert.That(SightRuleQueries.LineOfSightIgnoreSource(attacker.GetValue(), mortar, evaluator),
                Is.EqualTo("Indirect"), "the effect is attributed to its rule by name.");
            Assert.That(SightRuleQueries.IgnoresTerrain(attacker.GetValue(), rifle, evaluator), Is.False,
                "the same unit's plain rifle doesn't.");
            Assert.That(SightRuleQueries.IgnoresCover(attacker.GetValue(), mortar, evaluator), Is.True,
                "Indirect also ignores cover.");
            Assert.That(SightRuleQueries.IgnoresCover(attacker.GetValue(), rifle, evaluator), Is.False);
        }

        private async Task<CombatActionContext> RunStrikeOrderStage(DataBinding<UnitData> charger,
            DataBinding<UnitData> defender)
        {
            var stage = new DetermineStrikeOrderStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnStrikeOrderDetermined.Bind("done");

            var context = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            await stage.Enter(context);
            return context;
        }

        private static Weapon MakeBlade(string name = "Blade") =>
            new Weapon(name, rangeInches: 0f, attacks: 1, armorPenetration: 0,
                specialRules: new HashSet<ISpecialRule_Weapon>());

        /// <summary> A 3-model unit where every model carries the same weapon list. </summary>
        private DataBinding<UnitData> MakeUnit(params Weapon[] weapons) =>
            MakeUnitPerModelWeapons(weapons, weapons, weapons);

        private DataBinding<UnitData> MakeUnitPerModelWeapons(params Weapon[][] weaponsPerModel)
        {
            var modelBindings = new List<DataBinding<ModelData>>(weaponsPerModel.Length);
            foreach (Weapon[] weapons in weaponsPerModel)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: weapons.ToList(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
