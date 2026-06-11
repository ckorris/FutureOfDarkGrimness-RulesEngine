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