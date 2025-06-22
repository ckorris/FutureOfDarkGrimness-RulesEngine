using FDG.Stages;
using FDG.StageResolution;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;

namespace FDG
{
    public interface IGameContextAccessor
    {
        public IGameContext GameContext { get; }

    }

    public static class IGameContextAccessorExtensions
    {
        public static void Log(this IGameContextAccessor contextAccessor, string message)
        {
            contextAccessor.GameContext.TextOutput.Log(message);
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
            return contextAccessor.GameContext.GameDataStore();
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