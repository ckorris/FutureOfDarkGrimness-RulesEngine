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

        // TUNED 2026-07-10 after the A4-2 gate collapse (mirror avg 23.75% - ledger entry): the
        // objective terms were flat bonuses (2.0-2.5) while damage terms are value-FRACTIONS
        // (~0.0-0.5), so every unit rushed objectives and nothing ever fought back. One scale now:
        // a flip is worth a strong exchange, not ten of them.
        public const float ActivationKillOpportunity = 1.0f;
        public const float ActivationObjectiveFlip = 0.75f;
        public const float ActivationUnderThreat = 0.75f;

        // --- Action + movement choice (A4-2) --------------------------------------------------------
        // score = MoveDamage * (value-weighted damage from the endpoint; melee margin for charges)
        //       - MoveRetaliation * (best value-weighted damage an enemy can put on the endpoint)
        //       + MoveObjective   * (objectives newly held minus held objectives abandoned)
        //       + MoveReachableBonus when the candidate fully reaches its goal.
        // Objectives dominate deliberately: they decide the winner (house invariant), and the
        // baseline showed tie-heavy games from objective-blind play.

        public const float MoveDamage = 1.0f;
        public const float MoveRetaliation = 0.6f;
        public const float MoveObjective = 0.75f;
        public const float MoveReachableBonus = 0.05f;

        // ADDED 2026-07-10 after the second gate failure (mirror avg 25.4% - ledger entry): melee
        // armies collapsed because a one-step score gives units outside charge reach no reason to
        // close (offense 0 beyond 12", retaliation punishes proximity). Approach = the melee
        // exchange margin-if-reached x the fraction of the charge gap this move closes; 0.75 keeps
        // a completed approach worth less than the actual charge (MoveDamage 1.0), so real charges
        // still dominate when reachable.
        public const float MoveApproach = 0.75f;

        // --- Casting (A5) ---------------------------------------------------------------------------
        // A cast is layered (it never ends the activation), so the planner casts whenever the net
        // expected value is positive: base 4+ success chance x the summed target values, minus a
        // small opportunity cost per token burned (the attempt spends them win or lose). Non-damage
        // effects (buffs, debuffs, forced moves) price a flat fraction of the target's value - the
        // documented A5 placeholder; anticipatory buff valuation arrives with Phase C's evaluator.
        public const float CastEffectStaticFraction = 0.2f;
        public const float CastTokenValue = 0.02f;
        // Assist (#103): one token shifts the caster's 4+ one face = 1/6 of the spell's value,
        // boosting a friend or denying an enemy alike (the solo bot always declines). Spend while
        // that beats the token cost, capped per request so one cast never drains a whole pool.
        public const int CastAssistMaxTokens = 2;

        // --- Target choice (A4-3) -------------------------------------------------------------------
        // Shooting/melee targets score by value-weighted damage; finishing a unit off is worth extra
        // (a dead unit stops acting; a wounded one does not).
        public const float ShootingKillBonus = 1.5f;
        public const float MeleeKillBonus = 1.5f;
    }
}
