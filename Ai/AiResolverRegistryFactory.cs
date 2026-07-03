using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.StageResolution;

namespace FDG.Ai
{
    public static class AiResolverRegistryFactory
    {
        // Set to true to insert a 1-second pause before each AI decision, useful for
        // watching the AI play in real time.
        public const bool SlowMode = false;
        private const int SlowModeDelayMs = 1000;

        public static IStageResolverRegistry BuildSoloRules(ITableState tableState, PlayerID playerID)
        {
            IStageResolverRegistry registry = new StageResolverRegistry()
                .RegisterResolver(new AiYesNoResolver())
                .RegisterResolver(new AiStringSelectionResolver(tableState, playerID))
                .RegisterResolver(new AiCastAssistResolver())
                .RegisterResolver(new AiChooseDeploymentZoneResolver())
                .RegisterResolver(new AiChooseRangedAttackResolver())
                .RegisterResolver(new AiChooseMeleeDefenderResolver())
                .RegisterResolver(new AiDefineMovementResolver(tableState, playerID))
                .RegisterResolver(new AiAircraftAdvanceResolver())
                .RegisterResolver(new AiConsolidationMoveResolver(tableState, playerID))
                .RegisterResolver(new AiAssignWoundsResolver())
                .RegisterResolver(new AiSelectionResolver<UnitData>())
                .RegisterResolver(new AiSelectionResolver<ModelData>())
                .RegisterResolver(new AiSelectionResolver<RectangularZone>())
                .RegisterResolver(new AiPlaceObjectsResolver<ModelData>(tableState))
                .RegisterResolver(new AiPlaceObjectiveResolver(tableState))
                .RegisterResolver(new AiPlaceOneTerrainResolver(tableState));

            if (SlowMode)
                registry = new AiSlowModeRegistry(registry, SlowModeDelayMs);

            return registry;
        }

        public static ComputerPlayerController CreateSoloRulesController(
            string name, PlayerID id, FDGGame_AsLocal localGame)
        {
            var registry = BuildSoloRules(localGame.TableState, id);
            return new ComputerPlayerController(name, id, localGame, registry);
        }
    }

    internal sealed class AiSlowModeRegistry : IStageResolverRegistry
    {
        private readonly IStageResolverRegistry _inner;
        private readonly int _delayMs;

        internal AiSlowModeRegistry(IStageResolverRegistry inner, int delayMs)
        {
            _inner = inner;
            _delayMs = delayMs;
        }

        public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageTaskRequest<TReply>
        {
            _inner.RegisterResolver(resolver);
            return this;
        }

        public async Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            await Task.Delay(_delayMs);
            return await _inner.ResolveRequest<TRequest, TReply>(request);
        }

        public async Task<string> ResolveRequestAsJson(string typeFullName, string requestJson,
            IReadableGameDataStore gameDataStore)
        {
            await Task.Delay(_delayMs);
            return await _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
        }
    }
}
