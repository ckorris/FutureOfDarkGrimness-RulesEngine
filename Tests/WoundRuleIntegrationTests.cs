using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Deadly's wound multiplier flows through the
    // REAL AssignWoundsStage. The stage fires the Shooting_OnPreApplyWound "when", the RuleEvaluator
    // queues MultiplyWounds, and the WoundModifierSink folds the net multiplier into the wound count
    // the stage hands to the player — none of it interpreted by the stage. The defender has 5 wounds
    // so the multiplied count stays sub-lethal and lands in the "ask the player how to assign" branch,
    // where a capturing requester records the requested wound count (the cleanest observable).
    [TestFixture]
    public class WoundRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester);
        }

        [Test]
        public async Task NoRules_WoundCountUnmultiplied()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(1f),
                "one failed save → one wound to assign, with no rule to multiply it.");
        }

        // Deadly's no-carry-over confinement: against single-wound models the multiplier is WASTED — each
        // failed save is a clump of X that lands on one model, but a 1-wound model absorbs only 1, the rest
        // lost (Deadly's whole point is anti-Tough). One failed save → one dead model → one wound to assign.
        [Test]
        public async Task DeadlyAttacker_VsSingleWoundModels_MultiplierWasted()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(Landed(metadata), Is.EqualTo(1f),
                "Deadly(3) against 1-wound models is wasted: the clump kills one model, the extra 2 don't carry over.");
        }

        // Against a Tough model the clump deals the full multiplier to that one model: Deadly(3) + one
        // failed save → 3 wounds on a single Tough(5) model (which survives, so it routes to the player).
        [Test]
        public async Task DeadlyAttacker_VsToughModels_ClumpDealsMultiplierToOneModel()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2, woundsPerModel: 5);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(Landed(metadata), Is.EqualTo(3f),
                "Deadly(3) lands a full 3-wound clump on one Tough(5) model (vs only 1 on a single-wound model).");
        }

        // Overkill within a clump is NOT carried to the next model: Deadly(3) + two failed saves against
        // Tough(5) models spends both clumps on the first model (ceil(5/3) = 2 clumps to kill), dealing 5 —
        // the 6th wound (overkill) is lost rather than spilling onto the second model. (Naive total×X = 6.)
        [Test]
        public async Task DeadlyAttacker_OverkillNotCarriedToNextModel()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2, woundsPerModel: 5);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(Landed(metadata), Is.EqualTo(5f),
                "two clumps kill the first Tough(5) model (5 wounds); the overkill doesn't carry to the second.");
        }

        // #400: the clump confinement must walk the order wounds are ACTUALLY assigned — already-wounded
        // models first (#023's mandatory pre-assignment) — not the raw model list. Three Tough(3) models
        // with one already down to 1 remaining wound, hit by Deadly(3) x1: the single clump is forced onto
        // the wounded model, where only 1 of its 3 wounds fits and the other 2 are LOST. Before the fix the
        // confinement priced the clump against whichever model came first in the list, so it returned 3 and
        // the pre-assignment spent 1 on the wounded model and spilled the other 2 onto a fresh one — the
        // carry-over Deadly explicitly forbids. Parameterized over the wounded model's index because the
        // old answer depended on it (index 0 was right by accident, index 2 was wrong).
        [TestCase(0)]
        [TestCase(2)]
        public async Task DeadlyAttacker_ClumpOnAlreadyWoundedModel_OverkillDoesNotSpill(int woundedIndex)
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3, woundsPerModel: 3);
            defender.GetValue().ModelBindings[woundedIndex].GetValue().DealWounds(2); // 1 wound left

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 1);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(result.TotalAssignedWounds, Is.EqualTo(1f),
                "the clump is confined to the already-wounded model, which absorbs 1 of its 3 wounds.");
            AssertPlacement(result, defender, woundedIndex, expected: 1f);
        }

        // #400, two clumps: the first is forced onto the already-wounded model (1 of 3 lands), the second
        // kills a fresh Tough(3) outright — 4 wounds, and the third model is untouched. Before the fix this
        // returned 6 and left the third model on 1 remaining.
        [TestCase(0)]
        [TestCase(2)]
        public async Task DeadlyAttacker_TwoClumps_SecondDoesNotReachTheThirdModel(int woundedIndex)
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3, woundsPerModel: 3);
            defender.GetValue().ModelBindings[woundedIndex].GetValue().DealWounds(2);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(result.TotalAssignedWounds, Is.EqualTo(4f),
                "clump 1 lands 1 on the wounded model; clump 2 kills one fresh model. 1 + 3 = 4.");
            AssertPlacement(result, defender, woundedIndex, expected: 1f);
            // Exactly one fresh model takes the second clump in full; the other takes nothing.
            List<float> freshWounds = PlacedWounds(result, defender)
                .Where((_, index) => index != woundedIndex).ToList();
            Assert.That(freshWounds, Is.EquivalentTo(new[] { 3f, 0f }),
                "the second clump kills one fresh model outright and does not touch the third.");
        }

        // Control for the pair above: with the squad UNDAMAGED the two orders coincide, so the pre-#400
        // behaviour was already correct here — 2 clumps kill 2 of the 3 Tough(3) models.
        [Test]
        public async Task DeadlyAttacker_FreshSquad_TwoClumpsKillTwoModels()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3, woundsPerModel: 3);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(Landed(metadata), Is.EqualTo(6f),
                "fresh Tough(3) models: each clump kills one outright, nothing is wasted.");
        }

        // What actually landed on the unit - the results object's tally, not the request's headline
        // (under Deadly the queue's carry is an upper bound; a clump's excess is lost at its model).
        private static float Landed(CombatMetadata metadata)
        {
            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            return result.TotalAssignedWounds;
        }

        // The wounds each of the defender's models ended up carrying, in unit-list order.
        private static List<float> PlacedWounds(AssignWoundsResults result, DataBinding<UnitData> defender) =>
            defender.GetValue().ModelBindings
                .Select(binding => result.PendingWounds
                    .Where(pending => pending.Model == binding)
                    .Sum(pending => pending.Wounds))
                .ToList();

        private static void AssertPlacement(AssignWoundsResults result, DataBinding<UnitData> defender,
            int modelIndex, float expected)
        {
            Assert.That(PlacedWounds(result, defender)[modelIndex], Is.EqualTo(expected),
                $"model {modelIndex} should carry {expected} wound(s) from this attack.");
        }

        // ── The OPR Discord thread of 2026-07-22, where the rule's author answered it directly ────────
        // Justin: "I get hit with 5 Deadly attacks and don't save any" against three troops — are the
        // extras spread around, or lost? Adam (OPR): "excess wounds are lost"; "if all the models in the
        // unit are killed then yeah there would be nothing for the last 2 hits to do." Three 1-wound
        // models, Deadly(3) x5: three clumps kill the unit, two are wasted entirely.
        [Test]
        public async Task Deadly_MoreClumpsThanModels_WipesUnitAndWastesTheRest()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 5);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(result.TotalAssignedWounds, Is.EqualTo(3f),
                "three 1-wound models absorb one wound each; the other two clumps have nothing to kill.");
        }

        // Yorekani, same thread: "If your example unit has 2 models left, then the unit gets wiped if 2 or
        // more Deadly(3) wounds get through, unless one of those models is an attached hero with Tough(6),
        // then you'd need 3 or more wounds with Deadly(3) to wipe the floor." The hero is assigned wounds
        // LAST (#006), so the grunt eats clump 1 and the Tough(6) hero needs ceil(6/3) = 2 more.
        [Test]
        public async Task Deadly_ToughSixHero_TwoClumpsLeaveTheHeroStanding()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeHeroJoinedUnit(gruntCount: 1, heroTough: 6);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(result.TotalAssignedWounds, Is.EqualTo(4f),
                "clump 1 kills the 1-wound grunt; clump 2 puts 3 on the Tough(6) hero, which survives.");
            Assert.That(defender.GetValue().Models.Last().GetIsAlive(), Is.True, "the hero is still up.");
        }

        [Test]
        public async Task Deadly_ToughSixHero_ThreeClumpsWipeTheUnit()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeHeroJoinedUnit(gruntCount: 1, heroTough: 6);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 3);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(result.TotalAssignedWounds, Is.EqualTo(7f),
                "1 (grunt) + 6 (hero, two clumps) = the unit's whole wound pool.");
        }

        // ── #401: Deadly + Regeneration resolve per clump ─────────────────────────────────────────
        // Filed under #400 as [Ignore]d "desired behaviour" tests and un-ignored by #401. From the OPR
        // Discord thread of 2026-07-22 — Yorekani: "IF a unit of multiple models with Tough(3) fails to
        // block hits with Deadly(X) and the models have Regeneration or similar, you're out of luck and
        // you'll have to slow-roll the Regeneration saves. That's because you can't accurately prevent
        // spillover otherwise." Adam (OPR): "just do them one at a time and it'll work out fine."
        //
        // The stage now carries the wounds as a packet queue (a Deadly clump is a confined packet of X),
        // rolls Regeneration per packet BEFORE any capacity cap, and AssignWoundsResults commits each
        // packet to one model with the excess lost - so both hold by construction.

        // Regeneration rolls once per wound the clump DEALS, not once per wound that fits. A Deadly(3)
        // clump on a 1-wound model is three wounds arriving at that model, so it gets three chances to
        // shrug — it survives only if it ignores all three. Before #401 the capacity cap was applied first,
        // so a single die was rolled and the model shrugged off the whole clump a third of the time.
        [Test]
        public async Task Deadly_Regeneration_RollsOncePerMultipliedWound()
        {
            var presenter = new RecordingPresenter();
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller(), presenter);
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachRegeneration(defender);

            await RunStage(attacker, defender, failedSaves: 1);

            DiceRolledBeat regenBeat = presenter.Beats.OfType<DiceRolledBeat>()
                .Single(beat => beat.Label == "Regeneration");
            Assert.That(regenBeat.FaceCounts.Sum(), Is.EqualTo(3f).Within(0.001f),
                "the clump deals 3 wounds to the model, so Regeneration rolls 3 dice - the capacity cap " +
                "applies to what survives them, not to what is rolled for.");
        }

        // Wounds a clump loses to Regeneration are lost to THAT clump, on THAT model - they must not free
        // up budget that reaches the next model. Two Tough(3) models with Regeneration (ignore 5+, so the
        // probabilistic roller shrugs exactly 1 of every 3), Deadly(3) x2: clump 1 puts 2 on model A;
        // clump 2 must finish A (#024) and only 1 of its surviving 2 fits, the other lost. Model B is
        // never reached. Before #401 the pool was 6, Regeneration took 2 off it, and the leftover 4th wound
        // spilled onto B - the exact spillover the thread says you must slow-roll to avoid.
        [Test]
        public async Task Deadly_Regeneration_IgnoredWoundsDoNotSpillToTheNextModel()
        {
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachDeadly(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2, woundsPerModel: 3);
            AttachRegeneration(defender);

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(PlacedWounds(result, defender)[1], Is.EqualTo(0f).Within(0.001f),
                "the second model is never assigned a clump, so Regeneration's leftovers cannot reach it.");
            Assert.That(result.TotalAssignedWounds, Is.EqualTo(3f).Within(0.001f),
                "clump 1 lands 2 on the first model; clump 2 lands only the 1 that still fits.");
        }

        // #100 Shred: each unmodified 1 to block adds a wound. The harness rolls every failed save as an
        // unmodified 1, so two failed saves → +2 Shred wounds on top of the 2 they already dealt = 4.
        // The 5-model defender survives, routing to the player branch where the count is captured.
        [Test]
        public async Task ShredAttacker_AddsAWoundPerUnmodifiedBlockOf1()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachShred(attacker);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(4f),
                "2 failed saves (each an unmodified 1 to block) → +2 Shred wounds → 4 total.");
        }

        // Control: no Shred → the two failed saves stay two wounds.
        [Test]
        public async Task NoShred_WoundCountUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f));
        }

        // #093: "Shred when shooting" adds its wounds on a shooting attack (the Not(IsMelee) gate holds) —
        // 2 unmodified-1 blocks → +2 → 4, like Shred.
        [Test]
        public async Task ShredWhenShootingAttacker_Shooting_AddsWounds()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            attacker.GetValue().AttachRuleDefinition(
                new ResolvedRule("Shred when shooting", CoreRuleCatalog.ShredWhenShooting));
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2); // shooting

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(4f),
                "shooting: the gate holds, Shred adds +2 → 4 total.");
        }

        // In melee the gate fails, so no extra wounds are injected — the two failed saves stay two wounds.
        [Test]
        public async Task ShredWhenShootingAttacker_Melee_NoExtraWounds()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            attacker.GetValue().AttachRuleDefinition(
                new ResolvedRule("Shred when shooting", CoreRuleCatalog.ShredWhenShooting));
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(attacker, defender, failedSaves: 2, isMelee: true);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f),
                "melee: the gate fails, no Shred wounds are added.");
        }

        // Regression for the single-living-model auto-resolve branch: a lone multi-wound model (Tough) taking
        // a sub-lethal hit must be assigned the wounds actually DEALT, not its full remaining health. The
        // branch used to assign defenderRemainingWounds, instantly killing any Tough monster that took even
        // one wound (a user shot a Tough(12) Carnivo-Rex for 3 wounds and "applying 12 wounds" killed it).
        [Test]
        public async Task SingleToughModel_SubLethalHit_AssignsOnlyWoundsDealt()
        {
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 1, woundsPerModel: 12); // Tough(12), lone model

            CombatMetadata metadata = await RunStage(attacker, defender, failedSaves: 3);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            Assert.That(result.TotalAssignedWounds, Is.EqualTo(3f),
                "3 failed saves → 3 wounds assigned, not the model's full 12 remaining.");
            Assert.That(result.PendingWounds, Has.Count.EqualTo(1));
            Assert.That(result.PendingWounds[0].Wounds, Is.EqualTo(3f));
            Assert.That(result.PendingWounds[0].Wounds,
                Is.LessThan(((ModelData)defender.GetValue().Models[0]).TotalWounds),
                "the lone Tough model survives a sub-lethal hit.");
        }

        // #183 finding-2 — Resistance's two thresholds through the REAL stage: the base 6+ on weapon
        // wounds, and the spell facet's 2+ ("if the wounds were from a spell, they are ignored on a 2+
        // instead") when the metadata is flagged IsSpell. ProbabilisticDiceRoller makes the per-wound
        // ignore roll deterministic and fractional: 2 wounds keep 2*(5/6) on a 6+ but only 2*(1/6) on a 2+.
        [Test]
        public async Task ResistanceDefender_WeaponWounds_IgnoredOnSixPlus()
        {
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachResistance(defender);

            await RunStage(attacker, defender, failedSaves: 2);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f - 2f / 6f).Within(0.001f),
                "weapon wounds: each ignored only on a 6+, so 1/6 of the 2 wounds are dropped.");
        }

        [Test]
        public async Task ResistanceDefender_SpellWounds_IgnoredOnTwoPlus()
        {
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachResistance(defender);

            await RunStage(attacker, defender, failedSaves: 2, isSpell: true);

            Assert.That(_requester.Captured!.TotalWoundsToAssign, Is.EqualTo(2f / 6f).Within(0.001f),
                "spell wounds: the IsSpell-gated 2+ entry wins the sink's best-threshold fold, " +
                "so 5/6 of the 2 wounds are dropped.");
        }

        private async Task<CombatMetadata> RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            int failedSaves, bool isMelee = false, bool isSpell = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isSpell: isSpell);

            // One FailedSaveInfo per wound (SaveCount == its dice TotalRolls == 1).
            var failedList = new List<FailedSaveInfo>();
            for (int i = 0; i < failedSaves; i++)
            {
                failedList.Add(new FailedSaveInfo(TestDice.Faces(1), new PendingSaveRolls(TestDice.Faces(1), 4)));
            }
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failedList));

            await stage.Enter(metadata);
            return metadata;
        }

        private static void AttachDeadly(DataBinding<UnitData> unit, int x)
        {
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule("Deadly", CoreRuleCatalog.Deadly, new RuleArgument[] { new RuleArgument.Int(x) }));
        }

        private static void AttachShred(DataBinding<UnitData> unit)
        {
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Shred", CoreRuleCatalog.Shred));
        }

        private static void AttachRegeneration(DataBinding<UnitData> unit)
        {
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Regeneration", CoreRuleCatalog.Regeneration));
        }

        // A unit of 1-wound grunts with a Tough(heroTough) Hero joined in (#006). AttachHero APPENDS the
        // hero's binding, so the hero is last in Models - which is also where the wound-allocation order
        // puts it, deliberately, rather than by relying on that.
        private DataBinding<UnitData> MakeHeroJoinedUnit(int gruntCount, int heroTough)
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: gruntCount);
            var heroModel = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: _store);
            heroModel.SetMaxWounds(heroTough);
            DataBinding<ModelData> hero = _store.GetDataBinding<ModelData>(_store.Create(heroModel));
            unit.GetValue().AttachHero(
                new HeroAttachment(hero.GetValue().ID, quality: 3, defense: 3, heroWounds: heroTough),
                new List<DataBinding<ModelData>> { hero });
            return unit;
        }

        private static void AttachResistance(DataBinding<UnitData> unit)
        {
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Resistance", CoreRuleCatalog.Resistance));
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                if (woundsPerModel != 1) model.SetMaxWounds(woundsPerModel); // Tough(woundsPerModel)
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }

    // Captures the AssignWoundsRequest the stage emits and auto-resolves it so the stage completes.
    // (TestGameContext's NullPlayerRequester never completes, so the "ask the player" branch needs
    // a real reply to be testable.)
    internal sealed class CapturingWoundRequester : IPlayerRequestByID
    {
        public AssignWoundsRequest? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is AssignWoundsRequest woundRequest)
            {
                Captured = woundRequest;
                var result = new AssignWoundsResults(woundRequest.UnitReceivingWounds, woundRequest.Packets);
                result.AutoFill();
                return Task.FromResult((TReply)(object)result);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Minimal IGameContext with a real RuleEvaluator and an injectable requester.
    internal sealed class WoundTestContext : IGameContext
    {
        public ITextOutput TextOutput { get; } = new EmptyTextOutput();
        public IDiceRoller DiceRoller { get; }
        // #193: tests get a fixed-seed stream so any Rng-driven stage behaves reproducibly.
        public Random Rng { get; } = new Random(20260709);
        public RuleEvaluator RuleEvaluator { get; }
        public IPlayerRequestByID PlayerRequester { get; }
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public IPresenter Presenter { get; }
        public GameSettings Settings { get; } = GameSettings.GetDefault();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        // diceRoller defaults to a fixed 4 (Deadly tests don't roll); the wound-ignore tests inject a
        // ProbabilisticDiceRoller so Regeneration's per-wound roll is deterministic and fractional. Pass a
        // presenter (e.g. RecordingPresenter) to assert which beats a stage emits; defaults to a no-op sink.
        // ruleResolver is optional and defaults to null, matching the bare-evaluator default: granted-rule
        // read-back no-ops without one. Tests that grant a rule by token (#197 P20's one-shot Unwieldy)
        // pass a resolver carrying that definition.
        public WoundTestContext(GameDataStore store, IPlayerRequestByID requester, IDiceRoller? diceRoller = null,
            IPresenter? presenter = null, IRuleResolver? ruleResolver = null)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            PlayerRequester = requester;
            DiceRoller = diceRoller ?? new FixedDiceRoller(4);
            RuleEvaluator = new RuleEvaluator(DiceRoller, ruleResolver: ruleResolver);
            Presenter = presenter ?? new LocalPresenter(null, new InstantPresentationClock());
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
        public void NotifyGameCompleted(GameResult result) { }
    }
}
