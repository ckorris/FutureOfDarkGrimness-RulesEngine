using FDG.Stages;

namespace FDG.Samples
{
    /*
    public class ChooseActionSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;

        private MainUnitActionStage _mainUnitActionStage;

        private IPlayerTurnContext _playerTurnContext;
        private IUnitActionContext _unitActionContext;
        private IChooseActionHandler _chooseActionHandler;
        private IChooseMeleeDefenderHandler _chooseMeleeDefenderHandler;
        private IDefinePathHandler _definePathHandler;
        private IExecuteMoveHandler _executeMoveHandler;

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IChooseMeleeDefenderHandler meleeDefenderHandler,
            IDefinePathHandler definePathHandler, IExecuteMoveHandler executeModeHandler, ERandomnessType randomnessType)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            _chooseActionHandler = actionHandler;
            _chooseMeleeDefenderHandler = meleeDefenderHandler;
            _definePathHandler = definePathHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IChooseMeleeDefenderHandler meleeDefenderHandler,
            IDefinePathHandler definePathHandler, IExecuteMoveHandler executeModeHandler, 
            ITextOutput textOutput, IDiceRoller diceRoller)
        {
            _textOutput = textOutput;
            _diceRoller = diceRoller;

            _chooseActionHandler = actionHandler;
            _chooseMeleeDefenderHandler = meleeDefenderHandler;
            _definePathHandler = definePathHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IChooseMeleeDefenderHandler meleeDefenderHandler,
            IDefinePathHandler definePathHandler, IExecuteMoveHandler executeModeHandler, IDiceRoller diceRoller)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = diceRoller;

            _chooseActionHandler = actionHandler;
            _chooseMeleeDefenderHandler = meleeDefenderHandler;
            _definePathHandler = definePathHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public ChooseActionSimulator(IChooseActionHandler actionHandler, IChooseMeleeDefenderHandler meleeDefenderHandler,
            IDefinePathHandler moveHandler, IExecuteMoveHandler executeModeHandler, 
            ITextOutput textOutput, ERandomnessType randomnessType)
        {
            _textOutput = textOutput;
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            _chooseActionHandler = actionHandler;
            _chooseMeleeDefenderHandler = meleeDefenderHandler;
            _definePathHandler = moveHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public void SimulateAction(IUnit activatedUnit, List<Army> armies)
        {
            StageHandlerRegistry handlers = new StageHandlerRegistry()
                //To test.
                .RegisterHandle<IChooseActionHandler>(_chooseActionHandler)
                .RegisterHandle<IChooseMeleeDefenderHandler>(_chooseMeleeDefenderHandler)
                //Melee.
                .RegisterHandle<IChooseMeleeWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IOfferStrikeBackHandler>(new BasicTesterOfferStrikeBackHandler(false))
                //Ranged.
                .RegisterHandle<IChooseRangedWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IChooseRangedTargetHandler>(new BasicTesterChooseRangedTargetHandler())
                //Both.
                .RegisterHandle<IAssignWoundsHandler>(new BasicTesterAssignWoundsHandler())
                //Movement.
                .RegisterHandle<IDefinePathHandler>(_definePathHandler)
                .RegisterHandle<IExecuteMoveHandler>(_executeMoveHandler);

            //For now, make an empty TableState. May need to be updated later.
            GameContext gameContext = new GameContext(_textOutput, _diceRoller, handlers, tableState);
            _playerTurnContext = new PlayerTurnContext(gameContext);

            EmptyParent<IPlayerTurnContext> emptyParent = new EmptyParent<IPlayerTurnContext>();

            _mainUnitActionStage = new MainUnitActionStage(gameContext, emptyParent);
            _mainUnitActionStage.ToReconcileEndOfActivation.Bind(new EmptyEndStage<IPlayerTurnContext>(gameContext, emptyParent));

            _playerTurnContext.ChooseUnitToActivate(activatedUnit);
            _mainUnitActionStage.Enter(_playerTurnContext);
        }
    }
    */
}