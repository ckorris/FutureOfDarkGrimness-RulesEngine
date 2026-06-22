using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
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
    /// Slice 2a: only SELF-targeted abilities resolve (target = bearer). Cross-unit targeting (Friend/Foe
    /// via the ability's <c>TargetSelector</c> + a <see cref="SelectionRequest{T}"/>) is slice 2b.
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
                List<AbilityOffer> selfOffers = GameContext.RuleEvaluator
                    .GatherOffers(new PreAttackContext(unit, _actionType))
                    .Where(o => o.Ability.TargetSelector.TargetAffinity == ETargetAffinity.Self
                                && !usedThisEntry.Contains(o.RuleName))
                    .ToList();

                if (selfOffers.Count == 0)
                {
                    break;
                }

                List<string> options = selfOffers.Select(o => o.RuleName).ToList();
                options.Add(DONE_CHOICE);

                StringSelectionRequest request = new StringSelectionRequest(context.ActivatingPlayer(),
                    "Use a pre-attack ability?", options, new List<StringSelectionRequest.InvalidOption>());
                string choice = await GameContext.PlayerRequester
                    .RequestDecision<StringSelectionRequest, string>(request);

                if (choice == DONE_CHOICE)
                {
                    break;
                }

                AbilityOffer chosen = selfOffers.First(o => o.RuleName == choice);
                usedThisEntry.Add(chosen.RuleName);

                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.ResolveAbility(chosen, new[] { unit });
                OperationApplier.ApplyTokenOperations(ops);
                GameContext.Log($"{unit.Name} used {chosen.RuleName} before attacking.");
            }

            await OnFinished.Activate(context);
        }
    }
}
