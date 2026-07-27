using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
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

        public ReconcileObjectivesStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToReconcileEndOfTurn = new StageBinding(this);
            ToVictoryCalculation = new StageBinding(this);
        }

        public override async Task Enter(IMainPhaseContext context)
        {
            // #195: the game ends after NUMBER_OF_ROUNDS *game* rounds, which is not the same as the
            // number of times this stage instance has been entered. A resumed game builds a fresh stage,
            // so an entry counter restarts at zero and the game plays a full four MORE rounds (a save
            // resumed at round 2 ran 2..5). The round number is authoritative: it is restored from the
            // save, and SingleRoundStage has already advanced it (OnEndOfRound, on its way out) by the
            // time we get here — so the round that just finished is RoundCount - 1.
            int roundJustFinished = context.RoundCount - 1;
            GameContext.Log($"Reconciling objectives (end of round {roundJustFinished}).");

            var tableState = GameContext.TableState;

            // Build a model→unit map so we can get PlayerID from a model.
            var modelToUnit = new Dictionary<IModel, IUnit>();
            foreach (var unit in tableState.Units.Objects)
                foreach (var model in unit.Models)
                    modelToUnit[model] = unit;

            foreach (var objective in tableState.Objectives.Objects)
            {
                var nearbyPlayers = PlayersNearObjective(objective, tableState.Models.Objects, modelToUnit);

                // #297: team-aware - allied players guarding a marker together hold it for their
                // side instead of contesting it to neutral (victory already pools per team, #257).
                PlayerID? resolved = ITeamExtensions.ReconcileObjectiveOwner(
                    tableState.Teams.Objects, objective.OwnerID, nearbyPlayers);

                if (nearbyPlayers.Count == 0)
                {
                    string ownerDesc = objective.OwnerID.HasValue
                        ? $"player {objective.OwnerID.Value.ID}"
                        : "neutral";
                    GameContext.Log($"  Objective uncontested - remains {ownerDesc}.");
                }
                else if (!resolved.HasValue)
                {
                    objective.SetOwner(null);
                    GameContext.Log($"  Objective contested - becomes neutral.");
                }
                else
                {
                    // Unconditional SetOwner + "seized" wording even when the owner is unchanged -
                    // exactly what the old per-player branch did for a solely-held marker, so 1v1
                    // logs (and anything keying on them) are bit-identical.
                    objective.SetOwner(resolved.Value);
                    GameContext.Log($"  Objective seized by player {resolved.Value.ID}.");
                }
            }

            // #100 #13: fire round-end rules for every living unit BEFORE the token sweep below, so
            // tokens that clear at round end are still visible to them and the ManualOnly markers they
            // grant (Fortified Growth's accumulation) survive the sweep. Mirror of the round-start loop
            // in StartOfRoundExtraActionStage.GrantRoundStartTokens; units with no round-end rules produce no
            // operations. Models are passed so a joined hero's model-level rule is still seen (#093).
            foreach (IUnit unit in tableState.Units.Objects.ToList())
            {
                if (!unit.GetIsAlive()) continue;

                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.Evaluate(
                    unit, ERuleSeat.Actor, new RoundEndContext(unit), weapon: null, models: unit.Models);
                OperationApplier.ApplyTokenOperations(ops);
                await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));
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

            if (roundJustFinished < GameWideConstants.NUMBER_OF_ROUNDS)
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
