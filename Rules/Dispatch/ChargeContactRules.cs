using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using System.Linq;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #345 — "can this unit do anything in a melee?", the question the charge gate and the AI's melee
    /// valuation both need. A melee weapon is the obvious answer; the other one is a rule that fires on
    /// the CHARGE itself rather than off a weapon (Impact(X), Heavy Impact(X), Ravage(X)).
    ///
    /// <para>Asked of the rule DEFINITIONS, not of a name list: a rule declares that it acts on charge
    /// contact by carrying a passive <see cref="HookEntry"/> at <see cref="EHookID.Melee_OnChargeContact"/>
    /// at the <see cref="ERuleSeat.Actor"/> seat, and that declaration is exactly what
    /// <c>ResolveImpactHitsStage</c> dispatches. So any rule authored at that hook in future enables the
    /// charge with no engine change, and core Counter is excluded automatically — its charge-contact entry
    /// sits at the <see cref="ERuleSeat.Subject"/> seat, because reducing a charger's impact dice is
    /// something you do when charged, not a reason you may charge.</para>
    ///
    /// <para>A plain read over the carriers' attached rules, like <see cref="AircraftRules"/> — these gates
    /// run in stage and AI code with no <see cref="RuleEvaluator"/> in scope. It deliberately does NOT
    /// evaluate conditions: the gate runs before a defender is picked, and a condition that reads the
    /// target cannot be answered yet. A charge whose rule then declines to fire is a charge that deals no
    /// impact hits, which the melee flow already handles.</para>
    /// </summary>
    public static class ChargeContactRules
    {
        /// <summary>
        /// Whether any rule on the unit, its models, or their weapons acts when this unit charges into
        /// contact. All three carriers are checked because the dispatcher unions them: Impact is
        /// unit-scoped today, but nothing stops a charge-contact rule riding a model or a weapon.
        /// </summary>
        public static bool ActsOnChargeContact(IUnit unit)
        {
            if (unit == null) return false;

            if (unit.RuleDefinitions.Any(ActsOnContact)) return true;

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                if (model.RuleDefinitions.Any(ActsOnContact)) return true;

                foreach (Weapon weapon in model.Weapons)
                {
                    if (weapon.RuleDefinitions.Any(ActsOnContact)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The engine-wide "this unit can fight in melee" predicate: something to swing, or something that
        /// fires on charge contact. The charge gate and every AI melee-capability check share it, so a unit
        /// that may charge is never one the AI writes off as harmless in melee.
        /// </summary>
        public static bool CanFightInMelee(IUnit unit) =>
            unit != null && (unit.GetMeleeWeapons().Count > 0 || ActsOnChargeContact(unit));

        private static bool ActsOnContact(ResolvedRule rule) =>
            rule.Definition.Passive.Any(entry =>
                entry.HookID == EHookID.Melee_OnChargeContact && entry.Seat == ERuleSeat.Actor);
    }
}
