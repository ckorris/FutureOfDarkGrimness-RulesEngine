using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using System;
using System.Collections.Generic;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Army-load resolution for an army's embedded spell list (#033), the spell-side mirror of
    /// <see cref="ArmyListRuleResolution"/>. Each <see cref="SpellDefinition"/> becomes a
    /// <see cref="RuntimeSpell"/>; for a damage spell (<see cref="Effect.DealHits"/>) the effect's
    /// <c>WithRules</c> names are resolved here — where the rule resolver is live — into the weapon-scoped
    /// <see cref="ResolvedRule"/>s the cast stage attaches to the synthetic spell weapon. An unresolvable
    /// or mis-scoped weapon rule is skipped with a warning (the same tolerance
    /// <see cref="ArmyListRuleResolution.ResolveForScope"/> applies to unit/weapon rules), so a partial
    /// spell still casts.
    /// </summary>
    public static class ArmyListSpellResolution
    {
        public static IReadOnlyList<RuntimeSpell> ResolveSpells(ArmyListFile armyListFile, IRuleResolver ruleResolver)
        {
            List<RuntimeSpell> spells = new List<RuntimeSpell>(armyListFile.Spells.Count);
            foreach (SpellDefinition definition in armyListFile.Spells)
            {
                spells.Add(new RuntimeSpell(definition, ResolveWeaponRules(definition, ruleResolver)));
            }
            return spells;
        }

        private static IReadOnlyList<ResolvedRule> ResolveWeaponRules(SpellDefinition spell, IRuleResolver ruleResolver)
        {
            if (spell.Effect is not Effect.DealHits dealHits || dealHits.WithRules.Count == 0)
            {
                return Array.Empty<ResolvedRule>();
            }

            List<ResolvedRule> rules = new List<ResolvedRule>(dealHits.WithRules.Count);
            foreach (string ruleName in dealHits.WithRules)
            {
                SpecialRuleEntry entry = SpecialRuleEntryParser.Parse(ruleName);
                ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(
                    ruleResolver, entry, ERuleScope.Weapon, $"spell '{spell.Name}'");
                if (resolved != null)
                {
                    rules.Add(resolved);
                }
            }
            return rules;
        }
    }
}
