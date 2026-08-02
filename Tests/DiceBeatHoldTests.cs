using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Presentation;
using FDG.Presentation.Beats;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #322 — a dice roll must be readable for longer than it blocks the game. Every
    /// <see cref="DiceRolledBeat"/> is HELD by default, so the presenter spends only the settle lead-in
    /// on it and the front-end keeps the panel up (on its stack) for the rest of the reading time.
    ///
    /// <para>These pins matter because holding rolls was tried once before and reverted (ea91d68, a #204
    /// follow-up): with a single front-end dice slot the next roll evicted the last. The stack removed
    /// that constraint; if someone flips the default back, a multi-threshold volley silently returns to
    /// ~1.8s of dead time per roll.</para>
    /// </summary>
    [TestFixture]
    public class DiceBeatHoldTests
    {
        private static IDiceResults Roll() => new DiceResults(new float[] { 0f, 0f, 1f, 1f, 1f, 1f });

        [Test]
        public void EveryConstructionPath_HoldsByDefault()
        {
            var direct = new DiceRolledBeat(new List<float> { 1f, 1f, 1f, 1f, 1f, 1f }, 1, 4,
                ERandomnessType.Realistic, "Roll to Hit");
            DiceRolledBeat from = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Roll to Hit");
            DiceRolledBeat decisive = DiceRolledBeat.FromDecisive(Roll(), 4, "Morale Test");

            Assert.That(direct.Held, Is.True);
            Assert.That(from.Held, Is.True);
            Assert.That(decisive.Held, Is.True, "#289 decisive rolls hold like any other");
        }

        [Test]
        public void HoldingIsOptOutable_ForARollThatShouldStopPlay()
        {
            DiceRolledBeat blocking = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic,
                "Roll to Hit", held: false);

            Assert.That(blocking.Held, Is.False, "the explicit opt-out still exists");
        }

        [Test]
        public void TheLeadIn_IsAFractionOfTheReadingTime()
        {
            DiceRolledBeat plain = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Roll to Hit");
            DiceRolledBeat chippy = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Roll to Hit",
                modifierTags: new[] { "Quality 4+", "Stealth -1" });

            Assert.That(plain.HoldLeadIn, Is.LessThan(plain.NominalDuration));
            Assert.That(chippy.HoldLeadIn, Is.LessThan(chippy.NominalDuration));
            Assert.That(chippy.HoldLeadIn, Is.GreaterThan(plain.HoldLeadIn),
                "#245: chips are extra reading, so they buy a longer settle before play resumes");
        }

        [Test]
        public async Task AMultiThresholdVolley_CostsTheLeadInPerRoll_NotTheFullDuration()
        {
            // The shape ea91d68 was fighting: two save thresholds back to back. Held, the pair costs two
            // lead-ins (~1.2s) instead of two full envelopes (~3.6s), and neither panel is cut short -
            // the front-end stacks them.
            var clock = new FakePresentationClock();
            var presenter = new LocalPresenter(new RecordingPresentationSink(), clock);

            DiceRolledBeat first = DiceRolledBeat.From(Roll(), 4, ERandomnessType.Realistic, "Rifle: 3 hits");
            DiceRolledBeat second = DiceRolledBeat.From(Roll(), 5, ERandomnessType.Realistic,
                "Rifle: 2 hits, Rending AP+1");

            await presenter.Present(first);
            await presenter.Present(second);

            Assert.That(clock.Waits, Is.EqualTo(new[] { first.HoldLeadIn, second.HoldLeadIn }),
                "both thresholds pace identically - the unevenness ea91d68 hit came from the front-end's "
                + "single dice slot, not from the pacing");
            Assert.That(clock.Waits[0] + clock.Waits[1],
                Is.LessThan(first.NominalDuration),
                "the whole pair now costs less than ONE roll used to");
        }
    }
}
