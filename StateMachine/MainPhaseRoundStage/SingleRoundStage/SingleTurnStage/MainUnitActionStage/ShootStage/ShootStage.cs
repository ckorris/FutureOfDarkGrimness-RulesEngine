
using System.Collections.Generic;

namespace FDG.Stages
{

    public class ShootStage : ParentStage<IUnitActionContext, ICombatActionContext>
    {
        public const string SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION = "ShootToChildChooseRangedWeapon";

        public StageBinding OnFinishedShooting;

        public ShootStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            
        }

        public override async Task Enter(IUnitActionContext context)
        {
            GameContext.Log($"Shoot stage entered.");

            base.Enter(context);
        }

        protected override ICombatActionContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            return new CombatActionContext(contextSelf.ActivatingUnit);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            OnFinishedShooting = new StageBinding(this);
            OnFinishedShooting.OnWillActivate += OnShootingFinished;

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseRangedTargetStage(GameContext, this), out var chooseRangedTarget)
                .AddChild(new ChooseRangedWeaponStage(GameContext, this), out var chooseRangedWeapon)
                .AddChild(new FireStage(GameContext, this), out var fire)
                .AddChild(new ResolveRangedMoraleStage(GameContext, this), out var resolveRangedMorale)
                .AddChild(new DetermineCanKeepShootingStage(GameContext, this), out var determineCanKeepShooting)
                .AddSibling(nameof(OnFinishedShooting), OnFinishedShooting, out string onFinishedShootingEvent)
                .Build();

            startingChild = chooseRangedTarget;

            chooseRangedTarget.OnChoseTarget.Bind(chooseRangedWeapon);
            chooseRangedTarget.BackToChooseAction.Bind(onFinishedShootingEvent);
            chooseRangedWeapon.OnChoseWeapon.Bind(fire);
            fire.OnFinishedFiring.Bind(resolveRangedMorale);
            resolveRangedMorale.ToFinished.Bind(determineCanKeepShooting);
            determineCanKeepShooting.ReturnToChooseWeapon.Bind(chooseRangedWeapon);
            determineCanKeepShooting.ToFinishShooting.Bind(onFinishedShootingEvent);

            return dictionary;
        }

        private void OnShootingFinished(IUnitActionContext context)
        {
            context.RegisterAttackedFinished();
        }
    }
}