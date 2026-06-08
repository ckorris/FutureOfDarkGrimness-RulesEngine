using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #042 Counter: a charged unit with Counter strikes FIRST, before the charging unit's strikes.
    /// Fires the OnCounterTrigger "when" for the defender (Subject seat); if a StrikeFirst op is queued,
    /// swaps the attacker/defender roles for the rest of the melee. The existing flow then runs
    /// unchanged with roles reversed — the Counter unit takes the first swing, and the charger is offered
    /// the strike-back. Runs after charge-contact (Impact) so the charger still deals its impact hits.
    ///
    /// DEFERRED (Appendix C): the companion "-1 impact roll per Counter model" facet.
    /// </summary>
    public class DetermineStrikeOrderStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnStrikeOrderDetermined;

        public DetermineStrikeOrderStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            OnStrikeOrderDetermined = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new CounterTriggerContext(attacker, defender),
                (defender, ERuleSeat.Subject));

            if (operations.OfType<RuleOperation.StrikeFirst>().Any())
            {
                GameContext.Log($"{defender.Name}'s Counter: it strikes first against the charging {attacker.Name}.");
                context.SwapCombatRoles();
            }

            OnStrikeOrderDetermined.Activate(context);
        }
    }
}
