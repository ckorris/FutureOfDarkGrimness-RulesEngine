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
            bool seeThroughFriendlyUnits = false) => profile switch
        {
            EAiProfile.SoloRules => AiResolverRegistryFactory.BuildSoloRules(tableState, playerID, seed, slotID),
            EAiProfile.Tactician => TacticianResolverRegistryFactory.Build(tableState, playerID,
                new TacticianOptions { Seed = seed, SlotID = slotID, DecisionLog = decisionLog,
                    SeeThroughFriendlyUnits = seeThroughFriendlyUnits }),
            EAiProfile.Gunline => Gunline.GunlineResolverRegistryFactory.Build(tableState, playerID,
                seed, slotID, decisionLog),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown AI profile."),
        };

        public static ComputerPlayerController CreateController(EAiProfile profile, string name, PlayerID id,
            FDGGame_AsLocal localGame, int? seed = null, int slotID = 0,
            bool seeThroughFriendlyUnits = false)
        {
            IStageResolverRegistry registry = BuildRegistry(profile, localGame.TableState, id, seed, slotID,
                seeThroughFriendlyUnits: seeThroughFriendlyUnits);
            return new ComputerPlayerController(name, id, localGame, registry);
        }
    }
}
