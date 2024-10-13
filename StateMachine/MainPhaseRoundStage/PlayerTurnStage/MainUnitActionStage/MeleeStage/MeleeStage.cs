

namespace FDG.Stages
{
    public class MeleeStage : StateBase<IUnitActionContext>
    {
        public const string MELEE_TO_CHILD_CHARGING_UNIT_ATTACK_TRANSITION = "MeleeToChildChargingUnitAttack";

        public MeleeStage(StateMachine stateMachine, IUnitActionContext context, IMeleeContext meleeContext,
            StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            ChargingUnitAttackStage chargingUnitAttackStage = new ChargingUnitAttackStage(stateMachine, meleeContext, this);
            OfferStrikeBackStage offerStrikeBackStage = new OfferStrikeBackStage(stateMachine, meleeContext, this);
            StrikeBackStage strikeBackStage = new StrikeBackStage(stateMachine, meleeContext, this);
            ResolveMeleeMoraleStage resolveMeleeMoraleStage = new ResolveMeleeMoraleStage(stateMachine, meleeContext, this);

            stateMachine.AddTransition<MeleeStage>(MELEE_TO_CHILD_CHARGING_UNIT_ATTACK_TRANSITION,
                chargingUnitAttackStage);

            stateMachine.AddTransition<ChargingUnitAttackStage>(ChargingUnitAttackStage.CHARGING_UNIT_ATTACK_TO_OFFER_STRIKE_BACK_TRANSITION,
                offerStrikeBackStage);
            stateMachine.AddTransition<OfferStrikeBackStage>(OfferStrikeBackStage.OFFER_STRIKE_BACK_TO_STRIKE_BACK_TRANSITION,
                strikeBackStage);
            stateMachine.AddTransition<OfferStrikeBackStage>(OfferStrikeBackStage.OFFER_STRIKE_BACK_TO_RESOLVE_MELEE_MORALE_TRANSITION,
                resolveMeleeMoraleStage);
            stateMachine.AddTransition<StrikeBackStage>(StrikeBackStage.STRIKE_BACK_TO_RESOLVE_MELEE_MORALE_TRANSITION,
                resolveMeleeMoraleStage);

        }

        public override void Enter()
        {
            base.Enter();

            Context.TextOutput.Log($"Melee stage entering child: Charging Unit Attack.");
            MoveToChargingUnitAttack();
        }

        private void MoveToChargingUnitAttack()
        {
            SignalEvent(MELEE_TO_CHILD_CHARGING_UNIT_ATTACK_TRANSITION);
        }
    }
}