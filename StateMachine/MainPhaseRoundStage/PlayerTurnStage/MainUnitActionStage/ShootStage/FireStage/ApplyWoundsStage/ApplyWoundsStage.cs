
using System;

namespace FDG.Stages
{

    public class ApplyWoundsStage : CombatStage<ApplyWoundsResults, ApplyWoundsStage>
    {
        public ApplyWoundsStage(StateMachine stateMachine, ISingleAttackContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetaData metaData, Action<ApplyWoundsResults> onFinished)
        {
            onFinished(new ApplyWoundsResults());
        }
    }
}