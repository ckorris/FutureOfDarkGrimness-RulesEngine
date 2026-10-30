using System;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// The roles a weapon's own rules GRANT it on top of what its printed range says (#424). A flag set because
    /// the two arms are independent and both can be granted at once (Assault grants both).
    /// </summary>
    [Flags]
    public enum EWeaponRoles
    {
        None = 0,

        /// <summary>May be swung in melee whatever its printed range (<see cref="RuleOperation.EnableMeleeUse"/>).</summary>
        Melee = 1,

        /// <summary>May be fired as a ranged weapon whatever its printed range (<see cref="RuleOperation.EnableRangedUse"/>).</summary>
        Ranged = 2,
    }

    /// <summary>
    /// Derives a WEAPON's roles from its #042 rules (#424, Core Rules v3.6.0 Assault: "Counts as both a ranged
    /// weapon and a melee weapon."). The weapon-scoped member of the capability family - <see cref="CapabilityRuleQueries"/>
    /// asks what a UNIT can do, this asks what a weapon may be used as - and, like it, the answer is asked for rather than
    /// tested for: a rule confers a role by authoring <see cref="Effect.EnableMeleeUse"/> / <see cref="Effect.EnableRangedUse"/>
    /// at <see cref="EHookID.Lifecycle_OnCapabilityQuery"/> (Actor seat), and nothing anywhere compares against the
    /// name "Assault". The classification seam (<c>IWeaponExtensions.IsMelee</c> / <c>IsRanged</c>) widens by these
    /// grants: melee is "range 0 OR grants melee use", ranged is "range &gt; 0 OR grants ranged use".
    ///
    /// <para>Roles are GRANTS only. No operation negates a role, so two role rules on one weapon can only widen the
    /// set, and the question "is it melee?" has exactly one answer however many rules say so. A future "may not be
    /// used in melee" is an ACTION restriction (<see cref="ActionRestrictionQueries"/>), not a classification.</para>
    ///
    /// <para>Unlike the unit capabilities, a role is resolved from the weapon's OWN resolved rules alone, with no
    /// unit, table or <see cref="RuleEvaluator"/> in scope - the <see cref="StrafingRules"/> / <see cref="ChargeContactRules"/>
    /// shape, a structural read of the rule definitions - and is then cached on the <c>Weapon</c> for its lifetime
    /// (the <c>Weapon.EffectKey</c>-at-load precedent, #239), because the seam runs in hot paths (the AI's search
    /// walks it thousands of times per iteration, the overlays per frame) and from places that have no table at
    /// all (the Forge card, the list compiler, the importer). No 3.6.0 rule grants a role mid-game, so the build-time
    /// answer is exact; it follows that the entry's <see cref="Condition"/> is not consulted here (there is no live
    /// state for it to read) and token grants and suppression do not reach it. A role entry authored with any
    /// condition but <see cref="Condition.Always"/> is granted anyway, with a once-only warning so the author learns
    /// that the gate does nothing.</para>
    /// </summary>
    public static class WeaponRoleQueries
    {
        /// <summary>
        /// The roles <paramref name="weapon"/>'s own rules grant it. <see cref="EWeaponRoles.None"/> for the
        /// near-universal weapon whose roles come from its printed range alone. Callers in hot paths read the
        /// cached <c>Weapon.GrantedRoles</c> instead of calling this per query.
        /// </summary>
        public static EWeaponRoles Resolve(IWeapon weapon)
        {
            EWeaponRoles roles = EWeaponRoles.None;
            foreach (ResolvedRule rule in weapon.RuleDefinitions)
            {
                if (!rule.Definition.ListensAt(EHookID.Lifecycle_OnCapabilityQuery, ERuleSeat.Actor)) continue;

                foreach (HookEntry entry in rule.Definition.Passive)
                {
                    if (entry.HookID != EHookID.Lifecycle_OnCapabilityQuery || entry.Seat != ERuleSeat.Actor) continue;

                    EWeaponRoles granted = entry.Effect switch
                    {
                        Effect.EnableMeleeUse => EWeaponRoles.Melee,
                        Effect.EnableRangedUse => EWeaponRoles.Ranged,
                        _ => EWeaponRoles.None,
                    };
                    if (granted == EWeaponRoles.None) continue;

                    if (entry.Condition is not Condition.Always)
                    {
                        RuleDiagnostics.WarnOnce($"weapon-role-condition:{rule.Definition.Name}",
                            $"Rule '{rule.Definition.Name}' grants a weapon role under a condition, but weapon roles " +
                            "are resolved once when the weapon is built, with no table to read - the condition is " +
                            "ignored and the role is granted unconditionally.");
                    }

                    roles |= granted;
                }
            }

            return roles;
        }

        /// <summary>Whether <paramref name="weapon"/>'s rules let it be swung in melee whatever its range.</summary>
        public static bool GrantsMeleeUse(IWeapon weapon) => (Resolve(weapon) & EWeaponRoles.Melee) != 0;

        /// <summary>Whether <paramref name="weapon"/>'s rules let it be fired as a ranged weapon whatever its range.</summary>
        public static bool GrantsRangedUse(IWeapon weapon) => (Resolve(weapon) & EWeaponRoles.Ranged) != 0;

        /// <summary>
        /// The alias-aware display name of the rule that lets <paramref name="weapon"/> be swung in melee despite
        /// a printed range, or null when its melee role (if any) comes from being range 0. The <c>*Source</c>
        /// shape of <see cref="SightRuleQueries"/>: a front end attributing "also a melee weapon" names the rule
        /// the army wrote, so a renamed alias shows its own name.
        /// </summary>
        public static string? MeleeUseSource(IWeapon weapon) => Source(weapon, EWeaponRoles.Melee);

        /// <summary>The ranged twin of <see cref="MeleeUseSource"/>.</summary>
        public static string? RangedUseSource(IWeapon weapon) => Source(weapon, EWeaponRoles.Ranged);

        private static string? Source(IWeapon weapon, EWeaponRoles role)
        {
            foreach (ResolvedRule rule in weapon.RuleDefinitions)
            {
                if (!rule.Definition.ListensAt(EHookID.Lifecycle_OnCapabilityQuery, ERuleSeat.Actor)) continue;
                foreach (HookEntry entry in rule.Definition.Passive)
                {
                    if (entry.HookID != EHookID.Lifecycle_OnCapabilityQuery || entry.Seat != ERuleSeat.Actor) continue;
                    bool matches = role == EWeaponRoles.Melee
                        ? entry.Effect is Effect.EnableMeleeUse
                        : entry.Effect is Effect.EnableRangedUse;
                    if (matches) return rule.RequestedName;
                }
            }
            return null;
        }
    }
}
