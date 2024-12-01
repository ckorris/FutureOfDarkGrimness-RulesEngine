

using System.Collections.Generic;

namespace FDG.Stages
{
    public class PlayerTurnStage : ParentStage<IMainPhaseContext, IPlayerTurnContext>
    {
        private const string PLAYER_TURN_TO_CHILD_DETERMINE_PLAYER_TURN = "PlayerTurnToChildDeterminePlayerTurn";

        public StageBinding OnTurnFinished;

        private int _enterCount = 0;

        private readonly ReconcileEndOfActivationStage _reconcileEndOfActivationStage;

        /*
        public PlayerTurnStage(StateMachine stateMachine, IMainPhaseContext mainPhaseContext,
            IPlayerTurnContext playerTurnContext, IUnitActionContext mainUnitActionContext,
            IMeleeContext meleeContext, IRangedContext rangedContext, StageBase parentState = null)
            : base(stateMachine, mainPhaseContext, parentState)
        {
            DeterminePlayerTurnStage determinePlayerTurnStage = new DeterminePlayerTurnStage(stateMachine, playerTurnContext, this);
            ChooseUnitToActivateStage chooseUnitToActivateStage = new ChooseUnitToActivateStage(stateMachine, playerTurnContext, this);
            MainUnitActionStage mainUnitActionStage = new MainUnitActionStage(stateMachine, playerTurnContext, mainUnitActionContext,
                meleeContext, rangedContext, this);
            _reconcileEndOfActivationStage = new ReconcileEndOfActivationStage(stateMachine, playerTurnContext, this);

            determinePlayerTurnStage.Bind(DeterminePlayerTurnStage.DETERMINE_PLAYER_TO_CHOOSE_UNIT_TO_ACTIVATE_TRANSITION,
                chooseUnitToActivateStage);
            chooseUnitToActivateStage.Bind(ChooseUnitToActivateStage.CHOOSE_UNIT_TO_ACTIVATE_TO_MAIN_UNIT_ACTION_TRANSITION,
                mainUnitActionStage);
            _reconcileEndOfActivationStage.Bind(ReconcileEndOfActivationStage.RECONCILE_ACTIVATION_BACK_TO_DETERMINE_PLAYER_TURN_TRANSITION,
                determinePlayerTurnStage);

            Bind(PLAYER_TURN_TO_CHILD_DETERMINE_PLAYER_TURN, determinePlayerTurnStage);

            mainUnitActionStage.AssignExitStage(_reconcileEndOfActivationStage);
        }
        */

        public PlayerTurnStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent)
            : base(gameContext, parent)
        {
            OnTurnFinished = new StageBinding(this);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IPlayerTurnContext> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DeterminePlayerTurnStage(GameContext, this), out var determinePlayerTurnStage)
                .AddChild(new ChooseUnitToActivateStage(GameContext, this), out var chooseUnitToActivateStage)
                .AddChild(new MainUnitActionStage(GameContext, this), out var mainUnitActionStage)
                .AddChild(new ReconcileEndOfActivationStage(GameContext, this), out var reconcileEndOfActivationStage)
                .AddSibling("OnTurnFinished", OnTurnFinished, out string turnFinishedEventName)
                .Build();

            startingChild = determinePlayerTurnStage;

            determinePlayerTurnStage.ToChooseUnitToActivate.Bind(chooseUnitToActivateStage.Name);
            chooseUnitToActivateStage.ToMainUnitAction.Bind(mainUnitActionStage.Name);
            mainUnitActionStage.ToReconcileEndOfActivation.Bind(reconcileEndOfActivationStage.Name);
            reconcileEndOfActivationStage.ToDeterminePlayerTurn.Bind(turnFinishedEventName);

            return dictionary;

            /*
            //This is possible all in one go, but you have to reverse the flow to do so.
            Dictionary<string, Transition> dictionary = new ChildDictionaryBuilder(this)
                .AddSibling("OnTurnFinished", OnTurnFinished, out string turnFinishedEventName)
                .AddChild(new ReconcileEndOfActivationStage(GameContext, this)
                    .ToDeterminePlayerTurn.Bind(turnFinishedEventName),
                        out var reconcileEndOfActivationStage)
                .AddChild(new MainUnitActionStage(GameContext, this) 
                    .ToReconcileEndOfActivation.Bind(reconcileEndOfActivationStage),
                        out var mainUnitActionStage)
                .AddChild(new ChooseUnitToActivateStage(GameContext, this)
                    .ToMainUnitAction.Bind(mainUnitActionStage), 
                        out var chooseUnitToActivateStage)
                .AddChild(new DeterminePlayerTurnStage(GameContext, this)
                    .ToChooseUnitToActivate.Bind(chooseUnitToActivateStage))
                .Build();
            */
        }

        protected override IPlayerTurnContext GetNewChildContext(IMainPhaseContext contextSelf)
        {
            return new PlayerTurnContext(GameContext);
        }
    }
}