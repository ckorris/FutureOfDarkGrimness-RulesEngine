
using FDG.Utilities;
using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Stages
{

    public class DetermineHitRollNeededStage<TMetadata>
        : CombatStage<DetermineHitRollNeededResults, DetermineHitRollNeededStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public DetermineHitRollNeededStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<DetermineHitRollNeededResults, Task> onFinished)
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

            DetermineHitRollNeededResults results = new DetermineHitRollNeededResults(baseQuality);

            RollModifierSink rollModifiers = new RollModifierSink();
            rollModifiers.ApplyFrom(operations);

            results.HitRollNeeded -= rollModifiers.Net(ERollKind.Hit);

            GameContext.Log($"Base hit roll required is {results.HitRollNeeded} based on attacker's quality.");

            await onFinished(results);
        }
    }
}