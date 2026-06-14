using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;


namespace FDG.Stages
{
    /// <summary>A unit set aside during normal deployment, with the defer operation that
    /// describes how/when it should be placed (Scout: after deployment, within range of the zone).</summary>
    public record DeferredUnitEntry(DataBinding<UnitData> Unit, RuleOperation.DeferDeployment Defer);

    public interface IDeploymentTurnContext : IGameContextAccessor
    {
        bool HasStarted { get; set; }

        IReadOnlyList<ITeam> FirstDeploymentRollOrder { get; }

        IReadOnlyDictionary<ITeam, DataBinding<RectangularZone>>? PlayerDeploymentZones { get; }

        TeamPlayerAlternationCursor Cursor { get; }

        int CurrentDeployingTeamIndex { get; set; }

        Dictionary<ITeam, int> CurrentDeployingPlayerIndexPerTeam { get; }

        PlayerID GetCurrentDeployingPlayerID();

        public bool DoesTeamHaveRemainingDeployments(ITeam team);

        public bool DoesPlayerHaveRemainingDeployments(PlayerID playerID);

        public Dictionary<PlayerID, List<DataBinding<UnitData>>> UndeployedUnits { get; }

        /// <summary>Units pulled out of the normal pool by a DeferDeployment rule (Scout),
        /// to be placed by their own pass after normal deployment finishes.</summary>
        public IReadOnlyList<DeferredUnitEntry> DeferredUnits { get; }

        public DataBinding<UnitData>? CurrentDeployingUnit { get; set; }

    }

    public class DeploymentTurnContext : IDeploymentTurnContext
    {
        public IGameContext GameContext { get; private set; }

        public IReadOnlyList<ITeam> FirstDeploymentRollOrder { get; }

        public IReadOnlyDictionary<ITeam, DataBinding<RectangularZone>> PlayerDeploymentZones { get; }

        public TeamPlayerAlternationCursor Cursor { get; }

        public int CurrentDeployingTeamIndex
        {
            get => Cursor.CurrentTeamIndex;
            set => Cursor.CurrentTeamIndex = value;
        }

        public Dictionary<ITeam, int> CurrentDeployingPlayerIndexPerTeam => Cursor.CurrentPlayerIndexPerTeam;

        public bool HasStarted { get; set; } = false;

        public Dictionary<PlayerID, List<DataBinding<UnitData>>> UndeployedUnits { get; }

        public IReadOnlyList<DeferredUnitEntry> DeferredUnits => _deferredUnits;
        private readonly List<DeferredUnitEntry> _deferredUnits = new();

        public DataBinding<UnitData>? CurrentDeployingUnit { get; set; } = null;

        public DeploymentTurnContext(IGameContext gameContext, List<ITeam> firstDeploymentRollOrder,
            Dictionary<ITeam, DataBinding<RectangularZone>> playerDeploymentZones)
        {
            GameContext = gameContext;
            FirstDeploymentRollOrder = firstDeploymentRollOrder;
            PlayerDeploymentZones = playerDeploymentZones;
            Cursor = new TeamPlayerAlternationCursor(firstDeploymentRollOrder);

            UndeployedUnits = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();

            List<ArmyData> armies = GameContext.GameDataStore().GetAllValues<ArmyData>().ToList();

            foreach (ITeam team in firstDeploymentRollOrder)
            {
                foreach (PlayerID playerID in team.Players)
                {
                    List<DataBinding<UnitData>> playerUnits = new List<DataBinding<UnitData>>();

                    foreach (ArmyData army in armies.Where(a => a.IsOwnedBy(playerID)))
                    {
                        foreach (DataBinding<UnitData> unitBinding in army.UnitBindings)
                        {
                            // Scout-style rules (AfterNormalDeployment) pull the unit out of the normal
                            // pool here (the Deployment_OnPreDeploymentSelect "when") to be placed by a
                            // later pass. Ambush-style rules (LaterRound) stay in the normal pool: the
                            // player decides per-unit at selection time whether to hold it in reserve
                            // (see ChooseUnitToDeployStage), so the unit still appears in the deploy list.
                            if (TryGetDefer(unitBinding, out RuleOperation.DeferDeployment defer)
                                && defer.Timing == EDeferTiming.AfterNormalDeployment)
                            {
                                _deferredUnits.Add(new DeferredUnitEntry(unitBinding, defer));
                            }
                            else
                            {
                                playerUnits.Add(unitBinding);
                            }
                        }
                    }
                    UndeployedUnits.Add(playerID, playerUnits);
                }
            }
        }

        private bool TryGetDefer(DataBinding<UnitData> unitBinding, out RuleOperation.DeferDeployment defer)
        {
            IUnit unit = unitBinding.GetValue();
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.Evaluate(
                unit, ERuleSeat.Actor, new PreDeploymentSelectContext(unit));

            defer = ops.OfType<RuleOperation.DeferDeployment>().FirstOrDefault();
            return defer != null;
        }

        public PlayerID GetCurrentDeployingPlayerID() => Cursor.GetCurrentPlayerID();

        public bool DoesTeamHaveRemainingDeployments(ITeam team)
        {
            foreach(PlayerID playerID in team.Players)
            {
                if(DoesPlayerHaveRemainingDeployments(playerID))
                {
                    return true;
                }
            }

            return false;
        }

        public bool DoesPlayerHaveRemainingDeployments(PlayerID playerID)
        {
            return UndeployedUnits[playerID].Count > 0;
        }
    }
}
