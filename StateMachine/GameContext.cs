
namespace FDG
{
    /// <summary>
    /// References to required objects for running the state machine.
    /// <para>This is _not_ meant to be serialized.</para>
    /// </summary>
    public interface IGameContext : IGameContextAccessor
    {
        public ITextOutput TextOutput { get; }

        public IDiceRoller DiceRoller { get; }

        public StageHandlerRegistry Handlers { get; }

        public TableState TableState { get; }
    }

    public class GameContext : IGameContext
    {
        IGameContext IGameContextAccessor.GameContext => this;

        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public StageHandlerRegistry Handlers { get; }

        public TableState TableState { get; private set; }

        public GameContext(ITextOutput textOutput, IDiceRoller diceRoller,
            StageHandlerRegistry handlers, TableState tableState)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            Handlers = handlers;
            TableState = tableState;
        }
    }
}