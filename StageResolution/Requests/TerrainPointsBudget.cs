using FDG.SaveLoad;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #301 - snapshot of the requesting player's terrain-point state for one
    /// "Alternating: Points" placement request. Rides <see cref="PlaceOneTerrainRequest.PointsBudget"/>
    /// (null in every other placement mode). All affordability rules and their player-facing copy are
    /// composed HERE so the GUI picker, the CLI menu, the AI resolver and the stage's authoritative
    /// server-side check can never drift apart.
    /// </summary>
    public class TerrainPointsBudget
    {
        /// <summary>The player's whole pre-dealt share of the game's terrain points.</summary>
        public int AllotmentTotal { get; }

        /// <summary>Allotment minus everything spent so far (including debt overspends).</summary>
        public int AllotmentRemaining { get; }

        /// <summary>Points still to spend this turn. The turn ends when it reaches 0.</summary>
        public int TurnBudgetRemaining { get; }

        /// <summary>Points of this turn that debt from an earlier over-budget piece already consumed.</summary>
        public int DebtPaidThisTurn { get; }

        /// <summary>Pieces already placed this turn - only the FIRST placement of a turn may go over budget.</summary>
        public int PiecesPlacedThisTurn { get; }

        [JsonConstructor]
        public TerrainPointsBudget(int allotmentTotal, int allotmentRemaining, int turnBudgetRemaining,
            int debtPaidThisTurn, int piecesPlacedThisTurn)
        {
            AllotmentTotal = allotmentTotal;
            AllotmentRemaining = allotmentRemaining;
            TurnBudgetRemaining = turnBudgetRemaining;
            DebtPaidThisTurn = debtPaidThisTurn;
            PiecesPlacedThisTurn = piecesPlacedThisTurn;
        }

        /// <summary>A piece's cost, floored at 1 so a hand-authored 0/negative Points can never be placed for free.</summary>
        public static int CostOf(TerrainPieceEntry entry) => Math.Max(1, entry.Points);

        public static string Pts(int count) => count == 1 ? "1 point" : $"{count} points";

        public string PointsSummaryLine => $"Terrain points: {AllotmentRemaining} of {AllotmentTotal} left";

        public string TurnSummaryLine => $"This turn: {Pts(TurnBudgetRemaining)}";

        /// <summary>Yellow header notice when part of this turn went to debt; null otherwise.</summary>
        public string? DebtNoticeLine => DebtPaidThisTurn > 0
            ? $"{Pts(DebtPaidThisTurn)} spent on terrain last turn"
            : null;

        /// <summary>
        /// Whether a piece of the given cost may be placed right now, and with what consequence.
        /// The rules, in order: within the turn budget is always fine; nothing may exceed the
        /// player's remaining allotment; going over the turn budget (debt) is only open on the
        /// first placement of a turn that is not itself paying debt off.
        /// </summary>
        public TerrainPieceAffordability Evaluate(int cost)
        {
            if (cost <= TurnBudgetRemaining)
                return TerrainPieceAffordability.Allowed(debtIncurred: 0, warningText: null);

            if (cost > AllotmentRemaining)
                return TerrainPieceAffordability.Blocked(
                    $"Costs {Pts(cost)} - only {AllotmentRemaining} of your {AllotmentTotal} terrain points remain");

            if (DebtPaidThisTurn > 0)
                return TerrainPieceAffordability.Blocked(
                    $"Costs {Pts(cost)} - over this turn's {TurnBudgetRemaining} and you cannot take new debt while paying debt off");

            if (PiecesPlacedThisTurn > 0)
                return TerrainPieceAffordability.Blocked(
                    $"Costs {Pts(cost)} - over this turn's remaining {TurnBudgetRemaining}; a piece over budget must be placed first in the turn");

            int debt = cost - TurnBudgetRemaining;
            return TerrainPieceAffordability.Allowed(debt,
                $"Playing this piece will take {Pts(debt)} from your next turn");
        }
    }

    /// <summary>One piece's verdict from <see cref="TerrainPointsBudget.Evaluate"/>.</summary>
    public readonly struct TerrainPieceAffordability
    {
        public bool Playable { get; }

        /// <summary>Points this placement would borrow from the player's next turn(s). 0 when within budget.</summary>
        public int DebtIncurred { get; }

        /// <summary>Yellow warning to show on a playable piece that would incur debt; null otherwise.</summary>
        public string? WarningText { get; }

        /// <summary>Why the piece cannot be played right now; null when playable.</summary>
        public string? BlockedReason { get; }

        private TerrainPieceAffordability(bool playable, int debtIncurred, string? warningText, string? blockedReason)
        {
            Playable = playable;
            DebtIncurred = debtIncurred;
            WarningText = warningText;
            BlockedReason = blockedReason;
        }

        public static TerrainPieceAffordability Allowed(int debtIncurred, string? warningText) =>
            new TerrainPieceAffordability(true, debtIncurred, warningText, null);

        public static TerrainPieceAffordability Blocked(string reason) =>
            new TerrainPieceAffordability(false, 0, null, reason);
    }
}
