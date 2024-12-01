
using System.Collections.Generic;

namespace FDG.Stages
{

    public class MainUnitActionStage : ParentStage<IPlayerTurnContext, IUnitActionContext>
    {
        public const string MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION =
            "MainUnitActionToChildChooseAction";

        public StageBinding ToReconcileEndOfActivation;

        private readonly ChooseActionStage _chooseActionStage;
        private readonly MovementStage _movementStage;

        private readonly MeleeStage _meleeStage;

        private readonly ShootStage _shootStage;

        public MainUnitActionStage(IGameContext gameContext, IStateMachineLayer<IPlayerTurnContext> parent) : base(gameContext, parent)
        {
            ToReconcileEndOfActivation = new StageBinding(this);
        }

        /*
        public MainUnitActionStage(StateMachine stateMachine, IPlayerTurnContext context,
            IUnitActionContext mainUnitActionContext, IMeleeContext meleeContext,
            IRangedContext rangedContext, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _chooseActionStage = new ChooseActionStage(stateMachine, mainUnitActionContext, this);
            _movementStage = new MovementStage(stateMachine, mainUnitActionContext, this);
            _meleeStage = new MeleeStage(stateMachine, mainUnitActionContext, meleeContext, this);
            _shootStage = new ShootStage(stateMachine, mainUnitActionContext, rangedContext, this);

            Bind(MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION, _chooseActionStage);
            _chooseActionStage.Bind(ChooseActionStage.CHOOSE_ACTION_TO_MOVEMENT_TRANSITION,
                _movementStage);
            _chooseActionStage.Bind(ChooseActionStage.CHOOSE_ACTION_TO_CHARGE_TRANSITION,
                _meleeStage);
            _chooseActionStage.Bind(ChooseActionStage.CHOOSE_ACTION_TO_SHOOT_TRANSITION,
                _shootStage);

            _movementStage.Bind(MovementStage.MOVEMENT_TO_CHOOSE_ACTION_TRANSITION,
                _chooseActionStage);
            _meleeStage.AssignExitStage(_chooseActionStage);
            _shootStage.AssignExitStage(_chooseActionStage);
        }
        */

        public override void Enter(IPlayerTurnContext context)
        {
            GameContext.Log("Main Unit Action stage entered.");

            base.Enter(context);
        }

        protected override IUnitActionContext GetNewChildContext(IPlayerTurnContext contextSelf)
        {
            UnitActionContext unitActionContext = new UnitActionContext(GameContext);
            unitActionContext.Reset(contextSelf.ActivatedUnit);
            return unitActionContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IUnitActionContext> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseActionStage(GameContext, this), out var chooseActionStage)
                .AddChild(new MovementStage(GameContext, this), out var movementStage)
                .AddChild(new MeleeStage(GameContext, this), out var meleeStage)
                .AddChild(new ShootStage(GameContext, this), out var shootStage)
                .AddSibling(nameof(ToReconcileEndOfActivation), ToReconcileEndOfActivation, out string toReconcileActivationEvent)
                .Build();

            startingChild = chooseActionStage;

            chooseActionStage.ToMovement.Bind(movementStage);
            chooseActionStage.ToCharge.Bind(meleeStage);
            chooseActionStage.ToShoot.Bind(shootStage);
            chooseActionStage.ToReconcileEndOfActivation.Bind(toReconcileActivationEvent);
            movementStage.OnFinishedMovement.Bind(chooseActionStage);
            meleeStage.OnFinishedMelee.Bind(chooseActionStage);
            shootStage.OnFinishedShooting.Bind(chooseActionStage);

            return dictionary;
        }
    }
}