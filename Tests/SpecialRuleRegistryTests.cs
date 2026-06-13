using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 workstream 6: the army-builder rule picker derives from CoreRuleCatalog (+ the open army's
    // embedded rules), not a hand-maintained list. These pin that the picker offers exactly what the
    // engine implements, with the numeric/plain split following whether a definition reads an argument.
    [TestFixture]
    public class SpecialRuleRegistryTests
    {
        private static IReadOnlyList<SpecialRuleRegistry.PickerEntry> CoreEntries() =>
            SpecialRuleRegistry.GetPickerEntries();

        [Test]
        public void Picker_OffersEveryCoreCatalogRule()
        {
            HashSet<string> offered = CoreEntries().Select(e => e.Name).ToHashSet();
            foreach (SpecialRuleDefinition definition in CoreRuleCatalog.All)
            {
                Assert.That(offered, Does.Contain(definition.Name),
                    $"picker must offer implemented rule '{definition.Name}'.");
            }
        }

        [Test]
        public void Picker_SurfacesPreviouslyHiddenImplementedRules()
        {
            HashSet<string> offered = CoreEntries().Select(e => e.Name).ToHashSet();
            // These dispatch in the engine but the old hand-list never offered them.
            Assert.That(offered, Does.Contain("Strafing"));
            Assert.That(offered, Does.Contain("Vanguard"));
            Assert.That(offered, Does.Contain("Martial Prowess"));
        }

        [Test]
        public void Picker_DropsUnimplementedPhantomRules()
        {
            HashSet<string> offered = CoreEntries().Select(e => e.Name).ToHashSet();
            // The old hand-list offered these, but the engine has no definition — picking them did nothing.
            foreach (string phantom in new[] { "Sniper", "Flying", "Aircraft", "Caster", "Fear", "Transport" })
            {
                Assert.That(offered, Does.Not.Contain(phantom),
                    $"picker must not offer unimplemented rule '{phantom}'.");
            }
        }

        [Test]
        public void Picker_TagsNumericRulesByWhetherTheyReadAnArgument()
        {
            Dictionary<string, bool> numericByName =
                CoreEntries().ToDictionary(e => e.Name, e => e.IsNumeric);

            foreach (string numeric in new[] { "Tough", "Deadly", "Blast", "Impact" })
            {
                Assert.That(numericByName[numeric], Is.True, $"'{numeric}' reads an arg → numeric.");
            }
            foreach (string plain in new[] { "Stealth", "Furious", "Scout" })
            {
                Assert.That(numericByName[plain], Is.False, $"'{plain}' reads no arg → plain.");
            }
        }

        [Test]
        public void Picker_IncludesEmbeddedRules_TaggedAndOverridingCoreByName()
        {
            // A brand-new embedded rule that reads an argument (numeric).
            SpecialRuleDefinition customNumeric = new SpecialRuleDefinition("Hexed",
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Always(),
                        new Effect.MultiplyWounds(new ValueSource.Arg(0)),
                        ELifetime.ThisAttack),
                },
                new List<ActivatedAbility>());

            var entries = SpecialRuleRegistry.GetPickerEntries(new[] { customNumeric });
            SpecialRuleRegistry.PickerEntry hexed = entries.Single(e => e.Name == "Hexed");

            Assert.That(hexed.IsNumeric, Is.True, "embedded arg-reading rule is numeric.");
            Assert.That(entries.Select(e => e.Name), Does.Contain("Stealth"), "core rules still offered.");
        }
    }
}
