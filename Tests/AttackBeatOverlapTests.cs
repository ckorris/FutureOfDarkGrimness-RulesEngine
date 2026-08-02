using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Presentation;
using FDG.Presentation.Beats;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #238 — the attack animation plays WHILE the to-hit dice tumble, not before them. The mechanism
    /// is the held-beat seam: AttackBeat is Held with a ZERO lead-in, so presenting it costs the
    /// engine no pacing time and the DiceRolledBeat that always follows becomes active immediately.
    /// These pins guard both halves: the beat's declaration and the presenter honoring it.
    /// </summary>
    [TestFixture]
    public class AttackBeatOverlapTests
    {
        private static AttackBeat Beat(int volleys = 3) => new AttackBeat(isMelee: false,
            from: new List<Position> { new Position(0f, 0f) },
            to: new List<Position> { new Position(5f, 5f) },
            volleyCount: volleys, armorPenetration: 0);

        [Test]
        public void AttackBeat_IsHeld_WithZeroLeadIn()
        {
            AttackBeat beat = Beat();

            Assert.That(beat.Held, Is.True, "the attack must not block the dice beat behind it");
            Assert.That(beat.HoldLeadIn, Is.EqualTo(TimeSpan.Zero),
                "any lead-in would reintroduce a serial gap before the dice");
            Assert.That(beat.NominalDuration, Is.GreaterThan(TimeSpan.Zero),
                "the front-end still animates the attack over its real duration");
        }

        [Test]
        public async Task Presenter_PacesZero_ForTheAttack_AndFullDuration_ForTheDice()
        {
            var clock = new FakePresentationClock();
            var sink = new RecordingPresentationSink();
            var presenter = new LocalPresenter(sink, clock);

            AttackBeat attack = Beat();
            DiceRolledBeat dice = DiceRolledBeat.From(new DiceResults(new float[] { 0f, 0f, 1f, 1f, 1f, 1f }),
                successThreshold: 4, mode: ERandomnessType.Realistic, label: "Roll to Hit");

            await presenter.Present(attack);
            await presenter.Present(dice);

            Assert.That(sink.Beats, Is.EqualTo(new PresentationBeat[] { attack, dice }),
                "both beats still reach the sink, in order");
            Assert.That(clock.Waits[0], Is.EqualTo(TimeSpan.Zero),
                "the attack costs no pacing time - the dice start at once");
            Assert.That(clock.Waits[1], Is.EqualTo(dice.NominalDuration),
                "a roll is not held - it paces its whole envelope (see DiceRolledBeat.Held)");
            Assert.That(clock.Waits[1], Is.GreaterThan(attack.NominalDuration),
                "the dice envelope must outlast the attack animation, or the overlap would truncate it");
        }
    }
}
