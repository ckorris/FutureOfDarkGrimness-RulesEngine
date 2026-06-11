using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class StartOfRoundExtraActionStage : StageBase<IMainPhaseContext>
    {
        public StageBinding OnFinished;
        public StartOfRoundExtraActionStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(IMainPhaseContext context)
        {
            context.Log($"Entered {nameof(StartOfRoundExtraActionStage)}.");

            // Ambush reserves may arrive from round 2 onward.
            if (context.RoundCount >= 2)
            {
                await BringOnReserves();
            }

            OnFinished.Activate(context);
        }

        /// <summary>
        /// Offers each still-reserved Ambush unit (kept off-table by DeferDeployment(LaterRound) and
        /// never placed during deployment) to its owner this round; on accept, places it anywhere over
        /// its rule's range from enemy units via the normal PlaceObjectsRequest flow.
        /// </summary>
        private async Task BringOnReserves()
        {
            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>().ToList())
            {
                foreach (DataBinding<UnitData> unitBinding in army.UnitBindings.ToList())
                {
                    UnitData unit = unitBinding.GetValue();

                    if (!unit.GetIsAlive()) continue;
                    if (!IsUnplaced(unit)) continue;
                    if (!TryGetLaterRoundDefer(unit, out RuleOperation.DeferDeployment defer)) continue;

                    bool bringOn = await GameContext.PlayerRequester.RequestDecision<YesNoRequest, bool>(
                        new YesNoRequest(unit.PlayerID, $"Deploy {unit.Name} from Ambush this round?"));

                    if (!bringOn) continue;

                    await PlaceFromReserve(unit, defer.PlacementRangeInches);

                    // A unit that arrives from reserve can't seize or contest objectives the round it
                    // arrives. Mark it so ReconcileObjectivesStage excludes its models from this round's
                    // objective check; the RoundEnd clear trigger sweeps the marker after that check.
                    unit.Tokens.AddToken(new Token(TokenType.ArrivedFromReserve, 1,
                        new TokenClearTrigger.RoundEnd()));

                    GameContext.Log($"{unit.Name} arrived from Ambush.");
                }
            }
        }

        private async Task PlaceFromReserve(UnitData unit, float minDistanceFromEnemies)
        {
            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            DataBinding<RectangularZone> zoneBinding = GameContext.GameDataStore
                .GetDataBinding<RectangularZone>(GameContext.GameDataStore.Create(wholeTable));

            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, "Ambush Deploy",
                zoneBinding, unit.ModelBindings, minDistanceFromEnemiesInches: minDistanceFromEnemies);

            List<PlacedObjectEntry<ModelData>> placements = await GameContext.PlayerRequester
                .RequestDecision<PlaceObjectsRequest<ModelData>, List<PlacedObjectEntry<ModelData>>>(request);

            foreach (PlacedObjectEntry<ModelData> placement in placements)
            {
                placement.Binding.GetValue().SetPosition(placement.Position);
            }
        }

        // A unit that has never been placed has all models at the default origin (0,0,0).
        private static bool IsUnplaced(UnitData unit)
        {
            foreach (DataBinding<ModelData> model in unit.ModelBindings)
            {
                Position pos = model.GetValue().PositionBinding.GetValue();
                if (pos.x != 0f || pos.z != 0f) return false;
            }
            return true;
        }

        private bool TryGetLaterRoundDefer(IUnit unit, out RuleOperation.DeferDeployment defer)
        {
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.Evaluate(
                unit, ERuleSeat.Actor, new PreDeploymentSelectContext(unit));

            defer = ops.OfType<RuleOperation.DeferDeployment>()
                .FirstOrDefault(d => d.Timing == EDeferTiming.LaterRound);
            return defer != null;
        }
    }
}
