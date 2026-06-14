using NUnit.Framework;

namespace FDG.Tests
{
    // #090 — Decisive rolls. The probabilistic roller spreads a roll across faces as an expected value,
    // which is correct for aggregate rolls (many attacks) but ruinous for a single binary roll: a morale
    // die read as "0.5 of a success" can never clear the >= 1 pass bar, so every meaningful morale test
    // auto-fails. RollDecisive resolves one concrete face under any roller, restoring a real pass/fail.
    [TestFixture]
    public class DecisiveRollTests
    {
        private const int MoraleNeeded = 4; // Quality 4+

        [Test]
        public void Probabilistic_PlainRoll_SpreadsAndWouldAutoFailMorale()
        {
            // Documents the bug RollDecisive fixes: a plain single Roll under the probabilistic roller is
            // an expected value (0.5 at 4+), which never satisfies the ">= 1 success" morale pass test.
            var roller = new ProbabilisticDiceRoller();

            float successes = roller.Roll(1).AtOrAbove(MoraleNeeded);

            Assert.That(successes, Is.EqualTo(0.5f).Within(0.0001f), "expected value, not a real outcome.");
            Assert.That(successes >= 1f, Is.False, "so the morale pass test would always fail under a plain Roll.");
        }

        [Test]
        public void Probabilistic_RollDecisive_IsOneConcreteFace()
        {
            var roller = new ProbabilisticDiceRoller();

            for (int i = 0; i < 50; i++)
            {
                IDiceResults roll = roller.RollDecisive();
                Assert.That(roll.TotalRolls, Is.EqualTo(1f), "exactly one die came up.");
                Assert.That(roll.AtOrAbove(1), Is.EqualTo(1f), "and it landed on a real face (1..6).");
            }
        }

        [Test]
        public void Probabilistic_RollDecisive_GivesRealBinaryMorale_NotAutoFail()
        {
            // The regression guard: across many decisive rolls a Quality 4+ morale test both passes and
            // fails — proving it is a genuine binary outcome, not the old always-fail. With p=0.5 over 200
            // draws, "all same" has probability ~2^-200, so the bounds never flake in practice.
            var roller = new ProbabilisticDiceRoller();
            const int draws = 200;
            int passes = 0;

            for (int i = 0; i < draws; i++)
            {
                if (roller.RollDecisive().AtOrAbove(MoraleNeeded) >= 1f) passes++;
            }

            Assert.That(passes, Is.GreaterThan(0), "morale must not auto-fail under the probabilistic roller.");
            Assert.That(passes, Is.LessThan(draws), "nor auto-pass.");
            Assert.That(passes, Is.InRange(40, 160), "and should sit near the true 50% rate for 4+.");
        }

        [Test]
        public void Realistic_RollDecisive_IsOneConcreteFace()
        {
            var roller = new RealisticDiceRoller();

            for (int i = 0; i < 50; i++)
            {
                IDiceResults roll = roller.RollDecisive();
                Assert.That(roll.TotalRolls, Is.EqualTo(1f));
                Assert.That(roll.AtOrAbove(1), Is.EqualTo(1f));
            }
        }

        [Test]
        public void Default_RollDecisive_FollowsRoll_ForNonProbabilisticRollers()
        {
            // FixedDiceRoller does not override RollDecisive, so it rides the default (a single Roll). This
            // is what keeps tests deterministic: a fixed face drives morale exactly as a real die would.
            Assert.That(new FixedDiceRoller(5).RollDecisive().AtOrAbove(MoraleNeeded) >= 1f, Is.True,
                "a fixed 5 passes a 4+ test.");
            Assert.That(new FixedDiceRoller(3).RollDecisive().AtOrAbove(MoraleNeeded) >= 1f, Is.False,
                "a fixed 3 fails a 4+ test.");
        }
    }
}
