namespace FDG.Ai
{
    /// <summary>
    /// Which AI drives a player slot (#191, docs/ai-agent-plan.md). Every rung of the Tactician
    /// ladder stays selectable forever so any rung can be benchmarked against any other (plan G4);
    /// the solo-rules bot is the permanent baseline and is never modified (plan D1).
    /// </summary>
    public enum EAiProfile
    {
        SoloRules,
        Tactician,
        /// <summary>
        /// Scripted human stand-in (#191 tooling): holds a defensive line and shoots, moving only
        /// to claim enemy-free objectives. Measurement apparatus for behaviors the advancing solo
        /// bot never elicits (melee armies crossing a held firing line), not a ladder rung.
        /// </summary>
        Gunline,
        /// <summary>
        /// B rung (#191 campaign step 9): the Tactician's policy with a UCT search over macro-actions
        /// deciding each activation. Everything below the activation - movement geometry, targets,
        /// wound assignment - is still the A policy playing out the search's prescription, and any
        /// search failure degrades to plain Tactician (plan G3). Lobby name: "Strategist Bot".
        /// </summary>
        Strategist,
    }
}
