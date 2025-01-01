
using FDG.Stages;

namespace FDG.Samples
{
    public class RangedAttackSimulator
    {
        private ITextOutput _textOutput;
        private IDiceRoller _diceRoller;

        private IUnitActionContext _unitActionContext;
        private ICombatActionContext _rangedContext;
        //private StateMachine _stateMachine;
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
            _rangedContext = new CombatActionContext(attackingUnit);
            _rangedContext.BeginNewAttack(defendingUnit);
            _unitActionContext.Reset(attackingUnit);
            _shootStage.Enter(_unitActionContext);
        }

        private void CreateStateMachine()
        {
            StageHandlerRegistry handlers = new StageHandlerRegistry()
                .RegisterHandle<IChooseActionHandler>(new BasicTesterChooseActionHandler(BasicTesterChooseActionHandler.ETestActionChoice.Ranged))
                .RegisterHandle<IChooseRangedWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IChooseRangedTargetHandler>(new BasicTesterChooseRangedTargetHandler())
                .RegisterHandle<IAssignWoundsHandler>(new BasicTesterAssignWoundsHandler());

            //_stateMachine = new StateMachine();

            //For now, make an empty TableState. May need to be updated later.
            TableState tableState = new TableState();

            GameContext gameContext = new GameContext(_textOutput, _diceRoller, handlers, tableState);

            _unitActionContext = new UnitActionContext(gameContext);


            _shootStage = new ShootStage(gameContext, null);
            //_shootStage.AssignExitStage(new EmptyEndStage(_stateMachine));
        }
    }
}