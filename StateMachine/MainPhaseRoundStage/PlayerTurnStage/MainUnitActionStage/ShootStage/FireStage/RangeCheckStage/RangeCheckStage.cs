
using System;

namespace FDG.Stages
{

    public class RangeCheckStage : CombatStage<RangeCheckResults, RangeCheckStage, IRangedCombatMetadata>
    {
        public RangeCheckStage(IGameContext gameContext, IStateMachineLayer<IRangedCombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<RangeCheckResults> onFinished)
        {
            onFinished(new RangeCheckResults());
        }
    }

}