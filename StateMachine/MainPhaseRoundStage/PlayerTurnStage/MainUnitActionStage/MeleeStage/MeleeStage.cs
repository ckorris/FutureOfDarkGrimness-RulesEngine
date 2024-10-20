
namespace FDG.Stages
{
    public class MeleeStage : StateBase<IUnitActionContext>
    {
        private const string MELEE_TO_CHILD_ENTRANCE_TRANSITION = "MeleeToChildEntranceAttack";

        private readonly StateMachine _stateMachine;

        public MeleeStage(StateMachine stateMachine, IUnitActionContext context, IMeleeContext meleeContext,
            StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            PileInStage pileInStage 
                = new PileInStage(stateMachine, meleeContext, this);
            DetermineInRangeAttackersStage determineInRangeAttackersStage 
                = new DetermineInRangeAttackersStage(stateMachine, meleeContext, this);
            DetermineInRangeDefendersStage determineInRangeDefendersStage 
                = new DetermineInRangeDefendersStage(stateMachine, meleeContext, this);
            ChooseMeleeWeaponStage chooseMeleeWeaponStage 
                = new ChooseMeleeWeaponStage(stateMachine, meleeContext, this);
            SwingMeleeWeaponStage swingMeleeWeaponStage 
                = new SwingMeleeWeaponStage(stateMachine, meleeContext, this);
            DetermineCanKeepSwingingStage determineCanKeepSwingingStage 
                = new DetermineCanKeepSwingingStage(stateMachine, meleeContext, this);
            OfferStrikeBackStage offerStrikeBackStage 
                = new OfferStrikeBackStage(stateMachine, meleeContext, this);
            StrikeBackStage strikeBackStage 
                = new StrikeBackStage(stateMachine, meleeContext, this);
            DetermineMeleeWinnerStage determineMeleeWinnerStage
                = new DetermineMeleeWinnerStage(stateMachine, meleeContext, this);
            DetermineMoraleSaveNeededStage determineMoraleSaveNeededStage 
                = new DetermineMoraleSaveNeededStage(StateMachine, meleeContext, this);
            RollForMoraleStage rollForMoraleStage 
                = new RollForMoraleStage(stateMachine, meleeContext, this);
            AssignMeleeMoralePenaltyStage assignMoralePenaltyStage 
                = new AssignMeleeMoralePenaltyStage(stateMachine, meleeContext, this);
            ApplyFatigueStage applyFatigueStage
                = new ApplyFatigueStage(StateMachine, meleeContext, this);

            stateMachine.AddTransition<MeleeStage>(MELEE_TO_CHILD_ENTRANCE_TRANSITION, pileInStage);

            stateMachine.AddTransition<PileInStage>(PileInStage.PILE_IN_FINISHED_TRANSITION, determineInRangeAttackersStage);

            stateMachine.AddTransition<DetermineInRangeAttackersStage>(DetermineInRangeAttackersStage.DETERMINE_IN_RANGE_ATTACKER_FINISHED_TRANSITION,
                determineInRangeDefendersStage);

            stateMachine.AddTransition<DetermineInRangeDefendersStage>(DetermineInRangeDefendersStage.DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION,
                chooseMeleeWeaponStage);

            StateMachine.AddTransition<ChooseMeleeWeaponStage>(ChooseMeleeWeaponStage.CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION,
                swingMeleeWeaponStage);

            swingMeleeWeaponStage.AssignExitStage(determineCanKeepSwingingStage);

            determineCanKeepSwingingStage.BindReturnToChooseWeapon(chooseMeleeWeaponStage);
            determineCanKeepSwingingStage.BindOutOfWeapons(offerStrikeBackStage);
            determineCanKeepSwingingStage.BindDefenderKilled(applyFatigueStage);

            stateMachine.AddTransition<OfferStrikeBackStage>(OfferStrikeBackStage.OFFER_STRIKE_BACK_ACCEPTED_TRANSITION,
                strikeBackStage);
            stateMachine.AddTransition<OfferStrikeBackStage>(OfferStrikeBackStage.OFFER_STRIKE_BACK_REJECTED_TRANSITION,
                determineMoraleSaveNeededStage);

            strikeBackStage.AssignNormalExitStage(determineMeleeWinnerStage);
            strikeBackStage.AssignAttackerKilledExitStage(applyFatigueStage);

            stateMachine.AddTransition<DetermineMeleeWinnerStage>(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION,
                determineMoraleSaveNeededStage);
            stateMachine.AddTransition<DetermineMeleeWinnerStage>(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION,
                applyFatigueStage);

            stateMachine.AddTransition<DetermineMoraleSaveNeededStage>(DetermineMoraleSaveNeededStage.DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION,
                rollForMoraleStage);
            
            stateMachine.AddTransition<RollForMoraleStage>(RollForMoraleStage.ROLL_FOR_MORALE_PASSED_TRANSITION, applyFatigueStage);
            stateMachine.AddTransition<RollForMoraleStage>(RollForMoraleStage.ROLL_FOR_MORALE_FAILED_TRANSITION, assignMoralePenaltyStage);

            stateMachine.AddTransition<AssignMeleeMoralePenaltyStage>(AssignMeleeMoralePenaltyStage.ASSIGN_MELEE_MORALE_PENALTY_FINISHED_TRANSITION,
                applyFatigueStage);

            //Apply fatigue leaving has to be assigned from the outside, as it leaves this stage.
        }

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _stateMachine.AddTransition<ApplyFatigueStage>(ApplyFatigueStage.APPLY_FATIGUE_FINISHED_TRANSITION,
                targetStageWhenFinished);
        }

        public override void Enter()
        {
            base.Enter();

            Context.TextOutput.Log($"Melee stage entering child.");
            MoveToChargingUnitAttack();
        }

        private void MoveToChargingUnitAttack()
        {
            SignalEvent(MELEE_TO_CHILD_ENTRANCE_TRANSITION);
        }
    }
}