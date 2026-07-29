using System;
using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// The defender's wound count before a strafe, seeded into the child metadata by
    /// <see cref="StrafingStage"/> so <see cref="ResolveStrafeMoraleStage"/> can tell whether the attack is
    /// what pushed the unit to half strength. Mirrors <c>AttackedDefender.RemainingWoundsAtStart</c>, which
    /// serves the same purpose for an ordinary shoot action.
    /// </summary>
    public record StrafeMoraleBaseline(float RemainingWoundsBefore);

    /// <summary>The outcome of the strafe's morale test: null if none was taken.</summary>
    public record StrafeMoraleResults(bool? Passed);

    /// <summary>
    /// #197: the last link in <see cref="StrafingStage"/>'s chain. Strafing attacks "as if it was shooting",
    /// and a unit left at half strength or less by shooting takes a morale test - so the strafe's victim
    /// tests too. Runs the same <c>MoraleUtilities.ResolveWoundDrivenMorale</c> as
    /// <see cref="ResolveRangedMoraleStage"/>; only the baseline's provenance differs, since a strafe has no
    /// weapon loop to aggregate across.
    ///
    /// <para>This is a deliberate step away from the other mid-move attacks: Impact hits and Crossing Attack
    /// deal their wounds and never test. Owner-signed-off 2026-07-28 - Strafing's text says "as if it was
    /// shooting", and those two say nothing of the kind.</para>
    /// </summary>
    public class ResolveStrafeMoraleStage
        : CombatStage<StrafeMoraleResults, ResolveStrafeMoraleStage, ICombatMetadata>
    {
        public ResolveStrafeMoraleStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<StrafeMoraleResults, Task> onFinished)
        {
            StrafeMoraleBaseline baseline = QueryForResultOrThrowException<StrafeMoraleBaseline>(metaData);
            DataBinding<UnitData> defender = metaData.DefendingUnit;

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(
                GameContext, defender, baseline.RemainingWoundsBefore);

            if (result == true)
            {
                GameContext.Log($"{defender.Name()} is at half strength or less but passed its morale " +
                    $"test (needed {defender.Quality()}).");
            }
            else if (result == false)
            {
                GameContext.Log($"{defender.Name()} is at half strength or less, failed its morale test, " +
                    "and is now Shaken.");
            }

            await onFinished(new StrafeMoraleResults(result));
        }
    }
}
