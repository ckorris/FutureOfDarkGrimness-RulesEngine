using System.Collections.Generic;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #376 Bloodthirsty Fighter: after a melee swing's wounds are applied, roll the follow-up attacks
    /// its block-roll 1s earned (posted as <see cref="BonusAttackResults"/> by AssignWoundsStage) as a
    /// REAL child batch with the same weapon — the full hit -> save -> wound chain, so the weapon's AP
    /// and its other rules apply to the bonus attacks exactly as they did to the base swing (the
    /// ResolveExtraAttackStage shape, minus the ranged-only cover stage). Owner ruling 2026-08-22.
    ///
    /// <para>No chaining: the child metadata is flagged <c>IsBonusAttack</c>, and AssignWoundsStage
    /// refuses to post results for such a batch — "this rule doesn't apply to newly generated
    /// attacks". The batch is skipped outright when nothing was earned or the defender already died
    /// to the base swing. Runs inside SwingMeleeWeaponStage's chain, so a strike-back's swings get it
    /// too (StrikeBackStage reuses the same swing stage). The count stays fractional under the
    /// probabilistic roller and rides <c>AttackCountOverride</c>.</para>
    /// </summary>
    public class ResolveBonusMeleeAttacksStage : ParentStage<ICombatMetadata, ICombatMetadata>
    {
        public StageBinding OnBonusAttacksResolved;

        private float _attackCount;

        public ResolveBonusMeleeAttacksStage(IGameContext gameContext,
            IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        public override async Task Enter(ICombatMetadata context)
        {
            _attackCount = context.QueryForResult(out BonusAttackResults results)
                ? results.AttackCount : 0f;

            // Nothing earned (or, defensively, a bonus batch of our own - AssignWoundsStage never posts
            // for one, so this arm should be unreachable): pass straight through.
            if (_attackCount <= 0f || context.IsBonusAttack)
            {
                await OnBonusAttacksResolved.Activate(context);
                return;
            }

            // The base swing already killed the defender - there is nothing left to follow up on.
            if (context.DefendingUnit.RemainingWounds() <= 0)
            {
                GameContext.Log("The follow-up attacks lapse: the target was already destroyed.");
                await OnBonusAttacksResolved.Activate(context);
                return;
            }

            GameContext.Log($"{context.AttackingUnit.GetValue().Name} follows up with " +
                $"{_attackCount:0.##} bonus attack(s) using {context.WeaponType.Name}.");
            await base.Enter(context);
        }

        protected override ICombatMetadata GetNewChildContext(ICombatMetadata contextSelf)
        {
            // Same units, same REAL weapon and combat flags as the swing that earned the attacks; only
            // the dice pool differs (AttackCountOverride) and the batch is marked as a bonus.
            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.AttackingUnit,
                contextSelf.DefendingUnit, contextSelf.WeaponType, contextSelf.WeaponCount,
                attackerMoved: contextSelf.AttackerMoved, isMelee: true,
                isCharging: contextSelf.IsCharging,
                chargeOriginDistanceInches: contextSelf.ChargeOriginDistanceInches,
                unpredictableBranch: contextSelf.UnpredictableBranch,
                isBonusAttack: true, attackCountOverride: _attackCount);

            // Melee runs no cover check but the shared DetermineSaveRollsNeededStage still reads
            // CoverCheckResults - seed a zero bonus, as SwingMeleeWeaponStage does.
            metadata.AddResult(new CoverCheckResults(0));
            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnBonusAttacksResolved = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<ICombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new DetermineHitRollStage<ICombatMetadata>(GameContext, this), out var determineHitRollNeeded)
                .AddChild(new RollToHitStage<ICombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnBonusAttacksResolved), OnBonusAttacksResolved, out string resolvedEvent)
                .Build();

            startingChild = buildTargetList;

            buildTargetList.BindNextStage(determineHitRollNeeded)
                .BindNextStage(rollToHit)
                .BindNextStage(determineSaveRollsNeeded)
                .BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(resolvedEvent);

            return dictionary;
        }
    }
}
