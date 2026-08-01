using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 Teleport: "once per activation, before attacking, you may place this model anywhere fully within
    /// 6&quot; of its position." Modelled as a PLACEMENT, not a move (the same ruling as reposition-at-
    /// activation): nothing is asked of the path, only the destination, so it rides
    /// <see cref="PlaceObjectsRequest{T}"/> with the per-model <c>MaxDistanceFromStartInches</c> radius rather
    /// than a triggered move.
    ///
    /// <c>ChooseActionStage</c> routes the "Teleport" menu offer here by name (like Disembark/Embark), stashing
    /// the <see cref="AbilityOffer"/> on the context. This stage resolves that offer to pay the
    /// <see cref="Cost.OncePerActivation"/> (the <see cref="Effect.Teleport"/> marker itself is a no-op, so the
    /// only operation is the "used" marker token that stops it being re-offered this activation), then runs the
    /// placement and loops back to Choose Action via <see cref="OnFinished"/>. Layered - it sets neither
    /// HasMoved nor HasAttacked - so the unit re-evaluates Charge/Shoot/Pass from its new position (#206's
    /// proximity Pass gate is what makes "teleport clear of an enemy -> Pass returns" fall out for free).
    /// </summary>
    public class TeleportStage : StageBase<IUnitActionContext>
    {
        public const float TELEPORT_RANGE_INCHES = 6f;

        public StageBinding OnFinished;

        public TeleportStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            GameContext.LogDebug("Entered Teleport.");

            IUnit unit = context.ActivatingUnit.GetValue();

            // The chooser stashes the offer before routing here; consume it now, but DON'T pay its cost yet -
            // the once-per-activation gate is spent only if the placement is actually accepted (below), so
            // backing out of the prompt leaves Teleport available. (The CLI/AI resolvers default to a
            // stand-still Selected, not a cancel, so an automated run still pays and can't re-loop.)
            AbilityOffer? offer = context.PendingCustomAction;
            context.ClearPendingCustomAction();

            List<DataBinding<ModelData>> livingModels = context.ActivatingUnit.GetValue().ModelBindings
                .Where(binding => binding.GetValue().GetIsAlive()).ToList();

            if (livingModels.Count == 0)
            {
                await OnFinished.Activate(context);
                return;
            }

            GameContext.Log($"{unit.Name} may teleport each model up to {TELEPORT_RANGE_INCHES:0.##}\".");

            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID,
                $"Teleport {unit.Name} (up to {TELEPORT_RANGE_INCHES:0.##}\")",
                wholeTable, livingModels, allowCancel: true, maxDistanceFromStartInches: TELEPORT_RANGE_INCHES,
                cancelHint: "Cancel the teleport and pick a different action.");

            CancellableResult<List<PlacedObjectEntry<ModelData>>> result = await GameContext.PlayerRequester
                .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);

            if (result is not Selected<List<PlacedObjectEntry<ModelData>>> selected)
            {
                // Backed out - the teleport was not used, so its once-per-activation cost stays unspent and it
                // remains available if the unit re-enters Choose Action.
                GameContext.Log($"{unit.Name} did not teleport.");
                await OnFinished.Activate(context);
                return;
            }

            // #248: the placement is about to land and the cost about to be paid — irreversible. The
            // backed-out branch above returns before this, so a declined teleport stays pristine.
            context.MarkIrreversibleAction();

            // Accepted: pay the once-per-activation cost now (the Effect.Teleport marker is a no-op, so resolving
            // the offer yields only the "used" marker token that stops it being re-offered this activation).
            if (offer != null)
            {
                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.ResolveAbility(offer, new[] { unit });
                OperationApplier.ApplyTokenOperations(ops);
            }

            // Standing still is a legal answer (the "you MAY place" wording), and the CLI/AI resolvers give it
            // by default - so report what actually happened rather than claiming a teleport that never occurred.
            bool anyMoved = false;
            foreach (PlacedObjectEntry<ModelData> placement in selected.Value)
            {
                ModelData model = placement.Binding.GetValue();
                if (Position.GetDistance2D(model.Position, placement.Position) > 0.001f) anyMoved = true;

                model.SetPosition(placement.Position);
                if (placement.Facing.HasValue) model.SetFacing(placement.Facing.Value);
            }

            GameContext.Log(anyMoved
                ? $"{unit.Name} teleported."
                : $"{unit.Name} held its position.");

            await OnFinished.Activate(context);
        }
    }
}
