using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;

namespace FDG.Stages
{
    /// <summary>
    /// #010 — resolves a custom action the player chose in <see cref="ChooseActionStage"/> (an
    /// <see cref="AbilityOffer"/> stashed on the context), then loops back to Choose Action via
    /// <see cref="OnFinished"/>. It deliberately does NOT touch the move/attack flags: a custom action
    /// (e.g. a Caster's spell) is a layered overlay during activation, not an action that ends the turn —
    /// so the unit may still Move/Shoot afterward, gated only by the ability's own once-per-activation cost.
    ///
    /// The ability is resolved against the bearer itself (self-target). Target-selecting custom actions —
    /// resolving the ability's <c>TargetSelector</c> through a player request — are deferred until a live
    /// rule needs them (#010 notes / #033). <see cref="OperationApplier.ApplyTokenOperations"/> pays the
    /// cost (spell tokens / the once-per-X marker) and applies token-granting effects; effects that need
    /// engine services or a child pipeline (movement, damage spells) are handled by their own stage, as
    /// Strafing/Reactivate already do — out of scope for the generic seam.
    /// </summary>
    public class CustomActionStage : StageBase<IUnitActionContext>
    {
        public StageBinding OnFinished;

        public CustomActionStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            AbilityOffer? offer = context.PendingCustomAction;

            // Defensive: the chooser always sets a pending offer before routing here. If somehow absent,
            // just return to Choose Action rather than throwing mid-activation.
            if (offer == null)
            {
                GameContext.Log("Custom action entered with no pending offer; returning to Choose Action.");
                await OnFinished.Activate(context);
                return;
            }

            IUnit bearer = context.ActivatingUnit.GetValue();

            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                .ResolveAbility(offer, new[] { bearer });
            OperationApplier.ApplyTokenOperations(ops);

            GameContext.Log($"{bearer.Name} used {offer.RuleName}.");

            context.ClearPendingCustomAction();
            await OnFinished.Activate(context);
        }
    }
}
