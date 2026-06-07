
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

        protected override async Task RunStage(ICombatMetadata metaData, Action<DetermineHitRollNeededResults> onFinished)
        {
            DetermineHitRollNeededResults results = new DetermineHitRollNeededResults(metaData.AttackingUnit.Quality());
            
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical:true);

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, distance, AttackerMoved: metaData.AttackerMoved),
                (attacker, ERuleSeat.Actor), (defender, ERuleSeat.Subject));
            RollModifierSink rollModifiers = new RollModifierSink();
            rollModifiers.ApplyFrom(operations);

            results.HitRollNeeded -= rollModifiers.Net(ERollKind.Hit);
            
            GameContext.Log($"Base hit roll required is {results.HitRollNeeded} based on attacker's quality.");

            onFinished(results);
        }
    }
}