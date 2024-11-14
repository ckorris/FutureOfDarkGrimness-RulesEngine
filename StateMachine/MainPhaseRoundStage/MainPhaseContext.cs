
namespace FDG.Stages
{
    public interface IMainPhaseContext : ICommonContextItems
    {

    }

    public class MainPhaseContext : IMainPhaseContext
    {
        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public StageHandlerRegistry Handlers { get; }

        public MainPhaseContext(ITextOutput textOutput, IDiceRoller diceRoller, 
            StageHandlerRegistry handlers)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            Handlers = handlers;
        }
    }
}