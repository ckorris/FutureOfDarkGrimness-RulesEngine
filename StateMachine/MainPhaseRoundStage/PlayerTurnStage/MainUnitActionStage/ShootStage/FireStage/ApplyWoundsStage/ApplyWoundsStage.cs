
using System;

namespace FDG.Stages
{

    public class ApplyWoundsStage : CombatStage<ApplyWoundsResults, ApplyWoundsStage, ICombatMetadata>
    {
        public ApplyWoundsStage(StateMachine stateMachine, ISingleAttackContext<ICombatMetadata> context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<ApplyWoundsResults> onFinished)
        {
            onFinished(new ApplyWoundsResults());
        }
    }
}