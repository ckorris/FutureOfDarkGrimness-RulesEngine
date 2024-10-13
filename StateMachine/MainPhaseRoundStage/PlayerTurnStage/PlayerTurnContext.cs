

namespace FDG.Stages
{

    public interface IPlayerTurnContext : ICommonContextItems
    {
        public IChooseUnitToActivateHandler ChooseUnitToActivateHandler { get; }
    }

    public class PlayerTurnContext : IPlayerTurnContext
    {
        public IChooseUnitToActivateHandler ChooseUnitToActivateHandler { get; private set; }

        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public PlayerTurnContext(IChooseUnitToActivateHandler chooseUnitToActivateHandler, 
            ITextOutput textOutput, IDiceRoller diceRoller)
        {
            ChooseUnitToActivateHandler = chooseUnitToActivateHandler;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }
    }
}