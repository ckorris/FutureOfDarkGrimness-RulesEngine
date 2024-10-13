
namespace FDG.StateMachine
{
    public interface IMainPhaseContext : ICommonContextItems
    {
        public IReconcileNewTurnHandler ReconcileNewTurnHandler { get; }

        public IStartOfTurnExtraActionsHandler StartOfTurnExtraActionsHandler { get; }
    }

    public class MainPhaseContext : IMainPhaseContext
    {
        public IReconcileNewTurnHandler ReconcileNewTurnHandler { get; private set; }

        public IStartOfTurnExtraActionsHandler StartOfTurnExtraActionsHandler { get; private set; }

        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public MainPhaseContext(IReconcileNewTurnHandler reconcileNewTurnHandler,
            IStartOfTurnExtraActionsHandler startOfTurnExtraActionsHandler, ITextOutput textOutput, IDiceRoller diceRoller)
        {
            ReconcileNewTurnHandler = reconcileNewTurnHandler;
            StartOfTurnExtraActionsHandler = startOfTurnExtraActionsHandler;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }
    }
}