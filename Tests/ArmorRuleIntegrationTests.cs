using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 Armor(X): "counts as having Defense X+ in place of the
    // model's own Defense stat". The rule rides Tough's creation seam — Lifecycle_OnUnitCreated ->
    // SetDefense(Arg(0)) — folded by DefenseSetSink in UnitCreationRules.Apply (the same call FDGServer
    // makes at army-load) and WRITTEN onto UnitData.Defense, so every save path (volleys, melee, impact,
    // reflect, synthetic hits, and the AI's CombatMath) reads the set value through the stat with no
    // per-path folding. Owner-ruled 2026-07-29: a literal SET, not a floor — it replaces the base even
    // where the base was better (no current corpus site worsens). A joining hero's own Armor is baked
    // into its HeroAttachment by HeroJoinResolver, mirroring how heroWounds bakes in Tough.
    [TestFixture]
    public class ArmorRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new FixedDiceRoller(4)); // creation rules don't roll
        }

        // The supplement's Armor definition, authored inline: the engine has no core Armor rule (it is
        // data in GdfRuleSupplement.json), but the wiring under test — effect, sink, applicator — is
        // exactly this shape.
        private static SpecialRuleDefinition ArmorDefinition { get; } = new SpecialRuleDefinition("Armor",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                    new Condition.Always(),
                    new Effect.SetDefense(new ValueSource.Arg(0)),
                    ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>(),
            EngineArgumentCount: 1,
            Valence: EValence.Positive,
            Description: "Counts as having Defense X+ in place of the model's own Defense stat.");

        [Test]
        public void ArmorUnit_DefenseSetFromArgument()
        {
            UnitData unit = MakeUnit(modelCount: 3, defense: 5);
            AttachArmor(unit, x: 4);

            UnitCreationRules.Apply(unit, _evaluator);

            Assert.That(unit.Defense, Is.EqualTo(4), "Armor(4) replaces the unit's D5+ stat.");
            Assert.That(HeroStatRules.GetSaveDefense(unit), Is.EqualTo(4),
                "the save stages read the set value through GetSaveDefense.");
        }

        [Test]
        public void Armor_IsALiteralSet_NotAFloor()
        {
            // Owner-ruled 2026-07-29: "in place of" means SET. A base BETTER than the armor value is
            // still replaced — this is the assertion that pins set-vs-floor.
            UnitData unit = MakeUnit(modelCount: 1, defense: 3);
            AttachArmor(unit, x: 4);

            UnitCreationRules.Apply(unit, _evaluator);

            Assert.That(unit.Defense, Is.EqualTo(4),
                "Armor(4) replaces even a better D3+ base — a literal set, not a floor.");
        }

        [Test]
        public void SeveralArmorSources_BestOneWins()
        {
            UnitData unit = MakeUnit(modelCount: 1, defense: 6);
            AttachArmor(unit, x: 5);
            AttachArmor(unit, x: 3);

            UnitCreationRules.Apply(unit, _evaluator);

            Assert.That(unit.Defense, Is.EqualTo(3),
                "when several sets land, the sink keeps the lowest (best) value.");
        }

        [Test]
        public void NoArmor_DefenseUnchanged()
        {
            UnitData unit = MakeUnit(modelCount: 3, defense: 5);

            UnitCreationRules.Apply(unit, _evaluator);

            Assert.That(unit.Defense, Is.EqualTo(5), "no defense-set rule -> the stat is untouched.");
        }

        [Test]
        public void JoinedHero_AttachmentCarriesItsArmor_HostStatUntouched()
        {
            // A hero's standalone unit never passes through UnitCreationRules (the join consumes it
            // first), so HeroJoinResolver must bake the hero's Armor into the attachment — the value
            // GetSaveDefense returns once the hero is the sole survivor. The HOST's own stat must not
            // leak: while squadmates live, the unit saves at the unit's Defense, Armor or no Armor.
            UnitData host = MakeUnit(modelCount: 3, defense: 5);
            UnitData hero = MakeUnit(modelCount: 1, defense: 4);
            AttachHero(hero);
            AttachArmor(hero, x: 2);

            IReadOnlyList<UnitData> survivors = HeroJoinResolver.Apply(Pairs(
                ("host", null, host),
                (null, "host", hero)));

            Assert.That(survivors, Is.EqualTo(new[] { host }), "the hero is absorbed; only the host deploys.");
            Assert.That(host.HeroAttachment!.Defense, Is.EqualTo(2),
                "the attachment carries the hero's Armor(2), not its raw D4+ stat.");

            UnitCreationRules.Apply(host, _evaluator);

            Assert.That(host.Defense, Is.EqualTo(5),
                "the hero's Armor does not leak onto the host unit's own Defense stat.");
            Assert.That(HeroStatRules.GetSaveDefense(host), Is.EqualTo(5),
                "with squadmates alive, the unit still saves at the unit's Defense.");
        }

        [Test]
        public void JoinedHero_WithoutArmor_AttachmentKeepsRawDefense()
        {
            UnitData host = MakeUnit(modelCount: 3, defense: 5);
            UnitData hero = MakeUnit(modelCount: 1, defense: 4);
            AttachHero(hero);

            HeroJoinResolver.Apply(Pairs(
                ("host", null, host),
                (null, "host", hero)));

            Assert.That(host.HeroAttachment!.Defense, Is.EqualTo(4),
                "no defense-set rule -> the attachment carries the hero's own stat.");
        }

        // --- helpers (mirroring HeroRuleIntegrationTests / ToughRuleIntegrationTests) ---

        private static IReadOnlyList<(SaveLoad.UnitFileEntry Entry, UnitData Unit)> Pairs(
            params (string? Id, string? JoinsUnitId, UnitData Unit)[] specs) =>
            specs.Select(spec => (
                new SaveLoad.UnitFileEntry { Id = spec.Id, JoinsUnitId = spec.JoinsUnitId, Name = spec.Unit.Name },
                spec.Unit)).ToList();

        private static void AttachHero(UnitData unit) =>
            unit.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));

        private static void AttachArmor(UnitData unit, int x) =>
            unit.AttachRuleDefinition(
                new ResolvedRule("Armor", ArmorDefinition, new RuleArgument[] { new RuleArgument.Int(x) }));

        private UnitData MakeUnit(int modelCount, int defense)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: defense, modelBindings: modelBindings);
        }
    }
}
