using FDG.Stages;
using FDG_Stride.StageHandlers;

namespace FDG.Samples
{
    public class ChooseActionSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;

        private MainUnitActionStage _mainUnitActionStage;

        private IPlayerTurnContext _playerTurnContext;
        private IUnitActionContext _unitActionContext;

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IMovementHandler moveHandler,
            ERandomnessType randomnessType)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine(actionHandler, moveHandler);
        }

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IMovementHandler moveHandler,
            ITextOutput textOutput, IDiceRoller diceRoller)
        {
            _textOutput = textOutput;
            _diceRoller = diceRoller;

            CreateStateMachine(actionHandler, moveHandler);
        }

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IMovementHandler moveHandler,
            IDiceRoller diceRoller)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = diceRoller;

            CreateStateMachine(actionHandler, moveHandler);
        }

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IMovementHandler moveHandler,
            ITextOutput textOutput, ERandomnessType randomnessType)
        {
            _textOutput = textOutput;
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine(actionHandler, moveHandler);
        }

        public void SimulateAction(IUnit activatedUnit)
        {
            _playerTurnContext.ChooseUnitToActivate(activatedUnit);
            _mainUnitActionStage.Enter(_playerTurnContext);
        }

        private void CreateStateMachine(OptionChooserMultiHandler chooseActionHandler, IMovementHandler movementHandler)
        {
            StageHandlerRegistry handlers = new StageHandlerRegistry()
                //To test.
                .RegisterHandle<IChooseActionHandler>(chooseActionHandler)
                .RegisterHandle<IChooseMeleeDefenderHandler>(chooseActionHandler)
                .RegisterHandle<IMovementHandler>(movementHandler)
                //Melee.
                .RegisterHandle<IChooseMeleeWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IOfferStrikeBackHandler>(new BasicTesterOfferStrikeBackHandler(false))
                //Ranged.
                .RegisterHandle<IChooseRangedWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IChooseRangedTargetHandler>(new BasicTesterChooseRangedTargetHandler())
                //Both.
                .RegisterHandle<IAssignWoundsHandler>(new BasicTesterAssignWoundsHandler());

            //For now, make an empty TableState. May need to be updated later.
            TableState tableState = new TableState();
            GameContext gameContext = new GameContext(_textOutput, _diceRoller, handlers, tableState);
            _playerTurnContext = new PlayerTurnContext(gameContext);
            _mainUnitActionStage = new MainUnitActionStage(gameContext, null);
        }

    }
}
