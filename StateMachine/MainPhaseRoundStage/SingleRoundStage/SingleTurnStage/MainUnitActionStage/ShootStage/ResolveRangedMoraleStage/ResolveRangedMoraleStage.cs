using FDG.Data;
using FDG.Rules.Dispatch;
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
            if (context.AttackedDefenders.Count > 0)
                GameContext.Log("Resolving ranged morale.");

            // Once per unit that was shot at this activation, in the order they were first targeted, and
            // only after every weapon has fired. Melee already works this way (its morale sub-chain sits
            // after the weapon loop drains); shooting used to run this stage inside the weapon loop, which
            // tested only the current weapon's target and re-baselined its wounds on every weapon.
            foreach (AttackedDefender attacked in context.AttackedDefenders)
            {
                await ResolveForDefender(attacked);
            }

            await ToFinished.Activate(context);
        }

        // A unit left at half strength or less by wounds from shooting must take a morale test; a failed
        // test makes it Shaken (a non-melee morale failure never Routs — Rout is a melee-only result,
        // GF v3.5.1). The test fires whenever the shoot action wounded the unit and it now sits at half
        // or less — a unit already below half tests again every activation it takes wounds (#254). The
        // baseline is its wound count when this attacker first targeted it, so a unit shot by three
        // weapons tests at most once, and a targeted-but-unwounded unit doesn't test.
        private async Task ResolveForDefender(AttackedDefender attacked)
        {
            DataBinding<UnitData> defenderBinding = attacked.Defender;

            // #431 Scourge: wounds the defender only COUNTS as having taken this action ("... if shooting wounds
            // would trigger Morale Tests") join the check, and are used up by it.
            float counted = CountedWounds.Take(defenderBinding.GetValue());
            if (counted > 0f)
                GameContext.Log($"{defenderBinding.Name()} counts as having taken {counted:0.##} extra wound(s) for morale.");

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(
                GameContext, defenderBinding, attacked.RemainingWoundsAtStart, counted);

            if (result == true)
            {
                GameContext.Log($"{defenderBinding.Name()} is at half strength or less but passed its morale test (needed {defenderBinding.Quality()}).");
            }
            else if (result == false)
            {
                GameContext.Log($"{defenderBinding.Name()} is at half strength or less, failed its morale test, and is now Shaken.");
            }
            else if (MoraleUtilities.DescribeSkippedWoundDrivenMorale(
                         attacked.RemainingWoundsAtStart, defenderBinding.GetValue(), counted) is string reason)
            {
                GameContext.Log(reason);
            }
        }
    }
}
