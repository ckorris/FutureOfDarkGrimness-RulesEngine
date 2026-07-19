
using FDG.Data;
using System.Diagnostics;

namespace FDG.Stages
{
    public class SingleTurnStage : ParentStage<ISingleRoundContext, ISingleTurnContext>
    {
        // #197 Delayed Action gate: does any opposing team have strictly more units left to activate than
        // the acting player's team? Counted over living units still in each player's pool. Snapshotted into
        // the turn context so ChooseUnitToActivateStage can read it while deciding whether to offer hold-back.
        private bool OpponentHasMoreUnitsToActivate(ISingleRoundContext round, PlayerID currentPlayerID)
        {
            var teams = GameContext.TableState.Teams.Objects;
            ITeam myTeam = teams.First(team => team.IsPlayerOnTeam(currentPlayerID));

            int Living(PlayerID p) => round.UnactivatedUnits.TryGetValue(p, out var list)
                ? list.Count(u => u.GetValue().GetIsAlive())
                : 0;

            int mine = myTeam.Players.Sum(Living);
            int opponents = teams.Where(team => team != myTeam)
                .SelectMany(team => team.Players)
                .Sum(Living);

            return opponents > mine;
        }

        public StageBinding OnTurnFinished;

        public SingleTurnStage(IGameContext gameContext, IStateMachineLayer<ISingleRoundContext> parent)
            : base(gameContext, parent)
        {
        }

        protected override ISingleTurnContext GetNewChildContext(ISingleRoundContext contextSelf)
        {
            Debug.WriteLine($"Getting context for {nameof(SingleTurnStage)}.");
            PlayerID currentPlayerID = contextSelf.GetCurrentPlayerID();

            contextSelf.CleanDeadUnitsFromUnactivated();
            
            List<DataBinding<UnitData>> playerUnits = contextSelf.UnactivatedUnits[currentPlayerID]
                .Where(unit => unit.GetValue().GetIsAlive())
                .ToList();

            return new SingleTurnContext(GameContext, currentPlayerID, playerUnits,
                OpponentHasMoreUnitsToActivate(contextSelf, currentPlayerID));
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
            // #197 Delayed Action: holding a unit back skips its activation entirely, routing straight to the
            // (ActivatedUnit-null-safe) reconcile stage. ReconcileChildContextBeforeLeaving then skips
            // MarkUnitAsActivated for the delayed turn, leaving the unit in the pool for a later turn.
            chooseUnitToActivateStage.ToDelayedTurnEnd.Bind(reconcileEndOfActivationStage.Name);
            mainUnitActionStage.ToReconcileEndOfActivation.Bind(reconcileEndOfActivationStage.Name);
            // #248: a pristine activation was backed out — return to unit selection. Stays inside this
            // stage (no turn-end reconcile fires), the unit remains unactivated in the pool, and the next
            // pick overwrites ActivatedUnit.
            mainUnitActionStage.OnBackedOut.Bind(chooseUnitToActivateStage.Name);
            reconcileEndOfActivationStage.OnFinished.Bind(turnFinishedEventName);

            return dictionary;
        }

        protected override void ReconcileChildContextBeforeLeaving(ISingleRoundContext selfContext, ISingleTurnContext childContext)
        {
            base.ReconcileChildContextBeforeLeaving(selfContext, childContext);

            // #197 Delayed Action: the acting player held a unit back instead of activating. Nothing was
            // activated, so there is no unit to mark - the held-back unit stays in the pool and the cursor
            // advances to the opponent on the next DeterminePlayerTurnStage.
            if(childContext.WasDelayed)
            {
                return;
            }

            if(childContext.ActivatedUnit == null)
            {
                throw new NullReferenceException($"Activated unit was null after finishing a turn in {nameof(SingleTurnStage)}.");
            }

            selfContext.MarkUnitAsActivated(childContext.ActivatedUnit);
        }
    }
}
