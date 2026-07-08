using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #183 slice 2 (Subject-seat model visibility): a joined hero's
    // relocated DEFENSIVE (Subject-seat) rule is now visible at the defensive dispatch sites, which pass the
    // defender's living models (AnyOwner). The stage tests run the REAL RollToHitStage and read the folded
    // SaveModifier from the defender's Shielded (+1 to defense) - the cleanest observable of a Subject-seat
    // rule firing. The sole-survivor case is itself the wiring proof: if the stage still passed models:null,
    // a hero-relocated rule would never be collected even when the hero is the last model alive. The trace
    // test proves the other half - the rule is EVALUATED (not silently dropped, the pre-#183 bug), visible to
    // the #163 rule trace even while the gate suppresses it.
    [TestFixture]
    public class HeroSubjectRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(20, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6));
        }

        [TearDown]
        public void TearDown() => RuleTrace.Enabled = false; // process-wide switch - never leak it.

        // Baseline: a plain (non-hero) Shielded unit applies its +1. Zero-diff guard for slice 2 - passing
        // the defender's living models must not change a unit that has no per-model rules.
        [Test]
        public async Task NonHeroShieldedUnit_AppliesSaveBonus()
        {
            DataBinding<UnitData> defender = MakeShieldedUnit();

            RollToHitResults result = await RunHitStage(defender);

            Assert.That(result.SaveModifier, Is.EqualTo(1),
                "a homogeneous Shielded unit gets its +1 to defense, unchanged by slice 2.");
        }

        // Hero carries Shielded, host doesn't, grunts alive: collected (visible) but the all-models gate
        // fails over the grunts, so no bonus. Before #183 the relocated rule was never collected at all.
        [Test]
        public async Task HeroCarriedShielded_DormantWhileGruntsLive()
        {
            DataBinding<UnitData> defender = MakeMergedDefender(CoreRuleCatalog.Shielded,
                heroHasRule: true, hostHasRule: false);

            RollToHitResults result = await RunHitStage(defender);

            Assert.That(result.SaveModifier, Is.EqualTo(0),
                "a hero-only Shielded does not benefit the unit while ordinary models (without it) live.");
        }

        // The wiring proof: kill the grunts so the hero is the sole survivor - now every living model has
        // Shielded, the gate passes, and the bonus fires. This can ONLY pass if the stage threads the
        // defender's living models to the evaluator (models:null would never surface the hero's rule).
        [Test]
        public async Task HeroCarriedShielded_FiresWhenHeroIsSoleSurvivor()
        {
            DataBinding<UnitData> defender = MakeMergedDefender(CoreRuleCatalog.Shielded,
                heroHasRule: true, hostHasRule: false);
            KillGrunts(defender);

            RollToHitResults result = await RunHitStage(defender);

            Assert.That(result.SaveModifier, Is.EqualTo(1),
                "once the hero is the sole survivor every living model has Shielded, so it fires - proving " +
                "the stage threads the defender's living models.");
        }

        // Host has Shielded, joined hero lacks it, grunts alive: the unit LOSES the bonus (the host-side /
        // audit-Bug-24 direction, exercised end-to-end through the real stage).
        [Test]
        public async Task HostShielded_JoinedHeroLacksIt_SuppressedForUnit()
        {
            DataBinding<UnitData> defender = MakeMergedDefender(CoreRuleCatalog.Shielded,
                heroHasRule: false, hostHasRule: true);

            RollToHitResults result = await RunHitStage(defender);

            Assert.That(result.SaveModifier, Is.EqualTo(0),
                "a joined hero without Shielded breaks it for the whole unit.");
        }

        // Both host and hero carry Shielded: every living model has it, so it fires - exactly ONCE (per-unit
        // dedup), not +2 from the unit copy plus the hero-model copy.
        [Test]
        public async Task HostAndHeroBothShielded_FiresOnceNotTwice()
        {
            DataBinding<UnitData> defender = MakeMergedDefender(CoreRuleCatalog.Shielded,
                heroHasRule: true, hostHasRule: true);

            RollToHitResults result = await RunHitStage(defender);

            Assert.That(result.SaveModifier, Is.EqualTo(1),
                "the unit copy and the hero-model copy dedup to a single +1, not +2.");
        }

        // The non-silence guarantee (the #163 half): a hero-relocated defensive rule is EVALUATED - and so
        // narrated by the rule trace - even while the gate suppresses it. Pre-#183 it was invisible (never
        // collected at the Subject seat), so no trace line could ever appear. Uses a direct evaluator with a
        // capturing output, mirroring the stage's model-threaded Subject participant.
        [Test]
        public void HeroCarriedDefensiveRule_IsTracedNotSilent_WhileGruntsLive()
        {
            UnitData defender = MakeMergedDefender(CoreRuleCatalog.Evasive,
                heroHasRule: true, hostHasRule: false).GetValue();
            UnitData attacker = MakeAttacker().GetValue();

            var output = new CapturingOutput();
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4), output);
            RuleTrace.Enabled = true;

            evaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, DistanceInches: 5f),
                (attacker, ERuleSeat.Actor, (IWeapon?)null, (IReadOnlyList<IModel>?)null,
                    EModelRuleScope.AnyOwner),
                (defender, ERuleSeat.Subject, (IWeapon?)null, HeroStatRules.LivingModels(defender),
                    EModelRuleScope.AnyOwner));

            Assert.That(output.DebugLines,
                Has.Some.Match(".*Evasive.*condition AllModelsHaveThisRule not met.*"),
                "the hero's relocated Evasive is evaluated and its gate reported - not silently dropped.");
        }

        // --- harness (mirrors HeroPerModelRuleIntegrationTests) ---

        private sealed class CapturingOutput : ITextOutput
        {
            public readonly List<string> Lines = new();
            public readonly List<string> DebugLines = new();
            public void Log(string message, TextColor? color = null) => Lines.Add(message);
            public void LogDebug(string message, TextColor? color = null) => DebugLines.Add(message);
        }

        private async Task<RollToHitResults> RunHitStage(DataBinding<UnitData> defender)
        {
            DataBinding<UnitData> attacker = MakeAttacker();
            Weapon weapon = attacker.GetValue().Models[0].Weapons[0];

            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1)); // a 6 clears a 4+ threshold

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private DataBinding<UnitData> MakeAttacker() =>
            MakeUnit(modelCount: 1, AttackerPos, new Weapon("Rifle", 48f, 1, 0));

        private DataBinding<UnitData> MakeShieldedUnit()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 5, DefenderPos, weapon: null);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Shielded", CoreRuleCatalog.Shielded));
            return unit;
        }

        // A host unit carrying <paramref name="rule"/> (statically, when hostHasRule) merged with a 1-model
        // hero that either does or doesn't carry it - the merge relocates the hero's copy onto the hero MODEL.
        private DataBinding<UnitData> MakeMergedDefender(SpecialRuleDefinition rule, bool heroHasRule,
            bool hostHasRule)
        {
            UnitData host = MakeUnit(modelCount: 3, DefenderPos, weapon: null).GetValue();
            if (hostHasRule)
            {
                host.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            UnitData hero = MakeUnit(modelCount: 1, DefenderPos, weapon: null).GetValue();
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (heroHasRule)
            {
                hero.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            return _store.GetDataBinding<UnitData>(_store.Create(host));
        }

        private static void KillGrunts(DataBinding<UnitData> defender)
        {
            UnitData unit = defender.GetValue();
            ModelID heroId = unit.HeroAttachment!.HeroModelId;
            foreach (IModel model in unit.Models.Where(m => m.ID != heroId))
            {
                model.DealWounds(model.TotalWounds);
            }
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, Position position, Weapon? weapon)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var weapons = weapon == null ? new List<Weapon>() : new List<Weapon> { weapon };
                var model = new ModelData(0.75f, weapons, position, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
