using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
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
    // #422: the 3.6 Fatigue clause ("unless this unit is Fatigued") is pinned here; Counter's 3.5 impact
    // facet is gone (now Brace(X)) - see ImpactRuleIntegrationTests. Counter-Attack (dropped by 3.6) and
    // its test were removed with the catalog entry.
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

        // #422 - Core Rules v3.6.0: "May strike first with this weapon when charged, unless this unit is
        // Fatigued." A defender that already fought this round (Fatigued) keeps the normal order: the charger
        // swings first and the defender is offered the strike-back, with no banner.
        [Test]
        public async Task FatiguedCounterDefender_DoesNotStrikeFirst()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachCounter(defender);
            FatigueUtilities.ApplyFatigued(defender.GetValue());

            var context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "a Fatigued Counter unit may not strike first - the charger keeps the first swing.");
            Assert.That(context.DefendingUnit.GetValue(), Is.SameAs(defender.GetValue()));
            Assert.That(StrikeFirstBanner(), Is.Null, "no strike-first happened, so nothing is announced.");
        }

        // Shaken always counts as Fatigued in melee (#089), and the gate is the shared predicate
        // (FatigueUtilities.CountsAsFatiguedInMelee), so a Shaken Counter unit loses the strike-first too.
        [Test]
        public async Task ShakenCounterDefender_DoesNotStrikeFirst()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachCounter(defender);
            defender.GetValue().Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            var context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "Shaken counts as Fatigued in melee, so Counter's strike-first is off.");
            Assert.That(StrikeFirstBanner(), Is.Null);
        }

        // "unless THIS unit is Fatigued": the gate reads the countering unit, never the charger. A charger
        // that is itself Fatigued (it fought earlier this round) is still countered by a fresh defender.
        [Test]
        public async Task FatiguedCharger_DoesNotStopAFreshDefendersCounter()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachCounter(defender);
            FatigueUtilities.ApplyFatigued(charger.GetValue());

            var context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(defender.GetValue()),
                "the fresh Counter unit still swings first - only its own fatigue switches Counter off.");
            Assert.That(StrikeFirstBanner(), Is.Not.Null);
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
