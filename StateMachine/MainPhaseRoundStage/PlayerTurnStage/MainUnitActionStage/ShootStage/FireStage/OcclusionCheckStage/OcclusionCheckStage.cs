
using System;

namespace FDG.Stages
{

    public class OcclusionCheckStage : CombatStage<OcclusionCheckResults, OcclusionCheckStage, IRangedCombatMetadata>
    {
        public OcclusionCheckStage(IGameContext gameContext, IStateMachineLayer<ISingleAttackContext<IRangedCombatMetadata>> parent) : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<OcclusionCheckResults> onFinished)
        {
            //TODO: Test occlusion, and add mod to future things.

            onFinished(new OcclusionCheckResults());
        }
    }
}