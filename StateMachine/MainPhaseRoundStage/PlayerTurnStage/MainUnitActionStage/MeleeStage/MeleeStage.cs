
using System.Collections.Generic;

namespace FDG.Stages
{
    public class MeleeStage : ParentStage<IUnitActionContext, IMeleeContext>
    {
        public StageBinding OnFinishedMelee;

        public MeleeStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            OnFinishedMelee = new StageBinding(this);
        }

        /*
        public MeleeStage(StateMachine stateMachine, IUnitActionContext context, IMeleeContext meleeContext,
            StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            PileInStage pileInStage 
                = new PileInStage(stateMachine, meleeContext);
            DetermineInRangeAttackersStage determineInRangeAttackersStage 
                = new DetermineInRangeAttackersStage(stateMachine, meleeContext);
            DetermineInRangeDefendersStage determineInRangeDefendersStage 
                = new DetermineInRangeDefendersStage(stateMachine, meleeContext);
            ChooseMeleeWeaponStage chooseMeleeWeaponStage 
                = new ChooseMeleeWeaponStage(stateMachine, meleeContext);
            SwingMeleeWeaponStage swingMeleeWeaponStage 
                = new SwingMeleeWeaponStage(stateMachine, meleeContext);
            DetermineCanKeepSwingingStage determineCanKeepSwingingStage 
                = new DetermineCanKeepSwingingStage(stateMachine, meleeContext);
            OfferStrikeBackStage offerStrikeBackStage 
                = new OfferStrikeBackStage(stateMachine, meleeContext);
            StrikeBackStage strikeBackStage 
                = new StrikeBackStage(stateMachine, meleeContext);
            DetermineMeleeWinnerStage determineMeleeWinnerStage
                = new DetermineMeleeWinnerStage(stateMachine, meleeContext);
            DetermineMoraleSaveNeededStage determineMoraleSaveNeededStage 
                = new DetermineMoraleSaveNeededStage(StateMachine, meleeContext);
            RollForMoraleStage rollForMoraleStage 
                = new RollForMoraleStage(stateMachine, meleeContext);
            AssignMeleeMoralePenaltyStage assignMoralePenaltyStage 
                = new AssignMeleeMoralePenaltyStage(stateMachine, meleeContext);
            _applyFatigueStage
                = new ApplyFatigueStage(StateMachine, meleeContext);

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
                determineMeleeWinnerStage);

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
        */

        public override void Enter(IUnitActionContext context)
        {
            GameContext.Log($"Melee stage entered.");

            base.Enter(context);
        }

        protected override IMeleeContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            MeleeContext meleeContext = new MeleeContext(GameContext);
            //meleeContext.BeginNewAttack(contextSelf.ActivatingUnit, contextSelf.) //TODO: Ah?

            return meleeContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMeleeContext> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new PileInStage(GameContext, this), out var pileIn)
                .AddChild(new DetermineInRangeAttackersStage(GameContext, this), out var determineInRangeAttackers)
                .AddChild(new DetermineInRangeDefendersStage(GameContext, this), out var determineInRangeDefenders)
                .AddChild(new ChooseMeleeWeaponStage(GameContext, this), out var chooseMeleeWeapon)
                .AddChild(new SwingMeleeWeaponStage(GameContext, this), out var swingMeleeWeaponStage)
                .AddChild(new DetermineCanKeepSwingingStage(GameContext, this), out var determineCanKeepSwinging)
                .AddChild(new OfferStrikeBackStage(GameContext, this), out var offerStrikeBack)
                .AddChild(new StrikeBackStage(GameContext, this), out var strikeBack)
                .AddChild(new DetermineMeleeWinnerStage(GameContext, this), out var determineMeleeWinner)
                .AddChild(new DetermineMoraleSaveNeededStage(GameContext, this), out var determineMoraleSaveNeeded)
                .AddChild(new RollForMoraleStage(GameContext, this), out var rollForMorale)
                .AddChild(new AssignMeleeMoralePenaltyStage(GameContext, this), out var assignMeleeMoralePenalty)
                .AddChild(new ApplyFatigueStage(GameContext, this), out var applyFatigueStage)
                .AddSibling(nameof(OnFinishedMelee), OnFinishedMelee, out string meleeFinishedEvent)
                .Build();

            startingChild = pileIn;

            pileIn.OnPiledIn.Bind(determineInRangeAttackers);
            determineInRangeAttackers.ToDetermineDefenders.Bind(determineInRangeDefenders);
            determineInRangeDefenders.ToChooseMeleeWeapons.Bind(chooseMeleeWeapon);
            chooseMeleeWeapon.OnChosen.Bind(swingMeleeWeaponStage);
            swingMeleeWeaponStage.FinishedSwinging.Bind(determineCanKeepSwinging);
            determineCanKeepSwinging.ReturnToChooseWeapon.Bind(chooseMeleeWeapon);
            determineCanKeepSwinging.OnOutOfWeapons.Bind(offerStrikeBack);
            determineCanKeepSwinging.OnDefenderKilled.Bind(applyFatigueStage);
            offerStrikeBack.OnOfferAccepted.Bind(strikeBack);
            offerStrikeBack.OnOfferRejected.Bind(determineMeleeWinner);
            strikeBack.FinishedStrikingBack.Bind(determineMeleeWinner);
            strikeBack.OnAttackerKilled.Bind(applyFatigueStage);
            determineMeleeWinner.OnNeedsRollToDecide.Bind(determineMoraleSaveNeeded);
            determineMeleeWinner.OnDoesntNeedRollToDecide.Bind(applyFatigueStage);
            determineMoraleSaveNeeded.ToRollForMorale.Bind(rollForMorale);
            rollForMorale.OnMoralePassed.Bind(applyFatigueStage);
            rollForMorale.OnMoraleFailed.Bind(assignMeleeMoralePenalty);
            assignMeleeMoralePenalty.OnAssignedPenalty.Bind(applyFatigueStage);
            applyFatigueStage.OnFatigueApplied.Bind(meleeFinishedEvent);

            return dictionary;
        }
    }
}