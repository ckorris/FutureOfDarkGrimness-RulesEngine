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
    /// Counter is weapon-scoped (#027: "strikes first with this weapon"), so the defender's melee
    /// weapons join the evaluation as carriers; the evaluator dedupes duplicate instances.
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
                SubjectWithMeleeWeapons(defender));

            // Only strike first if the Counter unit can actually fight — it must have a living model with a
            // melee weapon. Weapons are distributed across models (round-robin), so a unit can lose its only
            // melee-armed model yet keep others; swapping such a unit in as the attacker would enter
            // ChooseMeleeWeaponStage with an empty weapon pool and throw. (The normal strike-back path guards
            // the same way in OfferStrikeBackStage.)
            if (operations.OfType<RuleOperation.StrikeFirst>().Any()
                && context.DefendingUnit.GetValue().GetMeleeWeapons().Count > 0)
            {
                GameContext.Log($"{defender.Name}'s Counter: it strikes first against the charging {attacker.Name}.");
                context.SwapCombatRoles();
            }

            OnStrikeOrderDetermined.Activate(context);
        }

        /// <summary>
        /// The defender as a Subject participant once per living-model melee weapon (so
        /// weapon-scoped Counter fires), plus once weaponless (so unit-scoped defensive
        /// rules fire even with no melee weapons).
        /// </summary>
        internal static (IUnit, ERuleSeat, IWeapon?)[] SubjectWithMeleeWeapons(IUnit defender)
        {
            var participants = new List<(IUnit, ERuleSeat, IWeapon?)> { (defender, ERuleSeat.Subject, null) };
            foreach (Weapon meleeWeapon in defender.GetMeleeWeapons())
            {
                participants.Add((defender, ERuleSeat.Subject, meleeWeapon));
            }
            return participants.ToArray();
        }
    }
}
