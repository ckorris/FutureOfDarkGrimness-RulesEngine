using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// The single authority on whether a unit is being held off the table in reserve (Ambush).
    ///
    /// Reserve used to be inferred from "all of this unit's models sit at the world origin", a rule
    /// re-derived independently by the activation pool, two private <c>IsUnplaced</c> helpers, the
    /// renderer, the AI resolver, and the line-of-sight blocker builder. Nothing owned it, so a stray
    /// write to a reserve model's position silently promoted the unit to "deployed" - which is how a
    /// held-back unit became activatable on round 1 and was drawn in the table's bottom-left corner.
    ///
    /// The reserve state now lives on the unit as <see cref="TokenType.InReserve"/>. Stamped when the
    /// player chooses "Hold" at deployment; removed when the unit arrives.
    /// </summary>
    public static class ReserveRules
    {
        /// <summary> Whether this unit is held off-table for a later-round arrival. </summary>
        public static bool IsInReserve(IUnit unit) => unit.Tokens.HasToken(TokenType.InReserve);

        /// <summary> Hold this unit off-table. Called when the player declines to deploy it. </summary>
        public static void PlaceInReserve(IUnit unit)
        {
            if (IsInReserve(unit)) return;
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.InReserve));
        }

        /// <summary> The unit has arrived on the table; it is no longer in reserve. </summary>
        public static void ClearReserve(IUnit unit) => unit.Tokens.RemoveTokens(TokenType.InReserve);
    }
}
