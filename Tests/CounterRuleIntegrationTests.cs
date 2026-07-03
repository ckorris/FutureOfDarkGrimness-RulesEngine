using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Counter flows through the REAL melee strike-order
    // stage. DetermineStrikeOrderStage fires the OnCounterTrigger "when" for the defender (Subject seat);
    // on a queued StrikeFirst it swaps the attacker/defender roles so the charged Counter unit becomes the
    // first swinger and the charger becomes the strike-backer. The entire downstream swing/strike-back flow
    // keys off AttackingUnit/DefendingUnit, so the role swap IS "the charged unit strikes first". It also
    // announces the strike-first with a BannerBeat (captured here via a RecordingPresenter).
    [TestFixture]
    public class CounterRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;
        private RecordingPresenter _presenter = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _presenter = new RecordingPresenter();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), presenter: _presenter);
        }

        private BannerBeat? StrikeFirstBanner() =>
            _presenter.Beats.OfType<BannerBeat>().FirstOrDefault(b => b.BannerText.Contains("strikes first"));

        [Test]
        public async Task ChargedCounterUnit_StrikesFirst_RolesSwapped()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);   // 3 wounds
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);  // 5 wounds
            AttachCounter(defender);

            var context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(defender.GetValue()),
                "Counter: the charged unit becomes the attacker, so it swings first.");
            Assert.That(context.DefendingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "the charger becomes the defender — it is offered the strike-back.");
            Assert.That(context.AttackerRemainingWoundsAtStart, Is.EqualTo(5f),
                "the at-start wounds swap with the roles (the new attacker is the 5-wound Counter unit).");

            BannerBeat? banner = StrikeFirstBanner();
            Assert.That(banner, Is.Not.Null, "Counter's strike-first is announced with a banner beat.");
            Assert.That(banner!.BannerText, Does.Contain("counters the charge"),
                "the banner tells the player the charged unit strikes first thanks to Counter.");
        }

        [Test]
        public async Task NoCounter_KeepsNormalOrder()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            var context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "without Counter the charger stays the attacker and swings first.");
            Assert.That(context.DefendingUnit.GetValue(), Is.SameAs(defender.GetValue()));
            Assert.That(StrikeFirstBanner(), Is.Null, "no Counter → no strike-first announcement.");
        }

        [Test]
        public async Task CounterUnitWithoutMeleeWeapons_DoesNotStrikeFirst()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5, withMeleeWeapon: false);
            AttachCounter(defender);

            var context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "a Counter unit with no melee weapons cannot strike first — the swap is suppressed.");
            Assert.That(context.DefendingUnit.GetValue(), Is.SameAs(defender.GetValue()));
            Assert.That(StrikeFirstBanner(), Is.Null,
                "the swap is suppressed, so nothing is announced (the banner is gated on the actual strike-first).");
        }

        // Counter-Attack is Counter's strikes-first facet alone (no impact-dice reduction), so a charged
        // Counter-Attack unit gets the same role swap — it swings first.
        [Test]
        public async Task ChargedCounterAttackUnit_StrikesFirst_RolesSwapped()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            defender.GetValue().AttachRuleDefinition(new ResolvedRule("Counter-Attack", CoreRuleCatalog.CounterAttack));

            var context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(defender.GetValue()),
                "Counter-Attack: the charged unit strikes first via the same role swap as Counter.");
            Assert.That(context.DefendingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "the charger becomes the defender, offered the strike-back.");
            Assert.That(StrikeFirstBanner(), Is.Not.Null, "Counter-Attack also announces the strike-first with a banner.");
        }

        private async Task<CombatActionContext> RunStage(DataBinding<UnitData> charger, DataBinding<UnitData> defender)
        {
            var stage = new DetermineStrikeOrderStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnStrikeOrderDetermined.Bind("done");

            var context = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            await stage.Enter(context);
            return context;
        }

        private static void AttachCounter(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));

        private DataBinding<UnitData> MakeUnit(int modelCount, bool withMeleeWeapon = true)
        {
            // Counter only strikes first if the unit can actually fight, so DetermineStrikeOrderStage's
            // guard requires a living melee-armed model. Most tests give every model a melee weapon
            // (RangeInches 0); the no-weapon case proves the guard suppresses the swap.
            var weapons = withMeleeWeapon
                ? new List<Weapon> { new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0) }
                : new List<Weapon>();
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: weapons,
                    initialPosition: new Position(0, 0),
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
