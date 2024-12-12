
using FDG.Stages;
using Stride.Games;

namespace FDG.Samples
{
    public class MeleeAttackSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;

        private IUnitActionContext _unitActionContext;
        private IMeleeContext _meleeContext;

        private GameContext _gameContext;
        private MeleeStage _meleeStage;
        

        BasicTesterOfferStrikeBackHandler _offerStrikeBackHandler;

        public MeleeAttackSimulator(ERandomnessType randomnessType)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine();
        }

        public MeleeAttackSimulator(ITextOutput textOutput, ERandomnessType randomnessType)
        {
            _textOutput = textOutput;
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine();
        }

        public MeleeAttackSimulator(ITextOutput textOutput, IDiceRoller diceRoller)
        {
            _textOutput = textOutput;
            _diceRoller = diceRoller;

            CreateStateMachine();
        }

        public MeleeAttackSimulator(IDiceRoller diceRoller)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = diceRoller;

            CreateStateMachine();
        }

        public void SimulateMeleeAttack(IUnit attackingUnit, IUnit defendingUnit, bool defenderStrikesBack)
        {
            _offerStrikeBackHandler.StrikeBack = defenderStrikesBack;
            _meleeContext = new MeleeContext(attackingUnit);
            _meleeContext.BeginNewAttack(defendingUnit);
            _unitActionContext.Reset(attackingUnit);
            _meleeStage.Enter(_unitActionContext);
        }


        private void CreateStateMachine()
        {
            _offerStrikeBackHandler = new BasicTesterOfferStrikeBackHandler(false); //TEMP false.

            StageHandlerRegistry handlers = new StageHandlerRegistry()
                .RegisterHandle<IChooseActionHandler>(new BasicTesterChooseActionHandler(BasicTesterChooseActionHandler.ETestActionChoice.Melee))
                .RegisterHandle<IMovementHandler>(new BasicTesterMovementHandler())
                .RegisterHandle<IChooseMeleeWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IAssignWoundsHandler>(new BasicTesterAssignWoundsHandler())
                .RegisterHandle<IOfferStrikeBackHandler>(_offerStrikeBackHandler);

            SingleCombatHandlers singleCombatHandlers = new SingleCombatHandlers(new BasicTesterAssignWoundsHandler());

            //For now, make an empty TableState. May need to be updated later.
            TableState tableState = new TableState();

            _gameContext = new GameContext(_textOutput, _diceRoller, handlers, tableState);

            //_stateMachine = new StateMachine();
            _unitActionContext = new UnitActionContext(_gameContext);
            

            _meleeStage = new MeleeStage(_gameContext, null);
            //_meleeStage.AssignExitStage(new EmptyEndStage(_stateMachine));
        }
    }
}
