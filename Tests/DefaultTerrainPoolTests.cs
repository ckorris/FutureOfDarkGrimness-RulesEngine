using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #268 — the built-in terrain split into an AUTO LAYOUT (placed verbatim by AutoFromLayout) and a
    // TEMPLATE PALETTE (what the Alternating-mode picker offers). The palette gained small impassible
    // objects; the auto layout deliberately did not change, so generated maps play the same.
    [TestFixture]
    public class DefaultTerrainPoolTests
    {
        private const float TableW = 72f;
        private const float TableH = 48f;

        // Anything whose longest side is under this counts as a "small" piece for the purposes of the
        // complaint that prompted this (every built-in impassible was a 6-11" compound).
        private const float SmallPieceMaxDimensionInches = 5f;

        [Test]
        public void AutoLayout_IsUnchangedBy_ThePaletteExpansion()
        {
            Assert.That(DefaultTerrainPool.Get().Pieces, Has.Count.EqualTo(12),
                "AutoFromLayout places these verbatim - adding picker options must not make generated " +
                "maps denser. Update this number only when the auto MAP is meant to change.");
        }

        [Test]
        public void Palette_ContainsEveryAutoLayoutPiece()
        {
            IReadOnlyList<TerrainPieceEntry> palette = DefaultTerrainPool.GetPalette();

            foreach (TerrainPieceEntry piece in DefaultTerrainPool.Get().Pieces)
            {
                Assert.That(palette, Has.Some.SameAs(piece).Or.Some.Matches<TerrainPieceEntry>(
                        p => p.Name == piece.Name && p.TerrainType == piece.TerrainType),
                    $"'{piece.Name}' is in the auto layout but not offered in the picker.");
            }

            Assert.That(palette.Count, Is.GreaterThan(DefaultTerrainPool.Get().Pieces.Count),
                "the palette is a superset - it exists to offer MORE than the auto layout.");
        }

        [Test]
        public void Palette_OffersSeveralSmallImpassibleObjects()
        {
            var smallImpassible = DefaultTerrainPool.GetPalette()
                .Where(p => p.TerrainType.HasFlag(ETerrainType.Impassible))
                .Where(p => LongestSide(p) <= SmallPieceMaxDimensionInches)
                .ToList();

            Assert.That(smallImpassible, Has.Count.GreaterThanOrEqualTo(6),
                "the reported gap: every built-in impassible piece was a 6-11\" compound, so there was " +
                "nothing small to break up a firing lane with.");
        }

        [Test]
        public void Palette_OffersImpassibleTerrainThatDoesNotBlockLineOfSight()
        {
            // Impassible without Blocking = go around it, shoot over it. The built-in set had none:
            // every impassible piece was also Blocking.
            var goAroundShootOver = DefaultTerrainPool.GetPalette()
                .Where(p => p.TerrainType.HasFlag(ETerrainType.Impassible)
                         && !p.TerrainType.HasFlag(ETerrainType.Blocking))
                .ToList();

            Assert.That(goAroundShootOver, Is.Not.Empty);
        }

        [Test]
        public void EveryPalettePiece_FitsOnTheTable()
        {
            // A template larger than the table could never be placed - the validator would reject every
            // position, and the picker would offer a dead row.
            foreach (TerrainPieceEntry piece in DefaultTerrainPool.GetPalette())
            {
                (float lx, float hx, float ly, float hy) = piece.Shape.GetAABB();
                Assert.That(hx - lx, Is.LessThan(TableW), $"'{piece.Name}' is wider than the table.");
                Assert.That(hy - ly, Is.LessThan(TableH), $"'{piece.Name}' is taller than the table.");
            }
        }

        [Test]
        public void EveryPalettePiece_HasAnAsciiName_AndARealTerrainType()
        {
            foreach (TerrainPieceEntry piece in DefaultTerrainPool.GetPalette())
            {
                Assert.That(piece.Name, Is.Not.Null.And.Not.Empty,
                    "the picker leads with the name; an unnamed piece reads as a bare type + size.");
                // The ImGui font atlas bakes Basic Latin + Latin-1 only (CLAUDE.md), so anything above
                // U+00FF renders as '?' in game.
                Assert.That(piece.Name.All(c => c <= 'ÿ'), Is.True,
                    $"'{piece.Name}' has a non-Latin-1 character and would render as '?'.");
                Assert.That(piece.TerrainType, Is.Not.EqualTo(ETerrainType.None),
                    $"'{piece.Name}' would have no gameplay effect at all.");
            }
        }

        [Test]
        public void NoPalettePiece_UsesTheDeadElevatedFlag()
        {
            // ETerrainType.Elevated is declared but no engine code reads it. A piece carrying it would
            // look meaningful in the picker and do nothing. Delete this test when Elevated is implemented.
            Assert.That(DefaultTerrainPool.GetPalette().Any(p => p.TerrainType.HasFlag(ETerrainType.Elevated)),
                Is.False);
        }

        [Test]
        public void Palette_IsSortedByCost_AndOffersEachTemplateOnce()
        {
            // #299: the picker lists cheap pieces first (stable within a tier) and offers each distinct
            // template once - the auto layout's two Forests / two Sandbag lines differ only by their
            // baked-in positions, which mean nothing for a template the player positions on click.
            IReadOnlyList<TerrainPieceEntry> palette = DefaultTerrainPool.GetPalette();

            for (int i = 1; i < palette.Count; i++)
            {
                Assert.That(palette[i].Points, Is.GreaterThanOrEqualTo(palette[i - 1].Points),
                    $"'{palette[i].Name}' is listed out of cost order.");
            }

            var keys = palette.Select(p =>
            {
                (float lx, float hx, float ly, float hy) = p.Shape.GetAABB();
                return (p.Name, p.TerrainType, System.MathF.Round(hx - lx, 2), System.MathF.Round(hy - ly, 2));
            }).ToList();
            Assert.That(keys, Is.Unique, "the picker must not offer the same template twice.");
        }

        [Test]
        public void EveryPalettePiece_HasAPositivePointCost()
        {
            // #299 Alternating: Points - every built-in piece carries an explicit cost (1-3 today; the
            // exact values are a balance knob, so only the floor is pinned). A 0 would fall back to
            // TerrainPointsBudget.CostOf's floor of 1, but the data should say what it means.
            foreach (TerrainPieceEntry piece in DefaultTerrainPool.GetPalette())
            {
                Assert.That(piece.Points, Is.GreaterThanOrEqualTo(1),
                    $"'{piece.Name}' has no point cost for Alternating: Points mode.");
            }
        }

        [Test]
        public void EveryPalettePiece_PlacesLegally_ThroughTheRealPlacementPath()
        {
            // The end-to-end check that matters: rotate + translate-to-centre exactly as PlaceTerrainStage
            // does, then run the actual validator. A template that can't clear this would show up in the
            // picker as a row that rejects every click. Rotations included because the picker offers them
            // and a rotated rectangle's footprint grows.
            foreach (TerrainPieceEntry piece in DefaultTerrainPool.GetPalette())
            {
                foreach (float degrees in new[] { 0f, 45f, 90f })
                {
                    IZone rotated = TerrainTemplateUtilities.Rotate(piece.Shape, degrees);
                    IZone placed = TerrainTemplateUtilities.TranslateToCenter(
                        rotated, new Float2(TableW * 0.5f, TableH * 0.5f));

                    Assert.That(
                        TerrainPlacementValidator.Check(placed, TableW, TableH, System.Array.Empty<ITerrain>()),
                        Is.EqualTo(TerrainPlacementValidity.Valid),
                        $"'{piece.Name}' rotated {degrees} deg cannot be placed at the table centre.");
                }
            }
        }

        [Test]
        public void EveryPalettePiece_HasNonNegativeHeight()
        {
            foreach (TerrainPieceEntry piece in DefaultTerrainPool.GetPalette())
            {
                Assert.That(piece.HeightInches, Is.GreaterThanOrEqualTo(0f), $"'{piece.Name}'");
            }
        }

        private static float LongestSide(TerrainPieceEntry piece)
        {
            (float lx, float hx, float ly, float hy) = piece.Shape.GetAABB();
            return System.MathF.Max(hx - lx, hy - ly);
        }
    }
}
