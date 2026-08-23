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
    }
}
