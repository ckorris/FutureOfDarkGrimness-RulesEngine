using System;

namespace FDG.Stages
{
    /// <summary>
    /// #423 B1: ONE Parry dice budget per enemy melee attack. Core Rules v3.6.0 Parry(X) rolls "X dice per model with
    /// this rule in it, up to as many dice as hits taken"; OPR resolves all of a unit's melee attacks together, so the
    /// budget is fixed once - X x the living carriers when the attack's FIRST hits land (<see cref="Set"/>) - and then
    /// spent across every weapon profile the attacker swings, each swing taking min(remaining, its hits)
    /// (<see cref="Take"/>: the cap is a cap, not a choice - no withholding). A carrier that dies between two swings
    /// does not shrink a budget already set (#423 D4). Lives on <see cref="ICombatActionContext"/>, which already
    /// exists per attack: a strike-back builds a new context (a new attack, a fresh budget), and a Counter swap
    /// <see cref="Reset"/>s it because the parrying side changes. The charge's Impact hits resolve as their own
    /// hit-taking event with their own budget (#423 D3). The AI's CombatMath mirrors the same object.
    /// </summary>
    public sealed class ParryBudget
    {
        /// <summary>True once the attack's first hits have landed and the pool was sized.</summary>
        public bool IsSet { get; private set; }

        /// <summary>The whole pool for this attack (X summed over the carriers at the first hits).</summary>
        public int Total { get; private set; }

        /// <summary>Dice not yet rolled this attack (fractional under the probabilistic roller).</summary>
        public float Remaining { get; private set; }

        /// <summary>Dice rolled so far this attack.</summary>
        public float Spent => Total - Remaining;

        public void Set(int total)
        {
            IsSet = true;
            Total = Math.Max(0, total);
            Remaining = Total;
        }

        /// <summary>Takes the dice for one swing that landed <paramref name="hits"/> hits: min(remaining, hits).</summary>
        public float Take(float hits)
        {
            float dice = Math.Max(0f, Math.Min(Remaining, hits));
            Remaining -= dice;
            return dice;
        }

        /// <summary>Back to unsized - the next hits land on a different defender (a Counter role swap).</summary>
        public void Reset()
        {
            IsSet = false;
            Total = 0;
            Remaining = 0f;
        }
    }
}
