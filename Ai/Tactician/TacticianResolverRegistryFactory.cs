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
            // Wholesale delegation for now; as slices land this becomes an explicit registry mixing
            // Tactician resolvers with the remaining solo-rules ones (fallback discipline: plan G3).
            return AiResolverRegistryFactory.BuildSoloRules(tableState, playerID, options.Seed, options.SlotID);
        }

        public static ComputerPlayerController CreateController(string name, PlayerID id,
            FDGGame_AsLocal localGame, TacticianOptions options)
        {
            IStageResolverRegistry registry = Build(localGame.TableState, id, options);
            return new ComputerPlayerController(name, id, localGame, registry);
        }
    }
}
