using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #67: SpecialRuleEntryParser.Parse is the content parser for a rule reference written as a flat
    // string ("Bane", "Blast(3)", "Spawn(Spores [5])") — had no dedicated tests. Pins the three parsed
    // shapes plus the parser's error-tolerance contract: a malformed input never throws, it degrades to
    // a plain core name (the resolver then reports it as unimplemented, per the class doc).
    [TestFixture]
    public class SpecialRuleEntryParserTests
    {
        [Test]
        public void PlainName_ParsesAsCore()
        {
            Assert.That(SpecialRuleEntryParser.Parse("Bane"), Is.EqualTo(new SpecialRuleEntry_Core("Bane")));
        }

        [Test]
        public void PlainName_TrimsSurroundingWhitespace()
        {
            Assert.That(SpecialRuleEntryParser.Parse("  Bane  "), Is.EqualTo(new SpecialRuleEntry_Core("Bane")));
        }

        [Test]
        public void NameWithNumericParenthetical_ParsesAsCoreNumeric()
        {
            Assert.That(SpecialRuleEntryParser.Parse("Blast(3)"),
                Is.EqualTo(new SpecialRuleEntry_CoreNumeric("Blast", 3)));
        }

        [Test]
        public void NameWithNumericParenthetical_TrimsNameAndValue()
        {
            Assert.That(SpecialRuleEntryParser.Parse("Blast( 3 )"),
                Is.EqualTo(new SpecialRuleEntry_CoreNumeric("Blast", 3)));
        }

        [Test]
        public void NegativeNumericParenthetical_StillParsesAsCoreNumeric()
        {
            // int.TryParse accepts a leading '-'; nothing about the parser itself should reject it —
            // legality of a negative rule value is the resolver's business, not the parser's.
            Assert.That(SpecialRuleEntryParser.Parse("Regeneration(-1)"),
                Is.EqualTo(new SpecialRuleEntry_CoreNumeric("Regeneration", -1)));
        }

        // #197 P17: a non-numeric parenthetical that HUGS the name (no space before '(') is a text
        // argument, e.g. Spawn("Spores [5]") written flat as "Spawn(Spores [5])".
        [Test]
        public void NameWithHuggingTextParenthetical_ParsesAsText()
        {
            Assert.That(SpecialRuleEntryParser.Parse("Spawn(Spores [5])"),
                Is.EqualTo(new SpecialRuleEntry_Text("Spawn", "Spores [5]")));
        }

        // The sibling case #197 P17 exists to NOT break: a space before the paren is the ordinary
        // rule-name-with-parenthetical convention and must keep resolving as one plain core name, not
        // split into a text argument.
        [Test]
        public void NameWithSpaceBeforeParenthetical_StaysOnePlainCoreName()
        {
            Assert.That(SpecialRuleEntryParser.Parse("Versatile Attack (Piercing)"),
                Is.EqualTo(new SpecialRuleEntry_Core("Versatile Attack (Piercing)")));
        }

        [Test]
        public void UnclosedParenthesis_FallsBackToPlainCoreName()
        {
            Assert.That(SpecialRuleEntryParser.Parse("Blast(3"),
                Is.EqualTo(new SpecialRuleEntry_Core("Blast(3")));
        }

        [Test]
        public void EmptyParenthetical_FallsBackToPlainCoreName()
        {
            // Neither a valid int nor a non-empty text argument — the class doc's "malformed numeric
            // falls back to a plain core name" contract.
            Assert.That(SpecialRuleEntryParser.Parse("Something()"),
                Is.EqualTo(new SpecialRuleEntry_Core("Something()")));
        }

        [Test]
        public void EmptyString_DoesNotThrow_ParsesAsEmptyCoreName()
        {
            Assert.DoesNotThrow(() => SpecialRuleEntryParser.Parse(""));
            Assert.That(SpecialRuleEntryParser.Parse(""), Is.EqualTo(new SpecialRuleEntry_Core("")));
        }
    }
}
