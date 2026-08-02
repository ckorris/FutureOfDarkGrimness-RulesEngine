using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// The shared "you MAY place all models anywhere fully within Nin of their position" placement, folded
    /// from <see cref="RuleOperation.RepositionModels"/> operations. A placement, not a move: each model is
    /// constrained to its own radius and the usual destination legality (zone, overlap, cohesion, impassible
    /// terrain), but nothing is asked of the path. Declining is legal (<c>allowCancel</c>), which is what the
    /// CLI/AI resolvers do by default.
    ///
    /// Two stages fold repositions through here: <c>ActivationStartStage</c> (Wolfborn / Bounding / Rapid
    /// Blink, at activation start) and <c>DeployUnitStage</c> (Fanatic, at deployment). Both sum their
    /// <see cref="RuleOperation.RepositionModels"/> ops into a single call so a unit that stacks two
    /// reposition rules gets one prompt for the combined radius, not two.
    /// </summary>
    public static class RepositionPlacement
    {
        /// <summary>
        /// Offers the placement for the living models of <paramref name="unit"/> up to
        /// <paramref name="maxInches"/>. A no-op when the radius is not positive or no models are alive.
        /// </summary>
        public static async Task Offer(IGameContext gameContext, UnitData unit, float maxInches)
        {
            if (maxInches <= 0f) return;

            List<DataBinding<ModelData>> livingModels = unit.ModelBindings
                .Where(binding => binding.GetValue().GetIsAlive()).ToList();

            if (livingModels.Count == 0) return;

            gameContext.Log($"{unit.Name} may reposition each model up to {maxInches:0.##}\".");

            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID,
                $"Repositioning {unit.Name}",
                wholeTable, livingModels, allowCancel: true, maxDistanceFromStartInches: maxInches,
                cancelHint: "Cancel the reposition. Every model stays where it is.");

            CancellableResult<List<PlacedObjectEntry<ModelData>>> result = await gameContext.PlayerRequester
                .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);

            if (result is not Selected<List<PlacedObjectEntry<ModelData>>> selected)
            {
                gameContext.Log($"{unit.Name} stayed where it was.");
                return;
            }

            // Standing still is a legal answer, and the CLI/AI resolvers give it by default - so report what
            // actually happened rather than claiming a move that never occurred.
            bool anyMoved = false;
            foreach (PlacedObjectEntry<ModelData> placement in selected.Value)
            {
                ModelData model = placement.Binding.GetValue();
                if (Position.GetDistance2D(model.Position, placement.Position) > 0.001f) anyMoved = true;

                model.SetPosition(placement.Position);
                if (placement.Facing.HasValue) model.SetFacing(placement.Facing.Value);
            }

            gameContext.Log(anyMoved
                ? $"{unit.Name} repositioned."
                : $"{unit.Name} held its position.");
        }

        /// <summary>
        /// Sums the <see cref="RuleOperation.RepositionModels"/> ops in <paramref name="operations"/> and,
        /// if the total radius is positive, offers the placement for <paramref name="unit"/>.
        /// </summary>
        public static Task OfferFromOperations(IGameContext gameContext, UnitData unit,
            IReadOnlyList<RuleOperation> operations)
        {
            float inches = operations.OfType<RuleOperation.RepositionModels>().Sum(op => op.MaxInches);
            return Offer(gameContext, unit, inches);
        }

        /// <summary>
        /// Whether <paramref name="effect"/> resolves into a placement offered through <see cref="Offer"/>,
        /// i.e. one that ASKS ITS OWN cancellable question. An offering stage uses this to decide whether an
        /// optional ("you MAY") ability still needs a Yes/No gate in front of it: the Yes/No exists to make an
        /// irreversible ability optional, and an ability that already lets the player decline at the placement
        /// does not need to be asked twice (#197 Dash, owner-signed-off 2026-07-28).
        ///
        /// <para>Only <c>ReconcileEndOfActivationStage</c> consults this today. <c>DeployUnitStage</c> keeps
        /// its unconditional Yes/No for Vanguard and Fanatic: that prompting is shipped behaviour its tests
        /// pin, and changing it was out of scope for the Dash slice. Recorded rather than silently unified.</para>
        /// </summary>
        public static bool IsCancellablePlacement(Effect effect) =>
            effect is Effect.RepositionAtActivation or Effect.RepositionOnDeploy;
    }
}
