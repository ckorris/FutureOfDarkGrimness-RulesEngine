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

        // #393 - the heavy tier. "As large as the largest existing ones": the biggest built-in template
        // is the Collapsed wall at 11" on its long side, so a piece only counts as large here if its
        // longest side reaches within a couple of inches of that.
        private const float LargePieceMinDimensionInches = 9f;

        [Test]
        public void Palette_OffersSeveralLargeThreePointPieces()
        {
            var large = DefaultTerrainPool.GetPalette()
                .Where(p => p.Points >= 3)
                .Where(p => LongestSide(p) >= LargePieceMinDimensionInches)
                .ToList();

            Assert.That(large, Has.Count.GreaterThanOrEqualTo(10),
                "the reported gap: the 3-point tier was small enough to exhaust, so every game placed " +
                "one of each. #393 added ten large pieces to widen it.");
        }

        [Test]
        public void EveryFilterableType_HasALargePiece()
        {
            // #394 puts Impassible / Cover / Difficult / Dangerous filter buttons over the picker. A type
            // whose only pieces are small leaves its filtered list with no heavy option, which is exactly
            // the complaint #393 exists to fix.
            foreach (ETerrainType type in new[]
                     {
                         ETerrainType.Impassible, ETerrainType.Cover,
                         ETerrainType.Difficult, ETerrainType.Dangerous,
                     })
            {
                Assert.That(
                    DefaultTerrainPool.GetPalette().Any(
                        p => p.TerrainType.HasFlag(type)
                          && p.Points >= 3
                          && LongestSide(p) >= LargePieceMinDimensionInches),
                    Is.True,
                    $"no large 3-point piece carries {type} - its filtered list has no heavy option.");
            }
        }

        [Test]
        public void NoPalettePiece_OutgrowsTheSizeBand()
        {
            // Guard on the other side: "as large as the largest existing" is a size BAND, not a licence
            // to grow without limit. A piece far past the old maximum would dominate every table and
            // leave no room for the other placements.
            foreach (TerrainPieceEntry piece in DefaultTerrainPool.GetPalette())
            {
                Assert.That(LongestSide(piece), Is.LessThanOrEqualTo(12f),
                    $"'{piece.Name}' is bigger than any piece the palette has ever offered.");
            }
        }

        // The Cathedral shell's two doorway centres: west low, east high (see DefaultTerrainPool's
        // constants). Stated here rather than derived, so a change to the piece has to come past this test.
        private const float WestDoorCentreY = 2.25f;
        private const float EastDoorCentreY = 5.75f;

        [Test]
        public void CathedralShell_AdmitsTwoBasesAbreast_ThroughEachSideDoorway()
        {
            // The courtyard is only worth having if models can get into it. Both checks use the swept-disc
            // overload the MOVEMENT validator uses against terrain, so this is the real geometry, not a
            // coordinate restatement: two 28mm bases side by side, walked in from off-piece to the doorway
            // centre and on into the courtyard.
            TerrainPieceEntry shell = PalettePiece("Cathedral shell");
            (float lx, float hx, float ly, float hy) = shell.Shape.GetAABB();
            float radius = BaseShapeDefaults.CircleRadiusInches;

            foreach (float lane in new[] { -radius, radius })   // the two bases, shoulder to shoulder
            {
                Assert.That(
                    shell.Shape.DoesPathIntersectZone(
                        new Float2(lx - 1f, WestDoorCentreY + lane),
                        new Float2((lx + hx) * 0.5f, WestDoorCentreY + lane), radius),
                    Is.False, "the west doorway does not admit two 28mm bases abreast.");
                Assert.That(
                    shell.Shape.DoesPathIntersectZone(
                        new Float2(hx + 1f, EastDoorCentreY + lane),
                        new Float2((lx + hx) * 0.5f, EastDoorCentreY + lane), radius),
                    Is.False, "the east doorway does not admit two 28mm bases abreast.");
            }
        }

        [Test]
        public void CathedralShell_HasNoHorizontalSightLineThroughBothDoorways()
        {
            // Why the doors are staggered to opposite corners instead of centred: two doors facing each
            // other turn the piece from a sight blocker into a firing lane. A bare sight line (no base
            // inflation - this is what LoS traces) swept across the whole piece must be blocked at EVERY
            // height, which is only true while the two doorways' y ranges stay disjoint.
            TerrainPieceEntry shell = PalettePiece("Cathedral shell");
            (float lx, float hx, float ly, float hy) = shell.Shape.GetAABB();

            // Strictly INSIDE the footprint: y == ly and y == hy are tangent to the outer faces of the
            // south and north walls, and a boundary graze counts as a miss - a line that never enters the
            // piece is not a line through its doorways.
            int steps = (int)((hy - ly) * 20f);
            for (int step = 1; step < steps; step++)
            {
                float y = ly + step * 0.05f;
                Assert.That(
                    shell.Shape.DoesPathIntersectZone(new Float2(lx - 1f, y), new Float2(hx + 1f, y)),
                    Is.True,
                    $"a horizontal sight line at y = {y:0.00}\" passes clean through both doorways.");
            }
        }

        [Test]
        public void HabBlock_HasStreetsWideEnoughForTwoBasesAbreast()
        {
            // Same standard as the Cathedral doorways: the gaps between the towers are streets, not seams.
            TerrainPieceEntry hab = PalettePiece("Hab block");
            float radius = BaseShapeDefaults.CircleRadiusInches;

            foreach (float lane in new[] { -radius, radius })
            {
                // North-south street between the two southern towers.
                Assert.That(
                    hab.Shape.DoesPathIntersectZone(
                        new Float2(5.25f + lane, -1f), new Float2(5.25f + lane, 5f), radius),
                    Is.False, "the street between the southern towers is too narrow for two bases.");

                // East-west street between the south-west tower and the northern one.
                Assert.That(
                    hab.Shape.DoesPathIntersectZone(
                        new Float2(-1f, 5.25f + lane), new Float2(6f, 5.25f + lane), radius),
                    Is.False, "the street below the north tower is too narrow for two bases.");
            }
        }

        [Test]
        public void RockyRidge_StaysAContinuousBarrier_DespiteTheStagger()
        {
            // The stagger is cosmetic and must stay that way: a barrier with a hole in it is a worse piece
            // than a straight one, because the hole is invisible until a model walks through it. Sweep a
            // 28mm base straight across the ridge at every 0.1" of its width - every crossing must be blocked.
            TerrainPieceEntry ridge = PalettePiece("Rocky ridge");
            (float lx, float hx, float ly, float hy) = ridge.Shape.GetAABB();
            float radius = BaseShapeDefaults.CircleRadiusInches;

            for (int step = 0; step <= (int)((hx - lx) * 10f); step++)
            {
                float x = lx + step * 0.1f;
                Assert.That(
                    ridge.Shape.DoesPathIntersectZone(new Float2(x, ly - 1f), new Float2(x, hy + 1f), radius),
                    Is.True,
                    $"a 28mm base crosses the ridge at x = {x:0.0}\" - the stagger opened a hole in it.");
            }
        }

        [Test]
        public void RazorwireBelt_LeavesLanesAModelCanStandClearIn()
        {
            // The point of three bands rather than one slab: a model can halt between two bands instead of
            // being caught straddling wire. A lane only does that if a base fits with room to spare, so the
            // sweep runs a quarter-base either side of the lane centre rather than straight down it.
            TerrainPieceEntry wire = PalettePiece("Razorwire belt");
            (float lx, float hx, float ly, float hy) = wire.Shape.GetAABB();
            float radius = BaseShapeDefaults.CircleRadiusInches;
            const float StandingClearMargin = 0.25f;

            foreach (float laneCentre in new[] { 2f, 5f })
            {
                foreach (float wobble in new[] { -StandingClearMargin, StandingClearMargin })
                {
                    Assert.That(
                        wire.Shape.DoesPathIntersectZone(
                            new Float2(lx - 1f, laneCentre + wobble),
                            new Float2(hx + 1f, laneCentre + wobble), radius),
                        Is.False,
                        $"the lane at y = {laneCentre}\" is too tight for a 28mm base to sit clear of the wire.");
                }
            }
        }

        private static TerrainPieceEntry PalettePiece(string name)
        {
            TerrainPieceEntry? piece = DefaultTerrainPool.GetPalette().FirstOrDefault(p => p.Name == name);
            Assert.That(piece, Is.Not.Null, $"'{name}' is no longer in the palette.");
            return piece!;
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
            // #301: the picker lists cheap pieces first (stable within a tier) and offers each distinct
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
            // #301 Alternating: Points - every built-in piece carries an explicit cost (1-3 today; the
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
