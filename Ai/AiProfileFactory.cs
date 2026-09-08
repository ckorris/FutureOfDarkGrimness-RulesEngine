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
            bool seeThroughFriendlyUnits = false, Tactician.Search.UctOptions? searchBudget = null,
            Tactician.Search.IPositionEvaluator? evaluator = null) =>
            BuildRegistry(profile, tableState, playerID, out _, seed, slotID, decisionLog,
                seeThroughFriendlyUnits, searchBudget, evaluator);

        /// <summary>
        /// Same as the other overload, plus the driving <see cref="Tactician.TacticianPlanner"/>
        /// when <paramref name="profile"/> is Tactician (null otherwise) - #191 C1 exporter reads
        /// chosen_macro off it.
        /// </summary>
        public static IStageResolverRegistry BuildRegistry(EAiProfile profile, ITableState tableState,
            PlayerID playerID, out Tactician.TacticianPlanner? planner, int? seed = null, int slotID = 0,
            Action<string>? decisionLog = null, bool seeThroughFriendlyUnits = false,
            Tactician.Search.UctOptions? searchBudget = null,
            Tactician.Search.IPositionEvaluator? evaluator = null)
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
                            Search = searchBudget ?? DefaultSearchBudget,
                            Evaluator = evaluator ?? StrategistLeafOverride(decisionLog) ?? DefaultStrategistLeaf },
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
        /// The leaf a Strategist scores search positions with when no caller and no dev override
        /// names one (#191 step 15b): the learned net in
        /// <see cref="BuiltInAssets.BuiltInAssetHelper.STRATEGIST_LEAF_PATH"/>, which REPLACED the
        /// hand-weighted evaluator here on 2026-09-07 (owner's call) after the C4 slice measured it
        /// +8.9 pooled at the benchmark budget and +6.0 at the interactive budget over 960 games.
        /// <para>
        /// Loaded once and shared: the forward pass allocates its own buffers per call and the
        /// weights are never mutated, so one instance is safe across the search's root workers and
        /// across concurrent games - which matters, because a bench builds a registry per slot per
        /// game and re-parsing 400 KB of JSON each time would cost more than the search it feeds.
        /// </para>
        /// <para>
        /// A schema mismatch throws rather than falling back: the asset and
        /// <see cref="Tactician.Learning.PositionEncoder"/> are committed together, so a mismatch
        /// means a feature bump landed without a retrain. `StrategistLeafAssetTests` fails first and
        /// loudly, long before anyone reaches this path in a game.
        /// </para>
        /// <para>
        /// The plain <see cref="EAiProfile.Tactician"/> is deliberately NOT affected. It scores its
        /// macro-actions with the hand-weighted evaluator and it is both the benchmark opponent and
        /// the policy the search simulates with; changing it would silently move every measurement
        /// the campaign is calibrated against.
        /// </para>
        /// </summary>
        public static Tactician.Search.IPositionEvaluator DefaultStrategistLeaf => ShippedLeaf.Value;

        private static readonly Lazy<Tactician.Search.MlpPositionEvaluator> ShippedLeaf = new(() =>
            Tactician.Search.MlpPositionEvaluator.FromJson(System.Text.Encoding.UTF8.GetString(
                BuiltInAssets.BuiltInAssetHelper.GetEmbeddedResource(
                    BuiltInAssets.BuiltInAssetHelper.STRATEGIST_LEAF_PATH))));

        /// <summary>
        /// Dev-only override (#191 C4): the environment variable naming a
        /// <see cref="Tactician.Search.MlpPositionEvaluator"/> weights file for a Strategist built
        /// with no caller-supplied leaf. Unset (always, in normal play and CI) the leaf stays
        /// <see cref="DefaultStrategistLeaf"/>, the shipped net. Use it to try a CANDIDATE net
        /// without shipping it - plan invariant G9, an unpromoted net never becomes the default.
        /// </summary>
        public const string StrategistWeightsEnvVar = "FDG_STRATEGIST_WEIGHTS";

        /// <summary>
        /// The learned leaf named by <see cref="StrategistWeightsEnvVar"/>, or null (= the default
        /// leaf) when the variable is unset or empty. A path that names a MISSING file also yields
        /// null, loudly; a file that fails to parse throws (the caller asked for a net and did not
        /// get one - never fall back silently). One line says which file loaded, through
        /// <paramref name="decisionLog"/> when the caller has one, else the console.
        /// </summary>
        public static Tactician.Search.IPositionEvaluator? StrategistLeafOverride(Action<string>? decisionLog = null)
        {
            string? path = Environment.GetEnvironmentVariable(StrategistWeightsEnvVar);
            if (string.IsNullOrWhiteSpace(path)) return null;
            Action<string> log = decisionLog ?? Console.WriteLine;
            if (!File.Exists(path))
            {
                log($"Strategist: {StrategistWeightsEnvVar} names a missing file, shipped leaf kept: {path}");
                return null;
            }
            Tactician.Search.MlpPositionEvaluator net = Tactician.Search.MlpPositionEvaluator.FromFile(path);
            log($"Strategist: learned leaf loaded from {StrategistWeightsEnvVar}={Path.GetFullPath(path)}");
            return net;
        }

        /// <summary>
        /// What a Strategist plays under when no caller names a budget: the plan's "5-10s vs
        /// humans", with root parallelism on (design sec 6 - the workers are an ensemble over
        /// determinizations, so this is a correctness setting as much as a speed one).
        /// </summary>
        public static Tactician.Search.UctOptions DefaultSearchBudget =>
            Tactician.Search.UctOptions.Interactive with { Workers = DefaultSearchWorkers };

        /// <summary>
        /// The plan's four root workers, bounded by the machine (#191 R9): a search runs inside a
        /// live game whose front end and engine need a core of their own, so on a small laptop the
        /// ensemble shrinks rather than pinning every core for the whole budget. Never below one.
        /// </summary>
        public static int DefaultSearchWorkers => Math.Clamp(Environment.ProcessorCount - 1, 1, 4);

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
