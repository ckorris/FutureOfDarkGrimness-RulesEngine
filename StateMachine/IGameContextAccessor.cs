using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;

namespace FDG
{
    public interface IGameContextAccessor
    {
        public IGameContext GameContext { get; }

    }

    public static class IGameContextAccessorExtensions
    {
        public static void Log(this IGameContextAccessor contextAccessor, string message, TextColor? color = null)
        {
            contextAccessor.GameContext.TextOutput.Log(message, color);
        }

        /// <summary>
        /// Log a developer-facing detail line to the Debug category — hidden in the front end unless the
        /// player turns the Debug toggle on. Same call shape as <see cref="Log"/>.
        /// </summary>
        public static void LogDebug(this IGameContextAccessor contextAccessor, string message, TextColor? color = null)
        {
            contextAccessor.GameContext.TextOutput.LogDebug(message, color);
        }

        /// <summary>
        /// Announce a beat to the player: shows a big flashing banner (<see cref="BannerBeat"/>) AND
        /// writes the same text to the regular log, both in <paramref name="color"/> (white by
        /// default). Manually called, like <see cref="Log"/> — not automatic per stage. Awaitable so
        /// the engine paces the banner before continuing.
        /// </summary>
        public static Task Announce(this IGameContextAccessor contextAccessor, string text,
            TextColor? color = null, CancellationToken ct = default)
        {
            TextColor resolved = color ?? TextColor.White;
            contextAccessor.Log(text, resolved);
            return contextAccessor.GameContext.Presenter.Present(new BannerBeat(text, resolved), ct);
        }

        public static ITextOutput TextOutput(this IGameContextAccessor contextAccessor)
        {
            return contextAccessor.GameContext.TextOutput;
        }

        public static IDiceRoller DiceRoller(this IGameContextAccessor contextAccessor)
        {
            return contextAccessor.GameContext.DiceRoller;
        }

        public static ITableState TableState(this IGameContextAccessor contextAccessor)
        {
            return contextAccessor.GameContext.TableState;
        }

        public static IReadWriteableGameDataStore GameDataStore(this IGameContextAccessor contextAccessor)
        {
            return contextAccessor.GameContext.GameDataStore;
        }

        public static IPlayerRequestByID PlayerRequester(this IGameContextAccessor contextAccessor)
        {
            return contextAccessor.GameContext.PlayerRequester;
        }

        /// <summary>Human-readable name for a player (from the lobby/slot info), or a sensible fallback.</summary>
        public static string GetPlayerName(this IGameContextAccessor contextAccessor, PlayerID playerID)
        {
            IPlayerSlotInfo? slot = contextAccessor.GameContext.TableState.Players.Objects
                .FirstOrDefault(p => p.PlayerID == playerID);
            if (slot != null && slot.IsFilled && !string.IsNullOrWhiteSpace(slot.Name))
            {
                return slot.Name;
            }
            // Unfilled slots carry the "[Empty]" placeholder name (e.g. headless play with no lobby);
            // fall back to the team number rather than leaking that placeholder into a banner.
            return slot != null ? $"Team {slot.TeamNumber}" : "A player";
        }

        /// <summary>Name to represent a team in announcements — its first player's name, falling back to "Team N".</summary>
        public static string GetTeamLeadName(this IGameContextAccessor contextAccessor, ITeam team)
        {
            if (team.Players.Count > 0)
            {
                IPlayerSlotInfo? slot = contextAccessor.GameContext.TableState.Players.Objects
                    .FirstOrDefault(p => p.PlayerID == team.Players[0]);
                if (slot != null && slot.IsFilled && !string.IsNullOrWhiteSpace(slot.Name))
                {
                    return slot.Name;
                }
            }
            return $"Team {team.TeamNumber}";
        }
    }


    /*
    public interface ICommonContextItems
    {
        public ITextOutput TextOutput { get; }

        public IDiceRoller DiceRoller { get; }

        public StageHandlerRegistry Handlers { get; }
    }

    public static class ICommonContextItemsExtensions
    {
        public static void Log(this ICommonContextItems context, string message)
        {
            context.TextOutput.Log(message);
        }

        public static THandler GetHandler<THandler>(this ICommonContextItems context) where THandler : class
        {
            return context.Handlers.GetHandlerOfType<THandler>();
        }
    }
    */
}