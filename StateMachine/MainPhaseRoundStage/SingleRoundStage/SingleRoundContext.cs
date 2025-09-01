

using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{

    public interface ISingleRoundContext : IGameContextAccessor
    {
        /*
        public PlayerID? ActivatedPlayer { get; }

        public DataBinding<UnitData> ActivatedUnit { get; }

        public void SetActivatedPlayer(PlayerID playerID);

        public void ChooseUnitToActivate(DataBinding<UnitData> unitToActivate);
        */

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits { get; }

        public IReadOnlyList<ITeam> TeamActivateOrder { get; }

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

        public int RoundCount { get; private set; } = 0;

        public IReadOnlyList<ITeam> TeamActivateOrder => _teamActivateOrder;

        private List<ITeam> _teamActivateOrder;

        public int CurrentActivatingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam { get; }

        public IReadOnlyList<ITeam> CurrentRoundTeamFinishOrder => _currentRoundTeamFinishOrder;

        private List<ITeam> _currentRoundTeamFinishOrder = new List<ITeam>();

        public SingleRoundContext(IGameContext gameContext, List<ITeam> teamOrder)
        {
            GameContext = gameContext;

            GameContext = gameContext;
            _teamActivateOrder = teamOrder;

            _unactivatedUnits = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
            CurrentActivePlayerIndexPerTeam = new Dictionary<ITeam, int>();

            /*
            foreach (ITeam team in gameContext.TableState.Teams.Objects)
            {
                CurrentActivePlayerIndexPerTeam.Add(team, 0); //TODO: Same as elsewhere, this makes first player in each team kind of arbitrary.

                foreach (PlayerID playerID in team.Players)
                {
                    _unactivatedUnits.Add(playerID, new List<DataBinding<UnitData>>());
                }
            }
            */

            SetUnactivatedUnits();
        }

        public PlayerID GetCurrentPlayerID()
        {
            ITeam currentTeam = TeamActivateOrder[CurrentActivatingTeamIndex];
            int playerIndex = CurrentActivePlayerIndexPerTeam[currentTeam];
            PlayerID currentPlayerID = currentTeam.Players[playerIndex];

            return currentPlayerID;
        }

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
                //TODO: This will set the player order to somewhat arbitrary when there are multiple players per team,
                //but I'm not yet sure how I want to handle that.
                CurrentActivePlayerIndexPerTeam[team] = 0;

                foreach (PlayerID playerID in team.Players)
                {
                    List<DataBinding<UnitData>> playerUnits = new List<DataBinding<UnitData>>();

                    foreach (ArmyData army in armies.Where(a => a.IsOwnedBy(playerID)))
                    {
                        playerUnits.AddRange(army.UnitBindings.Where(unit => unit.GetValue().GetIsAlive()));
                    }
                    //_unactivatedUnits.Add(playerID, playerUnits);
                    _unactivatedUnits[playerID] = playerUnits;
                }
            }
        }

        public bool TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID)
        {
            int startingTeamIndex = CurrentActivatingTeamIndex;
            int teamCount = TeamActivateOrder.Count; //Shorthand.

            //Start with the logical next team. But we cache this one because if the loop brings us back to this one,
            //then everyone has gone and we're done.
            int firstTeamToCheck = (startingTeamIndex < teamCount - 1) ? startingTeamIndex + 1 : 0;

            int nextTeamIndex = firstTeamToCheck;

            while (true)
            {
                nextTeam = TeamActivateOrder[nextTeamIndex]; //Reuse for less allocation.
                if (DoesTeamHaveRemainingActivations(nextTeam))
                {
                    //We found a valid one. nextTeam is now the correct value.
                    CurrentActivatingTeamIndex = nextTeamIndex;
                    break;
                }

                nextTeamIndex = (nextTeamIndex < teamCount - 1) ? nextTeamIndex + 1 : 0;

                if (nextTeamIndex == firstTeamToCheck)
                {
                    //We've checked every team and there are none left, so we're done.
                    nextTeam = null;
                    nextPlayerID = null;
                    return false;
                }
            }

            //Find the next player within the team to deploy.
            //Often, likely usually, teams have one players, so we don't always need to do this.
            if (nextTeam.Players.Count() == 1)
            {
                nextPlayerID = nextTeam.Players[0];
                return true;
            }

            int playerCount = nextTeam.Players.Count(); //Shorthand.

            int startingPlayerIndex = CurrentActivePlayerIndexPerTeam[nextTeam];

            int firstPlayerToCheckIndex = (startingPlayerIndex < playerCount - 1) ? startingPlayerIndex + 1 : 0;
            int nextPlayerIndex = firstPlayerToCheckIndex;

            while (true)
            {
                nextPlayerID = nextTeam.Players[nextPlayerIndex];
                if (DoesPlayerHaveRemainingActivations(nextPlayerID.Value))
                {
                    CurrentActivePlayerIndexPerTeam[nextTeam] = nextPlayerIndex;
                    return true;
                }

                nextPlayerIndex = (nextPlayerIndex < playerCount - 1) ? nextPlayerIndex + 1 : 0;

                //To avoid an infinite loop in case there's a bug in the code to find if a team has valid deployments.
                if (nextPlayerIndex == firstPlayerToCheckIndex)
                {
                    throw new InvalidOperationException("Couldn't find a player within a team with deployments left, but " +
                        $"that team was listed as having deployments by {nameof(IDeploymentTurnContext.DoesTeamHaveRemainingDeployments)}.");
                }
            }
        }
    }
}