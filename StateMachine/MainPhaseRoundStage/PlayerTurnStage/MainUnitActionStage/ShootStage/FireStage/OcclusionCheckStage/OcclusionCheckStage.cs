
using System;

namespace FDG.Stages
{

    public class OcclusionCheckStage : CombatStage<OcclusionCheckResults, OcclusionCheckStage, ICombatMetadata>
    {
        public OcclusionCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<OcclusionCheckResults> onFinished)
        {
            //TODO: Test occlusion, and add mod to future things.

            onFinished(new OcclusionCheckResults());
        }
    }
}