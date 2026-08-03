using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// RollTags.Count - the dice-beat count labels ("N hits", "N wounds") agree in number with what is
    /// DISPLAYED: singular exactly when the 0.## rendering reads "1". Probabilistic mode makes counts
    /// fractional, so agreement follows the shown value, not the raw float.
    /// </summary>
    [TestFixture]
    public class RollTagsCountTests
    {
        [TestCase(1f, "1 hit")]
        [TestCase(0f, "0 hits")]
        [TestCase(2f, "2 hits")]
        [TestCase(0.5f, "0.5 hits")]
        [TestCase(1.5f, "1.5 hits")]
        [TestCase(1.02f, "1.02 hits")]
        [TestCase(1.001f, "1 hit")] // renders as "1", so it must read singular
        public void AgreesWithTheDisplayedValue(float count, string expected) =>
            Assert.That(RollTags.Count(count, "hit"), Is.EqualTo(expected));

        [Test]
        public void MultiWordNounsPluralizeOnTheLastWord() =>
            Assert.That(RollTags.Count(2f, "impact hit"), Is.EqualTo("2 impact hits"));
    }
}
