
using System;

namespace FDG.Stages
{

    public class CoverCheckStage : CombatStage<CoverCheckResults, CoverCheckStage, IRangedCombatMetadata>
    {
        public CoverCheckStage(IGameContext gameContext, IStateMachineLayer<IRangedCombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<CoverCheckResults> onFinished)
        {
            //TODO: Test cover, and add mod to things.

            onFinished(new CoverCheckResults());
        }
    }
}