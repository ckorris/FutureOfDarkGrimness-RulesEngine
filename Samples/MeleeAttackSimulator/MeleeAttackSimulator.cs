
using FDG.Stages;


namespace FDG.Samples
{
    public class MeleeAttackSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;

        private IMeleeContext _meleeContext;
        private StateMachine _stateMachine;
        private MeleeStage _meleeStage;

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

        public void SimulateMeleeAttack(IUnit attackingUnit, IUnit defendingUnit)
        {
            _meleeContext.BeginNewAttack(attackingUnit, defendingUnit);
            _stateMachine.Start(_meleeStage);
        }

        private void CreateStateMachine()
        {
            BasicTesterChooseActionHandler chooseActionHandler = new BasicTesterChooseActionHandler();
            BasicTesterMovementHandler movementHandler = new BasicTesterMovementHandler();
            BasicTesterChooseWeaponHandler chooseWeaponHandler = new BasicTesterChooseWeaponHandler();
            BasicTesterOfferStrikeBackHandler offerStrikeBackHandler = new BasicTesterOfferStrikeBackHandler(false); //TEMP false.

            SingleCombatHandlers singleCombatHandlers = new SingleCombatHandlers(new BasicTesterAssignWoundsHandler());

            _stateMachine = new StateMachine();
            IUnitActionContext unitActionContext = new UnitActionContext(chooseActionHandler, movementHandler,
                _textOutput, _diceRoller);
            _meleeContext = new MeleeContext(singleCombatHandlers, chooseWeaponHandler, offerStrikeBackHandler, _textOutput, _diceRoller);

            _meleeStage = new MeleeStage(_stateMachine, unitActionContext, _meleeContext);
            _meleeStage.AssignExitStage(new EmptyEndStage(_stateMachine));
        }
    }
}
