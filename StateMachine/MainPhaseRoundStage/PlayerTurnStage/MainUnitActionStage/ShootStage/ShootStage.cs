
using System.Collections.Generic;

namespace FDG.Stages
{

    public class ShootStage : ParentStage<IUnitActionContext, IRangedContext>
    {
        public const string SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION = "ShootToChildChooseRangedWeapon";

        public StageBinding OnFinishedShooting;

        public ShootStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            OnFinishedShooting = new StageBinding(this);
        }

        /*
        public ShootStage(StateMachine stateMachine, IUnitActionContext context, IRangedContext rangedContext,
            StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            ChooseRangedWeaponStage chooseRangedWeaponStage = new ChooseRangedWeaponStage(stateMachine, rangedContext, this);
            ChooseRangedTargetStage chooseRangedTargetStage = new ChooseRangedTargetStage(stateMachine, rangedContext, this);
            FireStage fireStage = new FireStage(stateMachine, rangedContext, this);
            DetermineCanKeepShootingStage determineCanKeepShootingStage = new DetermineCanKeepShootingStage(stateMachine, rangedContext, this);
            _resolveRangedMoraleStage = new ResolveRangedMoraleStage(stateMachine, rangedContext, this);


            Bind(SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION, chooseRangedWeaponStage);
            chooseRangedWeaponStage.Bind(ChooseRangedWeaponStage.CHOOSE_RANGED_WEAPON_TO_CHOOSE_RANGED_TARGET_TRANSITION,
                chooseRangedTargetStage);
            chooseRangedTargetStage.Bind(ChooseRangedTargetStage.CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION,
                fireStage);
            fireStage.AssignExitStage(determineCanKeepShootingStage);
            determineCanKeepShootingStage.BindReturnToChooseWeapon(chooseRangedWeaponStage);
            determineCanKeepShootingStage.BindFinishShooting(_resolveRangedMoraleStage);
        }
        */

        public override void Enter(IUnitActionContext context)
        {
            GameContext.Log($"Shoot stage entered.");

            base.Enter(context);
        }

        protected override IRangedContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            return new RangedContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IRangedContext> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseRangedWeaponStage(GameContext, this), out var chooseRangedWeapon)
                .AddChild(new ChooseRangedTargetStage(GameContext, this), out var chooseRangedTarget)
                .AddChild(new FireStage(GameContext, this), out var fire)
                .AddChild(new ResolveRangedMoraleStage(GameContext, this), out var resolveRangedMorale)
                .AddChild(new DetermineCanKeepShootingStage(GameContext, this), out var determineCanKeepShooting)
                .AddSibling(nameof(OnFinishedShooting), OnFinishedShooting, out string onFinishedShootingEvent)
                .Build();

            startingChild = chooseRangedWeapon;

            chooseRangedWeapon.ToChooseRangedTarget.Bind(chooseRangedTarget);
            chooseRangedTarget.ToFire.Bind(fire);
            fire.OnFinishedFiring.Bind(resolveRangedMorale);
            resolveRangedMorale.ToFinished.Bind(determineCanKeepShooting);
            determineCanKeepShooting.ReturnToChooseWeapon.Bind(chooseRangedWeapon);
            determineCanKeepShooting.ToFinishShooting.Bind(onFinishedShootingEvent);

            return dictionary;
        }
    }
}