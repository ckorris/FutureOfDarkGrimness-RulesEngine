
using System;

namespace FDG.Stages
{

    public class OcclusionCheckStage : CombatStage<OcclusionCheckResults, OcclusionCheckStage>
    {
        public OcclusionCheckStage(StateMachine stateMachine, ISingleRangedAttackContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetaData metaData, Action<OcclusionCheckResults> onFinished)
        {
            //TODO: Test occlusion, and add mod to future things.

            onFinished(new OcclusionCheckResults());
        }
    }
}