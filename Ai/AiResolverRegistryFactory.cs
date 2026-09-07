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

        /// <param name="seed">
        /// The GAME's seed (<see cref="GameSettings.DiceSeed"/>), not a per-player one — this method
        /// derives the player's own stream from it via <paramref name="slotID"/>, so two AI players never
        /// share a sequence and a seeded game replays identically (#193). Null = unseeded, as before.
        /// </param>
        /// <param name="slotID">The player's slot index. Stable across runs and across save/resume, unlike the PlayerID GUID.</param>
        public static IStageResolverRegistry BuildSoloRules(ITableState tableState, PlayerID playerID,
            int? seed = null, int slotID = 0)
        {
            int? playerSeed = GameRandom.DeriveForSlot(seed, slotID);
            Random? objectiveRng = playerSeed.HasValue ? GameRandom.Create(playerSeed, salt: 1) : null;
            Random? terrainRng = playerSeed.HasValue ? GameRandom.Create(playerSeed, salt: 2) : null;

            // #358: one latch per solo set - the path resolver's main-move decline tells the
            // action menu to skip the movement family once, breaking the decline-repick livelock.
            var declineLatch = new SoloMoveDeclineLatch();

            // #191 A5-10: the UnitData selection resolver asks the rule graph which deploy-order
            // options are transports (they go down first, so later cargo can be offered the ride).
            var evaluator = new Rules.Dispatch.RuleEvaluator(new ProbabilisticDiceRoller());

            var stringSelection = new AiStringSelectionResolver(tableState, playerID, declineLatch);

            IStageResolverRegistry registry = new StageResolverRegistry()
                .RegisterResolver(new AiYesNoResolver())
                .RegisterResolver<StageResolution.Requests.StringSelectionRequest, string>(stringSelection)
                // #191 B1 step 5a: Choose Action is its own request type; the same instance answers it.
                .RegisterResolver<StageResolution.Requests.ChooseActionRequest, string>(stringSelection)
                .RegisterResolver(new AiChooseAbilityEffectResolver())
                .RegisterResolver(new AiChooseSpellResolver())
                .RegisterResolver(new AiCastAssistResolver())
                .RegisterResolver(new AiChooseDeploymentZoneResolver())
                .RegisterResolver(new AiChooseRangedAttackResolver())
                .RegisterResolver(new AiChooseMeleeDefenderResolver())
                .RegisterResolver(new DerivedRequestAdapter<StageResolution.Requests.ChooseMeleeDefenderRequest,
                    StageResolution.Requests.CancellableSelectionRequest<UnitData>,
                    StageResolution.CancellableResult<Data.DataBinding<UnitData>>>(
                    new AiChooseMeleeDefenderResolver()))
                .RegisterResolver(new AiDefineMovementResolver(tableState, playerID, declineLatch))
                .RegisterResolver(new AiAircraftAdvanceResolver())
                .RegisterResolver(new AiConsolidationMoveResolver(tableState, playerID))
                .RegisterResolver(new AiAssignWoundsResolver())
                .RegisterResolver(new AiSelectionResolver<UnitData>(evaluator))
                // #191 A4-1's request split: activation is its own type; solo-rules keeps the same
                // first-option behavior via the adapter (pinned by the benchmark hashes).
                .RegisterResolver(new DerivedRequestAdapter<StageResolution.Requests.ChooseUnitToActivateRequest,
                    StageResolution.Requests.SelectionRequest<UnitData>, Data.DataBinding<UnitData>>(
                    new AiSelectionResolver<UnitData>()))
                .RegisterResolver(new AiSelectionResolver<ModelData>())
                .RegisterResolver(new AiSelectionResolver<RectangularZone>())
                .RegisterResolver(new AiPlaceObjectsResolver<ModelData>(tableState))
                .RegisterResolver(new AiPlaceObjectiveResolver(tableState, objectiveRng))
                .RegisterResolver(new AiPlaceOneTerrainResolver(tableState, terrainRng));

            if (SlowMode)
                registry = new AiSlowModeRegistry(registry, SlowModeDelayMs);

            return registry;
        }

        public static ComputerPlayerController CreateSoloRulesController(
            string name, PlayerID id, FDGGame_AsLocal localGame, int? seed = null, int slotID = 0)
        {
            var registry = BuildSoloRules(localGame.TableState, id, seed, slotID);
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
