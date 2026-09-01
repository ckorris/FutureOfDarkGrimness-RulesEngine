using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FDG.Tests
{
    // #392 — army creation must treat its ArmyListFile as READ-ONLY. UnitData's constructor used
    // to sort each entry's Weapons list IN PLACE (by quantity, for model distribution); with one
    // deserialized file shared across a benchmark matchup's concurrent games, two games racing
    // that sort against each other's enumeration captured different weapon orders - and weapon
    // order feeds resolution order and dice consumption, so outcomes depended on which games
    // happened to overlap in the process (7/16 games flipped between GC modes in the #392 repro).
    // The mutation was idempotent, so purely sequential reuse (smoke --repeat, the #193 gate)
    // never showed it. This pin serializes the whole file before and after the real launch path
    // and demands byte identity, so ANY future mutation of the input graph fails here.
    [TestFixture]
    public class ArmyFileImmutabilityTests
    {
        [Test]
        public void CreateArmy_LeavesTheArmyListFileByteIdentical()
        {
            ArmyListFile army = new ArmyListFile
            {
                Name = "Immutability probe",
                Faction = "Test",
                Units = new List<UnitFileEntry>
                {
                    new UnitFileEntry
                    {
                        Name = "Mixed Arms", ModelCount = 6, Quality = 4, Defense = 4,
                        Weapons = new List<WeaponFileEntry>
                        {
                            // Deliberately quantity-DESCENDING: the exact shape the pre-#392
                            // in-place sort reordered.
                            new WeaponFileEntry { Name = "Big Gun", RangeInches = 24, Attacks = 2, Quantity = 3 },
                            new WeaponFileEntry { Name = "Sidearm", RangeInches = 12, Attacks = 1, Quantity = 2 },
                            new WeaponFileEntry { Name = "Knife", RangeInches = 0, Attacks = 1, Quantity = 1 },
                        },
                    },
                },
            };
            string before = JsonSerializer.Serialize(army, RuleJson.Options);

            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
            GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);

            string after = JsonSerializer.Serialize(army, RuleJson.Options);
            Assert.That(after, Is.EqualTo(before),
                "army creation mutated its input ArmyListFile - concurrent games sharing one file "
                + "would couple through it (#392)");
        }
    }
}
