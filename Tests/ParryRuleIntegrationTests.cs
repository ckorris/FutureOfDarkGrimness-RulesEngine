using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #423 vertical slice: Parry(X), new in Core Rules v3.6.0 - "When this unit takes hits in melee, roll X dice per
    // model with this rule in it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks
    // which)." Driven through the REAL stages: the melee hit chain (DetermineHitRoll -> RollToHit -> ParryHits ->
    // DetermineSaveRollsNeeded -> RollToSave), the whole SwingMeleeWeaponStage twice on one CombatActionContext (the
    // B1 budget), the whole MeleeStage (a strike-back is a new attack), ResolveImpactHitsStage (Impact hits are
    // parryable) and ResolveRavageWoundsStage (Ravage deals wounds, not hits). A scripted roller returns every die of
    // one roll on the face queued for it and records each roll's size, so "how many Parry dice were rolled" is the
    // second recorded roll of a swing (hits, Parry, saves).
    //   - dice = min(X x living carriers, hits taken); the carriers are counted by the rule graph (Effect.ParryHits
    //     names the models whose melee weapons carry the attachment), never by name;
    //   - each 6+ removes a hit from the WORST group first (Rending's natural-6 group before the base group);
    //   - one budget per enemy melee attack (B1): a two-profile attacker's second swing gets the remainder, a dead
    //     carrier does not shrink a pool already set, a strike-back is a fresh pool, Impact hits have their own;
    //   - the defender's own roll: a Fatigued defender's 6s count (#418 gates the ATTACKER's hit roll);
    //   - presentation: a Defense dice beat "Parry vs {weapon}" whose result line names the parried groups, the log
    //     line carries the same text, and the save beat that follows reads "(after Parry)".
    [TestFixture]
    public class ParryRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        // ---- the dice count: min(X x carriers, hits) --------------------------------------------------------------

        [Test]
        public async Task FiveParryOneModels_VsEightHits_RollFiveDice()
        {
            var roller = new ScriptedRoller(4);
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, Claws(2));
            DataBinding<UnitData> defender = MakeUnit("Defender", 5, HandWeapon(1));

            await RunChain(Context(roller), charger, defender, Claws(2), 4);

            Assert.That(roller.Counts[0], Is.EqualTo(8f), "premise: 8 attacks all hit");
            Assert.That(roller.Counts[1], Is.EqualTo(5f), "5 models x Parry(1) = 5 dice, capped by nothing (8 hits)");
        }

        [Test]
        public async Task TwoParryOneModels_VsOneHit_RollOneDie()
        {
            var roller = new ScriptedRoller(4);
            DataBinding<UnitData> charger = MakeUnit("Charger", 1, Claws(1));
            DataBinding<UnitData> defender = MakeUnit("Defender", 2, HandWeapon(1));

            await RunChain(Context(roller), charger, defender, Claws(1), 1);

            Assert.That(roller.Counts[1], Is.EqualTo(1f), "\"up to as many dice as hits taken\": 1 hit caps 2 dice at 1");
        }

        [Test]
        public async Task TwoParryThreeModels_VsTenHits_RollSixDice()
        {
            var roller = new ScriptedRoller(4);
            DataBinding<UnitData> charger = MakeUnit("Charger", 5, Claws(2));
            DataBinding<UnitData> defender = MakeUnit("Defender", 2, HandWeapon(3));

            await RunChain(Context(roller), charger, defender, Claws(2), 5);

            Assert.That(roller.Counts[1], Is.EqualTo(6f), "2 models x Parry(3) = 6 dice against 10 hits");
        }

        [Test]
        public async Task OnlyModelsCarryingAParryWeapon_Count()
        {
            // 3 models: two with Hand Weapons (Parry(1)), one with a plain blade. 4 hits -> 2 dice, not 3.
            var roller = new ScriptedRoller(4);
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, Claws(1));
            DataBinding<UnitData> defender = MakeUnit("Defender", new[]
            {
                new List<Weapon> { HandWeapon(1) }, new List<Weapon> { HandWeapon(1) }, new List<Weapon> { Blade() },
            });

            await RunChain(Context(roller), charger, defender, Claws(1), 4);

            Assert.That(roller.Counts[1], Is.EqualTo(2f), "\"X dice per model with this rule\": the blade-only model adds none");
        }

        [Test]
        public async Task AParryWeaponThatIsNotAMeleeWeapon_CountsNoCarrier()
        {
            // A unit-level mention is re-homed onto EVERY weapon at army load (the Brace shape); a bow carrying Parry
            // brings nothing to the melee. 3 models with Parry bows only: no Parry roll at all.
            var roller = new ScriptedRoller(4);
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, Claws(1));
            Weapon bow = new Weapon("Bow", rangeInches: 18f, attacks: 1, armorPenetration: 0);
            bow.AttachRuleDefinition(ParryRule(1));
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, bow);
            var presenter = new RecordingPresenter();

            await RunChain(Context(roller, presenter), charger, defender, Claws(1), 4);

            Assert.That(ParryBeats(presenter), Is.Empty);
            Assert.That(roller.Counts, Is.EqualTo(new[] { 4f, 4f }), "hits, then saves: no Parry roll between them");
        }

        [Test]
        public async Task ParryAttachedToTheUnitItself_CountsEveryLivingModel()
        {
            // A grant, or a hand-built attachment: no carrying weapon, so "per model with this rule" is every model.
            var roller = new ScriptedRoller(4);
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, Claws(2));
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, Blade());
            defender.GetValue().AttachRuleDefinition(ParryRule(1));
            defender.GetValue().Models[0].DealWounds(1f);

            await RunChain(Context(roller), charger, defender, Claws(2), 4);

            Assert.That(roller.Counts[1], Is.EqualTo(2f), "3 models minus the dead one");
        }

        // ---- the pick: worst group first ----------------------------------------------------------------------------

        [Test]
        public async Task ASix_RemovesARendingHit_BeforeABaseHit()
        {
            // Rending claws: 4 attacks land 2 natural 6s (the AP+4 group) and 2 base hits. 3 Parry dice, all 6s:
            // both Rending hits go first, then one base hit. The groups keep their identity and source.
            var roller = new ScriptedRoller(4);
            roller.QueueHistogram(0, 0, 0, 2, 0, 2); // the hit roll
            roller.QueueFace(6);                      // the Parry roll
            Weapon claws = Claws(1);
            claws.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, claws);
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, HandWeapon(1));

            CombatMetadata metadata = await RunChain(Context(roller), charger, defender, claws, 4, runSaves: false);

            metadata.QueryForResult(out RollToHitResults hits);
            SuccessfulHitInfo rending = hits.SuccessfulHitList.Single(g => g.Source.Kind == EHitSourceKind.PerHitAp);
            SuccessfulHitInfo baseGroup = hits.SuccessfulHitList.Single(g => g.Source.Kind == EHitSourceKind.BaseRolled);
            Assert.That(rending.HitCount, Is.EqualTo(0f), "both Rending hits parried first");
            Assert.That(rending.Source.RuleName, Is.EqualTo("Rending"), "the emptied group keeps its source");
            Assert.That(baseGroup.HitCount, Is.EqualTo(1f), "one of the two base hits parried");

            metadata.QueryForResult(out ParryResults parry);
            Assert.That(parry.Summary, Is.EqualTo("Parry: 3 dice, 3 x 6+ - ignored 2 Rending hits, 1 base hit"));
            Assert.That(parry.Ignored.Select(g => g.Name), Is.EqualTo(new[] { "Rending", "base" }), "worst first");
        }

        [Test]
        public async Task OneSix_RemovesOneRendingHit_AndLeavesTheBaseHitsAlone()
        {
            var roller = new ScriptedRoller(4);
            roller.QueueHistogram(0, 0, 0, 2, 0, 2);
            roller.QueueHistogram(0, 0, 2, 0, 0, 1); // one 6+ of three Parry dice
            Weapon claws = Claws(1);
            claws.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, claws);
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, HandWeapon(1));

            CombatMetadata metadata = await RunChain(Context(roller), charger, defender, claws, 4, runSaves: false);

            metadata.QueryForResult(out RollToHitResults hits);
            Assert.That(hits.SuccessfulHitList.Single(g => g.Source.Kind == EHitSourceKind.PerHitAp).HitCount, Is.EqualTo(1f));
            Assert.That(hits.SuccessfulHitList.Single(g => g.Source.Kind == EHitSourceKind.BaseRolled).HitCount, Is.EqualTo(2f));
            metadata.QueryForResult(out ParryResults parry);
            Assert.That(parry.Summary, Is.EqualTo("Parry: 3 dice, one 6+ - ignored 1 Rending hit"));
        }

        [Test]
        public async Task NoSixes_RemovesNothing_AndSaysSo()
        {
            var roller = new ScriptedRoller(4);
            roller.QueueFace(4); // hits
            roller.QueueFace(5); // Parry: 5s are not 6+
            DataBinding<UnitData> charger = MakeUnit("Charger", 3, Claws(1));
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, HandWeapon(1));
            var presenter = new RecordingPresenter();

            CombatMetadata metadata = await RunChain(Context(roller, presenter), charger, defender, Claws(1), 3);

            metadata.QueryForResult(out RollToHitResults hits);
            Assert.That(hits.SuccessfulHitList.Sum(g => g.HitCount), Is.EqualTo(3f));
            metadata.QueryForResult(out ParryResults parry);
            Assert.That(parry.Summary, Is.EqualTo("Parry: 3 dice, no 6+"));
            Assert.That(parry.RemovedHits, Is.False);
            Assert.That(SaveBeats(presenter).Single().Label, Is.EqualTo("Claws: 3 hits"),
                "nothing was removed, so the save beat is not labelled (after Parry)");
        }

        // ---- the budget (B1): one pool per enemy melee attack --------------------------------------------------------

        [Test]
        public async Task ATwoProfileAttacker_SharesOneParryPoolAcrossBothSwings()
        {
            // 6 x Parry(1) = 6 dice for the whole attack. Claws land 4 hits -> 4 dice; the Fangs' 4 hits get the 2 left.
            var roller = new ScriptedRoller(4);
            roller.QueueFace(4); roller.QueueFace(1); roller.QueueFace(6); // claws: hits, Parry, saves
            roller.QueueFace(4); roller.QueueFace(1); roller.QueueFace(6); // fangs: hits, Parry, saves
            Weapon claws = Claws(4, "Claws");
            Weapon fangs = Claws(4, "Fangs");
            DataBinding<UnitData> charger = MakeUnit("Charger", new[] { new List<Weapon> { claws, fangs } }, woundsPerModel: 3);
            DataBinding<UnitData> defender = MakeUnit("Defender", 6, HandWeapon(1));
            var presenter = new RecordingPresenter();
            ParryTestContext ctx = Context(roller, presenter);
            CombatActionContext attack = Attack(ctx, charger, defender);

            await RunSwing(ctx, attack, claws);
            await RunSwing(ctx, attack, fangs);

            Assert.That(roller.Counts, Is.EqualTo(new[] { 4f, 4f, 4f, 4f, 2f, 4f }),
                "hits 4, Parry 4, saves 4; hits 4, Parry 2 (the remainder), saves 4");
            Assert.That(attack.ParryBudget.Total, Is.EqualTo(6));
            Assert.That(attack.ParryBudget.Remaining, Is.EqualTo(0f));
            Assert.That(ParryBeats(presenter).Last().ModifierTags, Does.Contain("6 of 6 Parry dice used this attack"));
        }

        [Test]
        public async Task AThirdSwing_WithThePoolSpent_RollsNoParryDice_AndTheLogSaysWhy()
        {
            var roller = new ScriptedRoller(4);
            roller.QueueFace(4); roller.QueueFace(1); roller.QueueFace(6);
            roller.QueueFace(4); roller.QueueFace(6);
            Weapon claws = Claws(4, "Claws");
            Weapon fangs = Claws(4, "Fangs");
            DataBinding<UnitData> charger = MakeUnit("Charger", new[] { new List<Weapon> { claws, fangs } }, woundsPerModel: 3);
            DataBinding<UnitData> defender = MakeUnit("Defender", 4, HandWeapon(1));
            var log = new RecordingTextOutput();
            ParryTestContext ctx = Context(roller, log: log);
            CombatActionContext attack = Attack(ctx, charger, defender);

            await RunSwing(ctx, attack, claws);
            await RunSwing(ctx, attack, fangs);

            Assert.That(roller.Counts, Is.EqualTo(new[] { 4f, 4f, 4f, 4f, 4f }), "the second swing has no Parry roll");
            Assert.That(log.Entries.Select(e => e.Message), Has.Member(
                "Defender's Parry has no dice left this attack (4 already rolled); 4 hits from Charger's Fangs stand."));
        }

        [Test]
        public async Task ACarrierKilledBetweenSwings_DoesNotShrinkAPoolAlreadySet()
        {
            // D4: the pool is X x the carriers when the attack's FIRST hits land. 6 carriers; the claws' 2 hits take 2
            // dice (4 left); three carriers die; the fangs' 6 hits still get the 4 left, not 3.
            var roller = new ScriptedRoller(4);
            roller.QueueFace(4); roller.QueueFace(1); roller.QueueFace(6);
            roller.QueueFace(4); roller.QueueFace(1); roller.QueueFace(6);
            Weapon claws = Claws(2, "Claws");
            Weapon fangs = Claws(6, "Fangs");
            DataBinding<UnitData> charger = MakeUnit("Charger", new[] { new List<Weapon> { claws, fangs } }, woundsPerModel: 3);
            DataBinding<UnitData> defender = MakeUnit("Defender", 6, HandWeapon(1));
            ParryTestContext ctx = Context(roller);
            CombatActionContext attack = Attack(ctx, charger, defender);

            await RunSwing(ctx, attack, claws);
            foreach (IModel model in defender.GetValue().Models.Take(3)) model.DealWounds(1f);
            await RunSwing(ctx, attack, fangs);

            Assert.That(roller.Counts[1], Is.EqualTo(2f), "claws: 2 hits cap the 6 dice at 2");
            Assert.That(roller.Counts[4], Is.EqualTo(4f), "fangs: the 4 dice left, though only 3 carriers still stand");
        }

        [Test]
        public async Task AStrikeBack_IsANewAttack_WithItsOwnParryPool()
        {
            // The charger's 6 claw hits spend the defender's whole pool (6). The defender strikes back with 6 hits;
            // the CHARGER parries that with its own 3 x Parry(1) - a fresh pool, not the spent one.
            var roller = new ScriptedRoller(4);
            roller.QueueFace(4); roller.QueueFace(6);                      // charger hits 6, defender parries all 6
            roller.QueueFace(4); roller.QueueFace(1); roller.QueueFace(1); // strike-back hits 6, charger parries 3 (none), saves fail
            var log = new RecordingTextOutput();
            var (ctx, charger, defender) = BuildCharge(roller, log, chargerModels: 3, chargerWeapon: ParryClaws(2),
                defenderModels: 6, defenderWeapon: HandWeapon(1));

            await RunMelee(ctx, charger);

            Assert.That(roller.Counts.Take(5), Is.EqualTo(new[] { 6f, 6f, 6f, 3f, 6f }),
                $"hits 6 / Parry 6 / (saves skipped: nothing left) / strike-back hits 6 / Parry 3 / saves 6; got {roller.Trace}");
            Assert.That(defender.GetValue().RemainingWounds, Is.EqualTo(6f), "every charger hit was parried");
            Assert.That(log.Entries.Select(e => e.Message), Has.Some.StartsWith("Charger vs Defender's Hand Weapon - Parry: 3 dice, no 6+"));
        }

        // ---- Impact hits are parryable; Ravage wounds are not --------------------------------------------------------

        [Test]
        public async Task ImpactHits_AreParried_BeforeTheirSaves()
        {
            // Impact(5): 5 dice at 2+ all hit; 3 x Parry(1) = 3 dice, all 6s, ignore 3; the 2 left fail their saves.
            var roller = new ScriptedRoller(4);
            roller.QueueFace(2); roller.QueueFace(6); roller.QueueFace(1);
            DataBinding<UnitData> charger = MakeUnit("Charger", 1, Claws(1));
            charger.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(5) }));
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, HandWeapon(1));
            var presenter = new RecordingPresenter();

            await RunImpact(Context(roller, presenter), charger, defender);

            Assert.That(roller.Counts, Is.EqualTo(new[] { 5f, 3f, 2f }), "impact dice, Parry dice, the saves left");
            Assert.That(defender.GetValue().RemainingWounds, Is.EqualTo(1f), "3 wounds - 2 unsaved hits");
            Assert.That(ParryBeats(presenter).Single().Label, Is.EqualTo("Parry vs Impact"));
            Assert.That(SaveBeats(presenter, "Impact").Single().Label, Is.EqualTo("Impact: 2 hits (after Parry)"));
        }

        [Test]
        public async Task RavageWounds_AreNotHits_SoNothingIsParried()
        {
            var roller = new ScriptedRoller(6);
            DataBinding<UnitData> charger = MakeUnit("Charger", 1, Claws(1));
            charger.GetValue().AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, HandWeapon(1));
            var presenter = new RecordingPresenter();

            await RunRavage(Context(roller, presenter), charger, defender);

            Assert.That(roller.Counts, Is.EqualTo(new[] { 3f }), "the Ravage pool is the only roll - no Parry dice");
            Assert.That(ParryBeats(presenter), Is.Empty);
            Assert.That(defender.GetValue().RemainingWounds, Is.EqualTo(0f), "3 wounds, no save, no Parry");
        }

        // ---- the defender's own roll ---------------------------------------------------------------------------------

        [Test]
        public async Task AFatiguedDefender_StillParries_AndItsSixesCount()
        {
            var roller = new ScriptedRoller(4);
            roller.QueueFace(4); roller.QueueFace(6);
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, Claws(1));
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, HandWeapon(1));
            FatigueUtilities.ApplyFatigued(defender.GetValue());

            CombatMetadata metadata = await RunChain(Context(roller), charger, defender, Claws(1), 4);

            metadata.QueryForResult(out ParryResults parry);
            Assert.That(parry.DiceRolled, Is.EqualTo(3f));
            Assert.That(parry.HitsIgnored, Is.EqualTo(3f), "#418 gates the attacker's hit roll, not the defender's Parry dice");
        }

        [Test]
        public async Task AFatiguedAttackersSixes_AreStillHits_ThatParryCanIgnore()
        {
            // The attacker hits only on 6s and those 6s trigger no special rule (#418) - they are hits all the same.
            var roller = new ScriptedRoller(4);
            roller.QueueFace(6); roller.QueueFace(6);
            Weapon claws = Claws(1);
            claws.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, claws);
            FatigueUtilities.ApplyFatigued(charger.GetValue());
            DataBinding<UnitData> defender = MakeUnit("Defender", 2, HandWeapon(1));

            CombatMetadata metadata = await RunChain(Context(roller), charger, defender, claws, 4, runSaves: false);

            metadata.QueryForResult(out RollToHitResults hits);
            Assert.That(hits.SuccessfulHitList.Any(g => g.Source.Kind == EHitSourceKind.PerHitAp), Is.False,
                "premise: a fatigued attack's 6s peel no Rending group");
            metadata.QueryForResult(out ParryResults parry);
            Assert.That(parry.Summary, Is.EqualTo("Parry: 2 dice, 2 x 6+ - ignored 2 base hits"));
            Assert.That(hits.SuccessfulHitList.Sum(g => g.HitCount), Is.EqualTo(2f));
        }

        // ---- the probabilistic roller ----------------------------------------------------------------------------------

        [Test]
        public async Task UnderTheProbabilisticRoller_TheReductionIsFractional_AndNothingThrows()
        {
            // 8 attacks at 4+ -> 4 expected hits; 5 x Parry(1) capped at 4 dice; 4/6 of a hit ignored.
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, Claws(2));
            DataBinding<UnitData> defender = MakeUnit("Defender", 5, HandWeapon(1));
            var ctx = new ParryTestContext(_store, new ProbabilisticDiceRoller(), new RecordingTextOutput(), null, null);

            CombatMetadata metadata = await RunChain(ctx, charger, defender, Claws(2), 4);

            metadata.QueryForResult(out ParryResults parry);
            Assert.That(parry.DiceRolled, Is.EqualTo(4f).Within(0.001f));
            Assert.That(parry.HitsIgnored, Is.EqualTo(4f / 6f).Within(0.001f));
            metadata.QueryForResult(out RollToHitResults hits);
            Assert.That(hits.SuccessfulHitList.Sum(g => g.HitCount), Is.EqualTo(4f - 4f / 6f).Within(0.001f));
            Assert.That(parry.Summary, Is.EqualTo("Parry: 4 dice, 0.67 x 6+ - ignored 0.67 base hits"));
        }

        // ---- negative control ------------------------------------------------------------------------------------------

        [Test]
        public async Task ADefenderWithoutParry_IsUntouched()
        {
            var roller = new ScriptedRoller(4);
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, Claws(1));
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, Blade());
            var presenter = new RecordingPresenter();

            CombatMetadata metadata = await RunChain(Context(roller, presenter), charger, defender, Claws(1), 4);

            Assert.That(roller.Counts, Is.EqualTo(new[] { 4f, 4f }), "hits, saves - no roll between them");
            Assert.That(ParryBeats(presenter), Is.Empty);
            Assert.That(SaveBeats(presenter).Single().Label, Is.EqualTo("Claws: 4 hits"));
            metadata.QueryForResult(out ParryResults parry);
            Assert.That(parry, Is.SameAs(ParryResults.None));
        }

        // ---- presentation ----------------------------------------------------------------------------------------------

        [Test]
        public async Task TheParryBeat_IsADefenseRoll_WhoseResultLineNamesTheParriedGroups()
        {
            var roller = new ScriptedRoller(4);
            roller.QueueHistogram(0, 0, 0, 2, 0, 2);
            roller.QueueFace(6);
            Weapon claws = Claws(1);
            claws.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            DataBinding<UnitData> charger = MakeUnit("Charger", 4, claws);
            DataBinding<UnitData> defender = MakeUnit("Defender", 3, HandWeapon(1));
            var presenter = new RecordingPresenter();
            var log = new RecordingTextOutput();

            await RunChain(Context(roller, presenter, log), charger, defender, claws, 4);

            DiceRolledBeat beat = ParryBeats(presenter).Single();
            Assert.That(beat.Label, Is.EqualTo("Parry vs Claws"));
            Assert.That(beat.Category, Is.EqualTo(ERollBeatCategory.Defense));
            Assert.That(beat.SuccessThreshold, Is.EqualTo(6), "6+ highlighted");
            Assert.That(beat.Total, Is.EqualTo(3f));
            Assert.That(beat.Context, Is.EqualTo("Defender parries Charger"));
            Assert.That(beat.ResultSummary, Is.EqualTo("Parry: 3 dice, 3 x 6+ - ignored 2 Rending hits, 1 base hit"));
            Assert.That(beat.ModifierTags, Is.EqualTo(new[] { "3 models x Parry(1)", "4 hits -> 3 dice" }));

            Assert.That(log.Entries.Select(e => e.Message), Has.Member(
                "Defender vs Charger's Claws - Parry: 3 dice, 3 x 6+ - ignored 2 Rending hits, 1 base hit " +
                "(3 models x Parry(1); 4 hits -> 3 dice)."));

            // The beats come in table order: hits, Parry, then the (reduced) saves.
            List<DiceRolledBeat> dice = presenter.Beats.OfType<DiceRolledBeat>().ToList();
            Assert.That(dice.Select(b => b.Label), Is.EqualTo(new[]
            {
                "Roll to Hit - Claws", "Parry vs Claws", "Claws: 1 hit (after Parry)",
            }), "the Rending bucket vanished (both its hits parried), the base bucket reads 1 hit after Parry");
        }

        [Test]
        public void TheResultLine_HasOneShape_ForTheThreeOutcomes()
        {
            Assert.That(ParryHitsStage<ICombatMetadata>.Summary(5f, 3f, new[]
            {
                new ParriedGroup("Rending", 2f), new ParriedGroup("base", 1f),
            }), Is.EqualTo("Parry: 5 dice, 3 x 6+ - ignored 2 Rending hits, 1 base hit"));
            Assert.That(ParryHitsStage<ICombatMetadata>.Summary(3f, 1f, new[] { new ParriedGroup("Rending", 1f) }),
                Is.EqualTo("Parry: 3 dice, one 6+ - ignored 1 Rending hit"));
            Assert.That(ParryHitsStage<ICombatMetadata>.Summary(3f, 0f, Array.Empty<ParriedGroup>()),
                Is.EqualTo("Parry: 3 dice, no 6+"));
            Assert.That(ParryHitsStage<ICombatMetadata>.Summary(1f, 1f, new[] { new ParriedGroup("base", 1f) }),
                Is.EqualTo("Parry: 1 die, one 6+ - ignored 1 base hit"));
        }

        [Test]
        public void GroupNames_AreTheRuleThatMadeTheGroupWorse_OrBase()
        {
            Assert.That(ParryHitsStage<ICombatMetadata>.GroupName(new HitGroupSource(EHitSourceKind.PerHitAp, "Rending", 4)),
                Is.EqualTo("Rending"));
            Assert.That(ParryHitsStage<ICombatMetadata>.GroupName(new HitGroupSource(EHitSourceKind.PerHitAp, "", 4)),
                Is.EqualTo("AP"));
            Assert.That(ParryHitsStage<ICombatMetadata>.GroupName(HitGroupSource.Base), Is.EqualTo("base"));
            Assert.That(ParryHitsStage<ICombatMetadata>.GroupName(new HitGroupSource(EHitSourceKind.ExtraHits, "Furious", 1)),
                Is.EqualTo("base"), "Furious's extras save at the base threshold - they are not a worse group");
            Assert.That(ParryHitsStage<ICombatMetadata>.GroupName(new HitGroupSource(EHitSourceKind.BlastMultiplier, "Blast", 3)),
                Is.EqualTo("base"));
        }

        [Test]
        public void RemoveWorstHitsFirst_EditsTheGroupsInPlace_WorstFirst_ThenInVolleyOrder()
        {
            var groups = new List<SuccessfulHitInfo>
            {
                new SuccessfulHitInfo(Faces(0, 0, 0, 3, 0, 0), 0, HitGroupSource.Base),
                new SuccessfulHitInfo(Faces(0, 0, 0, 0, 0, 2), -4, new HitGroupSource(EHitSourceKind.PerHitAp, "Rending", 4)),
                new SuccessfulHitInfo(Faces(0, 0, 0, 0, 0, 2), 0, new HitGroupSource(EHitSourceKind.ExtraHits, "Furious", 2)),
            };

            IReadOnlyList<ParriedGroup> ignored = ParryHitsStage<ICombatMetadata>.RemoveWorstHitsFirst(groups, 4f);

            Assert.That(groups[1].HitCount, Is.EqualTo(0f), "Rending first");
            Assert.That(groups[0].HitCount, Is.EqualTo(1f), "then the rolled base group");
            Assert.That(groups[2].HitCount, Is.EqualTo(2f), "Furious's extras untouched: the 4 sixes were spent");
            Assert.That(ignored, Is.EqualTo(new[] { new ParriedGroup("Rending", 2f), new ParriedGroup("base", 2f) }));
            Assert.That(groups[1].Source.RuleName, Is.EqualTo("Rending"), "identity and source kept");
        }

        [Test]
        public void WithoutTopRolls_RemovesFromTheHighestFaceDown()
        {
            IDiceResults left = Faces(1, 0, 0, 2, 0, 2).WithoutTopRolls(3f);

            Assert.That(left.At(6), Is.EqualTo(0f));
            Assert.That(left.At(4), Is.EqualTo(1f));
            Assert.That(left.At(1), Is.EqualTo(1f));
            Assert.That(left.TotalRolls, Is.EqualTo(2f));
            Assert.That(Faces(0, 0, 0, 1, 0, 1).WithoutTopRolls(5f).TotalRolls, Is.EqualTo(0f), "over-removal floors at zero");
        }

        [Test]
        public void TheBudget_IsSetOnce_TakesTheMinimum_AndResetsOnARoleSwap()
        {
            var budget = new ParryBudget();
            Assert.That(budget.IsSet, Is.False);
            budget.Set(5);
            Assert.That(budget.Take(8f), Is.EqualTo(5f), "capped by the pool");
            Assert.That(budget.Take(3f), Is.EqualTo(0f), "spent");
            budget.Reset();
            budget.Set(2);
            Assert.That(budget.Take(1f), Is.EqualTo(1f), "capped by the hits");
            Assert.That(budget.Remaining, Is.EqualTo(1f));
            Assert.That(budget.Spent, Is.EqualTo(1f));
        }

        [Test]
        public void TheCatalogEntry_IsWeaponScoped_TakesOneArgument_AndCarriesThe36Sentence()
        {
            SpecialRuleDefinition parry = CoreRuleCatalog.Parry;
            Assert.That(parry.Scope, Is.EqualTo(ERuleScope.Weapon));
            Assert.That(parry.EngineArgumentCount, Is.EqualTo(1));
            Assert.That(parry.Valence, Is.EqualTo(EValence.Positive));
            HookEntry entry = parry.Passive.Single();
            Assert.That(entry.HookID, Is.EqualTo(EHookID.Melee_OnHitsTaken));
            Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Subject));
            Assert.That(entry.Effect, Is.EqualTo(new Effect.ParryHits(new ValueSource.Arg(0))));
            Assert.That(parry.Description, Is.EqualTo("When this unit takes hits in melee, roll X dice per model with " +
                "this rule in it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)."));
            Assert.That(CoreRuleCatalog.All.Any(r => r.Name == "Parry"), Is.True, "Parry must be in All.");
            Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Parry", out _), Is.True);
            Assert.That(RuleFireLint.Check(parry), Is.Empty);
        }

        [Test]
        public void TheSink_KeepsTheHighestXPerModel()
        {
            var a = new ModelID(Guid.NewGuid());
            var b = new ModelID(Guid.NewGuid());
            var sink = new ParrySink();
            sink.ApplyFrom(new RuleOperation[]
            {
                new RuleOperation.ParryDice(1, new[] { a, b }),
                new RuleOperation.ParryDice(3, new[] { a }),
            });

            Assert.That(sink.Carriers, Is.EqualTo(2));
            Assert.That(sink.TotalDice, Is.EqualTo(4), "a rolls 3 (the higher), b rolls 1");
            Assert.That(sink.UniformDicePerModel, Is.Null);
            Assert.That(ParryHitsStage<ICombatMetadata>.DescribePool(sink, "Parry"), Is.EqualTo("2 models, 4 Parry dice"));
        }

        // ---- harness -------------------------------------------------------------------------------------------------------

        // The melee hit chain up to (and, by default, through) the save roll, on one CombatMetadata the test keeps.
        private async Task<CombatMetadata> RunChain(ParryTestContext ctx, DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Weapon weapon, int weaponCount, ParryBudget? budget = null, bool runSaves = true)
        {
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount,
                isMelee: true, isCharging: true, parryBudget: budget);
            metadata.AddResult(new CoverCheckResults(0));
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new ParryHitsStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            if (runSaves)
            {
                await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
                await RunStage(new RollToSaveStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            }
            return metadata;
        }

        private static Task RunStage<TResult, TSelf>(CombatStage<TResult, TSelf, ICombatMetadata> stage, CombatMetadata metadata)
            where TSelf : CombatStage<TResult, TSelf, ICombatMetadata>
        {
            stage.NextStage.Bind("done");
            return stage.Enter(metadata);
        }

        // One attack: the charger's context with every model in range, as the melee stage sets it up before the swings.
        private static CombatActionContext Attack(ParryTestContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var context = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);
            context.SetInRangeAttackers(attacker.ModelBindings());
            context.SetInRangeDefenders(defender.ModelBindings());
            return context;
        }

        // The REAL SwingMeleeWeaponStage for one weapon profile of the attack.
        private static async Task RunSwing(ParryTestContext ctx, CombatActionContext attack, Weapon weapon)
        {
            attack.SetAttackWeapon(weapon, out _);
            var swing = new SwingMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            swing.FinishedSwinging.Bind("done");
            await swing.Enter(attack);
        }

        private static async Task RunImpact(ParryTestContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new ResolveImpactHitsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnImpactResolved.Bind("done");
            var context = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);
            await stage.Enter(context);
        }

        private static async Task RunRavage(ParryTestContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new ResolveRavageWoundsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnRavageResolved.Bind("done");
            var context = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);
            await stage.Enter(context);
        }

        private static async Task RunMelee(IGameContext ctx, DataBinding<UnitData> charger)
        {
            var melee = new MeleeStage(ctx, new NoOpLayer<IUnitActionContext>());
            melee.OnFinishedMelee.Bind("finishedMelee");
            melee.BackToChooseAction.Bind("backToChooseAction");
            await melee.Enter(new UnitActionContext(ctx, charger));
        }

        // The whole-melee table (MeleeResultRuleIntegrationTests' shape): the charger at x = 20 and the defenders in
        // a block in front of it, every one within the 3" strike range, on their own teams and armies.
        private (ParryTestContext Ctx, DataBinding<UnitData> Charger, DataBinding<UnitData> Defender) BuildCharge(
            ScriptedRoller roller, RecordingTextOutput log, int chargerModels, Weapon chargerWeapon,
            int defenderModels, Weapon defenderWeapon)
        {
            var ctx = new ParryTestContext(_store, roller, log, null, null, new MeleeRequester());

            var chargerPlayer = new PlayerID(Guid.NewGuid());
            var defenderPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { chargerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { defenderPlayer }));

            var chargerBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < chargerModels; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { chargerWeapon }, new Position(20f, 19f + i * 1.1f), _store);
                model.SetMaxWounds(3);
                chargerBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var chargerUnit = new UnitData(chargerPlayer, "Charger", quality: 4, defense: 4, modelBindings: chargerBindings);
            DataBinding<UnitData> charger = _store.GetDataBinding<UnitData>(_store.Create(chargerUnit));

            var defenderBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < defenderModels; i++)
            {
                float x = 21.5f + (i / 3) * 1.1f;
                float z = 18.9f + (i % 3) * 1.1f;
                var model = new ModelData(0.5f, new List<Weapon> { defenderWeapon }, new Position(x, z), _store);
                defenderBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var defenderUnit = new UnitData(defenderPlayer, "Defender", quality: 4, defense: 4, modelBindings: defenderBindings);
            DataBinding<UnitData> defender = _store.GetDataBinding<UnitData>(_store.Create(defenderUnit));

            _store.Create(new ArmyData(chargerPlayer, new List<DataBinding<UnitData>> { charger }));
            _store.Create(new ArmyData(defenderPlayer, new List<DataBinding<UnitData>> { defender }));
            return (ctx, charger, defender);
        }

        private ParryTestContext Context(IDiceRoller roller, IPresenter? presenter = null, RecordingTextOutput? log = null) =>
            new ParryTestContext(_store, roller, log ?? new RecordingTextOutput(), presenter, null);

        private DataBinding<UnitData> MakeUnit(string name, int models, Weapon weapon, int woundsPerModel = 1) =>
            MakeUnit(name, Enumerable.Range(0, models).Select(_ => new List<Weapon> { weapon }).ToArray(), woundsPerModel);

        // One model per entry, each carrying exactly the listed weapons, in a row along z = 0.
        private DataBinding<UnitData> MakeUnit(string name, IReadOnlyList<List<Weapon>> weaponsPerModel, int woundsPerModel = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < weaponsPerModel.Count; i++)
            {
                var model = new ModelData(0.5f, weaponsPerModel[i], new Position(name == "Charger" ? 0f : 1f, i * 1.2f), _store);
                if (woundsPerModel > 1) model.SetMaxWounds(woundsPerModel);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static Weapon Claws(int attacks, string name = "Claws") =>
            new Weapon(name, rangeInches: 0f, attacks: attacks, armorPenetration: 0);

        private static Weapon Blade() => new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0);

        private static Weapon HandWeapon(int x)
        {
            var weapon = new Weapon("Hand Weapon", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(ParryRule(x));
            return weapon;
        }

        private static Weapon ParryClaws(int attacks)
        {
            Weapon claws = Claws(attacks);
            claws.AttachRuleDefinition(ParryRule(1));
            return claws;
        }

        private static ResolvedRule ParryRule(int x) =>
            new ResolvedRule("Parry", CoreRuleCatalog.Parry, new RuleArgument[] { new RuleArgument.Int(x) });

        private static IDiceResults Faces(params float[] perFace) => new DiceResults(perFace, 1);

        private static List<DiceRolledBeat> ParryBeats(RecordingPresenter presenter) =>
            presenter.Beats.OfType<DiceRolledBeat>().Where(b => b.Label.StartsWith("Parry vs ")).ToList();

        private static List<DiceRolledBeat> SaveBeats(RecordingPresenter presenter, string weapon = "Claws") =>
            presenter.Beats.OfType<DiceRolledBeat>().Where(b => b.Label.StartsWith(weapon + ":")).ToList();

        // Every die of one Roll lands on the face (or exact histogram) queued for that roll; an empty queue falls back
        // to the default face. Records each roll's size. A Roll(0) - the save stage's empty hit groups - returns an
        // empty histogram without consuming a queued entry, so scripts can be written per MEANINGFUL roll.
        private sealed class ScriptedRoller : IDiceRoller
        {
            private readonly int _defaultFace;
            private readonly Queue<Func<float, float[]>> _script = new();
            public List<float> Counts { get; } = new();

            public ScriptedRoller(int defaultFace) => _defaultFace = defaultFace;

            public string Trace => string.Join(", ", Counts);

            public void QueueFace(int face) => _script.Enqueue(count => OnFace(face, count));

            public void QueueHistogram(params float[] perFace) => _script.Enqueue(_ => (float[])perFace.Clone());

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                if (rollCount <= 0f) return new DiceResults(new float[sideCount], 1);
                Counts.Add(rollCount);
                float[] faces = _script.Count > 0 ? _script.Dequeue()(rollCount) : OnFace(_defaultFace, rollCount);
                return new DiceResults(faces, 1);
            }

            private static float[] OnFace(int face, float count)
            {
                float[] faces = new float[6];
                faces[face - 1] = count;
                return faces;
            }
        }

        // Answers every prompt the melee poses: the first defender, the first weapon, always strike back, auto-filled
        // wound assignment, a consolidation that stays put.
        private sealed class MeleeRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case CancellableSelectionRequest<UnitData> defenderPick:
                        return Reply<TReply>(new Selected<DataBinding<UnitData>>(defenderPick.ValidOptions[0].Option));
                    case StringSelectionRequest weaponPick:
                        return Reply<TReply>(weaponPick.ValidOptions.First(o => !ChooseMeleeWeaponStage.IsHoldBackChoice(o)));
                    case YesNoRequest:
                        return Reply<TReply>(true);
                    case AssignWoundsRequest woundRequest:
                        var assigned = new AssignWoundsResults(woundRequest.UnitReceivingWounds, woundRequest.Packets);
                        assigned.AutoFill();
                        return Reply<TReply>(assigned);
                    case ConsolidationMoveRequest:
                        return Reply<TReply>(new List<ModelMoveEntry>());
                }
                throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }

            private static Task<TReply> Reply<TReply>(object reply) => Task.FromResult((TReply)reply);
        }

        // A game context with a recording log wired into BOTH the game log and the rule evaluator's narration, an
        // injectable presenter and a requester that auto-fills wound assignment (the swing chain asks for it).
        private sealed class ParryTestContext : IGameContext
        {
            public ITextOutput TextOutput { get; }
            public IDiceRoller DiceRoller { get; }
            public Random Rng { get; } = new Random(20261030);
            public RuleEvaluator RuleEvaluator { get; }
            public IPlayerRequestByID PlayerRequester { get; }
            public TableState TableState { get; }
            public IReadWriteableGameDataStore GameDataStore { get; }
            public IPresenter Presenter { get; }
            public GameSettings Settings { get; } = GameSettings.GetDefault();
            public List<ITeam>? FirstDeploymentRollOrder => null;
            IGameContext IGameContextAccessor.GameContext => this;

            public ParryTestContext(GameDataStore store, IDiceRoller dice, ITextOutput log, IPresenter? presenter,
                IRuleResolver? resolver, IPlayerRequestByID? requester = null)
            {
                GameDataStore = store;
                TableState = new TableState(store);
                DiceRoller = dice;
                TextOutput = log;
                RuleEvaluator = new RuleEvaluator(dice, log, resolver);
                Presenter = presenter ?? new LocalPresenter(null, new InstantPresentationClock());
                PlayerRequester = requester ?? new CapturingWoundRequester();
            }

            public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
            public void NotifyGameCompleted(GameResult result) { }
        }
    }
}
