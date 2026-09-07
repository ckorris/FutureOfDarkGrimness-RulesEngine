namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Configuration for one Tactician-driven player (#191, docs/ai-agent-plan.md). Deliberately
    /// minimal: fields land with the slice that consumes them (position evaluator and scoring
    /// weights in Phase A4+, search time budgets in Phase B) rather than speculatively.
    /// </summary>
    public sealed record TacticianOptions
    {
        /// <summary>
        /// The GAME's seed (GameSettings.DiceSeed) — the Tactician derives its own stream from it
        /// by slot, like every AI (#193). Null = unseeded.
        /// </summary>
        public int? Seed { get; init; }

        /// <summary>The player's slot index. Stable across runs and save/resume, unlike the PlayerID GUID.</summary>
        public int SlotID { get; init; }

        /// <summary>
        /// Analysis sink (#191 tooling): receives one block per Choose Action - the winner plus
        /// the full scored candidate table. Null (the default) in normal play.
        /// </summary>
        public Action<string>? DecisionLog { get; init; }

        /// <summary>
        /// The game's #384 see-through-allies house rule (<see cref="GameSettings.SeeThroughFriendlyUnits"/>).
        /// False (the default game setting, official rules) makes the planner count other friendly
        /// units' bases as sight blockers when scoring endpoints and aiming clear-lane goals.
        /// </summary>
        public bool SeeThroughFriendlyUnits { get; init; }

        /// <summary>
        /// B5 (#191 campaign step 9): when set, each activation is chosen by a UCT search under
        /// this budget instead of the A policy's urgency argmax - the Strategist rung. Null (the
        /// default) is plain A, and stays plain A: this is the ONE switch between the two, so both
        /// rungs remain benchmarkable against each other forever (plan G4).
        /// </summary>
        public Search.UctOptions? Search { get; init; }

        /// <summary>
        /// C3 (#191 campaign step 14): the leaf evaluator the search uses. Null (the default) is
        /// <see cref="Search.HandWeightedEvaluator"/>, the hand-weighted evaluator the B gate was
        /// measured on - so an unpromoted learned evaluator can never become the default by
        /// accident (G9), and both remain selectable forever once one is promoted (G4). Ignored
        /// when <see cref="Search"/> is null: plain A has no leaf to evaluate.
        /// </summary>
        public Search.IPositionEvaluator? Evaluator { get; init; }
    }
}
