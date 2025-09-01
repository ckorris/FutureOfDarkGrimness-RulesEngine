
using FDG.Data;

namespace FDG.Stages
{
    public class SingleTurnStage : ParentStage<ISingleRoundContext, ISingleTurnContext>
    {
        public StageBinding OnTurnFinished;

        public SingleTurnStage(IGameContext gameContext, IStateMachineLayer<ISingleRoundContext> parent)
            : base(gameContext, parent)
        {
        }

        protected override ISingleTurnContext GetNewChildContext(ISingleRoundContext contextSelf)
        {
            PlayerID currentPlayerID = contextSelf.GetCurrentPlayerID();

            contextSelf.CleanDeadUnitsFromUnactivated();
            
            List<DataBinding<UnitData>> playerUnits = contextSelf.UnactivatedUnits[currentPlayerID]
                .Where(unit => unit.GetValue().GetIsAlive())
                .ToList();

            //return new SingleTurnContext(GameContext, currentPlayerID, contextSelf.UnactivatedUnits[currentPlayerID]);
            return new SingleTurnContext(GameContext, currentPlayerID, playerUnits);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ISingleTurnContext> startingChild)
        {
            OnTurnFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseUnitToActivateStage(GameContext, this), out var chooseUnitToActivateStage)
                .AddChild(new MainUnitActionStage(GameContext, this), out var mainUnitActionStage)
                .AddChild(new ReconcileEndOfActivationStage(GameContext, this), out var reconcileEndOfActivationStage)
                .AddSibling(nameof(OnTurnFinished), OnTurnFinished, out string turnFinishedEventName)
                .Build();

            startingChild = chooseUnitToActivateStage;

            chooseUnitToActivateStage.ToMainUnitAction.Bind(mainUnitActionStage.Name);
            mainUnitActionStage.ToReconcileEndOfActivation.Bind(reconcileEndOfActivationStage.Name);
            reconcileEndOfActivationStage.ToDeterminePlayerTurn.Bind(turnFinishedEventName);

            return dictionary;
        }

        protected override void ReconcileChildContextBeforeLeaving(ISingleRoundContext selfContext, ISingleTurnContext childContext)
        {
            base.ReconcileChildContextBeforeLeaving(selfContext, childContext);

            if(childContext.ActivatedUnit == null)
            {
                throw new NullReferenceException($"Activated unit was null after finishing a turn in {nameof(SingleTurnStage)}.");
            }

            selfContext.MarkUnitAsActivated(childContext.ActivatedUnit);
        }
    }
}
