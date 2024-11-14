
namespace FDG
{

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
}