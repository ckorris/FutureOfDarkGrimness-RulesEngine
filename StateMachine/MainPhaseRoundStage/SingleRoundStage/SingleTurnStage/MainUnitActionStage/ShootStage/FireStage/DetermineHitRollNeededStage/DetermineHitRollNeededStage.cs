
using FDG.Utilities;
using System;

namespace FDG.Stages
{

    public class DetermineHitRollNeededStage<TMetadata>
        : CombatStage<DetermineHitRollNeededResults, DetermineHitRollNeededStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public DetermineHitRollNeededStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<DetermineHitRollNeededResults> onFinished)
        {
            DetermineHitRollNeededResults results = new DetermineHitRollNeededResults(metaData.AttackingUnit.Quality());

            GameContext.Log($"Base hit roll required is {results.HitRollNeeded} based on attacker's quality.");

            onFinished(results);
        }
    }
}