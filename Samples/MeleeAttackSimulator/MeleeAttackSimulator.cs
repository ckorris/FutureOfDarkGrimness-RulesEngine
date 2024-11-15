
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
            _meleeContext.BeginNewAttack(attackingUnit, defendingUnit);
            _stateMachine.Start(_meleeStage);
        }



        private void CreateStateMachine()
        {
            _offerStrikeBackHandler = new BasicTesterOfferStrikeBackHandler(false); //TEMP false.

            StageHandlerRegistry handlers = new StageHandlerRegistry()
                .RegisterHandle<IChooseActionHandler>(new BasicTesterChooseActionHandler())
                .RegisterHandle<IMovementHandler>(new BasicTesterMovementHandler())
                .RegisterHandle<IChooseMeleeWeaponHandler>(new BasicTesterChooseWeaponHandler())
                .RegisterHandle<IAssignWoundsHandler>(new BasicTesterAssignWoundsHandler())
                .RegisterHandle<IOfferStrikeBackHandler>(_offerStrikeBackHandler);


            SingleCombatHandlers singleCombatHandlers = new SingleCombatHandlers(new BasicTesterAssignWoundsHandler());

            GameContext gameContext = new GameContext(_textOutput, _diceRoller, handlers);

            _stateMachine = new StateMachine();
            IUnitActionContext unitActionContext = new UnitActionContext(gameContext);
            _meleeContext = new MeleeContext(gameContext);

            _meleeStage = new MeleeStage(_stateMachine, unitActionContext, _meleeContext);
            _meleeStage.AssignExitStage(new EmptyEndStage(_stateMachine));
        }
    }
}
