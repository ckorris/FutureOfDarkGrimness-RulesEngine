using System.Collections.Generic;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Builds the per-weapon sighting profiles (ignores-cover / ignores-terrain) for a unit's ranged
    /// weapons, for attaching to a <see cref="DefineMovementPathRequest"/>. Deduped by weapon name (the
    /// distinct weapon types are what matter to the resolver). Derives the flags from the unit's #042
    /// rules via the same <see cref="SightRuleQueries"/> the cover stage and targeting options use.
    /// </summary>
    internal static class WeaponSightProfileBuilder
    {
        public static List<WeaponSightProfile> For(IUnit unit, RuleEvaluator evaluator)
        {
            var profiles = new List<WeaponSightProfile>();
            var seenNames = new HashSet<string>();
            foreach (Weapon weapon in unit.GetRangedWeapons())
            {
                if (!seenNames.Add(weapon.Name)) continue;
                // The *Source variants return the responsible rule's display name (or null), so the movement
                // overlay can attribute the effect; non-logging, safe to call while building the request.
                // #425: the movement overlay previews the shot AFTER the move, so the queries are asked with
                // attackerMoved: true - Indirect's Hold-only line-of-sight bypass is off there (Quick Throw's is
                // not), and so is its Sight-Blocker-cover bypass.
                string? coverRule = SightRuleQueries.CoverIgnoreSource(unit, weapon, evaluator, attackerMoved: true);
                string? losRule = SightRuleQueries.LineOfSightIgnoreSource(unit, weapon, evaluator, attackerMoved: true);
                bool ignoresBlockerCover = SightRuleQueries.SightBlockerCoverFor(unit, weapon, evaluator,
                    attackerMoved: true) == ESightBlockerCover.Ignored;
                profiles.Add(new WeaponSightProfile(weapon,
                    coverRule != null, losRule != null, coverRule, losRule, ignoresBlockerCover));
            }
            return profiles;
        }
    }
}
