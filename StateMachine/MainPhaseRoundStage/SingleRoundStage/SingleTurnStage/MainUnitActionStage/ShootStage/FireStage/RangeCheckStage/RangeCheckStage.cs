
using System;

namespace FDG.Stages
{

    public class RangeCheckStage : CombatStage<RangeCheckResults, RangeCheckStage, ICombatMetadata>
    {
        public RangeCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<RangeCheckResults> onFinished)
        {
            onFinished(new RangeCheckResults());
        }
    }

}