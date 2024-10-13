
using System;

namespace FDG.Stages
{

    public class AssignWoundsStage : CombatStage<AssignWoundsResults, AssignWoundsStage>
    {
        public AssignWoundsStage(StateMachine stateMachine, ISingleRangedAttackContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetaData metaData, Action<AssignWoundsResults> onFinished)
        {
            onFinished(new AssignWoundsResults());
        }
    }
}