using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
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
            context.LogDebug($"Entered {nameof(StartOfRoundExtraActionStage)}.");

            await context.Announce($"Round {context.RoundCount}", new TextColor(120, 200, 255, 255));

            // Caster units replenish their spell tokens at the top of every round (#033), including round 1.
            await GrantSpellTokens();

            // Ambush reserves may arrive from round 2 onward; so do Aircraft that flew off the table edge.
            if (context.RoundCount >= 2)
            {
                await BringOnReserves();
                await RedeployOffTableAircraft();
            }

            await OnFinished.Activate(context);
        }

        /// <summary>
        /// Fires <see cref="EHookID.Round_OnRoundStart"/> for every living unit so Caster(X) grants its X
        /// <see cref="TokenType.SpellTokens"/> (#033), then clamps each unit's pool to
        /// <see cref="GameWideConstants.MAX_SPELL_TOKENS"/>. Non-Caster units produce no operations, so they
        /// gain nothing. Tokens carry over between rounds (ManualOnly clear), which is why the cap is
        /// enforced here at grant time rather than by a clear trigger. Runs every round (the round loop
        /// re-enters this stage each round); the resume path skips this stage, so a resumed game does not
        /// re-grant the round's tokens.
        /// </summary>
        private async Task GrantSpellTokens()
        {
            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>().ToList())
            {
                foreach (DataBinding<UnitData> unitBinding in army.UnitBindings.ToList())
                {
                    UnitData unit = unitBinding.GetValue();
                    if (!unit.GetIsAlive()) continue;

                    // Pass the unit's models so a joined Caster hero — whose Caster rule lives on its MODEL
                    // after the #006 hero-merge, not the host unit — still grants the unit its spell tokens
                    // at round start (#093 joined-Caster corner). A solo caster keeps Caster on the unit and
                    // is unaffected.
                    IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.Evaluate(
                        unit, ERuleSeat.Actor, new RoundStartContext(unit), weapon: null, models: unit.Models);
                    OperationApplier.ApplyTokenOperations(ops);
                    await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));

                    int excess = unit.Tokens.GetTokenCount(TokenType.SpellTokens)
                        - GameWideConstants.MAX_SPELL_TOKENS;
                    if (excess > 0)
                    {
                        unit.Tokens.RemoveTokens(TokenType.SpellTokens, excess);
                    }
                }
            }
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
                    if (!ReserveRules.IsInReserve(unit)) continue;
                    if (!TryGetLaterRoundDefer(unit, out RuleOperation.DeferDeployment defer)) continue;

                    bool bringOn = await GameContext.PlayerRequester.RequestDecision<YesNoRequest, bool>(
                        new YesNoRequest(unit.PlayerID, $"Deploy {unit.Name} from Ambush this round?", defaultAnswer: true));

                    if (!bringOn) continue;

                    await PlaceFromReserve(unit, defer.PlacementRangeInches);   // clears the reserve state
                    // A unit that arrives from reserve can't seize or contest objectives the round it
                    // arrives. Mark it so ReconcileObjectivesStage excludes its models from this round's
                    // objective check; the RoundEnd clear trigger sweeps the marker after that check.
                    unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ArrivedFromReserve));

                    await GameContext.Announce($"{unit.Name} arrives from Ambush!", new TextColor(255, 170, 60, 255));
                }
            }
        }

        /// <summary>
        /// #029: an Aircraft that flew off the table edge during its forced move (marked
        /// <see cref="TokenType.OffTableFromForcedMove"/>, models held at origin) flies back on at the start of
        /// the next round, placed touching any table edge (the request's MustTouchTableEdge constraint); how
        /// the player faces it at that placement IS its new heading (#150). Like a reserve arrival, it can't
        /// seize/contest objectives the round it returns.
        /// </summary>
        private async Task RedeployOffTableAircraft()
        {
            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>().ToList())
            {
                foreach (DataBinding<UnitData> unitBinding in army.UnitBindings.ToList())
                {
                    UnitData unit = unitBinding.GetValue();
                    if (!unit.GetIsAlive()) continue;
                    if (!unit.Tokens.HasToken(TokenType.OffTableFromForcedMove)) continue;

                    await PlaceFromReserve(unit, minDistanceFromEnemies: 0f,
                        mustTouchTableEdge: true, taskName: "Aircraft Redeploy");
                    unit.Tokens.RemoveTokens(TokenType.OffTableFromForcedMove);
                    unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ArrivedFromReserve));

                    await GameContext.Announce($"{unit.Name} (Aircraft) flies back on from the table edge!",
                        new TextColor(120, 200, 255, 255));
                }
            }
        }

        private async Task PlaceFromReserve(UnitData unit, float minDistanceFromEnemies,
            bool mustTouchTableEdge = false, string taskName = "Ambush Deploy")
        {
            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, taskName,
                wholeTable, unit.ModelBindings, minDistanceFromEnemiesInches: minDistanceFromEnemies,
                mustTouchTableEdge: mustTouchTableEdge);

            List<PlacedObjectEntry<ModelData>> placements = await PlacementRequesting
                .RequestMandatoryPlacement(GameContext.PlayerRequester, request);

            foreach (PlacedObjectEntry<ModelData> placement in placements)
            {
                placement.Binding.GetValue().SetPosition(placement.Position);
                if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
            }

            // On the table is the negation of in reserve. Asserting it here (rather than only at the ambush
            // call site) keeps the invariant true for the Aircraft redeploy path too.
            ReserveRules.ClearReserve(unit);
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
