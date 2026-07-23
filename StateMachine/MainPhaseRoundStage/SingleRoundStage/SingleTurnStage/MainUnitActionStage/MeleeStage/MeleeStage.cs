
using System.Collections.Generic;

namespace FDG.Stages
{
    public class MeleeStage : ParentStage<IUnitActionContext, ICombatActionContext>
    {
        public StageBinding OnFinishedMelee;

        /// <summary>
        /// Left the charge without fighting - no defender was reachable, or the player backed out of the
        /// defender pick. Distinct from <see cref="OnFinishedMelee"/> precisely because that binding's
        /// OnWillActivate spends the unit's attack; routing a back-out through it would leave the unit
        /// unable to Move, Charge or Shoot for the rest of its activation. Mirrors
        /// <see cref="ShootStage.BackToChooseAction"/>.
        /// </summary>
        public StageBinding BackToChooseAction;

        public MeleeStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {

        }


        public override async Task Enter(IUnitActionContext context)
        {
            GameContext.LogDebug($"Melee stage entered.");

            await base.Enter(context);
        }

        protected override ICombatActionContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            // Melee is only ever entered via a Charge action, so the activating unit's melee swing
            // is a charge — isCharging:true drives charge-only rules (Thrust). The defender's
            // strike-back (StrikeBackStage) builds its context with isCharging:false.
            //
            // #197: the activation context carries the pre-move distance to each enemy, which is the distance
            // this charge was declared from. Handed down so the "shoots or charges enemies over 9\" away"
            // rules can gate their melee arm once the defender is picked.
            return new CombatActionContext(contextSelf.GameContext, contextSelf.ActivatingUnit,
                isMelee: true, attackerMoved: contextSelf.HasMoved, isCharging: true,
                activationContext: contextSelf);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            OnFinishedMelee = new StageBinding(this);
            OnFinishedMelee.OnWillActivate += OnMeleeFinished;
            BackToChooseAction = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseMeleeDefenderStage(GameContext, this), out var chooseMeleeDefender)
                .AddChild(new ResolveImpactHitsStage(GameContext, this), out var resolveImpact)
                .AddChild(new ResolveRavageWoundsStage(GameContext, this), out var resolveRavage)
                .AddChild(new DetermineStrikeOrderStage(GameContext, this), out var determineStrikeOrder)
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
                .AddChild(new ConsolidateStage(GameContext, this), out var consolidate)
                .AddChild(new PostMeleeStage(GameContext, this), out var postMelee)
                .AddSibling(nameof(OnFinishedMelee), OnFinishedMelee, out string meleeFinishedEvent)
                .AddSibling(nameof(BackToChooseAction), BackToChooseAction, out string backToChooseEvent)
                .Build();

            startingChild = chooseMeleeDefender;

            chooseMeleeDefender.OnDefenderChosen.Bind(resolveImpact);
            // Nothing has been rolled or moved yet, so this exit must not spend the unit's attack.
            chooseMeleeDefender.BackToChooseAction.Bind(backToChooseEvent);
            resolveImpact.OnImpactResolved.Bind(resolveRavage); // #042 Impact: charge-contact auto-hits before swings.
            resolveRavage.OnRavageResolved.Bind(determineStrikeOrder); // #197 P10 Ravage: charge-contact auto-WOUNDS before swings.
            determineStrikeOrder.OnStrikeOrderDetermined.Bind(pileIn); // #042 Counter: charged unit may strike first.
            pileIn.OnPiledIn.Bind(determineInRangeAttackers);
            determineInRangeAttackers.ToDetermineDefenders.Bind(determineInRangeDefenders);
            // #017: no in-range attacker (e.g. the defender was wiped by Impact hits before the swing) →
            // skip the swing but follow the same applyFatigue → consolidate → finish path as a defender
            // killed mid-swing, so the charger still consolidates.
            determineInRangeAttackers.OnNoAttackersInRange.Bind(applyFatigueStage);
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
            applyFatigueStage.OnFatigueApplied.Bind(consolidate);
            // After the melee fully resolves, the charged unit may make its post-melee move (Harassing);
            // PostMeleeStage fires the Melee_OnPostMelee hook then finishes. The BackToChooseAction exit
            // (no melee occurred) leaves via backToChooseEvent, bypassing both the move and the attack spend.
            consolidate.OnConsolidated.Bind(postMelee);
            postMelee.ToFinished.Bind(meleeFinishedEvent);

            return dictionary;
        }

        private void OnMeleeFinished(IUnitActionContext context)
        {
            context.RegisterAttackedFinished();
        }
    }
}