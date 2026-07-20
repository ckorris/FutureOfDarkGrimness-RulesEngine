using System.Collections.Generic;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #225 defect B — estimating a base for a unit OPR declares none for. OPR emits bases {"round":"none"}
// for vehicles/superheavies, and the importer used to fall through to a 28mm circle, putting every titan
// and tank on an infantry dot. These pin the estimate and, more importantly, the SHAPE of the decision:
// Hero -> circle (character/creature), otherwise rectangle (vehicle), sized by Tough.
[TestFixture]
public class DefaultBaseEstimatorTests
{
    private const float MmPerInch = 25.4f;

    private static List<SpecialRuleEntry> Rules(params SpecialRuleEntry[] rules) => new(rules);
    private static SpecialRuleEntry Core(string name) => new SpecialRuleEntry_Core(name);
    private static SpecialRuleEntry Tough(int n) => new SpecialRuleEntry_CoreNumeric("Tough", n);

    [Test]
    public void UnsizedDefault_IsRecognised_OnlyForACircle()
    {
        // The marker: a bare BaseFileEntry is the 28mm circle the importer used to fall through to.
        Assert.That(DefaultBaseEstimator.IsUnsizedDefault(new BaseFileEntry()), Is.True);

        // A real imported circle is not the default.
        Assert.That(DefaultBaseEstimator.IsUnsizedDefault(
            new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = 32f / MmPerInch }), Is.False);
    }

    [Test]
    public void UnsizedDefault_IgnoresARectanglesDeadDiameterField()
    {
        // Every Rectangle in the data also carries a leftover 28mm DiameterInches in its unused field.
        // Testing the diameter without gating on shape would flag correctly-sized rectangles as unsized.
        var rect = new BaseFileEntry
        {
            Shape = EBaseShapeKind.Rectangle,
            WidthInches = 92f / MmPerInch,
            HeightInches = 120f / MmPerInch,
            // DiameterInches left at its 28mm default, exactly as the real files carry it.
        };
        Assert.That(DefaultBaseEstimator.IsUnsizedDefault(rect), Is.False,
            "a sized rectangle must not be mistaken for an unsized default");
    }

    [Test]
    public void Hero_GetsACircle_NotAVehicleRectangle()
    {
        // All six Tough(3) units in the affected corpus are Hero+Unique named characters.
        BaseFileEntry b = DefaultBaseEstimator.Estimate(Rules(Core("Hero"), Core("Unique"), Tough(3)), out _);

        Assert.That(b.Shape, Is.EqualTo(EBaseShapeKind.Circle));
        Assert.That(b.DiameterInches, Is.EqualTo(40f / MmPerInch).Within(0.001f));
    }

    [Test]
    public void LargeHero_GetsABiggerCircle_StillNotARectangle()
    {
        // e.g. AlienHives "Drekhor the Vengeful" - Tough(12), Hero, Flying: a monster, not a tank.
        BaseFileEntry b = DefaultBaseEstimator.Estimate(
            Rules(Core("Hero"), Core("Flying"), Tough(12)), out _);

        Assert.That(b.Shape, Is.EqualTo(EBaseShapeKind.Circle));
        Assert.That(b.DiameterInches, Is.EqualTo(60f / MmPerInch).Within(0.001f));
    }

    [TestCase(6, 90f, 52f)]
    [TestCase(9, 105f, 70f)]
    [TestCase(12, 120f, 92f)]
    [TestCase(18, 160f, 122f)]
    [TestCase(24, 175f, 125f)]
    public void Vehicle_GetsARectangle_SizedByTough_LengthOnTheFacingAxis(int tough, float lengthMm, float widthMm)
    {
        BaseFileEntry b = DefaultBaseEstimator.Estimate(Rules(Tough(tough), Core("Impact")), out _);

        Assert.That(b.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(b.HeightInches, Is.EqualTo(lengthMm / MmPerInch).Within(0.001f),
            "length must land on the facing axis (#225 defect A)");
        Assert.That(b.WidthInches, Is.EqualTo(widthMm / MmPerInch).Within(0.001f));
    }

    [Test]
    public void EveryEstimatedRectangle_IsLongerThanItIsWide()
    {
        // The invariant defect A established: a real base is never wider than it is long along the facing.
        // An estimate must not reintroduce what the retrofit just corrected.
        foreach (int tough in new[] { 0, 6, 9, 12, 18, 24 })
        {
            BaseFileEntry b = DefaultBaseEstimator.Estimate(Rules(Tough(tough)), out _);
            if (b.Shape != EBaseShapeKind.Rectangle) continue;
            Assert.That(b.HeightInches, Is.GreaterThan(b.WidthInches), $"Tough({tough})");
        }
    }

    [Test]
    public void EstimateIsNeverTheUnsizedDefault_SoTheRetrofitConverges()
    {
        // If an estimate could itself look "unsized", re-running the retrofit would re-estimate forever.
        foreach (int tough in new[] { 0, 3, 6, 9, 12, 18, 24 })
        {
            Assert.That(DefaultBaseEstimator.IsUnsizedDefault(
                DefaultBaseEstimator.Estimate(Rules(Tough(tough)), out _)), Is.False, $"Tough({tough})");
            Assert.That(DefaultBaseEstimator.IsUnsizedDefault(
                DefaultBaseEstimator.Estimate(Rules(Core("Hero"), Tough(tough)), out _)), Is.False,
                $"Hero Tough({tough})");
        }
    }

    [Test]
    public void ToughReadThroughAnAlias_StillSizesTheBase()
    {
        // A faction that renames Tough must still size correctly - rule lookups are alias-aware.
        var aliased = new SpecialRuleEntry_Alias("Armour Plating", new SpecialRuleEntry_CoreNumeric("Tough", 18));
        BaseFileEntry b = DefaultBaseEstimator.Estimate(Rules(aliased), out _);

        Assert.That(b.HeightInches, Is.EqualTo(160f / MmPerInch).Within(0.001f));
    }

    [Test]
    public void NoRulesAtAll_StillProducesAUsableBase()
    {
        BaseFileEntry b = DefaultBaseEstimator.Estimate(Rules(), out string describe);

        Assert.That(b.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(b.HeightInches, Is.GreaterThan(0f));
        Assert.That(describe, Is.Not.Empty, "the caller needs something to report");
    }
}
