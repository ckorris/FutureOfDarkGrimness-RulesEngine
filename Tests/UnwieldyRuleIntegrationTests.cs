using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P20 Unwieldy - "Strikes last when charging." The exact mirror of Counter, seen from the
    // charger's side: both describe one swapped melee, so both route through DetermineStrikeOrderStage's
    // single role swap. These pin that the swap happens from the ATTACKER's seat (Counter's participants
    // are all Subject-seated, so an Actor-seated rule was invisible to this stage before), that a charger
    // and defender who BOTH have their rule still swap exactly once, and that the corpus's one-shot
    // ("gets Unwieldy in melee once, next time the effect would apply") is spent by the melee it applies to.
    [TestFixture]
    public class UnwieldyRuleIntegrationTests
    {
        private const string RuleName = "Unwieldy in melee";

        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;
        private RecordingPresenter _presenter = null!;
        private RuleResolver _resolver = null!;

        // The shipped shape, hand-built because the engine suite cannot read the app's rule supplement.
        // QuickShotAndUnwieldyShippedDataTests asserts the authored definition matches this.
        private static readonly SpecialRuleDefinition Unwieldy = new(RuleName,
            new[]
            {
                new HookEntry(EHookID.Melee_OnCounterTrigger,
                    new Condition.Always(),
                    new Effect.StrikeLast(),
                    ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>());

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _presenter = new RecordingPresenter();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(Unwieldy);
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), presenter: _presenter,
                ruleResolver: _resolver);
        }

        private BannerBeat? StrikeOrderBanner() =>
            _presenter.Beats.OfType<BannerBeat>().FirstOrDefault(b => b.BannerText.Contains("strikes first"));

        [Test]
        public async Task AnUnwieldyCharger_LetsTheChargedUnitStrikeFirst()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            Attach(charger);

            CombatActionContext context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(defender.GetValue()),
                "'strikes last when charging' - the charged unit takes the first swing.");
            Assert.That(context.DefendingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "the Unwieldy charger becomes the strike-backer.");
            Assert.That(context.ChargingUnit!.GetValue(), Is.SameAs(charger.GetValue()),
                "the charger is still recorded as the charger - it is the one that gets Fatigued (#020).");
        }

        [Test]
        public async Task WithoutIt_TheChargerStrikesFirstAsNormal()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            CombatActionContext context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()));
            Assert.That(StrikeOrderBanner(), Is.Null, "nothing to announce.");
        }

        [Test]
        public async Task TheBanner_NamesTheChargersOwnFailing_NotACounter()
        {
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            Attach(charger);

            await RunStage(charger, defender);

            BannerBeat? banner = StrikeOrderBanner();
            Assert.That(banner, Is.Not.Null, "the swap is announced, exactly as Counter's is.");
            Assert.That(banner!.BannerText, Does.Contain("unwieldy"),
                "the charger fumbled - saying the defender 'counters the charge' would credit the wrong unit.");
            Assert.That(banner.BannerText, Does.Not.Contain("counters"));
        }

        [Test]
        public async Task AgainstADefenderThatCannotSwing_TheChargerStrikesNormally()
        {
            // The swap only makes sense if there is someone to put ahead of the charger. Swapping in a unit
            // with no melee weapon drops the melee into ChooseMeleeWeaponStage with an empty pool.
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5, withMeleeWeapon: false);
            Attach(charger);

            CombatActionContext context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()));
            Assert.That(StrikeOrderBanner(), Is.Null, "suppressed swap - nothing announced.");
        }

        [Test]
        public async Task AnUnwieldyChargerIntoACounterUnit_SwapsExactlyOnce()
        {
            // Both rules ask for the same thing. Applying each in turn would swap twice and hand the first
            // swing back to the Unwieldy charger - the one outcome neither rule describes.
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            Attach(charger);
            defender.GetValue().AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));

            CombatActionContext context = await RunStage(charger, defender);

            Assert.That(context.AttackingUnit.GetValue(), Is.SameAs(defender.GetValue()),
                "one swap, not two - the charged unit still swings first.");
        }

        [Test]
        public async Task AOneShotGrant_IsSpentByTheMeleeItApplies_To()
        {
            // "which gets Unwieldy in melee ONCE (next time the effect would apply)" - the debuff's grant.
            // DetermineStrikeOrderStage's evaluation is a live one, so it spends the grant as it fires.
            DataBinding<UnitData> charger = MakeUnit(modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            GrantOnce(charger, RuleName);

            CombatActionContext first = await RunStage(charger, defender);
            Assert.That(first.AttackingUnit.GetValue(), Is.SameAs(defender.GetValue()),
                "the granted rule fires on the melee it was granted for.");

            CombatActionContext second = await RunStage(charger, defender);
            Assert.That(second.AttackingUnit.GetValue(), Is.SameAs(charger.GetValue()),
                "'once' - a second charge is unaffected.");
        }

        private async Task<CombatActionContext> RunStage(DataBinding<UnitData> charger,
            DataBinding<UnitData> defender)
        {
            var stage = new DetermineStrikeOrderStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnStrikeOrderDetermined.Bind("done");

            var context = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            await stage.Enter(context);
            return context;
        }

        private static void Attach(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(RuleName, Unwieldy));

        private static void GrantOnce(DataBinding<UnitData> unit, string ruleName) =>
            unit.GetValue().Tokens.AddToken(new Rules.Tokens.Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.FirstTrigger(),
                Payload: new Rules.Tokens.TokenPayload.RuleGrant(ruleName, ELifetime.NextTrigger)));

        private DataBinding<UnitData> MakeUnit(int modelCount, bool withMeleeWeapon = true)
        {
            var weapons = withMeleeWeapon
                ? new List<Weapon> { new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0) }
                : new List<Weapon>();
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, weapons, new Position(0, 0), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
