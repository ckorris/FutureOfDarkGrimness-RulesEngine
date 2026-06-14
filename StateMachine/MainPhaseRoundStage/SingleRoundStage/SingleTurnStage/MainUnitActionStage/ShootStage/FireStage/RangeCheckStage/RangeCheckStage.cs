
using System;

namespace FDG.Stages
{

    public class RangeCheckStage : CombatStage<RangeCheckResults, RangeCheckStage, ICombatMetadata>
    {
        public RangeCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<RangeCheckResults, Task> onFinished)
        {
            await onFinished(new RangeCheckResults());
        }
    }

}