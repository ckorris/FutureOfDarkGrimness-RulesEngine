
using FDG.Utilities;
using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Stages
{

    public class DetermineHitRollStage<TMetadata>
        : CombatStage<DetermineHitRollResults, DetermineHitRollStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public DetermineHitRollStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<DetermineHitRollResults, Task> onFinished)
        {
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical:true);

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, distance, AttackerMoved: metaData.AttackerMoved,
                    IsMelee: metaData.IsMelee, IsCharging: metaData.IsCharging),
                (attacker, ERuleSeat.Actor, metaData.WeaponType), (defender, ERuleSeat.Subject, null));

            // #042 quality-floor rules (Reliable) set the BASE quality before per-roll modifiers:
            // "treated as 2+, still modifiable". Fold the floor sink and improve the base, then let
            // the roll-modifier sink (Stealth/Artillery/Indirect) stack on top. Stage interprets no op.
            int baseQuality = metaData.AttackingUnit.Quality();
            QualityFloorSink qualityFloor = new QualityFloorSink();
            qualityFloor.ApplyFrom(operations);
            if (qualityFloor.HasFloor)
            {
                baseQuality = Math.Min(baseQuality, qualityFloor.Quality);
            }

            // #015 Attack count: how many attack dice this volley rolls (weapon Attacks × weapons
            // firing). Computed here, beside the hit threshold, so attack-count modifiers fold at this
            // point with the same accumulate-before-use discipline as the hit-roll modifiers above (no
            // such rule exists yet — the producer side is deferred). RollToHitStage reads the result.
            float attackCount = metaData.WeaponType.Attacks * metaData.WeaponCount;

            DetermineHitRollResults results = new DetermineHitRollResults(baseQuality, attackCount);

            RollModifierSink rollModifiers = new RollModifierSink();
            rollModifiers.ApplyFrom(operations);

            results.HitRollNeeded -= rollModifiers.Net(ERollKind.Hit);

            GameContext.Log($"Base hit roll required is {results.HitRollNeeded} based on attacker's quality.");

            // #020 Fatigue: a unit that has already charged or struck back this round — or that is Shaken
            // (#089) — hits only on unmodified 6s in melee. Override the computed threshold rather than
            // stacking a modifier, since the rule ignores all modifiers; the d6 comparison in
            // RollToHitStage then admits only natural 6s.
            if (metaData.IsMelee && FatigueUtilities.CountsAsFatiguedInMelee(attacker))
            {
                results.HitRollNeeded = 6;
                GameContext.Log($"{attacker.Name} is fatigued — hits only on unmodified 6s in melee.");
            }

            await onFinished(results);
        }
    }
}