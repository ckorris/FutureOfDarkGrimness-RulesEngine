using NUnit.Framework;

namespace FDG.Tests
{
    // Regression pin for a latent DiceResults defect found while building #197's reroll-threshold slice:
    // TotalWithinRange offset-corrected its LOWER bound by SideMin but used its upper bound raw. That is
    // accidentally correct for a full die (SideMin 1) and wrong for every SUBSET, whose SideMin is its
    // lowest kept face - so a range query over a subset over-counted or threw IndexOutOfRange.
    //
    // Nothing had asked a subset for a range before: the reroll path used At(SideMax), which indexes
    // directly and never went through TotalWithinRange. Asking AtOrAbove(5) of a subset is what the
    // Mischievous/Scrapper Boost band needs, and is what surfaced it.
    [TestFixture]
    public class DiceResultsSubsetRangeTests
    {
        // A d6 with one die on each face. The second argument is the LOWEST face, not the side count.
        private static DiceResults OnePerFace() =>
            new DiceResults(new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1);

        [Test]
        public void ASubsetsRangeQueries_AreMeasuredInFaceValues_NotArrayOffsets()
        {
            IDiceResults saved = OnePerFace().SubsetAtOrAbove(4);   // faces 4, 5, 6

            Assert.That(saved.SideMin, Is.EqualTo(4));
            Assert.That(saved.SideMax, Is.EqualTo(6));
            Assert.That(saved.AtOrAbove(4), Is.EqualTo(3f).Within(0.001f), "faces 4, 5 and 6");
            Assert.That(saved.AtOrAbove(5), Is.EqualTo(2f).Within(0.001f), "faces 5 and 6 - the Boost band");
            Assert.That(saved.AtOrAbove(6), Is.EqualTo(1f).Within(0.001f), "face 6 - the base band");
        }

        [Test]
        public void ASubsetsRangeQueries_AgreeWithItsPerFaceCounts()
        {
            IDiceResults saved = OnePerFace().SubsetAtOrAbove(4);

            // The two read paths must not disagree: At() indexes directly, AtOrAbove() sums a range. The
            // old bug made them differ for exactly the case the reroll now asks about.
            Assert.That(saved.AtOrAbove(6), Is.EqualTo(saved.At(saved.SideMax)).Within(0.001f));
            Assert.That(saved.AtOrAbove(5),
                Is.EqualTo(saved.At(5) + saved.At(6)).Within(0.001f));
        }

        [Test]
        public void AFullDiesRangeQueries_AreUnchanged()
        {
            // The pre-existing behaviour the whole engine relies on: SideMin 1, where the old raw upper
            // bound happened to be right. This is the "did the fix move anything it shouldn't" guard.
            IDiceResults all = OnePerFace();

            Assert.That(all.AtOrAbove(1), Is.EqualTo(6f).Within(0.001f));
            Assert.That(all.AtOrAbove(4), Is.EqualTo(3f).Within(0.001f));
            Assert.That(all.AtOrAbove(6), Is.EqualTo(1f).Within(0.001f));
            Assert.That(all.Below(4), Is.EqualTo(3f).Within(0.001f), "faces 1, 2, 3");
            Assert.That(all.Range(2, 4), Is.EqualTo(3f).Within(0.001f), "faces 2, 3, 4");
        }
    }
}
