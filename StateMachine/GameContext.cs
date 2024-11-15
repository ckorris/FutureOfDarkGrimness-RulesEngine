
namespace FDG.Stages
{
    public interface IGameContext : IGameContextAccessor
    {
        public ITextOutput TextOutput { get; }

        public IDiceRoller DiceRoller { get; }

        public StageHandlerRegistry Handlers { get; }
    }

    public class GameContext : IGameContext
    {
        IGameContext IGameContextAccessor.GameContext => this;

        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public StageHandlerRegistry Handlers { get; }


        public GameContext(ITextOutput textOutput, IDiceRoller diceRoller,
            StageHandlerRegistry handlers)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            Handlers = handlers;
        }
    }
}