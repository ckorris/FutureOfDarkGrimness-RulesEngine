

namespace FDG.Stages
{

    public interface IPlayerTurnContext : ICommonContextItems
    {
    }

    public class PlayerTurnContext : IPlayerTurnContext
    {
        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public StageHandlerRegistry Handlers { get; }

        public PlayerTurnContext(ITextOutput textOutput, IDiceRoller diceRoller, 
            StageHandlerRegistry handlers)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            Handlers = handlers;
        }
    }
}