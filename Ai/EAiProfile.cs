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
    }
}
