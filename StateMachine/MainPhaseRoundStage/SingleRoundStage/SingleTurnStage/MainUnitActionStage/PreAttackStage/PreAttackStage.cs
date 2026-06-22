using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #100 #2 — fires <see cref="EHookID.Activation_OnPreAttack"/> once the unit has committed to an
    /// attack action (Shoot or Charge), before targets/weapons resolve, and offers the pre-attack
    /// activated abilities a rule contributes there (buffs, Mend, Re-Position, marks). The acting player
    /// picks which to use (each gated by its own once-per-X <see cref="Cost"/>); the chosen ability is
    /// resolved and its token operations applied, then the stage hands off to the real attack via
    /// <see cref="OnFinished"/>. Layered like <see cref="CustomActionStage"/> — it never sets
    /// HasMoved/HasAttacked, so it doesn't change what the unit may still do.
    ///
    /// One instance sits on each attack edge of <see cref="ChooseActionStage"/> (Charge → melee,
    /// Shoot → shoot); the <see cref="EActionType"/> it carries is what the PreAttackContext reports.
    /// Charge is exact; the shoot edge passes <see cref="EActionType.Hold"/> as a best effort (there is no
    /// "Shoot" action type — shooting is a sub-step), which is fine because no corpus pre-attack ability
    /// gates on it. If one ever needs to distinguish shoot from melee here, give PreAttackContext a
    /// combat-kind rather than overloading the action type.
    ///
    /// Self abilities resolve against the bearer; Friend/Foe/Any abilities resolve their
    /// <c>TargetSelector</c> through <see cref="PreAttackTargeting"/> and a
    /// <see cref="CancellableSelectionRequest{T}"/> so the player picks the unit(s) (slice 2b).
    /// </summary>
    public class PreAttackStage : StageBase<IUnitActionContext>
    {
        /// <summary> Sentinel option the player picks to stop using pre-attack abilities and attack. </summary>
        public const string DONE_CHOICE = "Done";

        public StageBinding OnFinished;
        private readonly EActionType _actionType;

        public PreAttackStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent,
            EActionType actionType) : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
            _actionType = actionType;
        }

        // Distinct per instance (the melee-edge and shoot-edge copies differ by action type), so the two
        // siblings don't collide on the parent's transition key. See StageBase.Name.
        public override string Name => $"{nameof(PreAttackStage)}_{_actionType}";

        public override async Task Enter(IUnitActionContext context)
        {
            IUnit unit = context.ActivatingUnit.GetValue();

            // Used-this-entry guard: a pre-attack ability is offered at most once per attack regardless of
            // its cost, so a cost-free (mis-authored) ability can't loop the menu. Normal once-per-X
            // abilities are also dropped by their own cost gate after use; this is the belt to that braces.
            HashSet<string> usedThisEntry = new HashSet<string>();

            while (true)
            {
                // Offer only abilities that (a) haven't been used this attack and (b) actually have enough
                // valid targets to fire — so a "pick an enemy" ability with no enemy in range isn't shown.
                List<AbilityOffer> usable = GameContext.RuleEvaluator
                    .GatherOffers(new PreAttackContext(unit, _actionType))
                    .Where(o => !usedThisEntry.Contains(o.RuleName)
                                && PreAttackTargeting.EligibleTargets(context.ActivatingUnit,
                                       o.Ability.TargetSelector, GameContext).Count
                                   >= o.Ability.TargetSelector.MinCount)
                    .ToList();

                if (usable.Count == 0)
                {
                    break;
                }

                List<string> options = usable.Select(o => o.RuleName).ToList();
                options.Add(DONE_CHOICE);

                StringSelectionRequest request = new StringSelectionRequest(context.ActivatingPlayer(),
                    "Use a pre-attack ability?", options, new List<StringSelectionRequest.InvalidOption>());
                string choice = await GameContext.PlayerRequester
                    .RequestDecision<StringSelectionRequest, string>(request);

                if (choice == DONE_CHOICE)
                {
                    break;
                }

                AbilityOffer chosen = usable.First(o => o.RuleName == choice);
                // Picked → not re-offered this attack even if the player then backs out of target selection.
                usedThisEntry.Add(chosen.RuleName);

                IReadOnlyList<IUnit>? targets = await SelectTargets(context, chosen.Ability.TargetSelector);
                if (targets == null)
                {
                    // Backed out of (or couldn't complete) target selection — nothing applied, no cost paid.
                    continue;
                }

                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.ResolveAbility(chosen, targets);
                OperationApplier.ApplyTokenOperations(ops);
                GameContext.Log($"{unit.Name} used {chosen.RuleName} before attacking.");
            }

            await OnFinished.Activate(context);
        }

        /// <summary>
        /// Resolves the ability's <see cref="TargetSelector"/> into the chosen target unit(s): the bearer
        /// for Self; otherwise the player picks between MinCount and MaxCount eligible units one at a time
        /// (each removed from the pool as it's taken). Returns null if the player backed out before meeting
        /// the minimum — the caller treats that as "ability not used."
        /// </summary>
        private async Task<IReadOnlyList<IUnit>?> SelectTargets(IUnitActionContext context, TargetSelector selector)
        {
            if (selector.TargetAffinity == ETargetAffinity.Self)
            {
                return new[] { context.ActivatingUnit.GetValue() };
            }

            List<DataBinding<UnitData>> remaining = PreAttackTargeting.EligibleTargets(
                context.ActivatingUnit, selector, GameContext);
            List<IUnit> chosen = new List<IUnit>();

            while (chosen.Count < selector.MaxCount && remaining.Count > 0)
            {
                List<CancellableSelectionRequest<UnitData>.ValidOption> valid = remaining
                    .Select(b => new CancellableSelectionRequest<UnitData>.ValidOption(b, b.GetValue().Name))
                    .ToList();

                CancellableSelectionRequest<UnitData> request = new CancellableSelectionRequest<UnitData>(
                    context.ActivatingPlayer(),
                    $"Choose target ({chosen.Count + 1} of up to {selector.MaxCount})",
                    valid, new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

                CancellableResult<DataBinding<UnitData>> result = await GameContext.PlayerRequester
                    .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(request);

                if (result is Cancelled<DataBinding<UnitData>>)
                {
                    // Cancelling past the minimum just stops adding extras; before it, it aborts the ability.
                    return chosen.Count >= selector.MinCount ? chosen : null;
                }

                DataBinding<UnitData> picked = ((Selected<DataBinding<UnitData>>)result).Value;
                chosen.Add(picked.GetValue());
                remaining.Remove(picked);
            }

            return chosen.Count >= selector.MinCount ? chosen : null;
        }
    }
}
