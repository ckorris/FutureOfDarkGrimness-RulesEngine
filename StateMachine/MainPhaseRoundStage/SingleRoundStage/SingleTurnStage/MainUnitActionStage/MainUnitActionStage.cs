
using System.Collections.Generic;

namespace FDG.Stages
{

    public class MainUnitActionStage : ParentStage<ISingleTurnContext, IUnitActionContext>
    {
        public const string MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION =
            "MainUnitActionToChildChooseAction";

        public StageBinding ToReconcileEndOfActivation;

        // #248: the player backed out of a pristine activation (ChooseActionStage.ToBackOut) — exit to
        // SingleTurnStage, which routes back to ChooseUnitToActivateStage. Its own sibling exit (not
        // ToReconcileEndOfActivation) so nothing marks the unit as activated.
        public StageBinding OnBackedOut;

        public MainUnitActionStage(IGameContext gameContext, IStateMachineLayer<ISingleTurnContext> parent) : base(gameContext, parent)
        {
            
        }

        public override async Task Enter(ISingleTurnContext context)
        {
            GameContext.Log("Main Unit Action stage entered.");

            // Record the activating unit on the progress record so the read model can spotlight it.
            // Guarded like the rolling snapshot so minimal-store unit tests (no GameProgressData) are unaffected.
            if (context.ActivatedUnit != null && GameContext.GameDataStore.IsTypeAssigned<GameProgressData>())
                GameProgressUtilities.SetActivatingUnit(GameContext.GameDataStore, context.ActivatedUnit);

            await base.Enter(context);
        }

        protected override IUnitActionContext GetNewChildContext(ISingleTurnContext contextSelf)
        {
            if(contextSelf.ActivatedUnit == null)
            {
                throw new NullReferenceException($"{nameof(ISingleTurnContext.ActivatedUnit)} was null when creating child context in {nameof(MainUnitActionStage)}.");
            }

            UnitActionContext unitActionContext = new UnitActionContext(GameContext, contextSelf.ActivatedUnit);
            unitActionContext.Reset(contextSelf.ActivatedUnit);
            return unitActionContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IUnitActionContext> startingChild)
        {
            ToReconcileEndOfActivation = new StageBinding(this);
            OnBackedOut = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                // #197 P5a — fires Activation_OnActivationStart before anything else, so the "pick one effect
                // until the end of the activation" rules resolve before an action is chosen. Only ever entered
                // as the starting child; every loop-back below returns to ChooseAction, so it runs once.
                .AddChild(new ActivationStartStage(GameContext, this), out var activationStart)
                // #197 Surprise Attack — "the first time this unit is activated, pick one enemy within 6in
                // in LoS and roll X dice". Mandatory, so it sits between activation start and the menu
                // rather than being offered as an action; a unit without the rule passes straight through.
                .AddChild(new SurpriseAttackStage(GameContext, this), out var surpriseAttack)
                .AddChild(new ChooseActionStage(GameContext, this), out var chooseAction)
                .AddChild(new MovementStage(GameContext, this), out var movement)
                .AddChild(new MeleeStage(GameContext, this), out var melee)
                .AddChild(new ShootStage(GameContext, this), out var shoot)
                // "Before attacking" activated abilities (Piercing Spotter, Regeneration Buff, Precision
                // Fighting Mark, Mend, Breath Attack, ...) are offered in Choose Action as their own menu
                // actions (gated on !HasAttacked), NOT as a prompt after committing to an attack - so they
                // can be used even when the unit cannot attack anything. This stage resolves the one chosen
                // ability (target select + token ops + the DealHits save/wound pipeline) and loops back.
                .AddChild(new BeforeAttackActionStage(GameContext, this), out var beforeAttackAction)
                .AddChild(new CustomActionStage(GameContext, this), out var customAction)
                .AddChild(new CastSpellStage(GameContext, this), out var castSpell)
                .AddChild(new DisembarkStage(GameContext, this), out var disembark)
                .AddChild(new EmbarkStage(GameContext, this), out var embark)
                // #197 Teleport — a 6" reposition-placement offered in Choose Action; loops back so the unit
                // re-evaluates its options from the new position (layered, doesn't end the turn).
                .AddChild(new TeleportStage(GameContext, this), out var teleport)
                // #197 P10 Storm of X - a once-per-game rolled multi-target hit burst offered in Choose
                // Action; its per-target batches loop through OnBatchDone (back to itself) until the queue
                // drains, then OnAllDone returns to the menu. Fully layered like Teleport.
                .AddChild(new StormStage(GameContext, this), out var storm)
                .AddSibling(nameof(ToReconcileEndOfActivation), ToReconcileEndOfActivation, out string toReconcileActivationEvent)
                // #248: the pristine back-out leaves through its own sibling event so no end-of-activation
                // reconcile runs and nothing marks the unit as activated.
                .AddSibling(nameof(OnBackedOut), OnBackedOut, out string backedOutEvent)
                .Build();

            startingChild = activationStart;
            // The burst (if any) resolves before the unit chooses what to do, so its 6in is measured from
            // where the activation started. Every loop-back below returns to ChooseAction, so it runs once.
            activationStart.OnFinished.Bind(surpriseAttack);
            surpriseAttack.OnFinished.Bind(chooseAction);

            chooseAction.ToMovement.Bind(movement);
            chooseAction.ToCharge.Bind(melee);
            chooseAction.ToShoot.Bind(shoot);
            // "Before attacking" abilities are now menu actions; the chosen one resolves and loops back to
            // Choose Action, layered (no HasMoved/HasAttacked), so the unit may still move/attack after.
            chooseAction.ToBeforeAttackAction.Bind(beforeAttackAction);
            beforeAttackAction.OnFinished.Bind(chooseAction);
            chooseAction.ToCustomAction.Bind(customAction);
            chooseAction.ToCast.Bind(castSpell);
            chooseAction.ToDisembark.Bind(disembark);
            chooseAction.ToEmbark.Bind(embark);
            chooseAction.ToTeleport.Bind(teleport);
            chooseAction.ToStorm.Bind(storm);
            // Each resolved batch re-enters StormStage to process the next target; when the queue empties it
            // exits to the menu. The unit may then still move/attack (Storm is layered, not an attack).
            storm.OnBatchDone.Bind(storm);
            storm.OnAllDone.Bind(chooseAction);
            chooseAction.ToReconcileEndOfActivation.Bind(toReconcileActivationEvent);
            chooseAction.ToBackOut.Bind(backedOutEvent);
            movement.OnFinishedMovement.Bind(chooseAction);
            // Abandoning the move at the path prompt returns without registering a move distance.
            movement.BackToChooseAction.Bind(chooseAction);
            melee.OnFinishedMelee.Bind(chooseAction);
            // Backing out of a charge before any dice or movement must not spend the attack, so it returns
            // through its own binding rather than OnFinishedMelee. Same shape as the shoot pair below.
            melee.BackToChooseAction.Bind(chooseAction);
            shoot.OnFinishedShooting.Bind(chooseAction);
            shoot.BackToChooseAction.Bind(chooseAction);
            // #010 — a resolved custom action loops back to Choose Action (layered, doesn't end the turn).
            customAction.OnFinished.Bind(chooseAction);
            // #033 — casting also loops back to Choose Action, layered (doesn't set HasMoved/HasAttacked).
            castSpell.OnFinished.Bind(chooseAction);
            // #035 — after disembarking (Advance-equivalent), loop back so the unit may still Shoot.
            disembark.OnFinished.Bind(chooseAction);
            // Cancelling the disembark placement leaves the unit aboard with its move unspent.
            disembark.OnBackToChooseAction.Bind(chooseAction);
            // #035 slice D — boarding a transport ends the activation (the unit is now inside); cancelling
            // the transport choice returns to the action menu.
            embark.OnEmbarked.Bind(toReconcileActivationEvent);
            embark.OnBackToChooseAction.Bind(chooseAction);
            // #197 — after teleporting (or declining), loop back so Charge/Shoot/Pass re-evaluate from the new
            // position. Layered like Cast/CustomAction (doesn't set HasMoved/HasAttacked).
            teleport.OnFinished.Bind(chooseAction);

            return dictionary;
        }
    }
}