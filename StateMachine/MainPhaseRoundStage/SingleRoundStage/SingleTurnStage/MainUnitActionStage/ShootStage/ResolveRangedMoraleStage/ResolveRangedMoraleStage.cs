using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{

    public class ResolveRangedMoraleStage : StageBase<ICombatActionContext>
    {
        public const string RESOLVE_RANGED_MORALE_FINISHED_TRANSITION =
            "ResolveRangedMoraleFinished";

        public StageBinding ToFinished;

        public ResolveRangedMoraleStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToFinished = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Resolving ranged morale.");

            ResolveForDefender(context);

            await ToFinished.Activate(context);
        }

        // A unit reduced to half strength or less by shooting must take a morale test; because the
        // trigger condition is being at half strength, a failed test always Routs it. The test fires only
        // on the fire that *crosses* into half strength: DefenderRemainingWoundsAtStart is re-snapshotted
        // by ChooseRangedAttackStage before each weapon, so once a unit is below half a later weapon at
        // the same target sees a sub-half start and won't re-test (no double jeopardy).
        private void ResolveForDefender(ICombatActionContext context)
        {
            DataBinding<UnitData> defenderBinding = context.DefendingUnit;
            if (defenderBinding == null) return; // nothing was fired at (e.g. occluded before a target was set)

            bool? result = MoraleUtilities.ResolveWoundDrivenMorale(
                GameContext, defenderBinding, context.DefenderRemainingWoundsAtStart);

            if (result == true)
            {
                GameContext.Log($"{defenderBinding.Name()} was reduced to half strength but passed its morale test (needed {defenderBinding.Quality()}).");
            }
            else if (result == false)
            {
                GameContext.Log($"{defenderBinding.Name()} was reduced to half strength, failed its morale test, and is Routed.");
            }
        }
    }
}
