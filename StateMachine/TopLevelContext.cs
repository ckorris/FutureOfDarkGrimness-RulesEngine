
namespace FDG.Stages
{
    public interface ITopLevelContext : ICommonContextItems
    {

    }

    public class TopLevelContext : ITopLevelContext
    {

        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public StageHandlerRegistry Handlers { get; }

        public TopLevelContext(ITextOutput textOutput, IDiceRoller diceRoller,
            StageHandlerRegistry handlers)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            Handlers = handlers;
        }
    }
}