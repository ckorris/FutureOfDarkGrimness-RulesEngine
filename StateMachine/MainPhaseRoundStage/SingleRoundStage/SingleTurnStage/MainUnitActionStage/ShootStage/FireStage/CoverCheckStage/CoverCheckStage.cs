
using System;

namespace FDG.Stages
{

    public class CoverCheckStage : CombatStage<CoverCheckResults, CoverCheckStage, ICombatMetadata>
    {
        public CoverCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Action<CoverCheckResults> onFinished)
        {
            //TODO: Test cover, and add mod to things.

            onFinished(new CoverCheckResults());
        }
    }
}