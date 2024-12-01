
using System;

namespace FDG.Stages
{

    public class CoverCheckStage : CombatStage<CoverCheckResults, CoverCheckStage, IRangedCombatMetadata>
    {
        public CoverCheckStage(StateMachine stateMachine, ISingleAttackContext<IRangedCombatMetadata> context, StageBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<CoverCheckResults> onFinished)
        {
            //TODO: Test cover, and add mod to things.

            onFinished(new CoverCheckResults());
        }
    }
}