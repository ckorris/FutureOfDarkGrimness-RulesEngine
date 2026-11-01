using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #422 - Brace(X), new in Core Rules v3.6.0: "Enemies charging this unit get -X total Impact rolls,
    // unless this unit is Fatigued." It took over 3.5 Counter's impact facet. Proved through the REAL
    // ResolveImpactHitsStage, which evaluates the charge-contact hook for both sides and folds the
    // charger's Impact(X) and the defender's Brace reduction into one ImpactSink before rolling the net.
    //
    // "Total" is read as the SUM over the defender's living models carrying the rule (X per model): Brace is
    // a weapon rule (AoF spears carry "Counter, Brace(1)"), so a 10-model spear unit cancels 10 dice. The
    // dice are counted with a recording roller - its first roll is the impact pool the stage threw, and an
    // empty record means the stage threw nothing.
    [TestFixture]
    public class BraceRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        // Brace(2) on 3 models cancels 2 x 3 = 6 dice: Impact(6) - 6 = 0, so nothing is rolled.
        [Test]
        public async Task BraceTwo_OnThreeModels_CancelsImpactSix()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            DataBinding<UnitData> defender = MakeUnit(MeleeModels(3));
            AttachBraceToUnit(defender, x: 2);

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts, Is.Empty, "Impact(6) - Brace(2) x 3 models = 0 dice: nothing to roll.");
            Assert.That(defender.RemainingWounds(), Is.EqualTo(3f));
        }

        // Brace(1) on 2 models cancels 2: Impact(6) rolls 4 dice.
        [Test]
        public async Task BraceOne_OnTwoModels_LeavesFourOfSixDice()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            DataBinding<UnitData> defender = MakeUnit(MeleeModels(2));
            AttachBraceToUnit(defender, x: 1);

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts.First(), Is.EqualTo(4f), "Impact(6) - Brace(1) x 2 models = 4 dice.");
        }

        // "... unless this unit is Fatigued": a defender that already fought this round cancels nothing.
        [Test]
        public async Task FatiguedBraceUnit_CancelsNothing()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            DataBinding<UnitData> defender = MakeUnit(MeleeModels(3));
            AttachBraceToUnit(defender, x: 2);
            FatigueUtilities.ApplyFatigued(defender.GetValue());

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts.First(), Is.EqualTo(6f),
                "a Fatigued Brace unit gets no reduction - all 6 impact dice are rolled.");
        }

        // Shaken counts as Fatigued in melee (#089): the same shared predicate switches Brace off.
        [Test]
        public async Task ShakenBraceUnit_CancelsNothing()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            DataBinding<UnitData> defender = MakeUnit(MeleeModels(3));
            AttachBraceToUnit(defender, x: 2);
            defender.GetValue().Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts.First(), Is.EqualTo(6f));
        }

        // Each side's clause reads its own bearer: a Fatigued charger rolls nothing whatever the defender has,
        // and the defender's (fresh) Brace has nothing to cancel.
        [Test]
        public async Task FatiguedCharger_IntoBraceUnit_RollsNothing()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            FatigueUtilities.ApplyFatigued(charger.GetValue());
            DataBinding<UnitData> defender = MakeUnit(MeleeModels(2));
            AttachBraceToUnit(defender, x: 1);

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts, Is.Empty);
        }

        // The corpus shape: Brace(1) rides a SPEAR (weapon scope), so only the models carrying one count.
        // 3 models, 2 with Brace spears, 1 with a plain blade: Impact(6) - 2 = 4 dice.
        [Test]
        public async Task BraceOnWeapons_CountsOnlyTheModelsCarryingOne()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            ResolvedRule brace = BraceAttachment(1);
            DataBinding<UnitData> defender = MakeUnit(new[]
            {
                new[] { Spear(brace) }, new[] { Spear(brace) }, new[] { Blade() },
            });

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts.First(), Is.EqualTo(4f),
                "only the two spear-carriers bring Brace(1) - the blade-armed model cancels nothing.");
        }

        // A Brace copy on a RANGED weapon brings nothing to the melee ("on its weapons in melee"), even when
        // the same attachment rides a melee weapon elsewhere in the unit (a unit-level mention is re-homed onto
        // every weapon the unit has).
        [Test]
        public async Task BraceOnlyOnARangedWeapon_DoesNotCount()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            ResolvedRule brace = BraceAttachment(1);
            DataBinding<UnitData> defender = MakeUnit(new[]
            {
                new[] { Spear(brace) },
                new[] { Rifle(brace), Blade() },
            });

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts.First(), Is.EqualTo(5f),
                "the rifleman's Brace is not on a melee weapon - only the spearman cancels a die.");
        }

        // A dead model's spear cancels nothing: the count is over LIVING carriers, so casualties shrink it.
        [Test]
        public async Task DeadCarriers_DoNotCount()
        {
            var roller = new RecordingDiceRoller();
            DataBinding<UnitData> charger = MakeImpactCharger(x: 6);
            ResolvedRule brace = BraceAttachment(1);
            DataBinding<UnitData> defender = MakeUnit(new[]
            {
                new[] { Spear(brace) }, new[] { Spear(brace) }, new[] { Spear(brace) },
            });
            IModel casualty = defender.GetValue().Models[0];
            casualty.DealWounds(casualty.TotalWounds);

            await RunImpactStage(roller, charger, defender);

            Assert.That(roller.RollCounts.First(), Is.EqualTo(4f), "two living spearmen cancel 2 of 6 dice.");
        }

        // An (X) rule fires once per distinct attachment. A fresh army shares ONE attachment across a weapon
        // entry's copies (fires once, counts every holder); a resumed game rehydrates one attachment PER WEAPON
        // (fires once per weapon, each counting only its own holder). Both must cancel 3 dice for 3 spearmen -
        // counting every carrier on every firing would cancel 9 after a resume.
        [Test]
        public async Task SharedAndPerWeaponAttachments_CancelTheSameDice()
        {
            ResolvedRule shared = BraceAttachment(1);
            DataBinding<UnitData> fresh = MakeUnit(new[]
            {
                new[] { Spear(shared) }, new[] { Spear(shared) }, new[] { Spear(shared) },
            });
            DataBinding<UnitData> resumed = MakeUnit(new[]
            {
                new[] { Spear(BraceAttachment(1)) }, new[] { Spear(BraceAttachment(1)) },
                new[] { Spear(BraceAttachment(1)) },
            });

            Assert.That(NetImpactDice(MakeImpactCharger(x: 6), fresh), Is.EqualTo(3), "one shared attachment");
            Assert.That(NetImpactDice(MakeImpactCharger(x: 6), resumed), Is.EqualTo(3),
                "one attachment per weapon (the resume shape) must not multiply the reduction.");
        }

        // A unit's two DIFFERENT Brace weapons stack, as (X) rules do: a model whose spear has Brace(1) and
        // whose shield-wall item put Brace(1) on another weapon cancels 2.
        [Test]
        public async Task TwoBraceAttachmentsOnOneModel_Stack()
        {
            DataBinding<UnitData> defender = MakeUnit(new[]
            {
                new[] { Spear(BraceAttachment(1)), Blade(BraceAttachment(1)) },
            });

            Assert.That(NetImpactDice(MakeImpactCharger(x: 6), defender), Is.EqualTo(4));
        }

        // Brace sits at the Subject seat: reducing a charger's impact dice is done when CHARGED, so it is never
        // a reason its bearer may charge (the #355 gate reads the Actor seat only).
        [Test]
        public void Brace_IsNotAReasonToCharge()
        {
            DataBinding<UnitData> unit = MakeUnit(new[] { new[] { Rifle(BraceAttachment(1)) } });

            Assert.That(ChargeContactRules.ActsOnChargeContact(unit.GetValue()), Is.False);
        }

        // The catalog entry: weapon scope (re-homed from a unit-level mention like Counter), one argument,
        // the 3.6 text, and the Fatigue gate as its condition.
        [Test]
        public void CatalogEntry_IsWeaponScoped_TakesX_AndCarriesTheFatigueGate()
        {
            SpecialRuleDefinition brace = CoreRuleCatalog.Brace;

            Assert.That(brace.Scope, Is.EqualTo(ERuleScope.Weapon));
            Assert.That(brace.EngineArgumentCount, Is.EqualTo(1));
            Assert.That(RuleArgumentArity.MaxReferencedArgIndex(brace), Is.EqualTo(0));
            Assert.That(brace.Description,
                Is.EqualTo("Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued."));

            HookEntry entry = brace.Passive.Single();
            Assert.That(entry.HookID, Is.EqualTo(EHookID.Melee_OnChargeContact));
            Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Subject));
            Assert.That(entry.Condition, Is.EqualTo(new Condition.Not(new Condition.CountsAsFatiguedInMelee())));
            Assert.That(entry.Effect, Is.EqualTo(new Effect.CancelImpactDice(new ValueSource.Arg(0))));
            Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Brace", out _), Is.True);
        }

        // --- harness ---

        private async Task RunImpactStage(RecordingDiceRoller roller, DataBinding<UnitData> charger,
            DataBinding<UnitData> defender)
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), roller);
            var stage = new ResolveImpactHitsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnImpactResolved.Bind("done");

            var context = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            await stage.Enter(context);
        }

        // The stage's own fold, read without rolling: the net dice it would throw (negative = cancelled past 0).
        private static int NetImpactDice(DataBinding<UnitData> charger, DataBinding<UnitData> defender)
        {
            var participants = new List<RuleParticipant> { RuleParticipant.Actor(charger.GetValue()) };
            participants.AddRange(DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender.GetValue()));
            var sink = new ImpactSink();
            sink.ApplyFrom(new RuleEvaluator(new FixedDiceRoller(4)).EvaluateAll(
                new ChargeContactContext(charger.GetValue(), defender.GetValue()), participants.ToArray()));
            return sink.TotalDice;
        }

        private static ResolvedRule BraceAttachment(int x) =>
            new ResolvedRule("Brace", CoreRuleCatalog.Brace, new RuleArgument[] { new RuleArgument.Int(x) });

        private static void AttachBraceToUnit(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(BraceAttachment(x));

        private DataBinding<UnitData> MakeImpactCharger(int x)
        {
            DataBinding<UnitData> charger = MakeUnit(MeleeModels(1));
            charger.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(x) }));
            return charger;
        }

        private static Weapon Spear(ResolvedRule brace)
        {
            var spear = new Weapon("Spear", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            spear.AttachRuleDefinition(brace);
            return spear;
        }

        private static Weapon Blade(ResolvedRule? rule = null)
        {
            var blade = new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            if (rule != null) blade.AttachRuleDefinition(rule);
            return blade;
        }

        private static Weapon Rifle(ResolvedRule rule)
        {
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            rifle.AttachRuleDefinition(rule);
            return rifle;
        }

        private static Weapon[][] MeleeModels(int count) =>
            Enumerable.Range(0, count).Select(_ => new[] { Blade() }).ToArray();

        private DataBinding<UnitData> MakeUnit(Weapon[][] weaponsPerModel)
        {
            var modelBindings = new List<DataBinding<ModelData>>(weaponsPerModel.Length);
            foreach (Weapon[] weapons in weaponsPerModel)
            {
                var model = new ModelData(baseRadiusInches: 0.75f, weapons: weapons.ToList(),
                    initialPosition: new Position(0, 0), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private sealed class RecordingDiceRoller : IDiceRoller
        {
            public List<float> RollCounts { get; } = new();

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                RollCounts.Add(rollCount);
                float[] perSide = new float[sideCount];
                perSide[1] = rollCount; // every die a 2: each impact die hits, each save (4+) fails
                return new DiceResults(perSide);
            }
        }
    }
}
