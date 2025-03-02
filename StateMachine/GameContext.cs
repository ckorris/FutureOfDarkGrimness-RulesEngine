
using FDG.Data;
using FDG.Players;

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

        public IPlayerRequestByID PlayerRequester { get; }

        public StageHandlerRegistry Handlers { get; }

        public TableState TableState { get; }

        //TODO: Maybe emove these from being accessible, if the engine layer
        //is to have access to it (I'm undecided). But that would
        //mean redoing lots of stages, and I'm on an airplane as I type this.
        public IReadWriteableGameDataStore GameDataStore { get; }
    }

    public class GameContext : IGameContext
    {
        IGameContext IGameContextAccessor.GameContext => this;

        public ITextOutput TextOutput { get; }

        public IDiceRoller DiceRoller { get; }

        public IPlayerRequestByID PlayerRequester { get; }

        public StageHandlerRegistry Handlers { get; }

        public TableState TableState { get; }

        public IReadWriteableGameDataStore GameDataStore { get; }


        public GameContext(ITextOutput textOutput, IDiceRoller diceRoller,
                IPlayerRequestByID playerRequester,
                StageHandlerRegistry handlers, TableState tableState,
                IReadWriteableGameDataStore gameDataStore)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            PlayerRequester = playerRequester;
            Handlers = handlers;
            TableState = tableState;
            GameDataStore = gameDataStore;
        }
    }
}