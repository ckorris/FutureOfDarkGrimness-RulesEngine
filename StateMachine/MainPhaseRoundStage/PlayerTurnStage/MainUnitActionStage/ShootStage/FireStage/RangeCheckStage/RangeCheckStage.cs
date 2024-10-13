
using System;

namespace FDG.Stages
{

    public class RangeCheckStage : CombatStage<RangeCheckResults, RangeCheckStage>
    {
        public RangeCheckStage(StateMachine stateMachine, ISingleRangedAttackContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetaData metaData, Action<RangeCheckResults> onFinished)
        {
            onFinished(new RangeCheckResults());
        }
    }

}