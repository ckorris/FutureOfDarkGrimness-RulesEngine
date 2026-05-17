
using System.Collections.Generic;

namespace FDG.Stages
{

    public class ShootStage : ParentStage<IUnitActionContext, ICombatActionContext>
    {
        public const string SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION = "ShootToChildChooseRangedWeapon";

        public StageBinding OnFinishedShooting;
        public StageBinding BackToChooseAction;

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
            return new CombatActionContext(contextSelf.GameContext, contextSelf.ActivatingUnit, 
                isMelee: false);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            OnFinishedShooting = new StageBinding(this);
            OnFinishedShooting.OnWillActivate += OnShootingFinished;
            BackToChooseAction = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseRangedAttackStage(GameContext, this), out var chooseRangedWeapon)
                //.AddChild(new ChooseRangedTargetStage(GameContext, this), out var chooseRangedTarget)
                .AddChild(new FireStage(GameContext, this), out var fire)
                .AddChild(new ResolveRangedMoraleStage(GameContext, this), out var resolveRangedMorale)
                .AddChild(new DetermineCanKeepShootingStage(GameContext, this), out var determineCanKeepShooting)
                .AddSibling(nameof(OnFinishedShooting), OnFinishedShooting, out string onFinishedShootingEvent)
                .AddSibling(nameof(BackToChooseAction), BackToChooseAction, out string backToChooseEvent)
                .Build();

            startingChild = chooseRangedWeapon;

            chooseRangedWeapon.OnChoseWeapon.Bind(fire);
            chooseRangedWeapon.BackToChooseAction.Bind(backToChooseEvent);
            chooseRangedWeapon.OnNoValidShots.Bind(onFinishedShootingEvent);
            
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