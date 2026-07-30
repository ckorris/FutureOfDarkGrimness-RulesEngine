
using System.Collections.Generic;
using FDG.Utilities;

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
            // #197 P20: re-derive the same narrowing the shoot gate applied. A unit that moved past its
            // advance-and-shoot allowance without Quick Shot of its own only got here because some enemy
            // carries a Quick Shot mark, so it may shoot marked units only.
            bool overMoveShootCap = contextSelf.HasMoved
                && !Rules.Dispatch.AircraftRules.IsAircraft(contextSelf.ActivatingUnit.GetValue())
                && contextSelf.MoveDistance.LessThanOrAlmostEqual(contextSelf.MoveShootAllowance) == false;
            bool markedTargetsOnly = overMoveShootCap
                && !Rules.Dispatch.ShootAfterRushRules.CanShootAfterRush(
                    contextSelf.ActivatingUnit.GetValue(), GameContext.RuleEvaluator);

            return new CombatActionContext(contextSelf.GameContext, contextSelf.ActivatingUnit,
                isMelee: false, attackerMoved: contextSelf.HasMoved,
                markedTargetsOnly: markedTargetsOnly);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            OnFinishedShooting = new StageBinding(this);
            OnFinishedShooting.OnWillActivate += OnShootingFinished;
            BackToChooseAction = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseRangedAttackStage(GameContext, this), out var chooseRangedWeapon)
                //.AddChild(new ChooseRangedTargetStage(GameContext, this), out var chooseRangedTarget)
                .AddChild(new ResolveRangedExtraAttackStage(GameContext, this), out var resolveExtraAttack)
                .AddChild(new FireStage(GameContext, this), out var fire)
                .AddChild(new DetermineMorePendingShotsStage(GameContext, this), out var morePendingShots)
                .AddChild(new ResolveRangedMoraleStage(GameContext, this), out var resolveRangedMorale)
                .AddChild(new DetermineCanKeepShootingStage(GameContext, this), out var determineCanKeepShooting)
                .AddChild(new PostShootStage(GameContext, this), out var postShoot)
                .AddSibling(nameof(OnFinishedShooting), OnFinishedShooting, out string onFinishedShootingEvent)
                .AddSibling(nameof(BackToChooseAction), BackToChooseAction, out string backToChooseEvent)
                .Build();

            startingChild = chooseRangedWeapon;

            // #197 P16: the extra-attack window opens between the weapon/target choice and the volley, so a
            // Takedown Shot lands "against the target" of a shot whose range and line of sight are already
            // validated. Its own once-per-game cost keeps it from re-offering on a later weapon this action.
            chooseRangedWeapon.OnChoseWeapon.Bind(resolveExtraAttack);
            resolveExtraAttack.OnExtraAttackResolved.Bind(fire);
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