using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #418 - Core Rules v3.6.0 Fatigue: "After a melee where they attacked, units hit on unmodified rolls of 6
    // in melee until the end of that round, and those hits don't count as rolls of 6 for special rules."
    // Shaken units "may strike back counting as Fatigued".
    //
    // The engine decides it once, on the hit context (HitRollCompleteContext.SixesCountForSpecialRules ->
    // the SpecialRuleHitRolls view, face 6 zeroed), and every on-a-6 trigger reads that view. So these
    // tests drive the REAL DetermineHitRollStage -> RollToHitStage pair (the threshold stage forces 6s, the
    // roll stage builds the context) and check each 6-trigger family stays silent for a fatigued melee
    // attack while its non-fatigued control still fires: Furious and Surge (extra hits), Rending and
    // Rending in melee (per-hit AP), the dice beat's proc chips, and a Shaken striker counting as Fatigued.
    // The negative space is pinned too: shooting is never Fatigued (Relentless fires), defender-side rolls
    // are untouched (Shred's 1s to block, Bane's re-rolled defense 6s), and only 6s are stripped
    // (Hazardous's natural 1s still self-wound).
    //
    // FixedFaceDiceRoller(6) makes every die a natural 6, so a fatigued unit still hits with all of them -
    // the only thing that changes is whether the rules fire.
    [TestFixture]
    public class FatigueSpecialSixesRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(20, 5); // > 9": Relentless's range gate holds

        private const int Attacks = 3;

        private GameDataStore _store = null!;
        private RecordingPresenter _presenter = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _presenter = new RecordingPresenter();
            _ctx = new TestGameContext(_store, new FixedFaceDiceRoller(6), presenter: _presenter);
        }

        // ---- Furious -------------------------------------------------------------------------------------

        [Test]
        public async Task Furious_FatiguedCharger_SixesHitButAddNoExtraHits()
        {
            Weapon blade = Blade();
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            (DetermineHitRollResults threshold, RollToHitResults hits) =
                await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: true);

            Assert.Multiple(() =>
            {
                Assert.That(threshold.HitRollNeeded, Is.EqualTo(6), "fatigued: 6s only");
                Assert.That(TotalHits(hits), Is.EqualTo(Attacks), "every natural 6 still hits");
                Assert.That(HitsOfKind(hits, EHitSourceKind.ExtraHits), Is.Zero,
                    "a fatigued charge's 6s don't count as 6s for Furious - no extra hits");
            });
        }

        // Control: the same charge without Fatigue gets one extra hit per natural 6.
        [Test]
        public async Task Furious_FreshCharger_AddsAnExtraHitPerSix()
        {
            Weapon blade = Blade();
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);

            (DetermineHitRollResults threshold, RollToHitResults hits) =
                await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: true);

            Assert.Multiple(() =>
            {
                Assert.That(threshold.HitRollNeeded, Is.EqualTo(4));
                Assert.That(HitsOfKind(hits, EHitSourceKind.ExtraHits), Is.EqualTo(Attacks));
                Assert.That(TotalHits(hits), Is.EqualTo(2 * Attacks));
            });
        }

        // ---- Surge ---------------------------------------------------------------------------------------

        [TestCase(false, 2 * Attacks, TestName = "Surge_FreshMelee_AddsAnExtraHitPerSix")]
        [TestCase(true, Attacks, TestName = "Surge_FatiguedMelee_AddsNoExtraHits")]
        public async Task Surge_InMelee(bool fatigued, int expectedHits)
        {
            Weapon blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            if (fatigued) FatigueUtilities.ApplyFatigued(attacker.GetValue());

            (_, RollToHitResults hits) =
                await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: false);

            Assert.That(TotalHits(hits), Is.EqualTo(expectedHits));
        }

        // ---- Rending (per-hit AP) ------------------------------------------------------------------------

        [TestCase("Rending")]
        [TestCase("Rending in melee")]
        public async Task Rending_FatiguedMelee_SixesGetNoApAndSaveAtBase(string ruleName)
        {
            Weapon blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule(ruleName, RendingDefinition(ruleName)));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos); // defense 4
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            (_, RollToHitResults hits) = await RunHitChain(attacker, defender, blade, isMelee: true, isCharging: true);

            Assert.Multiple(() =>
            {
                Assert.That(hits.SuccessfulHitList, Has.Count.EqualTo(1), "one base group, nothing peeled");
                Assert.That(hits.SuccessfulHitList[0].Source.Kind, Is.EqualTo(EHitSourceKind.BaseRolled));
                Assert.That(hits.SuccessfulHitList[0].SaveModifier, Is.Zero, "no AP(+4) on a fatigued 6");
                Assert.That(TotalHits(hits), Is.EqualTo(Attacks));
            });

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, blade, hits);
            Assert.That(saves.PendingSaveRollsList.Select(p => p.SaveNeeded), Is.All.EqualTo(4),
                "every hit saves at the defender's base 4+");
        }

        // Control: the same melee swing without Fatigue promotes every natural 6 to AP(+4) (save 8, pre-clamp).
        [TestCase("Rending")]
        [TestCase("Rending in melee")]
        public async Task Rending_FreshMelee_SixesGetApPlusFour(string ruleName)
        {
            Weapon blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule(ruleName, RendingDefinition(ruleName)));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);

            (_, RollToHitResults hits) = await RunHitChain(attacker, defender, blade, isMelee: true, isCharging: true);

            Assert.That(hits.SuccessfulHitList.Single().Source.Kind, Is.EqualTo(EHitSourceKind.PerHitAp));
            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, blade, hits);
            Assert.That(saves.PendingSaveRollsList.Select(p => p.SaveNeeded), Is.All.EqualTo(8));
        }

        // The view is read by the EFFECTS and the per-hit AP split too, not only by the UnmodifiedRollEquals
        // condition: a book rule authored with no face condition (Always -> AddExtraHit(6) /
        // PerHitSaveModifier(6)) still adds nothing for a fatigued melee 6, and a fresh swing still gets both.
        [TestCase(false, 2 * Attacks, EHitSourceKind.PerHitAp, TestName = "UnguardedSixRule_FreshMelee_Fires")]
        [TestCase(true, Attacks, EHitSourceKind.BaseRolled, TestName = "UnguardedSixRule_FatiguedMelee_AddsNothing")]
        public async Task UnguardedSixRule_ReadsTheViewToo(bool fatigued, int expectedHits, EHitSourceKind rolledKind)
        {
            Weapon blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Unguarded Sixes", UnguardedSixes));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            if (fatigued) FatigueUtilities.ApplyFatigued(attacker.GetValue());

            (_, RollToHitResults hits) =
                await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: false);

            Assert.Multiple(() =>
            {
                Assert.That(TotalHits(hits), Is.EqualTo(expectedHits));
                Assert.That(hits.SuccessfulHitList.First().Source.Kind, Is.EqualTo(rolledKind));
            });
        }

        // ---- Shaken strikes back counting as Fatigued ----------------------------------------------------

        [Test]
        public async Task ShakenStriker_CountsAsFatigued_NoSurgeNoRending()
        {
            Weapon blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));
            blade.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            DataBinding<UnitData> striker = MakeUnit(AttackerPos, blade);
            MoraleUtilities.ApplyShaken(striker);
            Assert.That(striker.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "precondition: Shaken only, no Fatigued token");

            // A strike-back: melee, not charging.
            (DetermineHitRollResults threshold, RollToHitResults hits) =
                await RunHitChain(striker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: false);

            Assert.Multiple(() =>
            {
                Assert.That(threshold.HitRollNeeded, Is.EqualTo(6), "Shaken counts as Fatigued: 6s only");
                Assert.That(TotalHits(hits), Is.EqualTo(Attacks), "no Surge extra hits");
                Assert.That(hits.SuccessfulHitList.Select(h => h.Source.Kind),
                    Is.All.EqualTo(EHitSourceKind.BaseRolled), "no Rending AP group, no extra-hit group");
            });
        }

        // ---- Shooting is never Fatigued ------------------------------------------------------------------

        [Test]
        public async Task FatiguedUnitShooting_RelentlessStillFires()
        {
            Weapon rifle = Rifle();
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, rifle);
            AttachUnitRule(attacker, "Relentless", CoreRuleCatalog.Relentless);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            (DetermineHitRollResults threshold, RollToHitResults hits) =
                await RunHitChain(attacker, MakeUnit(DefenderPos), rifle, isMelee: false, isCharging: false);

            Assert.Multiple(() =>
            {
                Assert.That(threshold.HitRollNeeded, Is.EqualTo(4), "Fatigue is melee-only");
                Assert.That(HitsOfKind(hits, EHitSourceKind.ExtraHits), Is.EqualTo(Attacks),
                    "beyond 9\" every natural 6 still counts as a 6 for Relentless");
            });
        }

        // ---- Dice beat: no proc chips for a fatigued 6 ---------------------------------------------------

        [Test]
        public async Task ToHitBeat_FatiguedMelee_CarriesNoProcChips_ButShowsTheFatigueChip()
        {
            Weapon blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: true);

            DiceRolledBeat toHit = ToHitBeat();
            Assert.Multiple(() =>
            {
                Assert.That(toHit.ProcTags, Is.Null,
                    "no 'Furious +N on 6s' / 'Rending AP+4 on 6s' chip, so the panel rings no 6");
                Assert.That(toHit.ModifierTags, Does.Contain("Fatigued: 6s only"));
            });
        }

        [Test]
        public async Task ToHitBeat_FreshMelee_NamesTheSixTriggers()
        {
            Weapon blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);

            await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: true);

            Assert.That(ToHitBeat().ProcTags, Is.EquivalentTo(new[] { "Furious +3 on 6s", "Rending AP+4 on 6s" }));
        }

        // ---- Histogram mode (ProbabilisticDiceRoller) ----------------------------------------------------

        // Six attack dice read as exactly one of each face. Fresh charge: 3 hits on 4+ and Furious's 1 extra
        // for the 6 = 4. Fatigued charge: only the 6 hits (1), and it adds nothing.
        [TestCase(false, 4f)]
        [TestCase(true, 1f)]
        public async Task Furious_ProbabilisticRoller_FatigueRemovesTheExtraHit(bool fatigued, float expectedHits)
        {
            _ctx = new TestGameContext(_store, new ProbabilisticDiceRoller(), presenter: _presenter);
            Weapon blade = Blade(attacks: 6);
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);
            if (fatigued) FatigueUtilities.ApplyFatigued(attacker.GetValue());

            (_, RollToHitResults hits) =
                await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: true);

            Assert.That(TotalHits(hits), Is.EqualTo(expectedHits).Within(0.0001f));
            Assert.That(HitsOfKind(hits, EHitSourceKind.ExtraHits),
                Is.EqualTo(fatigued ? 0f : 1f).Within(0.0001f));
        }

        // Only 6s are stripped: a fatigued melee swing with a Hazardous + Surge weapon still self-wounds on its
        // natural 1 (one of six dice) while its natural 6 adds no Surge hit.
        [Test]
        public async Task FatiguedMelee_OnlySixesAreStripped_HazardousOnesStillSelfWound()
        {
            _ctx = new TestGameContext(_store, new ProbabilisticDiceRoller(), presenter: _presenter);
            Weapon blade = Blade(attacks: 6);
            blade.AttachRuleDefinition(new ResolvedRule("Hazardous", HazardousSelfWound));
            blade.AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());

            (_, RollToHitResults hits) =
                await RunHitChain(attacker, MakeUnit(DefenderPos), blade, isMelee: true, isCharging: false);

            Assert.Multiple(() =>
            {
                Assert.That(hits.SelfWounds, Is.EqualTo(1f).Within(0.0001f), "the natural 1 still counts as a 1");
                Assert.That(TotalHits(hits), Is.EqualTo(1f).Within(0.0001f), "the 6 hits, Surge adds nothing");
            });
        }

        // ---- Defender-side rolls are untouched -----------------------------------------------------------

        // The whole volley through the real save and wound stages, in histogram mode. A fatigued charge with
        // six attack dice lands one hit (the 6); its save fails on 1-3 (0.5 wounds). Shred adds a wound per
        // unmodified 1 to block (+1/6) and Bane re-rolls the saved 6s (1/6 of a die, half of which then fail:
        // +1/12). Both read the DEFENDER's dice, so Fatigue - which is about the attacker's hits - leaves
        // them exactly as they would be for any other hit.
        [TestCase("", 0.5f, TestName = "DefenderSide_Fatigued_NoRuleControl")]
        [TestCase("Shred", 0.5f + 1f / 6f, TestName = "DefenderSide_Fatigued_ShredStillAddsWounds")]
        [TestCase("Bane", 0.5f + 1f / 12f, TestName = "DefenderSide_Fatigued_BaneStillRerollsSavedSixes")]
        public async Task DefenderSideRolls_AreUnaffectedByFatigue(string ruleName, float expectedWounds)
        {
            var woundCtx = new WoundTestContext(_store, new CapturingWoundRequester(), new ProbabilisticDiceRoller());
            Weapon blade = Blade(attacks: 6);
            if (ruleName == "Shred") blade.AttachRuleDefinition(new ResolvedRule("Shred", CoreRuleCatalog.Shred));
            if (ruleName == "Bane") blade.AttachRuleDefinition(new ResolvedRule("Bane", CoreRuleCatalog.Bane));
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos, blade);
            FatigueUtilities.ApplyFatigued(attacker.GetValue());
            DataBinding<UnitData> defender = MakeSquad(DefenderPos, models: 5);

            var metadata = new CombatMetadata(woundCtx, attacker, defender, blade, weaponCount: 1,
                attackerMoved: false, isMelee: true, isCharging: true);
            metadata.AddResult(new CoverCheckResults(0));
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(woundCtx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(woundCtx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(woundCtx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToSaveStage<ICombatMetadata>(woundCtx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new AssignWoundsStage<ICombatMetadata>(woundCtx, new NoOpLayer<ICombatMetadata>()), metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults hits), Is.True);
            Assert.That(TotalHits(hits), Is.EqualTo(1f).Within(0.0001f), "fatigued: only the natural 6 hits");
            Assert.That(metadata.QueryForResult(out AssignWoundsResults wounds), Is.True);
            Assert.That(wounds.TotalAssignedWounds, Is.EqualTo(expectedWounds).Within(0.0001f));
        }

        // ---- The context view itself ---------------------------------------------------------------------

        [Test]
        public void SpecialRuleView_ZeroesOnlyFaceSix_AndOnlyWhenSixesDontCount()
        {
            UnitData unit = MakeUnit(AttackerPos).GetValue();
            IDiceResults rolled = TestDice.Faces(1, 4, 6, 6);

            var counts = new HitRollCompleteContext(unit, unit, rolled, IsMelee: true);
            var fatigued = counts with { SixesCountForSpecialRules = false };

            Assert.Multiple(() =>
            {
                Assert.That(counts.SpecialRuleHitRolls.At(6), Is.EqualTo(2f));
                Assert.That(fatigued.SpecialRuleHitRolls.At(6), Is.Zero);
                Assert.That(fatigued.SpecialRuleHitRolls.At(1), Is.EqualTo(1f));
                Assert.That(fatigued.SpecialRuleHitRolls.At(4), Is.EqualTo(1f));
                Assert.That(fatigued.UnmodifiedHitRolls.At(6), Is.EqualTo(2f), "the raw roll is untouched");

                var invocation = new RuleInvocation(fatigued, unit, System.Array.Empty<RuleArgument>());
                Assert.That(new Condition.UnmodifiedRollEquals(6).Evaluate(invocation), Is.False);
                Assert.That(new Condition.UnmodifiedRollEquals(1).Evaluate(invocation), Is.True);
            });
        }

        // The split's own guard, independent of whether the per-hit op was emitted: a face the special-rule
        // view zeroes is never peeled, and a null view (every face counts) peels as before.
        [Test]
        public void PerHitApSplitter_NeverPeelsAFaceTheViewZeroes()
        {
            IDiceResults successful = TestDice.Faces(4, 6, 6);
            var ops = new List<RuleOperation> { new RuleOperation.ApplyPerHitSaveModifier(6, -4) };
            IDiceResults fatiguedView = successful.WithoutFaces(new[] { 6 });

            List<SuccessfulHitInfo> fresh = PerHitApSplitter.Split(successful, ops);
            List<SuccessfulHitInfo> fatigued = PerHitApSplitter.Split(successful, ops, fatiguedView);

            Assert.Multiple(() =>
            {
                Assert.That(fresh.Single(g => g.Source.Kind == EHitSourceKind.PerHitAp).HitCount, Is.EqualTo(2f));
                Assert.That(fatigued, Has.Count.EqualTo(1));
                Assert.That(fatigued[0].SaveModifier, Is.Zero);
                Assert.That(fatigued[0].HitCount, Is.EqualTo(3f), "all three hits stay in the base group");
            });
        }

        [Test]
        public void SixesCountForSpecialRules_FalseOnlyForAFatiguedOrShakenMeleeAttack()
        {
            DataBinding<UnitData> fresh = MakeUnit(AttackerPos);
            DataBinding<UnitData> tired = MakeUnit(AttackerPos);
            FatigueUtilities.ApplyFatigued(tired.GetValue());
            DataBinding<UnitData> shaken = MakeUnit(AttackerPos);
            MoraleUtilities.ApplyShaken(shaken);

            Assert.Multiple(() =>
            {
                Assert.That(FatigueUtilities.SixesCountForSpecialRules(fresh.GetValue(), isMelee: true), Is.True);
                Assert.That(FatigueUtilities.SixesCountForSpecialRules(tired.GetValue(), isMelee: true), Is.False);
                Assert.That(FatigueUtilities.SixesCountForSpecialRules(shaken.GetValue(), isMelee: true), Is.False);
                Assert.That(FatigueUtilities.SixesCountForSpecialRules(tired.GetValue(), isMelee: false), Is.True,
                    "shooting is never Fatigued");
            });
        }

        // ---- Helpers -------------------------------------------------------------------------------------

        // The shipped Hazardous self-wound facet (see HazardousRuleIntegrationTests for the full rule).
        private static readonly SpecialRuleDefinition HazardousSelfWound = new("Hazardous",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.UnmodifiedRollEquals(1),
                    new Effect.SelfWoundOnUnmodifiedRoll(OnRollValue: 1),
                    ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(),
            Scope: ERuleScope.Weapon);

        private static readonly SpecialRuleDefinition UnguardedSixes = new("Unguarded Sixes",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.Always(),
                    new Effect.AddExtraHit(OnRollValue: 6), ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.Always(),
                    new Effect.PerHitSaveModifier(OnRollValue: 6, Delta: -4), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(),
            Scope: ERuleScope.Weapon);

        private static SpecialRuleDefinition RendingDefinition(string ruleName) =>
            ruleName == "Rending" ? CoreRuleCatalog.Rending : CoreRuleCatalog.RendingInMelee;

        private async Task<(DetermineHitRollResults, RollToHitResults)> RunHitChain(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, Weapon weapon,
            bool isMelee, bool isCharging)
        {
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: isCharging);

            var determine = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            determine.NextStage.Bind("done");
            await determine.Enter(metadata);

            var roll = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            roll.NextStage.Bind("done");
            await roll.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults threshold), Is.True);
            Assert.That(metadata.QueryForResult(out RollToHitResults hits), Is.True);
            return (threshold, hits);
        }

        private async Task<DetermineSaveRollNeededResults> RunSaveNeededStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Weapon weapon, RollToHitResults hits)
        {
            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: true, isCharging: true);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result;
        }

        private static async Task RunStage<TResult, TSelf>(
            CombatStage<TResult, TSelf, ICombatMetadata> stage, CombatMetadata metadata)
            where TSelf : CombatStage<TResult, TSelf, ICombatMetadata>
        {
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);
        }

        private DiceRolledBeat ToHitBeat() =>
            _presenter.Beats.OfType<DiceRolledBeat>().Single(b => b.Label.StartsWith("Roll to Hit"));

        private static float TotalHits(RollToHitResults results) =>
            results.SuccessfulHitList.Sum(hit => hit.HitCount);

        private static float HitsOfKind(RollToHitResults results, EHitSourceKind kind) =>
            results.SuccessfulHitList.Where(hit => hit.Source.Kind == kind).Sum(hit => hit.HitCount);

        private static void AttachUnitRule(DataBinding<UnitData> unit, string name, SpecialRuleDefinition definition) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, definition));

        private static Weapon Blade(int attacks = Attacks) =>
            new Weapon("Blade", rangeInches: 0f, attacks: attacks, armorPenetration: 0);

        private static Weapon Rifle() =>
            new Weapon("Rifle", rangeInches: 48f, attacks: Attacks, armorPenetration: 0);

        private DataBinding<UnitData> MakeSquad(Position position, int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(position.x, position.z + i * 1.2f), gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Squad", quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private DataBinding<UnitData> MakeUnit(Position position, Weapon? weapon = null)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: weapon == null ? new List<Weapon>() : new List<Weapon> { weapon },
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
