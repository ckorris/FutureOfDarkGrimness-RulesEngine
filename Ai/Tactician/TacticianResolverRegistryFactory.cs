using FDG.GameModel;
using FDG.Players;
using FDG.StageResolution;

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
            TacticianOptions options)
        {
            // Solo-rules answers everything the Tactician has not replaced yet (fallback discipline,
            // plan G3); each A4 slice registers one more replacement below.
            var registry = new TacticianRegistry(
                AiResolverRegistryFactory.BuildSoloRules(tableState, playerID, options.Seed, options.SlotID));

            // A bare evaluator: rules attached to units/weapons evaluate fully; granted-token
            // read-back (aura buffs) needs the game's own resolver-backed evaluator, which resolvers
            // do not receive today - recorded gap in the #191 ledger.
            var evaluator = new Rules.Dispatch.RuleEvaluator(new ProbabilisticDiceRoller());
            var planner = new TacticianPlanner(tableState, evaluator);

            // A4-1: activation order by urgency (also announces the active unit to the planner).
            registry.RegisterResolver(new Resolvers.TacticianActivationResolver(tableState, evaluator, planner));

            // A4-2: the (action x macro-action) pair is planned once at Choose Action and played out
            // at the movement request; solo-rules instances are the per-request fallbacks (G3).
            registry.RegisterResolver(new Resolvers.TacticianActionResolver(planner,
                new FDG.Ai.Resolvers.AiStringSelectionResolver(tableState, playerID)));
            registry.RegisterResolver(new Resolvers.TacticianMovementResolver(planner, tableState,
                new FDG.Ai.Resolvers.AiDefineMovementResolver(tableState, playerID)));

            // A4-3: value-weighted target choice (shooting + melee defender).
            registry.RegisterResolver(new Resolvers.TacticianRangedAttackResolver(evaluator,
                new FDG.Ai.Resolvers.AiChooseRangedAttackResolver()));
            registry.RegisterResolver(new Resolvers.TacticianMeleeDefenderResolver(tableState, evaluator, planner));

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
