using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 Hazardous - "Attacks with this weapon get AP(4), but this weapon's unit takes one wound on
    // unmodified rolls of 1 to hit." The AP half shipped with #196; this suite covers the self-wound half.
    //
    // Three things here are easy to get wrong and invisible to --validate-rules or RuleFireLint (which
    // prove only that an entry CAN fire and that some stage reads its operation):
    //  * WHO takes the wound. The hook fires with the attacker as Actor and the defender as Subject, and
    //    every other effect at it hurts the DEFENDER. Wounding the wrong unit would look like a working
    //    rule in a log and be exactly backwards.
    //  * WHEN. Owner-ruled 2026-07-29: after the whole attack resolves, so the shot the models paid for
    //    completes and the attacking unit is never torn down mid-attack. Pinned by RecordingLayer, which
    //    samples the attacker at the instant the stage hands its results downstream.
    //  * The dice invariant. The 1-count comes off a histogram, so the wound total is FRACTIONAL. Flooring
    //    it (the pool-size precedent) would make a 2-attack pistol essentially never self-wound.
    [TestFixture]
    public class HazardousRuleIntegrationTests
    {
        private const string RuleName = "Hazardous";

        // The shipped shape, hand-built because the engine suite cannot read the app's rule supplement.
        // HazardousShippedDataTests asserts the authored definition matches this.
        private static readonly SpecialRuleDefinition Hazardous = new(RuleName,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Save, -4),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.UnmodifiedRollEquals(1),
                    new Effect.SelfWoundOnUnmodifiedRoll(OnRollValue: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            Scope: ERuleScope.Weapon);

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(Hazardous);
        }

        // ---- The self-wound --------------------------------------------------------------------------

        [Test]
        public async Task UnmodifiedOnes_WoundTheWeaponsOwnUnit()
        {
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit("Targets", modelCount: 3);

            // Three attacks, every die a 1: three unmodified 1s, so three wounds owed.
            await RunStage(attacker, defender, face: 1, attacks: 3, hazardous: true);

            Assert.That(WoundsDealt(attacker), Is.EqualTo(3f),
                "'takes one wound on unmodified rolls of 1 to hit' - one per 1, three 1s rolled.");
        }

        [Test]
        public async Task WithoutTheRule_TheSameOnesCostNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);

            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), face: 1, attacks: 3,
                hazardous: false);

            Assert.That(WoundsDealt(attacker), Is.Zero, "no Hazardous, no overheat.");
        }

        [Test]
        public async Task NoOnesRolled_NoSelfWound()
        {
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);

            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), face: 6, attacks: 3,
                hazardous: true);

            Assert.That(WoundsDealt(attacker), Is.Zero,
                "the whole volley hit - nothing came up 1, so the weapon never bit back.");
        }

        [Test]
        public async Task TheDefenderTakesNoneOfIt()
        {
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit("Targets", modelCount: 3);

            await RunStage(attacker, defender, face: 1, attacks: 3, hazardous: true);

            Assert.That(WoundsDealt(defender), Is.Zero,
                "the SHOOTER overheats. Every other effect at this hook hurts the defender; this one " +
                "must not, and a log line alone could not tell the difference.");
        }

        // ---- Ordering (the owner ruling) -------------------------------------------------------------

        [Test]
        public async Task TheHitRollStage_CountsTheWound_ButDoesNotApplyIt()
        {
            // Owner-ruled: not at the roll. The count rides RollToHitResults to the end of the chain, so
            // the attack the models paid for resolves in full before the weapon bites back - and the
            // attacking unit is never torn down while later stages of its own attack are still running.
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);

            RollToHitResults results = await RunHitRollOnly(attacker,
                MakeUnit("Targets", modelCount: 3), face: 1, attacks: 3, hazardous: true);

            Assert.That(results.SelfWounds, Is.EqualTo(3f), "counted where the unmodified dice are in hand");
            Assert.That(WoundsDealt(attacker), Is.Zero, "...but not a scratch yet.");
        }

        [Test]
        public async Task TheWoundIsAppliedByTheStageThatAppliesTheTargetsWounds()
        {
            // ApplyWoundsStage is the LAST stage of every chain that rolls to hit. Landing it there is
            // what makes "after the attack resolves" true in play: a combat stage's continuation after
            // onFinished() never runs, so code written below that call is dead in a real game and only
            // looks live under a test layer that returns from ExecuteTransition immediately. The first cut
            // of this rule did exactly that - green tests, nothing at all in the headless probe.
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);
            DataBinding<UnitData> defender = MakeUnit("Targets", modelCount: 3);

            RollToHitResults results = await RunHitRollOnly(attacker, defender, face: 1, attacks: 3,
                hazardous: true);
            Assert.That(WoundsDealt(attacker), Is.Zero, "precondition: nothing applied yet");

            await RunApplyWounds(attacker, defender, results);

            Assert.That(WoundsDealt(attacker), Is.EqualTo(3f),
                "the overheat lands once the target's wounds have been applied.");
        }

        // ---- Spread, death and the destruction seam --------------------------------------------------

        [Test]
        public async Task WoundsFillModelsFrontToBack()
        {
            // 2 wounds against three 1-wound models: the first two die, the third is untouched.
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);

            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), face: 1, attacks: 2,
                hazardous: true);

            List<IModel> models = attacker.GetValue().Models.ToList();
            Assert.That(models[0].GetIsAlive(), Is.False);
            Assert.That(models[1].GetIsAlive(), Is.False);
            Assert.That(models[2].GetIsAlive(), Is.True, "a pool of 2 cannot reach the third model.");
            Assert.That(attacker.GetValue().GetIsAlive(), Is.True);
        }

        [Test]
        public async Task ItCanDestroyTheFiringUnitOutright()
        {
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 2);

            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), face: 1, attacks: 4,
                hazardous: true);

            Assert.That(attacker.GetValue().GetIsAlive(), Is.False,
                "4 wounds owed by a 2-model unit kills it; the surplus is dropped, not wrapped.");
        }

        [Test]
        public async Task ADestroyedFiringUnit_ClearsTheMarksItPlaced()
        {
            // The destruction seam is what clears OwnerDestroyed marks and spills transports. If the
            // self-wound applied wounds without routing through it, a dead shooter's marks would linger.
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 1);
            DataBinding<UnitData> marked = MakeUnit("Targets", modelCount: 3);
            var mark = new TokenType("OverheatMark");
            marked.GetValue().Tokens.AddToken(new Rules.Tokens.Token(mark, 1,
                new TokenClearTrigger.OwnerDestroyed(), OwnerUnitID: attacker.GetValue().ID));

            await RunStage(attacker, marked, face: 1, attacks: 2, hazardous: true);

            Assert.That(attacker.GetValue().GetIsAlive(), Is.False);
            Assert.That(marked.GetValue().Tokens.HasToken(mark), Is.False,
                "the killer-less death still runs UnitDestructionNotifier.");
        }

        // ---- The dice invariant ----------------------------------------------------------------------

        [Test]
        public async Task UnderTheProbabilisticRoller_TheWoundStaysFractional()
        {
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);

            // Four attacks, an even histogram: 4/6 of a die sits on face 1. Deliberately NOT six attacks -
            // 6 x 1/6 is exactly 1.0 and survives a floor untouched, so that version of this test read like
            // it pinned the invariant while pinning nothing (caught by mutation, 2026-07-29).
            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), attacks: 4, hazardous: true,
                roller: new ProbabilisticDiceRoller());

            Assert.That(WoundsDealt(attacker), Is.EqualTo(4f / 6f).Within(0.001f),
                "flooring the count is the POOL-SIZE precedent and does not apply to a wound total - " +
                "a 2-attack pistol would never self-wound under a floor.");
        }

        [Test]
        public async Task ASingleAttack_OwesAFractionOfAWound()
        {
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);

            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), attacks: 1, hazardous: true,
                roller: new ProbabilisticDiceRoller());

            Assert.That(WoundsDealt(attacker), Is.EqualTo(1f / 6f).Within(0.001f),
                "one die owes 1/6 of a wound - the fractional tail a floor would silently discard.");
        }

        // ---- Interactions ----------------------------------------------------------------------------

        [Test]
        public async Task RegenerationDoesNotSaveTheShooterFromItsOwnGun()
        {
            // Owner-ruled 2026-07-29: unignorable, matching dangerous terrain and No Retreat. The
            // wound-ignore pipeline lives in the enemy-attack wound stage and is not consulted here.
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3);
            attacker.GetValue().AttachRuleDefinition(
                new ResolvedRule("Regeneration", CoreRuleCatalog.Regeneration));

            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), face: 1, attacks: 3,
                hazardous: true);

            Assert.That(WoundsDealt(attacker), Is.EqualTo(3f),
                "Regeneration ignores wounds from ATTACKS; there is no attack here to save against.");
        }

        [Test]
        public async Task OnlyTheHazardousWeaponBitesBack()
        {
            // Weapon-scoped: the models really carry a Hazardous rifle, but this volley is the CCW. Firing
            // one weapon must not trigger the other's overheat - the leak a unit-scoped authoring would
            // cause, and one that would only ever show up as mysterious casualties in melee.
            var rifle = new Weapon("Plas-Burst Rifle", rangeInches: 24f, attacks: 3, armorPenetration: 0);
            rifle.AttachRuleDefinition(_resolver.Resolve(RuleName));
            DataBinding<UnitData> attacker = MakeUnit("Plague Crew", modelCount: 3, carrying: rifle);

            var ccw = new Weapon("Rusty Blade", rangeInches: 0f, attacks: 3, armorPenetration: 0);
            await RunStage(attacker, MakeUnit("Targets", modelCount: 3), face: 1, attacks: 3,
                hazardous: false, weapon: ccw);

            Assert.That(WoundsDealt(attacker), Is.Zero,
                "the rule rides the WEAPON, so the rifle's 1s are not the blade's problem.");
        }

        // ---- Presentation ----------------------------------------------------------------------------

        [Test]
        public void TheToHitBeatCarriesASelfWoundChip()
        {
            // The wound lands after the attack, but the 1s on THIS roll caused it - so the player is told
            // at the roll, not seconds later when models start falling over.
            List<string> tags = RollToHitStage<ICombatMetadata>.ComposeProcTags(
                new[] { ((RuleOperation)new RuleOperation.InflictSelfWounds(2f), RuleName) });

            Assert.That(tags, Has.Exactly(1).Items);
            Assert.That(tags[0], Is.EqualTo("Hazardous 2 self-wounds"));
        }

        [Test]
        public void AZeroSelfWound_ChipsNothing()
        {
            List<string> tags = RollToHitStage<ICombatMetadata>.ComposeProcTags(
                new[] { ((RuleOperation)new RuleOperation.InflictSelfWounds(0f), RuleName) });

            Assert.That(tags, Is.Empty, "no 1s came up - no chip, no noise.");
        }

        // ---- Harness ---------------------------------------------------------------------------------

        // Drives the two production stages that matter, in the order FireStage / SwingMeleeWeaponStage /
        // StrafingStage chain them: RollToHitStage counts the overheat onto its results, ApplyWoundsStage
        // (the chain's last stage) applies it.
        private async Task RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            int attacks, bool hazardous, int face = 6, IDiceRoller? roller = null, Weapon? weapon = null)
        {
            RollToHitResults results = await RunHitRollOnly(attacker, defender, attacks: attacks,
                hazardous: hazardous, face: face, roller: roller, weapon: weapon);
            await RunApplyWounds(attacker, defender, results);
        }

        private async Task<RollToHitResults> RunHitRollOnly(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, int attacks, bool hazardous, int face = 6,
            IDiceRoller? roller = null, Weapon? weapon = null)
        {
            var ctx = new TestGameContext(_store, roller ?? new FixedFaceDiceRoller(face),
                ruleResolver: _resolver);

            var stage = new RollToHitStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            Weapon fired = weapon
                ?? new Weapon("Plas-Burst Rifle", rangeInches: 24f, attacks: attacks, armorPenetration: 0);
            if (hazardous) fired.AttachRuleDefinition(_resolver.Resolve(RuleName));

            CombatMetadata metadata = MakeMetadata(ctx, attacker, defender, fired, attacks);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults results), Is.True,
                "the hit stage must store its results");
            return results;
        }

        private async Task RunApplyWounds(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            RollToHitResults hitResults)
        {
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(6), ruleResolver: _resolver);

            var stage = new ApplyWoundsStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            CombatMetadata metadata = MakeMetadata(ctx, attacker, defender,
                new Weapon("Plas-Burst Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0),
                attacks: 1);
            metadata.AddResult(hitResults);
            // No wounds owed to the TARGET - this harness is about what the shooter owes itself. The stage
            // still walks its (empty) assignment first, so the self-wound lands after it either way.
            metadata.AddResult(new AssignWoundsResults(defender, totalWoundsToAssign: 0f));

            await stage.Enter(metadata);
        }

        private CombatMetadata MakeMetadata(TestGameContext ctx, DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, Weapon weapon, int attacks)
        {
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: false, isCharging: false);
            // Threshold 4+: face 1 always misses, face 6 always hits. The self-wound reads the unmodified
            // dice either way, so neither outcome is load-bearing here.
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: attacks));
            return metadata;
        }

        private static float WoundsDealt(DataBinding<UnitData> unit) =>
            unit.GetValue().Models.Sum(model => model.WoundsDealt);

        // A unit of one-wound models 1" apart, well inside any range gate.
        private DataBinding<UnitData> MakeUnit(string name, int modelCount, Weapon? carrying = null)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var weapons = carrying == null ? new List<Weapon>() : new List<Weapon> { carrying };
                var model = new ModelData(0.5f, weapons, new Position(i, 0), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var player = new PlayerID(Guid.NewGuid());
            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
