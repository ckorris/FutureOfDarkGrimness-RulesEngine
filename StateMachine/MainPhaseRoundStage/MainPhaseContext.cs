
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

        public bool DoesAnyTeamHaveRemainingActivations();

        public bool DoesTeamHaveRemainingActivations(ITeam team);

        public bool DoesPlayerHaveRemainingActivations(PlayerID playerID);

        public void NewRound();
    }

    public class MainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; private set; }

        public IReadOnlyDictionary<PlayerID, List<DataBinding<UnitData>>> UnactivatedUnits => _unactivatedUnits;


        public Dictionary<PlayerID, List<DataBinding<UnitData>>> _unactivatedUnits { get; }

        public int RoundCount { get; private set; } = 0;

        public IReadOnlyList<ITeam> TeamActivateOrder => _teamActivateOrder;

        private List<ITeam> _teamActivateOrder;

        public int CurrentActivatingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentActivePlayerIndexPerTeam { get; }


        private List<ITeam> _currentRoundTeamFinishOrder = new List<ITeam>();

        public MainPhaseContext(IGameContext gameContext, List<ITeam> firstDeploymentRollOrder)
        {
            GameContext = gameContext;
            _teamActivateOrder = firstDeploymentRollOrder;

            _unactivatedUnits = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
            CurrentActivePlayerIndexPerTeam = new Dictionary<ITeam, int>();

            foreach (ITeam team in gameContext.TableState.Teams.Objects)
            {
                CurrentActivePlayerIndexPerTeam.Add(team, 0); //TODO: Same as elsewhere, this makes first player in each team kind of arbitrary.

                foreach (PlayerID playerID in team.Players)
                {
                    _unactivatedUnits.Add(playerID, new List<DataBinding<UnitData>>());
                }
            }

            //Slightly hacky but since we use the deploy roll for the first turn, and team finish order
            //for subsequent ones, we can use NewRound to set up the first one by just inserting the roll order
            //as the finished order.
            _currentRoundTeamFinishOrder = new List<ITeam>(firstDeploymentRollOrder);

            //Buuut we don't actually call NewRound here because the stage should call that.
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
                foreach(PlayerID teamPlayer in playerTeam.Players)
                {
                    if(teamPlayer != playerID && DoesPlayerHaveRemainingActivations(teamPlayer))
                    {
                        foundTeammateWithActivations = true;
                    }
                }

                if(foundTeammateWithActivations == false)
                {
                    _currentRoundTeamFinishOrder.Add(playerTeam);
                }
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
                if (_currentRoundTeamFinishOrder.Contains(team) == false)
                {
                    throw new InvalidOperationException($"Couldn't find team {team.TeamNumber} in finished teams during call to {nameof(NewRound)}. " +
                        $"Total teams listed as finished: {_currentRoundTeamFinishOrder.Count}");
                }
            }

            SetUnactivatedUnits();

            _teamActivateOrder = new List<ITeam>(_currentRoundTeamFinishOrder);
            _currentRoundTeamFinishOrder.Clear();

            RoundCount++;
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