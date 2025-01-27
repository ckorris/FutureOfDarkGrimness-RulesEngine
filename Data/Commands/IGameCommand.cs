
namespace FDG.Data.Commands
{
    public interface IGameCommand
    {
        void Execute(CommandProcessor processor);
    }
}
