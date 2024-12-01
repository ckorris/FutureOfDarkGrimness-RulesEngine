
using System;

namespace FDG.Stages
{

    public class RangeCheckStage : CombatStage<RangeCheckResults, RangeCheckStage, IRangedCombatMetadata>
    {
        public RangeCheckStage(StateMachine stateMachine, ISingleAttackContext<IRangedCombatMetadata> context, StageBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<RangeCheckResults> onFinished)
        {
            onFinished(new RangeCheckResults());
        }
    }

}