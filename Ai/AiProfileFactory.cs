using FDG.Ai.Tactician;
using FDG.GameModel;
using FDG.Players;
using FDG.StageResolution;

namespace FDG.Ai
{
    /// <summary>
    /// The one profile -&gt; implementation dispatch point: launch paths that select an AI by
    /// <see cref="EAiProfile"/> (headless CLI, scenario resume, FdgLab; lobby in A6) go through
    /// here instead of switching locally.
    /// </summary>
    public static class AiProfileFactory
    {
        /// <param name="seed">
        /// The GAME's seed (GameSettings.DiceSeed), not a per-player one — each profile derives the
        /// player's own stream from it by <paramref name="slotID"/> (#193). Null = unseeded.
        /// </param>
        /// <param name="slotID">The player's slot index. Stable across runs and save/resume, unlike the PlayerID GUID.</param>
        /// <param name="decisionLog">Analysis sink (#191 tooling): profiles that plan (Tactician,
        /// Gunline) narrate each Choose Action decision into it. Null in normal play.</param>
        /// <param name="seeThroughFriendlyUnits">The game's #384 see-through-allies house rule
        /// (<see cref="GameSettings.SeeThroughFriendlyUnits"/>), so a planning profile's sight
        /// tests match what the shoot stage will rule. Default false = the official rules.</param>
        public static IStageResolverRegistry BuildRegistry(EAiProfile profile, ITableState tableState,
            PlayerID playerID, int? seed = null, int slotID = 0, Action<string>? decisionLog = null,
            bool seeThroughFriendlyUnits = false, Tactician.Search.UctOptions? searchBudget = null) =>
            BuildRegistry(profile, tableState, playerID, out _, seed, slotID, decisionLog,
                seeThroughFriendlyUnits, searchBudget);

        /// <summary>
        /// Same as the other overload, plus the driving <see cref="Tactician.TacticianPlanner"/>
        /// when <paramref name="profile"/> is Tactician (null otherwise) - #191 C1 exporter reads
        /// chosen_macro off it.
        /// </summary>
        public static IStageResolverRegistry BuildRegistry(EAiProfile profile, ITableState tableState,
            PlayerID playerID, out Tactician.TacticianPlanner? planner, int? seed = null, int slotID = 0,
            Action<string>? decisionLog = null, bool seeThroughFriendlyUnits = false,
            Tactician.Search.UctOptions? searchBudget = null)
        {
            planner = null;
            switch (profile)
            {
                case EAiProfile.SoloRules:
                    return AiResolverRegistryFactory.BuildSoloRules(tableState, playerID, seed, slotID);
                case EAiProfile.Tactician:
                    IStageResolverRegistry registry = TacticianResolverRegistryFactory.Build(tableState, playerID,
                        new TacticianOptions { Seed = seed, SlotID = slotID, DecisionLog = decisionLog,
                            SeeThroughFriendlyUnits = seeThroughFriendlyUnits }, out Tactician.TacticianPlanner built);
                    planner = built;
                    return registry;
                case EAiProfile.Strategist:
                    // B5 (#191 step 9): the Tactician's own registry, with a search deciding each
                    // activation. Default budget is the plan's human-facing one (5-10s); FdgLab
                    // passes UctOptions.Benchmark so a 100-game cell finishes this decade.
                    IStageResolverRegistry searched = TacticianResolverRegistryFactory.Build(tableState,
                        playerID, new TacticianOptions { Seed = seed, SlotID = slotID,
                            DecisionLog = decisionLog, SeeThroughFriendlyUnits = seeThroughFriendlyUnits,
                            Search = searchBudget ?? DefaultSearchBudget },
                        out Tactician.TacticianPlanner searchPlanner);
                    planner = searchPlanner;
                    return searched;
                case EAiProfile.Gunline:
                    return Gunline.GunlineResolverRegistryFactory.Build(tableState, playerID, seed, slotID, decisionLog);
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown AI profile.");
            }
        }

        /// <summary>
        /// What a Strategist plays under when no caller names a budget: the plan's "5-10s vs
        /// humans", with root parallelism on (design sec 6 - the workers are an ensemble over
        /// determinizations, so this is a correctness setting as much as a speed one).
        /// </summary>
        public static Tactician.Search.UctOptions DefaultSearchBudget =>
            Tactician.Search.UctOptions.Interactive with { Workers = 4 };

        public static ComputerPlayerController CreateController(EAiProfile profile, string name, PlayerID id,
            FDGGame_AsLocal localGame, int? seed = null, int slotID = 0,
            bool seeThroughFriendlyUnits = false, Tactician.Search.UctOptions? searchBudget = null)
        {
            IStageResolverRegistry registry = BuildRegistry(profile, localGame.TableState, id, seed, slotID,
                seeThroughFriendlyUnits: seeThroughFriendlyUnits, searchBudget: searchBudget);
            return new ComputerPlayerController(name, id, localGame, registry);
        }
    }
}
