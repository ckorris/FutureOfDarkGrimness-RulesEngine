
using FDG.Stages;

namespace FDG.Samples
{
    public class RangedAttackSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;


        private IRangedContext _rangedContext;
        private StateMachine _stateMachine;
        private ShootStage _shootStage;

        public RangedAttackSimulator(ERandomnessType randomnessType)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine();
        }

        public RangedAttackSimulator(ITextOutput textOutput, ERandomnessType randomnessType)
        {
            _textOutput = textOutput;
            _diceRoller = SampleUtilities.GetDiceRoller(randomnessType);

            CreateStateMachine();
        }

        public RangedAttackSimulator(ITextOutput textOutput, IDiceRoller diceRoller)
        {
            _textOutput = textOutput;
            _diceRoller = diceRoller;

            CreateStateMachine();
        }

        public RangedAttackSimulator(IDiceRoller diceRoller)
        {
            _textOutput = new BasicConsoleLogger();
            _diceRoller = diceRoller;

            CreateStateMachine();
        }

        public void SimulateRangedAttack(IUnit attackingUnit, IUnit defendingUnit)
        {
            _rangedContext.BeginNewAttack(attackingUnit, new List<IUnit>() { defendingUnit });
            _stateMachine.Start(_shootStage);
        }

        private void CreateStateMachine()
        {
            StageHandlerRegistry handlers = new StageHandlerRegistry()
                .RegisterHandle<IChooseActionHandler>(new BasicTesterChooseActionHandler())
                .RegisterHandle<IMovementHandler>(new BasicTesterMovementHandler())
                .RegisterHandle<IChooseRangedWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IChooseRangedTargetHandler>(new BasicTesterChooseRangedTargetHandler())
                .RegisterHandle<IAssignWoundsHandler>(new BasicTesterAssignWoundsHandler());

            _stateMachine = new StateMachine();
            IUnitActionContext unitActionContext = new UnitActionContext(_textOutput, _diceRoller, handlers);
            _rangedContext = new RangedContext(_textOutput, _diceRoller, handlers);

            _shootStage = new ShootStage(_stateMachine, unitActionContext, _rangedContext);
            _shootStage.AssignExitStage(new EmptyEndStage(_stateMachine));
        }
    }
}