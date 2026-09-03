using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;
using NUnit.Framework;

namespace FDG.Tests
{
    // #180: every concrete PresentationBeat must promise a sane pacing envelope (a negative
    // NominalDuration would desync the presenter/engine handshake described on the base class) and
    // must never throw out of its optional Text projection (front-ends and the CLI feed call it
    // unconditionally). One canonical instance per concrete type, table-driven so both invariants are
    // checked uniformly instead of per-type ad hoc; AllConcreteBeatTypes_AreCoveredByTheTable guards
    // against a newly added beat type silently going unchecked.
    [TestFixture]
    public class PresentationBeatInvariantTests
    {
        private static IEnumerable<TestCaseData> AllBeats()
        {
            yield return new TestCaseData(new AttackBeat(
                isMelee: false,
                from: new List<Position> { new Position(1f, 2f) },
                to: new List<Position> { new Position(3f, 4f) },
                volleyCount: 2, armorPenetration: 1)).SetName("AttackBeat");

            yield return new TestCaseData(
                new BannerBeat("Round 3", TextColor.White, EBannerTier.Headline)).SetName("BannerBeat_Headline");
            yield return new TestCaseData(
                new BannerBeat("Warriors embarked Rhino.", TextColor.White, EBannerTier.Toast)).SetName("BannerBeat_Toast");
            yield return new TestCaseData(
                new BannerBeat("Alice deploys first", TextColor.White, EBannerTier.Notice)).SetName("BannerBeat_Notice");

            yield return new TestCaseData(new DiceRolledBeat(
                new List<float> { 0f, 1f, 0f, 2f, 0f, 1f },
                sideMin: 1, successThreshold: 4, ERandomnessType.Realistic, "Roll to Hit")).SetName("DiceRolledBeat");

            yield return new TestCaseData(new ModelDiedBeat(
                new ModelID(Guid.NewGuid()), new UnitID(Guid.NewGuid()), "Heavy Gunners",
                new Position(5f, 6f))).SetName("ModelDiedBeat");

            yield return new TestCaseData(new ModelWoundedBeat(
                new ModelID(Guid.NewGuid()), new Position(4f, 7f))).SetName("ModelWoundedBeat");

            yield return new TestCaseData(new RollOffBeat("Map Side Roll-Off", new List<RollOffEntry>
            {
                new RollOffEntry("Team 1", 5, ERollOffResult.Won),
                new RollOffEntry("Team 2", 3, ERollOffResult.Lost),
            })).SetName("RollOffBeat");

            yield return new TestCaseData(new SaveBeat(
                new List<Position> { new Position(1f, 1f) }, savedCount: 2)).SetName("SaveBeat");

            yield return new TestCaseData(new SpellEffectBeat(ESpellVisual.TargetBoon,
                new List<Position> { new Position(1f, 1f) }, "Mend")).SetName("SpellEffectBeat");

            yield return new TestCaseData(new UnitMovedBeat(
                new UnitID(Guid.NewGuid()), "Warriors",
                new List<ModelMove>
                {
                    new ModelMove(new ModelID(Guid.NewGuid()),
                        new List<Position> { new Position(1f, 2f), new Position(3f, 4f) }),
                },
                PresentationDurations.UnitMove)).SetName("UnitMovedBeat");

            yield return new TestCaseData(new UnitRoutedBeat(
                new UnitID(Guid.NewGuid()), "Warriors",
                new List<RoutedModel> { new RoutedModel(new ModelID(Guid.NewGuid()), new Position(1f, 2f)) }
            )).SetName("UnitRoutedBeat");
        }

        [TestCaseSource(nameof(AllBeats))]
        public void NominalDuration_IsNeverNegative(PresentationBeat beat)
        {
            Assert.That(beat.NominalDuration, Is.GreaterThanOrEqualTo(TimeSpan.Zero),
                $"{beat.GetType().Name} must not promise a negative pacing envelope");
        }

        [TestCaseSource(nameof(AllBeats))]
        public void HoldLeadIn_IsNeverNegative(PresentationBeat beat)
        {
            Assert.That(beat.HoldLeadIn, Is.GreaterThanOrEqualTo(TimeSpan.Zero),
                $"{beat.GetType().Name} must not promise a negative hold lead-in");
        }

        [TestCaseSource(nameof(AllBeats))]
        public void Text_DoesNotThrow(PresentationBeat beat)
        {
            string? text = null;
            Assert.DoesNotThrow(() => text = beat.Text, $"{beat.GetType().Name}.Text must not throw");
            _ = text; // null is a valid projection (#180: "no text form"); only a throw is a failure.
        }

        // Guards the table itself: a new PresentationBeat subclass added anywhere in the engine
        // assembly must be added to AllBeats() above, or it silently skips both invariant checks.
        [Test]
        public void AllConcreteBeatTypes_AreCoveredByTheTable()
        {
            var covered = new HashSet<Type>(AllBeats().Select(tc => tc.Arguments[0]!.GetType()));

            // Scoped to the real beat namespace, not the whole assembly: NUnit/test-double types
            // (e.g. Tests.Doubles.TestBeat) currently compile into this same assembly (#068) and are
            // not production beats the front-end ever receives.
            List<Type> concreteBeatTypes = typeof(PresentationBeat).Assembly.GetTypes()
                .Where(t => typeof(PresentationBeat).IsAssignableFrom(t) && !t.IsAbstract
                            && t.Namespace == typeof(AttackBeat).Namespace)
                .ToList();

            List<Type> missing = concreteBeatTypes.Where(t => !covered.Contains(t)).ToList();
            Assert.That(missing, Is.Empty,
                "New PresentationBeat type(s) not covered by PresentationBeatInvariantTests.AllBeats(): "
                + string.Join(", ", missing.Select(t => t.Name)));
        }
    }
}
