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
    // no help at the one moment the player has to choose between weapons. The stage now attaches each
    // documented rule's text as the option's description, which both front ends already render (GUI subtext,
    // CLI indented line). Sibling of DeadlyWeaponPriorityTests, which covers the same stage's gating half.
    [TestFixture]
    public class MeleeWeaponRuleDescriptionTests
    {
        // A rule the engine resolves but whose catalog entry carries no player-facing text. Nothing to say
        // about it, so it must not produce an empty description line.
        private static readonly SpecialRuleDefinition UndocumentedRule = new SpecialRuleDefinition(
            "Mysterious", Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), ERuleScope.Weapon);

        [Test]
        public async Task DocumentedWeaponRule_IsDescribedUnderItsOption()
        {
            StringSelectionRequest request = await MeleeMenuFor(DeadlyWeapon("Axe", x: 3));

            string label = request.ValidOptions.Single();
            Assert.That(request.OptionDescriptions, Is.Not.Null);
            Assert.That(request.OptionDescriptions!.ContainsKey(label), Is.True,
                "the Deadly weapon's option must carry a description");
            Assert.That(request.OptionDescriptions[label],
                Is.EqualTo($"Deadly - {CoreRuleCatalog.Deadly.Description}"),
                "the description is the rule's name plus the catalog text that explains it");
        }

        [Test]
        public async Task WeaponWithSeveralRules_GetsOneLinePerRule()
        {
            var weapon = DeadlyWeapon("Halberd", x: 2);
            weapon.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));

            StringSelectionRequest request = await MeleeMenuFor(weapon);

            string description = request.OptionDescriptions![request.ValidOptions.Single()];
            Assert.That(description.Split('\n'), Has.Length.EqualTo(2),
                "each rule gets its own line so the subtext reads as a list");
            Assert.That(description, Does.StartWith("Deadly - "), "rules keep their attachment order");
            Assert.That(description, Does.Contain("\nRending - "));
        }

        [Test]
        public async Task PlainWeapons_CarryNoDescriptionsAtAll()
        {
            StringSelectionRequest request = await MeleeMenuFor(
                new Weapon("Blade", 0f, 1, 0), new Weapon("Spear", 0f, 1, 0));

            Assert.That(request.OptionDescriptions, Is.Null,
                "the common plain-weapon menu must stay exactly as it was");
        }

        [Test]
        public async Task UndocumentedRule_IsNotListed()
        {
            var weapon = new Weapon("Odd Blade", 0f, 1, 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Mysterious", UndocumentedRule));

            StringSelectionRequest request = await MeleeMenuFor(weapon);

            Assert.That(request.OptionDescriptions, Is.Null,
                "a rule name with no text adds nothing the option label doesn't already show");
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
