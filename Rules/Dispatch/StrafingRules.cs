using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #197 Strafing — "This weapon may only be used in this way." The clause that keeps a strafe weapon out
    /// of the ordinary attack pools, plus the bearer-capability check StrafingStage warns on. The
    /// <see cref="LimitedRules"/>/<see cref="AircraftRules"/> shape: gates that run in stage code and in the
    /// weapon-pool helpers, where no <see cref="RuleEvaluator"/> is in scope.
    ///
    /// <para>Detected STRUCTURALLY — by the effect, not by the rule's name — so an army's own embedded
    /// rule, or a renamed alias, is restricted on the same terms as core Strafing. Name-matching would let a
    /// re-labelled copy quietly become a free extra melee weapon.</para>
    ///
    /// <para>The restriction is load-bearing rather than cosmetic. Every corpus strafe weapon has range 0,
    /// and <c>IWeapon.IsMelee()</c> is defined as "range 0", so without this filter a Bomber Plane dragged
    /// into melee would swing its Flame Bombs as a close-combat weapon - Blast(3) and all.</para>
    /// </summary>
    public static class StrafingRules
    {
        /// <summary>
        /// True if <paramref name="weapon"/> carries a rule whose ability attacks with the carrying weapon
        /// (<see cref="Effect.AttackWithThisWeapon"/>) - i.e. a strafe weapon, usable only that way.
        /// </summary>
        public static bool IsStrafeOnly(IWeapon weapon) =>
            weapon.RuleDefinitions.Any(rule =>
                rule.Definition.Activated.Any(ability => ability.Effect is Effect.AttackWithThisWeapon));

        /// <summary> <paramref name="weapons"/> minus the strafe-only ones. </summary>
        public static IEnumerable<TWeapon> ExcludeStrafeOnly<TWeapon>(IEnumerable<TWeapon> weapons)
            where TWeapon : IWeapon => weapons.Where(weapon => !IsStrafeOnly(weapon));

        /// <summary>
        /// True if any living model in <paramref name="unit"/> carries a strafe-only weapon. Used to decide
        /// whether the bearer-capability warning is worth running at all.
        /// </summary>
        public static bool CarriesStrafeWeapon(IUnit unit) =>
            unit.Models.Any(model => model.GetIsAlive() && model.Weapons.Any(IsStrafeOnly));
    }
}
