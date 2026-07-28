using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class ReconcileEndOfActivationStage : StageBase<ISingleTurnContext>
    {

        public StageBinding OnFinished;

        int _enterCount = 0;

        public ReconcileEndOfActivationStage(IGameContext gameContext, IStateMachineLayer<ISingleTurnContext> parent) : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(ISingleTurnContext context)
        {
            // #203: stage transitions complete synchronously, so without a yield every decision in the
            // game leaves frames on one ever-deepening call stack and a long game dies as an uncatchable
            // StackOverflow. Yielding once per activation unwinds the accumulated chain, bounding stack
            // depth by the deepest single activation instead of the whole game.
            await Task.Yield();

            GameContext.LogDebug($"ReconcileEndOfActivationStage entrance {_enterCount}");

            // Clear the just-activated unit's "used this activation" markers (once-per-activation cost gates,
            // e.g. Strafing) so they reset for its next activation.
            if (context.ActivatedUnit != null)
            {
                IUnit unit = context.ActivatedUnit.GetValue();

                // #197 P22: "when this unit ends its activation" abilities (Ambush Re-Deployment's
                // self-removal), offered BEFORE the token sweep so the moment is still "the end of the
                // activation" rather than the bookkeeping after it. A unit wiped out during its own
                // activation (a melee strike-back) has no end-of-activation choices to make.
                if (unit.GetIsAlive())
                {
                    await OfferEndOfActivationAbilities(unit);
                }

                List<ITokenContainer> containers = new List<ITokenContainer> { unit.Tokens };
                containers.AddRange(unit.Models.Select(model => model.Tokens));
                new TokenClearService().ClearForHook(EHookID.Activation_OnEndOfActivation, containers);
            }

            // Activation over -- clear the spotlight target so nothing is highlighted between activations.
            if (GameContext.GameDataStore.IsTypeAssigned<GameProgressData>())
                GameProgressUtilities.SetActivatingUnit(GameContext.GameDataStore, null);

            await OnFinished.Activate(context);
        }

        /// <summary>
        /// The end-of-activation ability seam (#197 P22), mirroring <see cref="ActivationStartStage"/>'s
        /// shape: offers grouped by rule via <see cref="AbilityEffectChoice"/>, a single-ability rule
        /// asked as an optional Yes/No, a multi-ability rule a mandatory pick. One deliberate
        /// difference: the Yes/No DEFAULTS TO NO. At activation start the lone optional ability is a
        /// buff (Speed Feat) and the aggressive default suits the EOF/AI fallback; here the only corpus
        /// ability is Ambush Re-Deployment's once-per-game self-REMOVAL with a mandatory return, which
        /// an auto-accepting default would fire on every AI unit's first activation.
        /// </summary>
        private async Task OfferEndOfActivationAbilities(IUnit unit)
        {
            foreach (IReadOnlyList<AbilityOffer> ruleOffers in AbilityEffectChoice.GroupByRule(
                         GameContext.RuleEvaluator.GatherOffers(new ActivationEndContext(unit))))
            {
                if (ruleOffers.Count == 1)
                {
                    AbilityOffer offer = ruleOffers[0];
                    var question = new YesNoRequest(unit.PlayerID,
                        $"Use {offer.RuleName} on {unit.Name}?", defaultAnswer: false);
                    bool accepted = await GameContext.PlayerRequester
                        .RequestDecision<YesNoRequest, bool>(question);
                    if (!accepted) continue;
                }

                AbilityEffectChoice.Outcome outcome = await AbilityEffectChoice.Resolve(
                    GameContext, unit.PlayerID, unit, ruleOffers, "as this activation ends");

                GameContext.Log(ruleOffers.Count == 1
                    ? $"{unit.Name}: {outcome.Chosen.RuleName} applies."
                    : $"{unit.Name}: {outcome.Chosen.RuleName} - chose {outcome.Chosen.Ability.Label}.");
            }
        }
    }
}
