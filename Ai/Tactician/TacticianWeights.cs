namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Every tunable scalar in the Tactician's greedy policy, in one place (#191 A4 - "weights are
    /// named constants in one file; tuning is benchmark-driven and recorded"). Change these only
    /// with a benchmark run attached to the commit.
    /// </summary>
    public static class TacticianWeights
    {
        // --- Activation-order urgency (A4-1) -------------------------------------------------------
        // score = KillOpportunity * (best value-weighted damage the unit can deal this activation)
        //       + ObjectiveFlip   * (it can reach and change an objective this activation)
        //       + UnderThreat     * (best value-weighted damage the enemy could deal IT)
        // Rationale: act with a unit before the opponent's next activation can remove it or its
        // opportunity; flips beat damage because objectives decide the winner.

        public const float ActivationKillOpportunity = 1.0f;
        public const float ActivationObjectiveFlip = 2.0f;
        public const float ActivationUnderThreat = 0.75f;
    }
}
