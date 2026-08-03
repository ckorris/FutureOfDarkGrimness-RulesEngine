using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Presentation;
using FDG.Presentation.Beats;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #327 — a dice roll paces its FULL envelope. The gap between two rolls is the rhythm of the
    /// exchange, not dead time, so the engine still stops for each one; the "let it linger" half of #327
    /// is the front-end's dice stack, which keeps a panel on screen for seconds after its beat ends.
    ///
    /// <para>These pins exist because holding rolls has been tried twice and reverted twice (ea91d68,
    /// then #327 itself after playing it). If the default flips again, combat silently reads as rushed —
    /// a change nothing else in the suite would catch.</para>
    /// </summary>
    [TestFixture]
    public class DiceBeatHoldTests
    {
        private static IDiceResults Roll() => new DiceResults(new float[] { 0f, 0f, 1f, 1f, 1f, 1f });

        [Test]
        public void EveryConstructionPath_PacesInFull()
        {
            var direct = new DiceRolledBeat(new List<float> { 1f, 1f, 1f, 1f, 1f, 1f }, 1, 4,
                ERandomnessType.Realistic, "Roll to Hit");
            DiceRolledBeat from = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Roll to Hit");
            DiceRolledBeat decisive = DiceRolledBeat.FromDecisive(Roll(), 4, "Morale Test");

            Assert.That(direct.Held, Is.False);
            Assert.That(from.Held, Is.False);
            Assert.That(decisive.Held, Is.False, "#289 decisive rolls pace like any other");
        }

        [Test]
        public void HoldingRemainsAvailable_ForARollThatShouldNotStopPlay()
        {
            DiceRolledBeat nonBlocking = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic,
                "Roll to Hit", held: true);

            Assert.That(nonBlocking.Held, Is.True, "the opt-in still exists; nothing uses it today");
            Assert.That(nonBlocking.HoldLeadIn, Is.LessThan(nonBlocking.NominalDuration),
                "and it would cost only the settle");
        }

        [Test]
        public async Task AMultiThresholdVolley_PacesEachRollIdentically()
        {
            // The shape ea91d68 was fighting: two save thresholds back to back. Both get the same full
            // envelope, so neither flicks past — and #327's stack means the first panel is still on
            // screen underneath when the second arrives.
            var clock = new FakePresentationClock();
            var presenter = new LocalPresenter(new RecordingPresentationSink(), clock);

            DiceRolledBeat first = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Rifle: 3 hits");
            DiceRolledBeat second = DiceRolledBeat.From(Roll(), 5, ERandomnessType.Realistic,
                "Rifle: 2 hits, Rending AP+1");

            await presenter.Present(first);
            await presenter.Present(second);

            Assert.That(clock.Waits, Is.EqualTo(new[] { first.NominalDuration, second.NominalDuration }),
                "every threshold reads at the same tempo");
        }

        [Test]
        public void InfoChips_StretchTheEnvelope_SoThereIsTimeToReadThem()
        {
            DiceRolledBeat plain = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Roll to Hit");
            DiceRolledBeat chippy = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Roll to Hit",
                modifierTags: new[] { "Quality 4+", "Stealth -1" });

            Assert.That(chippy.NominalDuration, Is.GreaterThan(plain.NominalDuration));
        }
    }
}
