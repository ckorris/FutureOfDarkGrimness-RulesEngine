using System;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.SaveLoad;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #444 D2 (option B): every save now carries the OPR rules stamp it was written under, and a save whose stamp is
    /// absent (every pre-3.6 save), unparseable or from another ruleset gets a one-line, non-blocking notice that it
    /// plays with the current rules - the save-side twin of the #440 <see cref="LegacyArmyNoticeTests"/>. Loading is
    /// never refused on the stamp.
    /// </summary>
    [TestFixture]
    public class LegacySaveNoticeTests
    {
        private static string SaveOfADefaultStore() =>
            GameSaveSerializer.Save(GameDataStore.GameDataStoreBuilder.GetDefault());

        /// <summary>A save exactly as this build writes it, minus the stamp - what every pre-3.6 save looks like.</summary>
        private static string WithoutStamp(string json)
        {
            JObject file = JObject.Parse(json);
            Assert.That(file.Remove("RulesVersion"), Is.True, "the save should have carried a stamp to strip");
            return file.ToString();
        }

        private static string WithStamp(string json, string stamp)
        {
            JObject file = JObject.Parse(json);
            file["RulesVersion"] = stamp;
            return file.ToString();
        }

        [Test]
        public void TheOneEngineStamp_IsWhatSavesCarry_AndWhatBothNoticesCompareAgainst()
        {
            Assert.That(OprRulesVersion.CurrentStamp, Is.EqualTo("OPR v3.6.0"));
            Assert.That(OprRulesVersion.CurrentMajorMinor, Is.EqualTo("3.6"));
            Assert.That(LegacyArmyNotice.CurrentRules, Is.EqualTo(OprRulesVersion.CurrentMajorMinor),
                "the army notice derives its major.minor from the same stamp the saves carry");
            Assert.That(LegacyArmyNotice.IsLegacy(OprRulesVersion.CurrentStamp), Is.False);
            Assert.That(LegacySaveNotice.IsLegacy(OprRulesVersion.CurrentStamp), Is.False);
        }

        [Test]
        public void SaveThenLoad_ReadsBackTheCurrentStamp_AndRaisesNoNotice()
        {
            string json = SaveOfADefaultStore();
            Assert.That(JObject.Parse(json)["RulesVersion"]?.ToString(), Is.EqualTo("OPR v3.6.0"));

            GameDataStore loaded = GameSaveSerializer.Load(json, out string? stamp);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(stamp, Is.EqualTo(OprRulesVersion.CurrentStamp));
            Assert.That(LegacySaveNotice.Line(stamp), Is.Null);
        }

        [Test]
        public void ASaveWithNoStamp_StillLoads_AndIsReportedAsPredatingTheUpgrade()
        {
            string legacyJson = WithoutStamp(SaveOfADefaultStore());

            GameDataStore loaded = GameSaveSerializer.Load(legacyJson, out string? stamp);

            Assert.That(loaded, Is.Not.Null, "the stamp is advisory - its absence never refuses the load");
            Assert.That(stamp, Is.Null);
            Assert.That(LegacySaveNotice.IsLegacy(stamp), Is.True);
            Assert.That(LegacySaveNotice.Line(stamp),
                Is.EqualTo("This save predates the OPR 3.6 upgrade; it plays with 3.6 rules"));
            Assert.That(GameSaveSerializer.Load(legacyJson), Is.Not.Null, "the one-argument Load is unchanged");
        }

        [Test]
        public void A35Save_StillLoads_AndNamesItsRuleset()
        {
            GameDataStore loaded = GameSaveSerializer.Load(WithStamp(SaveOfADefaultStore(), "OPR v3.5.3"),
                out string? stamp);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(stamp, Is.EqualTo("OPR v3.5.3"));
            Assert.That(LegacySaveNotice.Line(stamp),
                Is.EqualTo("This save was written for OPR v3.5.3; it plays with 3.6 rules"));
        }

        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("  ", true)]
        [TestCase("custom", true)]
        [TestCase("OPR v3.5.3", true)]
        [TestCase("OPR v3.5.1", true)]
        [TestCase("v3.5", true)]
        [TestCase("OPR v3.6.0", false)]
        [TestCase("OPR v3.6.2", false)]
        [TestCase(" OPR v3.6.0 ", false)]
        public void IsLegacy_UnlessTheStampNamesTheCurrentMajorMinor(string? stamp, bool legacy) =>
            Assert.That(LegacySaveNotice.IsLegacy(stamp), Is.EqualTo(legacy));

        [Test]
        public void TheLines_AreAscii_AndTheUnparseableCaseQuotesTheStamp()
        {
            string? absent = LegacySaveNotice.Line(null);
            string? odd = LegacySaveNotice.Line("custom");
            string? old = LegacySaveNotice.Line("OPR v3.5.3");

            Assert.That(odd, Is.EqualTo("This save was written for custom; it plays with 3.6 rules"));
            foreach (string line in new[] { absent!, odd!, old! })
                Assert.That(line.All(c => c < 128), Is.True, $"game text is ASCII-only: {line}");
        }

        [Test]
        public void TheArmyNotice_StillIgnoresAnAbsentStamp_WhileTheSaveNoticeDoesNot()
        {
            // The two notices differ on purpose: a hand-authored army has no book and so no stamp (nothing says it
            // is old), while every save this build writes is stamped, so an unstamped save is by definition older.
            Assert.That(LegacyArmyNotice.IsLegacy(null), Is.False);
            Assert.That(LegacySaveNotice.IsLegacy(null), Is.True);
        }
    }
}
