
using System.Collections.Generic;

namespace FDG.Stages
{

    public class StrikeBackStage : ParentStage<IMeleeContext, IMeleeContext>
    {
        public StageBinding FinishedStrikingBack;
        public StageBinding OnAttackerKilled;

        /*
        public StrikeBackStage(StateMachine stateMachine, IMeleeContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            //This is constructed much like a normal melee stage, but reduced. 
            _reversedContext = new MeleeContext(context.GameContext);

            ChooseMeleeWeaponStage chooseMeleeWeaponStage
                = new ChooseMeleeWeaponStage(stateMachine, _reversedContext, this);
            SwingMeleeWeaponStage swingMeleeWeaponStage
                = new SwingMeleeWeaponStage(stateMachine, _reversedContext, this);
            _determineCanKeepSwingingStage
                = new DetermineCanKeepSwingingStage(stateMachine, _reversedContext, this);

            Bind(STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION, chooseMeleeWeaponStage);

            chooseMeleeWeaponStage.Bind(ChooseMeleeWeaponStage.CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION,
                swingMeleeWeaponStage);

            swingMeleeWeaponStage.AssignExitStage(_determineCanKeepSwingingStage);

            _determineCanKeepSwingingStage.BindReturnToChooseWeapon(chooseMeleeWeaponStage);
        }
        */

        public StrikeBackStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            FinishedStrikingBack = new StageBinding(this);
            OnAttackerKilled = new StageBinding(this);
        }

        protected override IMeleeContext GetNewChildContext(IMeleeContext contextSelf)
        {
            MeleeContext meleeContext = new MeleeContext(GameContext);
            meleeContext.BeginNewAttack(contextSelf.DefendingUnit, contextSelf.AttackingUnit); //Purposefully reversed.
            return meleeContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMeleeContext> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseMeleeWeaponStage(GameContext, this), out var chooseMeleeWeapon)
                .AddChild(new SwingMeleeWeaponStage(GameContext, this), out var swingMeleeWeapon)
                .AddChild(new DetermineCanKeepSwingingStage(GameContext, this), out var determineCanKeepSwinging)
                .AddSibling(nameof(FinishedStrikingBack), FinishedStrikingBack, out string finishedStrikingBackEvent)
                .AddSibling(nameof(OnAttackerKilled), OnAttackerKilled, out string onAttackerKilledEvent)
                .Build();

            startingChild = chooseMeleeWeapon;

            chooseMeleeWeapon.OnChosen.Bind(swingMeleeWeapon);
            swingMeleeWeapon.FinishedSwinging.Bind(determineCanKeepSwinging);
            determineCanKeepSwinging.ReturnToChooseWeapon.Bind(chooseMeleeWeapon);
            determineCanKeepSwinging.OnOutOfWeapons.Bind(finishedStrikingBackEvent);
            determineCanKeepSwinging.OnDefenderKilled.Bind(onAttackerKilledEvent);

            return dictionary;
        }
    }
}