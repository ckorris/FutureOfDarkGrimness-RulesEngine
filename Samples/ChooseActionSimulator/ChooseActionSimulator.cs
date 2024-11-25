using FDG.Stages;

namespace FDG.Samples
{
    public class ChooseActionSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;

        private StateMachine _stateMachine;

        private MainUnitActionStage _mainUnitActionStage;

        private IUnitActionContext _unitActionContext;

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IMovementHandler moveHandler,
            ERandomnessType randomnessType)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine(actionHandler, moveHandler);
        }

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IMovementHandler moveHandler,
            ITextOutput textOutput, IDiceRoller diceRoller)
        {
            _textOutput = textOutput;
            _diceRoller = diceRoller;

            CreateStateMachine(actionHandler, moveHandler);
        }

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IMovementHandler moveHandler,
            IDiceRoller diceRoller)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = diceRoller;

            CreateStateMachine(actionHandler, moveHandler);
        }

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IMovementHandler moveHandler,
            ITextOutput textOutput, ERandomnessType randomnessType)
        {
            _textOutput = textOutput;
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine(actionHandler, moveHandler);
        }

        public void SimulateAction(IUnit activatedUnit)
        {
            _unitActionContext.Reset(activatedUnit);
            _stateMachine.Start(_mainUnitActionStage);
        }

        private void CreateStateMachine(IChooseActionHandler chooseActionHandler, IMovementHandler movementHandler)
        {
            StageHandlerRegistry handlers = new StageHandlerRegistry()
                //To test.
                .RegisterHandle<IChooseActionHandler>(chooseActionHandler)
                .RegisterHandle<IMovementHandler>(movementHandler)
                //Melee.
                .RegisterHandle<IChooseMeleeWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IOfferStrikeBackHandler>(new BasicTesterOfferStrikeBackHandler(false))
                //Ranged.
                .RegisterHandle<IChooseRangedWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IChooseRangedTargetHandler>(new BasicTesterChooseRangedTargetHandler())
                //Both.
                .RegisterHandle<IAssignWoundsHandler>(new BasicTesterAssignWoundsHandler());

            _stateMachine = new StateMachine();

            //For now, make an empty TableState. May need to be updated later.
            TableState tableState = new TableState();
            GameContext gameContext = new GameContext(_textOutput, _diceRoller, handlers, tableState);
            IPlayerTurnContext playerTurnContext = new PlayerTurnContext(gameContext);
            _unitActionContext = new UnitActionContext(gameContext);
            IMeleeContext meleeContext = new MeleeContext(gameContext);
            IRangedContext rangedContext = new RangedContext(gameContext);

            _mainUnitActionStage = new MainUnitActionStage(_stateMachine, playerTurnContext, _unitActionContext,
                meleeContext, rangedContext);

            _mainUnitActionStage.AssignExitStage(new EmptyEndStage(_stateMachine));
        }

    }
}
