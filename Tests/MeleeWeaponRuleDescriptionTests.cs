using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #298: the melee weapon menu used to offer rule NAMES only ("2x Axe - A1, AP0, Deadly(3)"), which is
    // no help at the one moment the player has to choose between weapons. The stage attaches each rule's
    // text to the option, which both front ends render (GUI: underlined names in the label + a details
    // strip; CLI: an indented line). #333 changed the SHAPE of that from a pre-formatted prose blob to
    // structured name/description pairs, so the GUI can find where each rule name sits inside the label.
    // Sibling of DeadlyWeaponPriorityTests, which covers the same stage's gating half.
    [TestFixture]
    public class MeleeWeaponRuleDescriptionTests
    {
        // A rule the engine resolves but whose catalog entry carries no player-facing text.
        private static readonly SpecialRuleDefinition UndocumentedRule = new SpecialRuleDefinition(
            "Mysterious", Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), ERuleScope.Weapon);

        [Test]
        public async Task DocumentedWeaponRule_IsAttachedToItsOption()
        {
            StringSelectionRequest request = await MeleeMenuFor(DeadlyWeapon("Axe", x: 3));

            string label = request.ValidOptions.Single();
            Assert.That(request.OptionRules, Is.Not.Null);
            Assert.That(request.OptionRules!.ContainsKey(label), Is.True,
                "the Deadly weapon's option must carry its rules");

            StringSelectionRequest.OptionRule rule = request.OptionRules[label].Single();
            Assert.That(rule.Name, Is.EqualTo("Deadly"),
                "the RESOLVED name, which is what appears inside the option label");
            Assert.That(rule.Description, Is.EqualTo(CoreRuleCatalog.Deadly.Description),
                "the catalog text that explains it, unformatted - the front end decides how to show it");
        }

        // The name must be findable inside the label, which is the whole contract the GUI relies on to
        // underline it in place rather than repeating it underneath.
        [Test]
        public async Task RuleName_AppearsVerbatimInTheOptionLabel()
        {
            StringSelectionRequest request = await MeleeMenuFor(DeadlyWeapon("Axe", x: 3));

            string label = request.ValidOptions.Single();
            Assert.That(label, Does.Contain(request.OptionRules![label].Single().Name));
        }

        [Test]
        public async Task WeaponWithSeveralRules_ListsThemInAttachmentOrder()
        {
            var weapon = DeadlyWeapon("Halberd", x: 2);
            weapon.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));

            StringSelectionRequest request = await MeleeMenuFor(weapon);

            List<StringSelectionRequest.OptionRule> rules =
                request.OptionRules![request.ValidOptions.Single()];
            Assert.That(rules.Select(r => r.Name), Is.EqualTo(new[] { "Deadly", "Rending" }),
                "attachment order, which is also the order the label appended the names in");
            Assert.That(rules.All(r => !string.IsNullOrEmpty(r.Description)), Is.True);
        }

        [Test]
        public async Task PlainWeapons_CarryNoRulesAtAll()
        {
            StringSelectionRequest request = await MeleeMenuFor(
                new Weapon("Blade", 0f, 1, 0), new Weapon("Spear", 0f, 1, 0));

            Assert.That(request.OptionRules, Is.Null,
                "the common plain-weapon menu must stay exactly as it was");
            Assert.That(request.OptionDescriptions, Is.Null);
        }

        // #333 reversed #298 here: an undocumented rule IS listed now, carrying a null description. The
        // front ends say it is not enforced in play, which is the shoot panel's long-standing treatment
        // (#292) and is real information when choosing what to swing with.
        [Test]
        public async Task UndocumentedRule_IsListedWithNoDescription()
        {
            var weapon = new Weapon("Odd Blade", 0f, 1, 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Mysterious", UndocumentedRule));

            StringSelectionRequest request = await MeleeMenuFor(weapon);

            StringSelectionRequest.OptionRule rule =
                request.OptionRules![request.ValidOptions.Single()].Single();
            Assert.That(rule.Name, Is.EqualTo("Mysterious"));
            Assert.That(rule.Description, Is.Null,
                "null, not empty - the front end shows the not-enforced note in its place");
        }

        // #333: an option the player cannot take right now is still part of the comparison - the whole
        // reason the Deadly gate is holding the other weapon back is that Deadly matters - so a greyed row
        // carries its rules too, exactly as the shoot panel greys an unavailable weapon but still explains it.
        [Test]
        public async Task GreyedOutWeapon_StillCarriesItsRules()
        {
            var rendingBlade = new Weapon("Blade", 0f, 2, 0);
            rendingBlade.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));

            // The Deadly axe gates every non-Deadly weapon this pass (#028), so the blade comes back invalid.
            StringSelectionRequest request = await MeleeMenuFor(DeadlyWeapon("Axe", x: 3), rendingBlade);

            string greyed = request.InvalidOptions
                .Single(o => o.Reason.Contains("Deadly weapons first")).Option;
            Assert.That(request.OptionRules![greyed].Single().Name, Is.EqualTo("Rending"));
        }

        // #333 split the two kinds of text apart: a weapon's RULES are structured on OptionRules, while
        // OptionDescriptions keeps only free-form prose about the choice itself - #320's hold-back line.
        // A hold-back must not also carry the weapon's rules; its owner's row right above already has them.
        [Test]
        public async Task HoldBack_KeepsItsConsequenceLine_AndCarriesNoRules()
        {
            var bomb = new Weapon("Demo Charge", 0f, 1, 0);
            bomb.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));

            // A second weapon, so holding the Limited one back does not mean striking with nothing (#318).
            StringSelectionRequest request = await MeleeMenuFor(bomb, new Weapon("Blade", 0f, 2, 0));

            string holdBack = request.ValidOptions.Single(o => o.Contains("Hold back"));
            Assert.That(request.OptionDescriptions![holdBack],
                Is.EqualTo("Keeps its Limited once-per-game use for a later melee."));
            Assert.That(request.OptionRules!.ContainsKey(holdBack), Is.False,
                "the decline carries no rules of its own");

            string swing = request.ValidOptions.Single(o => o.Contains("Demo Charge") && o != holdBack);
            Assert.That(request.OptionRules[swing].Single().Name, Is.EqualTo("Limited"),
                "the weapon's own row is where its rules live");
            Assert.That(request.OptionDescriptions.ContainsKey(swing), Is.False,
                "and a swing option has no free-form prose at all");
        }

        // --- fixtures ---

        private static Weapon DeadlyWeapon(string name, int x)
        {
            var weapon = new Weapon(name, rangeInches: 0f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(x) }));
            return weapon;
        }

        private static async Task<StringSelectionRequest> MeleeMenuFor(params Weapon[] weapons)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new DeadlyWeaponPriorityTests.CapturingStringSelectionRequester();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(store, requester);

            var model = new ModelData(baseRadiusInches: 0.5f, weapons: weapons.ToList(),
                initialPosition: new Position(0, 0, 0), gameDataStore: store);
            var modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Attacker", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            var unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));

            var combatCtx = new CombatActionContext(ctx, unitBinding, isMelee: true);
            var stage = new ChooseMeleeWeaponStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChosen.Bind("test-on-chosen");
            await stage.Enter(combatCtx);

            Assert.That(requester.Captured, Is.Not.Null, "Resolver should have been called.");
            return requester.Captured!;
        }
    }
}
