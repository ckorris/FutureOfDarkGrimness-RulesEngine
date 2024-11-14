

namespace FDG.Stages
{

    public interface IUnitActionContext : ICommonContextItems
    {
    }

    public class UnitActionContext : IUnitActionContext
    {
        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public StageHandlerRegistry Handlers { get; }


        public UnitActionContext(ITextOutput textOutput, IDiceRoller diceRoller, StageHandlerRegistry handlers)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            Handlers = handlers;
        }
    }

}