
namespace FDG.Stages
{
    /// <summary>
    /// Aggregates per-piece sight-line evaluations into a single result for a given
    /// (attacker, target) pair. The terrain pieces decide their own effect; this helper
    /// just combines them with priority Blocking > Cover > Clear.
    /// </summary>
    public static class LineOfSightUtilities
    {
        public static ESightLineEffect EvaluateSightLine(Position attacker, Position target,
            IEnumerable<ITerrain>? terrain)
        {
            if (terrain == null)
            {
                return ESightLineEffect.Clear;
            }

            ESightLineEffect worst = ESightLineEffect.Clear;

            foreach (ITerrain piece in terrain)
            {
                ESightLineEffect effect = piece.EvaluateSightLine(attacker, target);
                if (effect > worst)
                {
                    worst = effect;
                    if (worst == ESightLineEffect.Blocking)
                    {
                        //Can't get worse than this — early-out.
                        return worst;
                    }
                }
            }

            return worst;
        }

        /// <summary>
        /// Convenience wrapper for the binary "can attacker see target" check.
        /// Cover does not block sight; only Blocking does.
        /// </summary>
        public static bool HasLineOfSight(Position attacker, Position target,
            IEnumerable<ITerrain>? terrain)
            => EvaluateSightLine(attacker, target, terrain) != ESightLineEffect.Blocking;
    }
}
