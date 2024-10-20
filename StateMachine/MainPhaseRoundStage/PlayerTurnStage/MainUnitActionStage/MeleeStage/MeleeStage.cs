
namespace FDG.Stages
{
    public class MeleeStage : StateBase<IUnitActionContext>
    {
        private const string MELEE_TO_CHILD_ENTRANCE_TRANSITION = "MeleeToChildEntranceAttack";

        private readonly StateMachine _stateMachine;

        ApplyFatigueStage _applyFatigueStage;

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
            _applyFatigueStage
                = new ApplyFatigueStage(StateMachine, meleeContext, this);

            Bind(MELEE_TO_CHILD_ENTRANCE_TRANSITION, pileInStage);

            pileInStage.Bind(PileInStage.PILE_IN_FINISHED_TRANSITION, determineInRangeAttackersStage);

            determineInRangeAttackersStage.Bind(DetermineInRangeAttackersStage.DETERMINE_IN_RANGE_ATTACKER_FINISHED_TRANSITION,
                determineInRangeDefendersStage);

            determineInRangeDefendersStage.Bind(DetermineInRangeDefendersStage.DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION,
                chooseMeleeWeaponStage);

            chooseMeleeWeaponStage.Bind(ChooseMeleeWeaponStage.CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION,
                swingMeleeWeaponStage);

            swingMeleeWeaponStage.AssignExitStage(determineCanKeepSwingingStage);

            determineCanKeepSwingingStage.BindReturnToChooseWeapon(chooseMeleeWeaponStage);
            determineCanKeepSwingingStage.BindOutOfWeapons(offerStrikeBackStage);
            determineCanKeepSwingingStage.BindDefenderKilled(_applyFatigueStage);

            offerStrikeBackStage.Bind(OfferStrikeBackStage.OFFER_STRIKE_BACK_ACCEPTED_TRANSITION,
                strikeBackStage);
            offerStrikeBackStage.Bind(OfferStrikeBackStage.OFFER_STRIKE_BACK_REJECTED_TRANSITION,
                determineMoraleSaveNeededStage);

            strikeBackStage.AssignNormalExitStage(determineMeleeWinnerStage);
            strikeBackStage.AssignAttackerKilledExitStage(_applyFatigueStage);

            determineMeleeWinnerStage.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION,
                determineMoraleSaveNeededStage);
            determineMeleeWinnerStage.Bind(DetermineMeleeWinnerStage.DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION,
                _applyFatigueStage);

            determineMoraleSaveNeededStage.Bind(DetermineMoraleSaveNeededStage.DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION,
                rollForMoraleStage);
            
            rollForMoraleStage.Bind(RollForMoraleStage.ROLL_FOR_MORALE_PASSED_TRANSITION, _applyFatigueStage);
            rollForMoraleStage.Bind(RollForMoraleStage.ROLL_FOR_MORALE_FAILED_TRANSITION, assignMoralePenaltyStage);

            assignMoralePenaltyStage.Bind(AssignMeleeMoralePenaltyStage.ASSIGN_MELEE_MORALE_PENALTY_FINISHED_TRANSITION,
                _applyFatigueStage);

            //Apply fatigue leaving has to be assigned from the outside, as it leaves this stage.
        }

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _applyFatigueStage.Bind(ApplyFatigueStage.APPLY_FATIGUE_FINISHED_TRANSITION,
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