using FDG.Data;
using FDG.Ai.Tactician;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Gunline
{
    /// <summary>
    /// Builds the Gunline profile's resolver set (#191 tooling): the scripted hold-and-shoot
    /// stand-in for a human playing a defensive shooting army (see <see cref="GunlinePlanner"/>).
    /// Reuses the Tactician's battle-tested micro where the human equivalent is "obvious play" -
    /// deployment aims, target choice, wound assignment, model picks - and solo-rules answers
    /// everything else (G3 fallback discipline). Deliberately NOT registered: casting (a stand-in
    /// line does not need spells) and the melee-defender pick (solo default).
    /// </summary>
    public static class GunlineResolverRegistryFactory
    {
        public static IStageResolverRegistry Build(ITableState tableState, PlayerID playerID,
            int? seed = null, int slotID = 0, Action<string>? decisionLog = null)
        {
            var registry = new TacticianRegistry(
                AiResolverRegistryFactory.BuildSoloRules(tableState, playerID, seed, slotID));

            var evaluator = new Rules.Dispatch.RuleEvaluator(new ProbabilisticDiceRoller());
            var planner = new GunlinePlanner(tableState, evaluator, decisionLog);

            registry.RegisterResolver(new GunlineActivationResolver(planner));
            registry.RegisterResolver(new GunlineActionResolver(planner,
                new FDG.Ai.Resolvers.AiStringSelectionResolver(tableState, playerID)));
            registry.RegisterResolver(new Tactician.Resolvers.TacticianMovementResolver(planner, tableState,
                new FDG.Ai.Resolvers.AiDefineMovementResolver(tableState, playerID)));

            // Shared micro: a human's "obvious" choices, identical for any competent player.
            registry.RegisterResolver(new Tactician.Resolvers.TacticianRangedAttackResolver(evaluator,
                new FDG.Ai.Resolvers.AiChooseRangedAttackResolver(), tableState));
            registry.RegisterResolver(new Tactician.Resolvers.TacticianAssignWoundsResolver());
            registry.RegisterResolver(new Tactician.Resolvers.TacticianModelSelectionResolver(
                new FDG.Ai.Resolvers.AiSelectionResolver<ModelData>()));
            registry.RegisterResolver<PlaceObjectsRequest<ModelData>,
                CancellableResult<List<PlacedObjectEntry<ModelData>>>>(
                new Tactician.Resolvers.TacticianPlaceObjectsResolver<ModelData>(tableState, evaluator));
            registry.RegisterResolver(new Tactician.Resolvers.TacticianPlaceObjectiveResolver(tableState));

            return registry;
        }
    }
}
