
namespace FDG.Stages
{

    public interface IUnitActionContext : ICommonContextItems
    {
        public IChooseActionHandler ChooseActionHandler { get; }
        public IMovementHandler MovementHandler { get; }
    }

    public class UnitActionContext : IUnitActionContext
    {
        public IChooseActionHandler ChooseActionHandler { get; private set; }
        public IMovementHandler MovementHandler { get; private set; }
        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public UnitActionContext(IChooseActionHandler chooseActionHandler, 
            IMovementHandler movementHandler, ITextOutput textOutput, IDiceRoller diceRoller)
        {
            ChooseActionHandler = chooseActionHandler;
            MovementHandler = movementHandler;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }
    }

}