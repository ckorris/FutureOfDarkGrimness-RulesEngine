

using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{

    public interface ISingleRoundContext : IGameContextAccessor
    {
        public int RoundCount { get; }

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits { get; }

        public IReadOnlyList<ITeam> TeamActivateOrder { get; }

        public TeamPlayerAlternationCursor Cursor { get; }

        public int CurrentActivatingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam { get; }

        public IReadOnlyList<ITeam> CurrentRoundTeamFinishOrder { get; }

        public PlayerID GetCurrentPlayerID();

        public void MarkUnitAsActivated(DataBinding<UnitData> activatedUnit);

        public void CleanDeadUnitsFromUnactivated();

        public bool TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID);

        public bool DoesAnyTeamHaveRemainingActivations();

        public bool DoesTeamHaveRemainingActivations(ITeam team);

        public bool DoesPlayerHaveRemainingActivations(PlayerID playerID);
    }

    public class SingleRoundContext : ISingleRoundContext
    {
        public IGameContext GameContext { get; private set; }

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits => _unactivatedUnits;


        public Dictionary<PlayerID, List<DataBinding<UnitData>>> _unactivatedUnits { get; }

        public int RoundCount { get; private set; }

        public IReadOnlyList<ITeam> TeamActivateOrder => Cursor.TeamOrder;

        public TeamPlayerAlternationCursor Cursor { get; }

        public int CurrentActivatingTeamIndex
        {
            get => Cursor.CurrentTeamIndex;
            set => Cursor.CurrentTeamIndex = value;
        }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam => Cursor.CurrentPlayerIndexPerTeam;

        public IReadOnlyList<ITeam> CurrentRoundTeamFinishOrder => _currentRoundTeamFinishOrder;

        private List<ITeam> _currentRoundTeamFinishOrder = new List<ITeam>();

        public SingleRoundContext(IGameContext gameContext, List<ITeam> teamOrder, int roundCount = 0)
        {
            GameContext = gameContext;
            RoundCount = roundCount;
            Cursor = new TeamPlayerAlternationCursor(teamOrder);

            _unactivatedUnits = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();

            SetUnactivatedUnits();
        }

        public PlayerID GetCurrentPlayerID() => Cursor.GetCurrentPlayerID();

        public void MarkUnitAsActivated(DataBinding<UnitData> activatedUnit)
        {
            PlayerID playerID = activatedUnit.GetValue().PlayerID;

            if (_unactivatedUnits[playerID].Remove(activatedUnit) == false)
            {
                throw new ArgumentOutOfRangeException($"Unit not found as unactivated when marking activated: {activatedUnit.GetValue().Name}");
            }

            //If we've removed the last living unit from the list, mark that player as finished.
            if (_unactivatedUnits[playerID].Where(unit => unit.GetValue().GetIsAlive()).Count() == 0)
            {
                //Clean that player's list just in case there are dead units.
                _unactivatedUnits[playerID].Clear();

                //If that player's team is all done, mark the team as finished.
                ITeam playerTeam = GameContext.TableState.Teams.Objects.First(team => team.IsPlayerOnTeam(playerID));
                bool foundTeammateWithActivations = false;
                foreach (PlayerID teamPlayer in playerTeam.Players)
                {
                    if (teamPlayer != playerID && DoesPlayerHaveRemainingActivations(teamPlayer))
                    {
                        foundTeammateWithActivations = true;
                    }
                }

                if (foundTeammateWithActivations == false)
                {
                    _currentRoundTeamFinishOrder.Add(playerTeam);
                }
            }
        }

        public void CleanDeadUnitsFromUnactivated()
        {
            foreach(KeyValuePair<PlayerID, List<DataBinding<UnitData>>> kvp in _unactivatedUnits)
            {
                kvp.Value.RemoveAll(unit => unit.GetIsDead());
            }
        }

        public bool DoesAnyTeamHaveRemainingActivations()
        {
            foreach (ITeam team in GameContext.TableState.Teams.Objects)
            {
                if (DoesTeamHaveRemainingActivations(team))
                {
                    return true;
                }
            }

            return false;
        }

        public bool DoesTeamHaveRemainingActivations(ITeam team)
        {
            foreach (PlayerID playerID in team.Players)
            {
                if (DoesPlayerHaveRemainingActivations(playerID))
                {
                    return true;
                }
            }

            return false;
        }

        public bool DoesPlayerHaveRemainingActivations(PlayerID playerID)
        {
            return _unactivatedUnits[playerID].Where(unit => unit.GetValue().GetIsAlive()).Count() > 0;
        }

        private void SetUnactivatedUnits()
        {
            IEnumerable<ArmyData> armies = GameContext.GameDataStore().GetAllValues<ArmyData>();

            foreach (ITeam team in GameContext.TableState.Teams.Objects)
            {
                foreach (PlayerID playerID in team.Players)
                {
                    List<DataBinding<UnitData>> playerUnits = new List<DataBinding<UnitData>>();

                    foreach (ArmyData army in armies.Where(a => a.IsOwnedBy(playerID)))
                    {
                        // Reserve units (Ambush) that haven't arrived are alive but off-table — they
                        // don't activate until they're placed (from a later round's start).
                        playerUnits.AddRange(army.UnitBindings.Where(unit =>
                            unit.GetValue().GetIsAlive() && unit.GetValue().GetIsOnBattlefield()));
                    }
                    _unactivatedUnits[playerID] = playerUnits;
                }
            }
        }

        public bool TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID)
        {
            return Cursor.TryAdvance(
                teamHasRemainingWork: DoesTeamHaveRemainingActivations,
                playerHasRemainingWork: DoesPlayerHaveRemainingActivations,
                out nextTeam,
                out nextPlayerID);
        }
    }
}
