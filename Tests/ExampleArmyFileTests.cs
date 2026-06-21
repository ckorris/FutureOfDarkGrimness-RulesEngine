using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 workstream 4: proves the whole authoring pipeline against a real committed .fdgarmy on disk
    // (armies/example-with-rules.fdgarmy) — not an in-memory fixture or a throwaway temp file. The file
    // is the canonical example the schema doc (docs/rule-json-schema.md) documents; this test is its
    // contract. If the serialized format drifts, regenerate the file and update the doc together.
    [TestFixture]
    public class ExampleArmyFileTests
    {
        private static string ExampleArmyPath()
        {
            // TestDirectory is <submodule>/bin/Debug/net8.0; the armies/ dir is at the submodule root.
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "armies", "example-with-rules.fdgarmy");
            return Path.GetFullPath(path);
        }

        [Test]
        public void ExampleArmyFile_ExistsOnDisk()
        {
            Assert.That(File.Exists(ExampleArmyPath()), Is.True,
                $"missing canonical example army at {ExampleArmyPath()}");
        }

        [Test]
        public void ExampleArmyFile_LoadsFromDisk_AndCarriesItsEmbeddedRule()
        {
            string json = File.ReadAllText(ExampleArmyPath());
            ArmyListFile army = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;

            Assert.That(army.Name, Is.EqualTo("Example Raiders"));
            Assert.That(army.RuleDefinitions.Select(r => r.Name), Does.Contain("Frenzied"),
                "the army must carry its embedded 'Frenzied' definition.");

            // 'Frenzied' is NOT a core rule — it can only resolve via the embed.
            Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Frenzied", out _), Is.False,
                "precondition: 'Frenzied' is army-specific, not in the core catalog.");
        }

        // #033: the canonical caster example (armies/example-caster.fdgarmy) — a Caster(3) unit plus an
        // army spell list (a damage spell + a buff spell). Pins the spell JSON shape and the load→resolve
        // path the same way the rules example above does.
        [Test]
        public void CasterExampleArmy_LoadsAndResolvesItsSpells()
        {
            string path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "armies", "example-caster.fdgarmy"));
            Assert.That(File.Exists(path), Is.True, $"missing caster example army at {path}");

            ArmyListFile army = JsonSerializer.Deserialize<ArmyListFile>(File.ReadAllText(path), RuleJson.Options)!;

            // The Caster unit names Caster(3) as a numeric core rule.
            UnitFileEntry magus = army.Units.Single(u => u.Name == "Magus");
            Assert.That(magus.SpecialRules.OfType<SpecialRuleEntry_CoreNumeric>()
                .Any(r => r.Name == "Caster" && r.NumericValue == 3), Is.True);

            // Two spells: a damage spell (DealHits with AP) and a buff (AddRule).
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            System.Collections.Generic.IReadOnlyList<RuntimeSpell> spells =
                ArmyListSpellResolution.ResolveSpells(army, resolver);

            Assert.That(spells.Select(s => s.Name), Is.EquivalentTo(new[] { "Fire Bolt", "Haste" }));
            RuntimeSpell fireBolt = spells.Single(s => s.Name == "Fire Bolt");
            Assert.That(((Effect.DealHits)fireBolt.Effect).ArmorPenetration, Is.EqualTo(1));
            RuntimeSpell haste = spells.Single(s => s.Name == "Haste");
            Assert.That(((Effect.AddRule)haste.Effect).RuleName, Is.EqualTo("Furious"));
        }

        [Test]
        public void ExampleArmyFile_RegistersAndResolvesEndToEnd()
        {
            string json = File.ReadAllText(ExampleArmyPath());
            ArmyListFile army = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            // Passes load validation (workstream 3) and registers the embedded rule.
            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);

            Assert.That(resolver.TryResolve("Frenzied", out ResolvedRule frenzied), Is.True,
                "the embedded rule must resolve after registration.");
            Assert.That(frenzied.Definition.Passive, Has.Count.EqualTo(1));
            Assert.That(resolver.TryResolve("Furious", out _), Is.True, "core rules remain available.");

            // The referencing unit names the embedded rule.
            UnitFileEntry berserkers = army.Units.Single(u => u.Name == "Berserkers");
            Assert.That(berserkers.SpecialRules.OfType<SpecialRuleEntry_Core>().Select(r => r.Name),
                Does.Contain("Frenzied"));
        }
    }
}
