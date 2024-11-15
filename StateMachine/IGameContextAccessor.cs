
using FDG.Stages;

namespace FDG
{
    public interface IGameContextAccessor
    {
        public IGameContext GameContext { get; }

    }

    public static class IGameContextAccessorExtensions
    {
        public static void Log(this IGameContextAccessor context, string message)
        {
            context.GameContext.TextOutput.Log(message);
        }

        public static THandler GetHandler<THandler>(this IGameContextAccessor context) where THandler : class
        {
            return context.GameContext.Handlers.GetHandlerOfType<THandler>();
        }

        public static ITextOutput TextOutput(this IGameContextAccessor context)
        {
            return context.GameContext.TextOutput;
        }

        public static IDiceRoller DiceRoller(this IGameContextAccessor context)
        {
            return context.GameContext.DiceRoller;
        }

        public static StageHandlerRegistry Handlers(this IGameContextAccessor context)
        {
            return context.GameContext.Handlers;
        }

        public static ITableState TableState(this IGameContextAccessor context)
        {
            return context.GameContext.TableState;
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