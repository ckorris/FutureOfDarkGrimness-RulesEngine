
namespace FDG.Stages
{

    public class ReconcileObjectivesStage : StageBase<IMainPhaseContext>
    {
        public const string RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN = "ReconcileObjectivesBackToReconcileNewTurn";
        public const string RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION = "ReconcileObjectivesBackToDeterminePlayerTurn";

        private const float SeizureRadiusInches = 3f;

        public StageBinding ToReconcileEndOfTurn;
        public StageBinding ToVictoryCalculation;

        private int _timesEntered = 0;

        public ReconcileObjectivesStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToReconcileEndOfTurn = new StageBinding(this);
            ToVictoryCalculation = new StageBinding(this);
        }

        public override async Task Enter(IMainPhaseContext context)
        {
            _timesEntered++;
            GameContext.Log($"Reconciling objectives (end of round {_timesEntered}).");

            var tableState = GameContext.TableState;

            // Build a model→unit map so we can get PlayerID from a model.
            var modelToUnit = new Dictionary<IModel, IUnit>();
            foreach (var unit in tableState.Units.Objects)
                foreach (var model in unit.Models)
                    modelToUnit[model] = unit;

            foreach (var objective in tableState.Objectives.Objects)
            {
                var nearbyPlayers = PlayersNearObjective(objective, tableState.Models.Objects, modelToUnit);

                if (nearbyPlayers.Count == 1)
                {
                    var seizer = nearbyPlayers.First();
                    objective.SetOwner(seizer);
                    GameContext.Log($"  Objective seized by player {seizer.ID}.");
                }
                else if (nearbyPlayers.Count > 1)
                {
                    objective.SetOwner(null);
                    GameContext.Log($"  Objective contested — becomes neutral.");
                }
                else
                {
                    string ownerDesc = objective.OwnerID.HasValue
                        ? $"player {objective.OwnerID.Value.ID}"
                        : "neutral";
                    GameContext.Log($"  Objective uncontested — remains {ownerDesc}.");
                }
            }

            if (_timesEntered < 4)
            {
                ToReconcileEndOfTurn.Activate(context);
            }
            else
            {
                GameContext.Log("Four rounds complete. Proceeding to victory calculation.");
                ToVictoryCalculation.Activate(context);
            }
        }

        // Returns the set of PlayerIDs whose living models have a base edge within SeizureRadiusInches of the objective.
        private static HashSet<PlayerID> PlayersNearObjective(
            IObjective objective,
            IEnumerable<IModel> allModels,
            Dictionary<IModel, IUnit> modelToUnit)
        {
            var result = new HashSet<PlayerID>();
            var objPos = objective.Position;

            foreach (var model in allModels)
            {
                if (!model.GetIsAlive()) continue;
                if (!modelToUnit.TryGetValue(model, out var unit)) continue;

                float dx = model.Position.x - objPos.x;
                float dz = model.Position.z - objPos.z;
                float centerDist = MathF.Sqrt(dx * dx + dz * dz);
                float baseEdgeDist = centerDist - model.BaseRadiusInches;

                if (baseEdgeDist <= SeizureRadiusInches)
                    result.Add(unit.PlayerID);
            }

            return result;
        }
    }
}
