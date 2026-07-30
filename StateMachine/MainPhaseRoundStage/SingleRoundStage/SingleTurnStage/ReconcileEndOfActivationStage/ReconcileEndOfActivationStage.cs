using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
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
                UnitData unit = context.ActivatedUnit.GetValue();

                // #197 P22: "when this unit ends its activation" abilities (Ambush Re-Deployment's
                // self-removal), offered BEFORE the token sweep so the moment is still "the end of the
                // activation" rather than the bookkeeping after it. A unit wiped out during its own
                // activation (a melee strike-back) has no end-of-activation choices to make.
                if (unit.GetIsAlive())
                {
                    await OfferEndOfActivationAbilities(context.ActivatedUnit);
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
        /// asked as an optional Yes/No, a multi-ability rule a mandatory pick.
        ///
        /// <para><b>The Yes/No defaults to YES</b> (owner ruling, 2026-07-30). It defaulted to NO until
        /// then, on the grounds that the ability it guarded was Ambush Re-Deployment's once-per-game
        /// self-REMOVAL and an auto-accepting default would fire it on every AI unit's first activation.
        /// The ruling reverses that reasoning: the rules offered here are paid-for one-shots, and a default
        /// of NO meant every AI and every EOF/automated resolver declined them every time — an army paying
        /// points for an ability only a human could ever use, and a human never seeing the mechanic played
        /// against them. A simple AI use of an interesting rule beats a sophisticated non-use of it.</para>
        ///
        /// <para>#197 Dash: an ability whose effect is itself a cancellable placement SKIPS the Yes/No —
        /// the placement is the "you may", and asking twice would double-prompt the player. See
        /// <see cref="RepositionPlacement.IsCancellablePlacement"/> for the principle.</para>
        ///
        /// <para>#197 P19: this is also the first end-of-activation ability with a real TARGET, so a
        /// non-Self selector is resolved here rather than through <see cref="AbilityEffectChoice"/>, which
        /// is self-targeted by construction.</para>
        /// </summary>
        private async Task OfferEndOfActivationAbilities(DataBinding<UnitData> bearer)
        {
            UnitData unit = bearer.GetValue();
            foreach (IReadOnlyList<AbilityOffer> ruleOffers in AbilityEffectChoice.GroupByRule(
                         GameContext.RuleEvaluator.GatherOffers(new ActivationEndContext(unit))))
            {
                // #197 P19: a targeted ability resolves against a unit the player picks, and is skipped
                // entirely when nothing is eligible - offering "activate another unit" with no other unit
                // left to activate would spend the offer on nothing.
                if (ruleOffers.Count == 1 && ruleOffers[0].Ability.Effect is Effect.ActivateUnitNext)
                {
                    await OfferActivateUnitNext(bearer, ruleOffers[0]);
                    continue;
                }

                if (ruleOffers.Count == 1
                    && !RepositionPlacement.IsCancellablePlacement(ruleOffers[0].Ability.Effect))
                {
                    AbilityOffer offer = ruleOffers[0];
                    var question = new YesNoRequest(unit.PlayerID,
                        $"Use {offer.RuleName} on {unit.Name}?", defaultAnswer: true);
                    bool accepted = await GameContext.PlayerRequester
                        .RequestDecision<YesNoRequest, bool>(question);
                    if (!accepted) continue;
                }

                AbilityEffectChoice.Outcome outcome = await AbilityEffectChoice.Resolve(
                    GameContext, unit.PlayerID, unit, ruleOffers, "as this activation ends");

                GameContext.Log(ruleOffers.Count == 1
                    ? $"{unit.Name}: {outcome.Chosen.RuleName} applies."
                    : $"{unit.Name}: {outcome.Chosen.RuleName} - chose {outcome.Chosen.Ability.Label}.");

                // "You MAY place all models anywhere fully within Nin of their position" - the same fold
                // ActivationStartStage and DeployUnitStage do, at the third of the rule family's triggers.
                await RepositionPlacement.OfferFromOperations(GameContext, unit, outcome.Operations);
            }
        }

        /// <summary>
        /// #197 P19 — the out-of-order activation grant. Picks an eligible target, resolves the ability
        /// against it, and turns the resulting <see cref="RuleOperation.InvokeActivateUnitNext"/> into the
        /// flag <c>DeterminePlayerTurnStage</c> and <c>ChooseUnitToActivateStage</c> read.
        ///
        /// <para>Eligibility is the selector's (affinity, range, line of sight) narrowed by two facts the
        /// selector cannot express: not the bearer itself ("ANOTHER friendly unit") and not a unit that has
        /// already activated. The latter reads <see cref="TokenType.ActivatedThisRound"/> rather than a
        /// pool, which is what lets an ALLY's unit qualify - no per-player pool reachable from here covers
        /// another player's units.</para>
        /// </summary>
        private async Task OfferActivateUnitNext(DataBinding<UnitData> bearer, AbilityOffer offer)
        {
            UnitData unit = bearer.GetValue();

            List<DataBinding<UnitData>> eligible = AbilityTargeting
                .EligibleTargets(bearer, offer.Ability.TargetSelector, GameContext)
                .Where(candidate => candidate != bearer
                    && candidate.GetValue().GetIsAlive()
                    && !candidate.GetValue().Tokens.HasToken(TokenType.ActivatedThisRound)
                    && !candidate.GetValue().Tokens.HasToken(TokenType.ActivatesNext))
                .ToList();

            if (eligible.Count == 0)
            {
                GameContext.LogDebug($"{offer.RuleName}: no unactivated friendly unit in range - not offered.");
                return;
            }

            var options = eligible
                .Select(candidate => new CancellableSelectionRequest<UnitData>.ValidOption(
                    candidate, DescribeCandidate(unit, candidate)))
                .ToList();

            var request = new CancellableSelectionRequest<UnitData>(unit.PlayerID,
                $"{offer.RuleName}: pick a friendly unit to activate next (or cancel)",
                options, System.Array.Empty<CancellableSelectionRequest<UnitData>.InvalidOption>());

            CancellableResult<DataBinding<UnitData>> reply = await GameContext.PlayerRequester
                .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(
                    request);

            if (reply is not Selected<DataBinding<UnitData>> selected)
            {
                return;
            }

            // Resolved only AFTER the pick, so cancelling costs nothing.
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                .ResolveAbility(offer, new[] { (IUnit)selected.Value.GetValue() });
            OperationApplier.ApplyTokenOperations(ops);
            await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));

            RuleOperation.InvokeActivateUnitNext? grant =
                ops.OfType<RuleOperation.InvokeActivateUnitNext>().FirstOrDefault();
            if (grant == null)
            {
                return;
            }

            UnitData target = selected.Value.GetValue();
            target.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatesNext));

            // The turn order is about to skip a player, and the beneficiary may not be the player who
            // granted it - which is exactly the kind of surprise a banner exists for. Notice, not Headline:
            // worth reading, not worth stopping the game for.
            string whose = target.PlayerID == unit.PlayerID
                ? string.Empty
                : $" ({GameContext.GetPlayerName(target.PlayerID)} controls it)";
            await GameContext.Announce($"{offer.RuleName}: {target.Name} activates next{whose}",
                new TextColor(130, 220, 130, 255), EBannerTier.Notice);
        }

        /// <summary>An ally's unit is labelled with its owner, since the picker is choosing across armies.</summary>
        private string DescribeCandidate(UnitData bearer, DataBinding<UnitData> candidate)
        {
            UnitData value = candidate.GetValue();
            return value.PlayerID == bearer.PlayerID
                ? value.Name
                : $"{value.Name} ({GameContext.GetPlayerName(value.PlayerID)})";
        }
    }
}
