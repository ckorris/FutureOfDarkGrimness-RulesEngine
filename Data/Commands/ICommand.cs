
namespace FDG.Data.Commands
{
    public interface ICommand
    {
        void Execute(CommandProcessor processor);
    }
}
