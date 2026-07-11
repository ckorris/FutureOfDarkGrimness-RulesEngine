using FDG.Data;
using FDG.Rules.Dispatch;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Matchup-aware deployment (#191 A5-9, Chris's option 2): during alternating deployment the
    /// enemy's layout is progressively visible, so lanes are scored by unit-vs-unit fit (put the
    /// Deadly platform opposite their Tough units, the gauss line opposite the horde) and
    /// matchup-SENSITIVE units are held for late picks, when more of the table is known.
    /// Deliberately rough (Chris: "no need to make it mega perfect"): one nominal engagement
    /// range, best-weapon-set output only, army lists treated as open information for the
    /// sensitivity ordering (they are - both lists are known at launch).
    /// </summary>
    public static class DeploymentMatchup
    {
        /// <summary>Nominal range both sides are priced at - close enough that most guns and all
        /// melee count, far enough that short-range profiles don't dominate.</summary>
        public const float EngagementRangeInches = 12f;

        /// <summary>
        /// How good it is for <paramref name="us"/> to end up fighting <paramref name="enemy"/>:
        /// our per-activation value-out minus theirs, at the nominal engagement range. Positive
        /// means we win that exchange lane.
        /// </summary>
        public static float Favorability(RuleEvaluator evaluator,
            DataBinding<UnitData> us, DataBinding<UnitData> enemy)
        {
            return OutputValue(evaluator, us, enemy) - OutputValue(evaluator, enemy, us);
        }

        /// <summary>
        /// How much this unit's OFFENSIVE value swings with who it ends up across from: the
        /// spread of its output over the enemy's units. Generalists score low and deploy early;
        /// counters (anti-tank, anti-horde) score high and are held back until the enemy layout
        /// is visible. Deliberately output-only: the incoming side would mark every fragile
        /// generalist "sensitive" just because different enemies kill it differently.
        /// </summary>
        public static float Sensitivity(RuleEvaluator evaluator, ITableState tableState,
            DataBinding<UnitData> us)
        {
            float min = float.MaxValue, max = float.MinValue;
            int seen = 0;
            foreach (DataBinding<UnitData> enemy in EnemyBindings(tableState, us.GetValue().PlayerID))
            {
                float f = OutputValue(evaluator, us, enemy);
                if (f < min) min = f;
                if (f > max) max = f;
                seen++;
            }
            return seen < 2 ? 0f : max - min;
        }

        /// <summary>All living enemy units, deployed or not (list contents are open information;
        /// POSITIONS are only used by callers for units that are actually on the battlefield).</summary>
        public static IEnumerable<DataBinding<UnitData>> EnemyBindings(ITableState tableState, PlayerID us)
        {
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (army.PlayerID == us || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                    if (unit.GetValue().GetIsAlive())
                        yield return unit;
            }
        }

        // Best of shooting at the engagement range and the melee exchange, as a value fraction.
        private static float OutputValue(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            float best = ValueFraction(CombatMath.EstimateShooting(evaluator, attacker, defender,
                new AttackContext(EngagementRangeInches, AttackerMoved: true)).ExpectedWounds, defender.GetValue());
            if (attacker.GetValue().GetMeleeWeapons().Count > 0)
            {
                best = Math.Max(best, ValueFraction(
                    CombatMath.EstimateMelee(evaluator, attacker, defender).AttackerAttack.ExpectedWounds,
                    defender.GetValue()));
            }
            return best;
        }

        private static float ValueFraction(float expectedWounds, UnitData target)
        {
            float remaining = Math.Max(1f, target.RemainingWounds);
            return Math.Min(1f, expectedWounds / remaining) * TacticalAnalysis.UnitValue(target) / 100f;
        }
    }
}
