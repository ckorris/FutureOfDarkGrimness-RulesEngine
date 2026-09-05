using FDG.Ai.Tactician.Learning;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// B3 (#191 campaign step 7): a hand-weighted combination of the C1
    /// <see cref="PositionEncoder"/> per-side block, mapped to a win-probability-shaped value in
    /// [0, 1]. This is B's real leaf evaluator - C3 (step 14) replaces the weights with a trained net
    /// over the SAME v1 features (plus the global scalars the C1 vector already carries) and touches
    /// nothing else in the search: every input below is a v1 feature or derivable from one, so no
    /// schema bump and no regenerated dataset.
    /// <para>
    /// <b>Revised 2026-09-05 (step 10, the B-gate's failure analysis).</b> The first version used
    /// three features (held share, value share, threat coverage) at 0.55/0.30/0.15, and two things
    /// followed from that arithmetic, both measured, not inferred:
    /// (1) one marker out of three was worth 0.092 of value; killing 10% of the enemy army was worth
    /// 0.0078 - a 12:1 ratio, i.e. a marker outweighed wiping out MORE than the whole enemy army; and
    /// (2) held share is a step function at the 3" seizure radius, so between markers the objective
    /// term was flat and the whole leaf landscape sat within ~0.004 (root values 0.500-0.504 across
    /// charge / shoot / retreat / rush-at-a-marker in the charge-vs-shoot probe), and the search
    /// degenerated to prior noise where the one-ply policy still ranked the options correctly.
    /// That is the mechanism behind "no 2v2 lift" (more of the game spent away from live marker
    /// contests) and the modest, wildly uneven 1v1 margin.
    /// </para>
    /// <para>
    /// Three changes. <b>Material has real weight, and it decays as the game runs out:</b> material
    /// is instrumental - it wins the marker contests of the rounds still to come - so it starts at
    /// 0.55 and falls quadratically to 0 by the last activation of the last round, when only
    /// projected holdings are the score. The objective weight rises to fill it (0.35 -> 0.90); threat
    /// coverage stays a small constant. Early: one marker of three ~ killing 30% of the enemy (2.9:1);
    /// round 4: markers dominate; final activation: markers only. <b>The objective term has gradient
    /// between markers:</b> 0.70 projected-held + 0.20 contested (in range, not owned - "in the fight
    /// for it") + 0.10 approach (one minus the closest unit's normalized distance to any marker), so
    /// walking toward a marker is a slope rather than a cliff. <b>Game progress</b> is read the same
    /// way the encoder's round_frac/activation_frac are: (round - 1 + activated fraction) / total
    /// rounds, from <see cref="ITableState.Progress"/> and the ActivatedThisRound token.
    /// </para>
    /// <para>
    /// Two-side complementarity (sec 7.2) keeps <see cref="ObjectiveShareEvaluator"/>'s proven shape -
    /// 0.5 + (own raw - best other raw) / 2 - which sums to exactly 1 for two sides with no clamp
    /// ever engaging (raw in [0,1] => the difference is in [-1,1] => the halved, offset result is
    /// already in [0,1]). The shape invariant (a 1v1 board and its reduced 2v2 evaluate identically)
    /// holds because every term is per-side and the progress scalar is global.
    /// </para>
    /// </summary>
    public sealed class HandWeightedEvaluator : IPositionEvaluator
    {
        // Weights that do not move with the clock.
        private const float ThreatWeight = 0.10f;
        private const float MaterialWeightAtStart = 0.55f;

        // The objective term's internal split: holding beats contesting beats approaching.
        private const float HeldWeight = 0.70f;
        private const float ContestedWeight = 0.20f;
        private const float ApproachWeight = 0.10f;

        public SideValues Evaluate(ITableState state, RuleEvaluator evaluator, SideMap sides)
        {
            var membersBySide = new List<PlayerID>[sides.Count];
            for (int side = 0; side < sides.Count; side++) membersBySide[side] = new List<PlayerID>();
            foreach (PlayerID player in sides.Players) membersBySide[sides.SideOf(player)].Add(player);

            // Material weight decays quadratically with game progress: still 0.41 at the start of
            // round 3, 0.24 at the start of round 4, 0 at the very end. Objective takes the rest.
            float progress = GameProgress(state);
            float materialWeight = MaterialWeightAtStart * (1f - progress * progress);
            float objectiveWeight = 1f - ThreatWeight - materialWeight;

            var raw = new float[sides.Count];
            for (int side = 0; side < sides.Count; side++)
            {
                var opposing = new List<PlayerID>();
                for (int other = 0; other < sides.Count; other++)
                    if (other != side) opposing.AddRange(membersBySide[other]);

                float[] block = PositionEncoder.EncodeSideBlock(state, evaluator, membersBySide[side], opposing);
                float held = block[6];          // obj_held_share (projected owner, seizure radius)
                float contested = block[7];     // obj_contested_share (in range, not owned)
                float approach = 1f - block[9]; // 1 - min_obj_dist_norm (closest unit to any marker)
                float valueShare = block[1];    // value_share (living UnitValue share)
                float threatCoverage = block[11];

                float objective = HeldWeight * held + ContestedWeight * contested + ApproachWeight * approach;
                raw[side] = Math.Clamp(
                    objectiveWeight * objective + materialWeight * valueShare + ThreatWeight * threatCoverage,
                    0f, 1f);
            }

            var values = new SideValues(sides.Count);
            for (int side = 0; side < sides.Count; side++)
            {
                float bestOther = 0f;
                for (int other = 0; other < sides.Count; other++)
                    if (other != side) bestOther = Math.Max(bestOther, raw[other]);
                values[side] = 0.5f + (raw[side] - bestOther) / 2f;
            }
            return values;
        }

        /// <summary>
        /// Where in the game we are, in [0, 1]: (round - 1 + fraction of living units already
        /// activated this round) / total rounds. The encoder's round_frac and activation_frac say the
        /// same thing to C's net. A store with no progress record (a bare test board) reads as the
        /// start of round 1.
        /// </summary>
        internal static float GameProgress(ITableState state)
        {
            int totalRounds = Math.Max(1, state.Progress.TotalRounds);
            int round = Math.Clamp(state.Progress.RoundCount ?? 1, 1, totalRounds);

            int living = 0, activated = 0;
            foreach (IUnit unit in PositionEncoder.LivingUnits(state, null))
            {
                living++;
                if (unit.Tokens.HasToken(TokenType.ActivatedThisRound)) activated++;
            }
            float activationFrac = living == 0 ? 0f : (float)activated / living;

            return Math.Clamp((round - 1 + activationFrac) / totalRounds, 0f, 1f);
        }
    }
}
