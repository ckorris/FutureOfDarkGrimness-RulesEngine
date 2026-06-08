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
                profiles.Add(new WeaponSightProfile(weapon,
                    SightRuleQueries.IgnoresCover(unit, weapon, evaluator),
                    SightRuleQueries.IgnoresTerrain(unit, weapon, evaluator)));
            }
            return profiles;
        }
    }
}
