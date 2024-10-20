
using System;

namespace FDG.Stages
{

    public class CoverCheckStage : CombatStage<CoverCheckResults, CoverCheckStage>
    {
        public CoverCheckStage(StateMachine stateMachine, ISingleAttackContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetaData metaData, Action<CoverCheckResults> onFinished)
        {
            //TODO: Test cover, and add mod to things.

            onFinished(new CoverCheckResults());
        }
    }
}