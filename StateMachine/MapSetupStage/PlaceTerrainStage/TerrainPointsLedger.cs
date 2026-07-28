using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #299 - the per-player accounting for "Alternating: Points" terrain placement. Deals the game's
    /// total terrain points into fixed per-player allotments up front, then tracks spend and debt as
    /// turns run. Pure state + arithmetic (no requests, no table access) so the rules are unit-testable
    /// without driving <see cref="PlaceTerrainStage"/>; the affordability rules themselves live on
    /// <see cref="TerrainPointsBudget"/>, which both this ledger and every resolver evaluate through.
    /// </summary>
    public class TerrainPointsLedger
    {
        private sealed class PlayerAccount
        {
            public int Allotment;
            public int Spent;
            public int Debt;
        }

        private readonly Dictionary<PlayerID, PlayerAccount> _accounts;

        public int PointsPerTurn { get; }

        public TerrainPointsLedger(IReadOnlyList<ITeam> teamOrder, int totalPoints, int pointsPerTurn)
        {
            PointsPerTurn = Math.Max(1, pointsPerTurn);

            _accounts = new Dictionary<PlayerID, PlayerAccount>();
            foreach (ITeam team in teamOrder)
                foreach (PlayerID player in team.Players)
                    _accounts[player] = new PlayerAccount();

            // Deal per-turn chunks in the exact order the placement loop will visit players (team
            // alternation, round-robin within team), so each player's total is fixed before the first
            // piece goes down: 20 total / 3 per turn across two players deals 3,3,3,3,3,3,2 - 11 to
            // whoever won the roll-off, 9 to the other.
            var cursor = new TeamPlayerAlternationCursor(teamOrder);
            int remaining = Math.Max(0, totalPoints);
            while (remaining > 0)
            {
                PlayerAccount account = _accounts[cursor.GetCurrentPlayerID()];
                int chunk = Math.Min(PointsPerTurn, remaining);
                account.Allotment += chunk;
                remaining -= chunk;
                cursor.TryAdvance(_ => true, _ => true, out _, out _);
            }
        }

        public int AllotmentOf(PlayerID player) => _accounts[player].Allotment;

        public int RemainingOf(PlayerID player)
        {
            PlayerAccount account = _accounts[player];
            return account.Allotment - account.Spent;
        }

        public int DebtOf(PlayerID player) => _accounts[player].Debt;

        public bool HasPointsRemaining(PlayerID player) => RemainingOf(player) > 0;

        public bool TeamHasPointsRemaining(ITeam team) => team.Players.Any(HasPointsRemaining);

        /// <summary>
        /// Opens a player's turn. Outstanding debt is repaid first - it consumes the front of the
        /// turn (up to the whole turn, never more), so deep debt from one huge piece can skip a turn
        /// outright and still bleed into the one after. The turn's spendable budget is what is left
        /// after debt, capped by the player's remaining allotment.
        /// </summary>
        public Turn BeginTurn(PlayerID player)
        {
            PlayerAccount account = _accounts[player];
            int debtPaid = Math.Min(account.Debt, PointsPerTurn);
            account.Debt -= debtPaid;
            int budget = Math.Min(PointsPerTurn - debtPaid, account.Allotment - account.Spent);
            return new Turn(this, player, debtPaid, budget);
        }

        /// <summary>
        /// Safety valve for a table so full nothing affordable fits: the player's unspent points are
        /// forfeited (and any debt written off) so the phase can end instead of re-prompting forever.
        /// </summary>
        public void ForfeitRemaining(PlayerID player)
        {
            PlayerAccount account = _accounts[player];
            account.Spent = account.Allotment;
            account.Debt = 0;
        }

        /// <summary>One player's live placement turn: the mutable budget the stage spends piece by piece.</summary>
        public class Turn
        {
            private readonly TerrainPointsLedger _ledger;

            public PlayerID Player { get; }

            public int DebtPaidThisTurn { get; }

            public int BudgetRemaining { get; private set; }

            public int PiecesPlaced { get; private set; }

            internal Turn(TerrainPointsLedger ledger, PlayerID player, int debtPaid, int budget)
            {
                _ledger = ledger;
                Player = player;
                DebtPaidThisTurn = debtPaid;
                BudgetRemaining = budget;
            }

            /// <summary>The immutable snapshot a <see cref="PlaceOneTerrainRequest"/> carries to the resolver.</summary>
            public TerrainPointsBudget Snapshot() => new TerrainPointsBudget(
                allotmentTotal: _ledger.AllotmentOf(Player),
                allotmentRemaining: _ledger.RemainingOf(Player),
                turnBudgetRemaining: BudgetRemaining,
                debtPaidThisTurn: DebtPaidThisTurn,
                piecesPlacedThisTurn: PiecesPlaced);

            public TerrainPieceAffordability Evaluate(int cost) => Snapshot().Evaluate(cost);

            public void RecordPlacement(int cost)
            {
                TerrainPieceAffordability verdict = Evaluate(cost);
                if (!verdict.Playable)
                    throw new InvalidOperationException(
                        $"Recorded a terrain placement the budget forbids: {verdict.BlockedReason}");

                PlayerAccount account = _ledger._accounts[Player];
                account.Spent += cost;
                account.Debt += verdict.DebtIncurred;
                BudgetRemaining = Math.Max(0, BudgetRemaining - cost);
                PiecesPlaced++;
            }
        }
    }
}
