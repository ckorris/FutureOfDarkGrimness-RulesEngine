using FDG.Stages;
using FDG_Stride.StageHandlers;
using System.Collections.Generic;
using System.Windows.Documents;

namespace FDG.Samples
{
    public class ChooseActionSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;

        private MainUnitActionStage _mainUnitActionStage;

        private IPlayerTurnContext _playerTurnContext;
        private IUnitActionContext _unitActionContext;
        private OptionChooserMultiHandler _optionChooserMultiHandler;
        private IDefinePathHandler _definePathHandler;
        private IExecuteMoveHandler _executeMoveHandler;

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IDefinePathHandler definePathHandler,
            IExecuteMoveHandler executeModeHandler, ERandomnessType randomnessType)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            _optionChooserMultiHandler = actionHandler;
            _definePathHandler = definePathHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IDefinePathHandler definePathHandler,
            IExecuteMoveHandler executeModeHandler, ITextOutput textOutput, IDiceRoller diceRoller)
        {
            _textOutput = textOutput;
            _diceRoller = diceRoller;

            _optionChooserMultiHandler = actionHandler;
            _definePathHandler = definePathHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IDefinePathHandler definePathHandler,
            IExecuteMoveHandler executeModeHandler, IDiceRoller diceRoller)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = diceRoller;

            _optionChooserMultiHandler = actionHandler;
            _definePathHandler = definePathHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public ChooseActionSimulator(OptionChooserMultiHandler actionHandler, IDefinePathHandler moveHandler,
            IExecuteMoveHandler executeModeHandler, ITextOutput textOutput, ERandomnessType randomnessType)
        {
            _textOutput = textOutput;
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            _optionChooserMultiHandler = actionHandler;
            _definePathHandler = moveHandler;
            _executeMoveHandler = executeModeHandler;
        }

        public void SimulateAction(IUnit activatedUnit, TableState tableState)
        {


            StageHandlerRegistry handlers = new StageHandlerRegistry()
                //To test.
                .RegisterHandle<IChooseActionHandler>(_optionChooserMultiHandler)
                .RegisterHandle<IChooseMeleeDefenderHandler>(_optionChooserMultiHandler)
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
}