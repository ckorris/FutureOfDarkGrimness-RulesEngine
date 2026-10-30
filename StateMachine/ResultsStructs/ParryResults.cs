using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>One hit group Parry removed hits from: "Rending" (the rule that made the group worse) or "base".</summary>
    public readonly record struct ParriedGroup(string Name, float Count);

    /// <summary>
    /// #423: what the Parry step did to one hit group arrival (one swing's hits, or the charge's Impact hits), posted on
    /// the combat metadata by <c>ParryHitsStage</c> so the save stage can label its beats "(after Parry)".
    /// <see cref="None"/> when the defender has no Parry, the attack was not melee, nothing hit, or the attack's
    /// Parry budget was already spent - the save flow then reads exactly as before.
    /// </summary>
    public sealed record ParryResults(float DiceRolled, float Sixes, float HitsIgnored,
        IReadOnlyList<ParriedGroup> Ignored, string Summary)
    {
        public static readonly ParryResults None = new(0f, 0f, 0f, Array.Empty<ParriedGroup>(), string.Empty);

        /// <summary>True when at least one hit was removed, i.e. the saves that follow roll fewer dice than hits landed.</summary>
        public bool RemovedHits => HitsIgnored > 0f;
    }
}
