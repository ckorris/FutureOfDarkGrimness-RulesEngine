
using System.Collections.Generic;
using FDG.Rules.Definitions;

namespace FDG.Stages
{

    public class MainUnitActionStage : ParentStage<ISingleTurnContext, IUnitActionContext>
    {
        public const string MAIN_UNIT_ACTION_TO_CHILD_CHOOSE_ACTION_TRANSITION =
            "MainUnitActionToChildChooseAction";

        public StageBinding ToReconcileEndOfActivation;

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

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                // #197 P5a — fires Activation_OnActivationStart before anything else, so the "pick one effect
                // until the end of the activation" rules resolve before an action is chosen. Only ever entered
                // as the starting child; every loop-back below returns to ChooseAction, so it runs once.
                .AddChild(new ActivationStartStage(GameContext, this), out var activationStart)
                .AddChild(new ChooseActionStage(GameContext, this), out var chooseAction)
                .AddChild(new MovementStage(GameContext, this), out var movement)
                .AddChild(new MeleeStage(GameContext, this), out var melee)
                .AddChild(new ShootStage(GameContext, this), out var shoot)
                // #100 #2 — a pre-attack stage sits on each attack edge, firing Activation_OnPreAttack
                // and offering pre-attack abilities before the real attack resolves. One per action type
                // so each reports the right kind to the hook (Charge exact; the shoot edge uses Hold —
                // there is no Shoot action type — which no corpus pre-attack ability gates on).
                .AddChild(new PreAttackStage(GameContext, this, EActionType.Charge), out var preAttackMelee)
                .AddChild(new PreAttackStage(GameContext, this, EActionType.Hold), out var preAttackShoot)
                .AddChild(new CustomActionStage(GameContext, this), out var customAction)
                .AddChild(new CastSpellStage(GameContext, this), out var castSpell)
                .AddChild(new DisembarkStage(GameContext, this), out var disembark)
                .AddChild(new EmbarkStage(GameContext, this), out var embark)
                // #197 Teleport — a 6" reposition-placement offered in Choose Action; loops back so the unit
                // re-evaluates its options from the new position (layered, doesn't end the turn).
                .AddChild(new TeleportStage(GameContext, this), out var teleport)
                .AddSibling(nameof(ToReconcileEndOfActivation), ToReconcileEndOfActivation, out string toReconcileActivationEvent)
                .Build();

            startingChild = activationStart;
            activationStart.OnFinished.Bind(chooseAction);

            chooseAction.ToMovement.Bind(movement);
            // #100 #2 — route the attack edges through the pre-attack stage, which hands off to the real
            // attack on finish. Layered (no HasMoved/HasAttacked), so the downstream attack is unchanged.
            chooseAction.ToCharge.Bind(preAttackMelee);
            chooseAction.ToShoot.Bind(preAttackShoot);
            preAttackMelee.OnFinished.Bind(melee);
            preAttackShoot.OnFinished.Bind(shoot);
            chooseAction.ToCustomAction.Bind(customAction);
            chooseAction.ToCast.Bind(castSpell);
            chooseAction.ToDisembark.Bind(disembark);
            chooseAction.ToEmbark.Bind(embark);
            chooseAction.ToTeleport.Bind(teleport);
            chooseAction.ToReconcileEndOfActivation.Bind(toReconcileActivationEvent);
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