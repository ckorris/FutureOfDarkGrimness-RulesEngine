
using System;

namespace FDG.Stages
{

    public class OcclusionCheckStage : CombatStage<OcclusionCheckResults, OcclusionCheckStage, IRangedCombatMetadata>
    {
        public OcclusionCheckStage(StateMachine stateMachine, ISingleAttackContext<IRangedCombatMetadata> context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<OcclusionCheckResults> onFinished)
        {
            //TODO: Test occlusion, and add mod to future things.

            onFinished(new OcclusionCheckResults());
        }
    }
}