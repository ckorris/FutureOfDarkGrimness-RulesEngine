using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Builds the Tactician's resolver set — the challenge-level agent (#191, docs/ai-agent-plan.md).
    /// <para>
    /// A0 scaffold: every request is currently answered by the unmodified solo-rules resolvers, so a
    /// seeded Tactician game is transcript-identical to a solo-rules game (pinned by
    /// <c>TacticianScaffoldTests</c>). Phase A replaces resolvers one request type at a time; the
    /// solo-rules bot itself is never changed (plan decision D1).
    /// </para>
    /// </summary>
    public static class TacticianResolverRegistryFactory
    {
        public static IStageResolverRegistry Build(ITableState tableState, PlayerID playerID,
            TacticianOptions options) => Build(tableState, playerID, options, out _);

        /// <summary>
        /// Same as <see cref="Build(ITableState, PlayerID, TacticianOptions)"/>, plus the planner
        /// instance driving this registry (#191 C1 exporter: chosen_macro reads
        /// <see cref="TacticianPlanner.LastMacroLabel"/> off it after every Choose Action call).
        /// </summary>
        public static IStageResolverRegistry Build(ITableState tableState, PlayerID playerID,
            TacticianOptions options, out TacticianPlanner planner)
        {
            // Solo-rules answers everything the Tactician has not replaced yet (fallback discipline,
            // plan G3); each A4 slice registers one more replacement below.
            var registry = new TacticianRegistry(
                AiResolverRegistryFactory.BuildSoloRules(tableState, playerID, options.Seed, options.SlotID));

            // A bare evaluator: rules attached to units/weapons evaluate fully; granted-token
            // read-back (aura buffs) needs the game's own resolver-backed evaluator, which resolvers
            // do not receive today - recorded gap in the #191 ledger.
            var evaluator = new Rules.Dispatch.RuleEvaluator(new ProbabilisticDiceRoller());
            planner = new TacticianPlanner(tableState, evaluator, options.DecisionLog,
                options.SeeThroughFriendlyUnits);

            // A4-1: activation order by urgency (also announces the active unit to the planner).
            // #389: the kill term's sight gate follows the same #384 house rule the planner does.
            var activationPolicy = new Resolvers.TacticianActivationResolver(tableState, evaluator,
                planner, options.DecisionLog, options.SeeThroughFriendlyUnits);
            if (options.Search is { } searchBudget)
            {
                // B5 (#191 step 9): the Strategist rung. The search picks the activation and
                // prescribes it; the A resolver above still PLAYS it, so everything below the
                // activation is unchanged and a search failure is just plain A (G3).
                registry.RegisterResolver(new Search.StrategistActivationResolver(tableState, planner,
                    activationPolicy, new Search.HandWeightedEvaluator(), searchBudget,
                    options.DecisionLog));
            }
            else
            {
                registry.RegisterResolver(activationPolicy);
            }

            // A4-2: the (action x macro-action) pair is planned once at Choose Action and played out
            // at the movement request; solo-rules instances are the per-request fallbacks (G3).
            // #358: the embedded fallback pair shares a decline latch like every solo set - when
            // the planner has no claim, the fallback policy must not loop a wedged unit's menu.
            var fallbackDeclineLatch = new FDG.Ai.Resolvers.SoloMoveDeclineLatch();
            var actionResolver = new Resolvers.TacticianActionResolver(planner, tableState,
                new FDG.Ai.Resolvers.AiStringSelectionResolver(tableState, playerID, fallbackDeclineLatch));
            registry.RegisterResolver<StageResolution.Requests.StringSelectionRequest, string>(actionResolver);
            // #191 B1 step 5a: Choose Action is its own request type; the same planner-backed instance
            // answers it (chosen_macro reads TacticianPlanner.LastMacroLabel off `planner` after this).
            registry.RegisterResolver<StageResolution.Requests.ChooseActionRequest, string>(actionResolver);
            registry.RegisterResolver(new Resolvers.TacticianMovementResolver(planner, tableState,
                new FDG.Ai.Resolvers.AiDefineMovementResolver(tableState, playerID, fallbackDeclineLatch),
                options.DecisionLog));

            // A4-3: value-weighted target choice (shooting + melee defender). A5-6 adds cargo-aware
            // target value and the charge-threat factor (tableState).
            registry.RegisterResolver(new Resolvers.TacticianRangedAttackResolver(evaluator,
                new FDG.Ai.Resolvers.AiChooseRangedAttackResolver(), tableState));
            registry.RegisterResolver(new Resolvers.TacticianMeleeDefenderResolver(tableState, evaluator, planner));

            // A5-6 (Chris's resolver pass): Takedown/sniper and single-model spell picks - WHICH
            // model dies is the rule's whole point; solo took "Model 1".
            registry.RegisterResolver(new Resolvers.TacticianModelSelectionResolver(
                new FDG.Ai.Resolvers.AiSelectionResolver<ModelData>()));

            // A4-4: wound assignment preserving output (cheapest-output casualties first); step 10 P0:
            // marker-aware (the last model on a held/contested marker dies last).
            registry.RegisterResolver(new Resolvers.TacticianAssignWoundsResolver(tableState));

            // A4b: objective-aware deployment. The subclass IS the solo resolver for every
            // non-deployment placement (disembark, spillout, ambush, reposition).
            registry.RegisterResolver<PlaceObjectsRequest<ModelData>,
                CancellableResult<List<PlacedObjectEntry<ModelData>>>>(
                new Resolvers.TacticianPlaceObjectsResolver<ModelData>(tableState, evaluator));

            // A4b-2: objective placement by army profile (firebase armies cluster the markers,
            // mobile/melee armies spread them; zones are chosen after objectives, so no side bias).
            registry.RegisterResolver(new Resolvers.TacticianPlaceObjectiveResolver(tableState));

            // A5: casting. Cast is taken at Choose Action whenever a positive-value spell x target
            // exists (layered - it costs the activation nothing; the spell picker itself is its own
            // request type since #244, answered below); target picks maximize value and never cancel
            // into the Choose Action livelock; assists spend tokens when a one-face threshold shift
            // beats their cost. Non-spell unit selections fall through to the embedded solo resolver.
            registry.RegisterResolver(new Resolvers.TacticianUnitSelectionResolver(planner,
                new FDG.Ai.Resolvers.AiSelectionResolver<UnitData>(), tableState, evaluator));
            registry.RegisterResolver(new Resolvers.TacticianChooseSpellResolver(planner,
                new FDG.Ai.Resolvers.AiChooseSpellResolver()));
            registry.RegisterResolver(new Resolvers.TacticianCastAssistResolver(tableState, evaluator,
                options.SeeThroughFriendlyUnits));

            return registry;
        }

        public static ComputerPlayerController CreateController(string name, PlayerID id,
            FDGGame_AsLocal localGame, TacticianOptions options)
        {
            IStageResolverRegistry registry = Build(localGame.TableState, id, options);
            return new ComputerPlayerController(name, id, localGame, registry);
        }
    }
}
