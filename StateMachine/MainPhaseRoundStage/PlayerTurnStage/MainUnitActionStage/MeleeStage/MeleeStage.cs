
using System.Collections.Generic;

namespace FDG.Stages
{
    public class MeleeStage : ParentStage<IUnitActionContext, ICombatActionContext>
    {
        public StageBinding OnFinishedMelee;

        public MeleeStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            
        }


        public override void Enter(IUnitActionContext context)
        {
            GameContext.Log($"Melee stage entered.");

            base.Enter(context);
        }

        protected override ICombatActionContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            return new CombatActionContext(contextSelf.ActivatingUnit);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            OnFinishedMelee = new StageBinding(this);
            OnFinishedMelee.OnWillActivate += OnMeleeFinished;

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseMeleeDefenderStage(GameContext, this), out var chooseMeleeDefender)
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

            startingChild = chooseMeleeDefender;

            chooseMeleeDefender.OnDefenderChosen.Bind(pileIn);
            chooseMeleeDefender.BackToChooseAction.Bind(meleeFinishedEvent); //Should go back to choosing.
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

        private void OnMeleeFinished(IUnitActionContext context)
        {
            context.RegisterAttackedFinished();
        }
    }
}