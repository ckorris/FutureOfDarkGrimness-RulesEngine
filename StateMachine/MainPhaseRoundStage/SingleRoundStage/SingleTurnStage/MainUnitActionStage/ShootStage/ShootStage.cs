
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
            GameContext.LogDebug($"Shoot stage entered.");

            await base.Enter(context);
        }

        protected override ICombatActionContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            return new CombatActionContext(contextSelf.GameContext, contextSelf.ActivatingUnit,
                isMelee: false, attackerMoved: contextSelf.HasMoved);
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
                .AddChild(new DetermineMorePendingShotsStage(GameContext, this), out var morePendingShots)
                .AddChild(new ResolveRangedMoraleStage(GameContext, this), out var resolveRangedMorale)
                .AddChild(new DetermineCanKeepShootingStage(GameContext, this), out var determineCanKeepShooting)
                .AddChild(new PostShootStage(GameContext, this), out var postShoot)
                .AddSibling(nameof(OnFinishedShooting), OnFinishedShooting, out string onFinishedShootingEvent)
                .AddSibling(nameof(BackToChooseAction), BackToChooseAction, out string backToChooseEvent)
                .Build();

            startingChild = chooseRangedWeapon;

            chooseRangedWeapon.OnChoseWeapon.Bind(fire);
            chooseRangedWeapon.BackToChooseAction.Bind(backToChooseEvent);
            // Both shoot exits — "fired all weapons" and "no further valid shots" — converge on
            // ResolveRangedMoraleStage and then PostShootStage, so morale is tested once and the
            // post-shoot move (Hit & Run / Harassing) is offered once per action. OnNoValidShots only
            // fires once a weapon has already been used, so it must not bypass morale.
            chooseRangedWeapon.OnNoValidShots.Bind(resolveRangedMorale);

            // #157: while queued attacks remain (a Takedown-split volley), loop FireStage — each entry
            // consumes one queued shot with its own target-model pick.
            fire.OnFinishedFiring.Bind(morePendingShots);
            morePendingShots.FireNextShot.Bind(fire);
            morePendingShots.OnVolleyComplete.Bind(determineCanKeepShooting);
            determineCanKeepShooting.ReturnToChooseWeapon.Bind(chooseRangedWeapon);
            // Morale runs after the LAST weapon, not after each one: one test per unit that was shot at,
            // measured against its wounds when this attacker first targeted it.
            determineCanKeepShooting.ToFinishShooting.Bind(resolveRangedMorale);
            resolveRangedMorale.ToFinished.Bind(postShoot);
            postShoot.ToFinished.Bind(onFinishedShootingEvent);

            return dictionary;
        }

        private void OnShootingFinished(IUnitActionContext context)
        {
            context.RegisterAttackedFinished();
        }
    }
}