
using FDG.Data;

namespace FDG.Stages
{
    public interface IMainPhaseContext : IGameContextAccessor
    {
        public int RoundCount { get; }

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits { get; }

        public IReadOnlyList<ITeam> TeamActivateOrder { get; }

        public int CurrentActivatingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam { get; }

        public PlayerID GetCurrentPlayerID();

        public void MarkUnitAsActivated(DataBinding<UnitData> activatedUnit);

        public bool DoesTeamHaveRemainingActivations(ITeam team);

        public bool DoesPlayerHaveRemainingActivations(PlayerID playerID);

        public void NewRound();
    }

    public class MainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; private set; }

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits => _unactivatedUnits;


        public Dictionary<PlayerID, List<DataBinding<UnitData>>> _unactivatedUnits { get; }

        public int RoundCount { get; private set; } = 1;

        public IReadOnlyList<ITeam> TeamActivateOrder => _teamActivateOrder;

        private List<ITeam> _teamActivateOrder;

        public int CurrentActivatingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam { get; }


        private List<PlayerID> _currentRoundPlayerFinishOrder = new List<PlayerID>();

        public MainPhaseContext(IGameContext gameContext, List<ITeam> firstDeploymentRollOrder)
        {
            GameContext = gameContext;
            _teamActivateOrder = firstDeploymentRollOrder;

            //For the first round, we get the team start order from deployment. 
            //For subsequent rounds, that should be reset in NewRound().

            _unactivatedUnits = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
            CurrentActivePlayerIndexPerTeam = new Dictionary<ITeam, int>();

            foreach (ITeam team in gameContext.TableState.Teams.Objects)
            {
                CurrentActivePlayerIndexPerTeam.Add(team, 0); //TODO: Same as elsewhere, this makes first player in each team kind of arbitrary.

                foreach(PlayerID playerID in team.Players)
                {
                    _unactivatedUnits.Add(playerID, new List<DataBinding<UnitData>>());
                }
            }

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
                _currentRoundPlayerFinishOrder.Add(playerID);
            }
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

        public void NewRound()
        {
            List<ArmyData> armies = GameContext.GameDataStore().GetAllValues<ArmyData>().ToList();

            //TODO: I anticipate this happening if units were killed before activating.
            if (_unactivatedUnits.Count > 0)
            {
                throw new InvalidOperationException($"Expected {nameof(_unactivatedUnits)} to be empty when calling {nameof(NewRound)}." +
                    $"Instead it had {_unactivatedUnits.Count} entry/entries.");
            }

            foreach (ITeam team in GameContext.TableState.Teams.Objects)
            {

                foreach (PlayerID playerID in team.Players)
                {
                    if (_currentRoundPlayerFinishOrder.Contains(playerID) == false)
                    {
                        throw new InvalidOperationException($"Couldn't find playerID {playerID} in finished players during call to {nameof(NewRound)}. " +
                            $"Total players listed as finished: {_currentRoundPlayerFinishOrder.Count}");
                    }
                }
            }

            SetUnactivatedUnits();

            //Get the order of finished teams.
            IEnumerable<ITeam> allTeams = GameContext.TableState.Teams.Objects;
            int teamCount = allTeams.Count();
            int playerCount = _currentRoundPlayerFinishOrder.Count();

            ITeam[] teamOrder = new ITeam[GameContext.TableState.Teams.Objects.Count()];

            HashSet<ITeam> finishedTeams = new HashSet<ITeam>(GameContext.TableState.Teams.Objects);

            int finishedTeamIndex = teamCount - 1;
            for (int pc = playerCount - 1; pc > 0; pc--)
            {
                PlayerID finishedPlayer = _currentRoundPlayerFinishOrder[pc];

                ITeam finishedPlayerTeam = allTeams.First(thisTeam => thisTeam.IsPlayerOnTeam(finishedPlayer));

                if (finishedTeams.Contains(finishedPlayerTeam))
                {
                    //We haven't yet come across a player who finished from this team yet, so register it.
                    teamOrder[finishedTeamIndex] = finishedPlayerTeam;
                    finishedTeamIndex--;
                    finishedTeams.Remove(finishedPlayerTeam);

                    if (finishedTeamIndex < 0)
                    {
                        break; //We've assigned all teams to the array.
                    }
                }
            }

            _teamActivateOrder = new List<ITeam>(teamOrder);
            _currentRoundPlayerFinishOrder.Clear();
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
                    _unactivatedUnits.Add(playerID, playerUnits);
                }
            }
        }

    }
}