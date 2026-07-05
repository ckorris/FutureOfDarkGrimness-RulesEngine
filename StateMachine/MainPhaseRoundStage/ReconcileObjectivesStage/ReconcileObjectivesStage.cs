using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

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
                    GameContext.Log($"  Objective contested - becomes neutral.");
                }
                else
                {
                    string ownerDesc = objective.OwnerID.HasValue
                        ? $"player {objective.OwnerID.Value.ID}"
                        : "neutral";
                    GameContext.Log($"  Objective uncontested - remains {ownerDesc}.");
                }
            }

            // End of round: clear "this round" tokens across every unit AFTER the objective check above —
            // the once-per-round cost gates and the "arrived from reserve this round" marker (which the
            // check just read to exclude newcomers). Clearing before the check would let a unit that
            // arrived this round seize objectives the very round it came on.
            List<ITokenContainer> containers = new List<ITokenContainer>();
            foreach (IUnit unit in tableState.Units.Objects)
            {
                containers.Add(unit.Tokens);
                containers.AddRange(unit.Models.Select(model => model.Tokens));
            }
            new TokenClearService().ClearForHook(EHookID.Round_OnRoundEnd, containers);

            if (_timesEntered < GameWideConstants.NUMBER_OF_ROUNDS)
            {
                await ToReconcileEndOfTurn.Activate(context);
            }
            else
            {
                GameContext.Log($"{GameWideConstants.NUMBER_OF_ROUNDS} rounds complete. Proceeding to victory calculation.");
                await ToVictoryCalculation.Activate(context);
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

                // A unit that arrived from reserve this round can neither seize nor contest — skip its
                // models so it counts toward neither (the marker is cleared at this round's end below).
                if (unit.Tokens.HasToken(TokenType.ArrivedFromReserve)) continue;

                // A Shaken unit can neither seize nor contest objectives (#008). Units that activated
                // and recovered this round have already had the token cleared, so only those still
                // Shaken at end of round are excluded here.
                if (unit.Tokens.HasToken(TokenType.Shaken)) continue;

                // #029 Aircraft can't seize or contest objectives — skip its models so it counts toward neither.
                if (AircraftRules.IsAircraft(unit)) continue;

                // Objective-centre-to-base-edge distance using the model's true footprint + facing (#150),
                // not the circumscribing circle.
                float baseEdgeDist = BaseShapeGeometry.SurfaceDistanceToPoint2D(
                    model.BaseShape, model.Position, model.Facing, objPos);

                if (baseEdgeDist <= SeizureRadiusInches)
                    result.Add(unit.PlayerID);
            }

            return result;
        }
    }
}
