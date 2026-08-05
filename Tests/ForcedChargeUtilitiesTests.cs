using System.Collections.Generic;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #334 — the forced-charge band, extracted so the Choose Action gate and the movement preview share one
    // predicate. ChooseActionPassDisableTests still covers the gate end-to-end through GetCanPass; these pin
    // the hypothetical form the resolvers use, whose whole value is that it agrees with the gate.
    //
    // A 0.5"-radius circle base against another gives base-to-base gap = centre distance - 1.0", so an enemy
    // centred 1.5" away sits at a 0.5" gap (inside the 1" band) and one at 2.5" sits at 1.5" (clear).
    [TestFixture]
    public class ForcedChargeUtilitiesTests
    {
        private const float R = 0.5f;

        private static ForcedChargeUtilities.StandoffPose Circle(float x, float z, float y = 0f) =>
            new(new Position(x, y, z), new CircleBase(R), new Float2(0f, 1f));

        [Test]
        public void IsInsideStandoff_ExactlyTheStandoffDistance_IsClear()
        {
            // Strict '<', matching the gate: a model held at exactly 1.0" is NOT forced to charge. This is the
            // boundary the CLI auto-advance aims for, so a rounding change here would start forcing charges on
            // moves that deliberately stop short.
            Assert.That(ForcedChargeUtilities.IsInsideStandoff(
                GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES), Is.False);
            Assert.That(ForcedChargeUtilities.IsInsideStandoff(
                GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES - 0.01f), Is.True);
        }

        [Test]
        public void Gap_MeasuresBaseToBase_NotCentreToCentre()
        {
            float gap = ForcedChargeUtilities.Gap(Circle(0, 0), Circle(0, 2.5f));

            Assert.That(gap, Is.EqualTo(1.5f).Within(0.001f));
        }

        [Test]
        public void FindContacts_EnemyInsideTheBand_ReportsThePair()
        {
            var movers = new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 0) };
            var enemies = new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 1.5f) };

            var contacts = ForcedChargeUtilities.FindContacts(movers, enemies);

            Assert.That(contacts, Has.Count.EqualTo(1));
            Assert.That(contacts[0].MoverIndex, Is.EqualTo(0));
            Assert.That(contacts[0].EnemyIndex, Is.EqualTo(0));
            Assert.That(contacts[0].GapInches, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void FindContacts_EnemyChargeableButOutsideTheBand_ReportsNothing()
        {
            // 1.5" gap: within melee range (2") so Charge is offered, but NOT forced - the unit may still Pass.
            // The preview must not cry forced-charge here or it would contradict the gate on the commonest
            // approach there is.
            var contacts = ForcedChargeUtilities.FindContacts(
                new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 0) },
                new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 2.5f) });

            Assert.That(contacts, Is.Empty);
        }

        [Test]
        public void FindContacts_AllPairsInsideTheBand_AreReported()
        {
            // Both the panel tally ("2 models") and the table highlight (which enemies light up) read every
            // pair, not one per mover.
            var movers = new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 0), Circle(0.8f, 0) };
            var enemies = new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 1.2f), Circle(0.8f, 1.2f) };

            var contacts = ForcedChargeUtilities.FindContacts(movers, enemies);

            Assert.That(contacts, Has.Count.EqualTo(4));
        }

        [Test]
        public void FindContacts_VerticalSeparationClearsTheBand()
        {
            // The gate measures in 3D (includeVertical: true). Two models directly on top of one another
            // horizontally but 2" apart vertically are outside the band.
            var contacts = ForcedChargeUtilities.FindContacts(
                new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 0) },
                new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 1.2f, y: 2f) });

            Assert.That(contacts, Is.Empty);
        }

        [Test]
        public void FindContacts_OverlappingBases_AreInsideTheBand()
        {
            // A charge lands base-to-base (gap ~0) and a pile-in can leave models overlapping; both must read
            // as inside, not as a negative gap that slips past the comparison.
            var contacts = ForcedChargeUtilities.FindContacts(
                new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 0) },
                new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 0.2f) });

            Assert.That(contacts, Has.Count.EqualTo(1));
            Assert.That(contacts[0].GapInches, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void FindContacts_RectangularBase_MeasuresItsOrientedFootprint()
        {
            // #150: a 1"x2" base broadside-on reaches 1" from its centre along the long axis, so an enemy
            // circle centred 2.4" away sits at a 0.9" gap - inside. Turn the same base 90 degrees and only its
            // 0.5" half-width faces the enemy, opening the gap to 1.4" - clear. Measuring by a bounding circle
            // (or ignoring facing) would get one of these two wrong.
            var enemy = new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 2.4f) };
            var rect = new RectangleBase(1f, 2f);

            var lengthwise = new List<ForcedChargeUtilities.StandoffPose>
                { new(new Position(0, 0), rect, new Float2(0f, 1f)) };
            var broadside = new List<ForcedChargeUtilities.StandoffPose>
                { new(new Position(0, 0), rect, new Float2(1f, 0f)) };

            Assert.That(ForcedChargeUtilities.FindContacts(lengthwise, enemy), Has.Count.EqualTo(1));
            Assert.That(ForcedChargeUtilities.FindContacts(broadside, enemy), Is.Empty);
        }

        [Test]
        public void FindContacts_EmptyInputs_ReportNothing()
        {
            var one = new List<ForcedChargeUtilities.StandoffPose> { Circle(0, 0) };
            var none = new List<ForcedChargeUtilities.StandoffPose>();

            Assert.That(ForcedChargeUtilities.FindContacts(one, none), Is.Empty);
            Assert.That(ForcedChargeUtilities.FindContacts(none, one), Is.Empty);
        }
    }
}
