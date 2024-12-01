
using System;

namespace FDG.Stages
{

    public class DetermineHitRollNeededStage : CombatStage<DetermineHitRollNeededResults, DetermineHitRollNeededStage, ICombatMetadata>
    {
        public DetermineHitRollNeededStage(StateMachine stateMachine, ISingleAttackContext<ICombatMetadata> context, StageBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<DetermineHitRollNeededResults> onFinished)
        {
            DetermineHitRollNeededResults results = new DetermineHitRollNeededResults(metaData.AttackingUnit.Quality);

            metaData.TextOutput.Log($"Base hit roll required is {results.HitRollNeeded} based on attacker's quality.");

            onFinished(results);
        }
    }
}