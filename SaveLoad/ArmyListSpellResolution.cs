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

            return ResolveWeaponRuleNames(dealHits.WithRules, ruleResolver, $"spell '{spell.Name}'");
        }

        /// <summary>
        /// Resolves a <see cref="Effect.DealHits.WithRules"/> name list into the weapon-scoped
        /// <see cref="ResolvedRule"/>s a synthetic weapon carries. Shared with the ability / Strafing
        /// DealHits paths (#164), which resolve at dispatch time rather than army load — an ability can be
        /// conferred at runtime by an aura or grant, so it has no army-load site of its own.
        /// <para>Names carry their arguments ("Blast(3)"), parsed by
        /// <see cref="SpecialRuleEntryParser"/>; an unresolvable or mis-scoped rule is skipped with a
        /// warning (the same tolerance army-load applies), so a partial effect still resolves.
        /// <paramref name="context"/> labels those warnings.</para>
        /// </summary>
        public static IReadOnlyList<ResolvedRule> ResolveWeaponRuleNames(
            IReadOnlyList<string> ruleNames, IRuleResolver ruleResolver, string context)
        {
            List<ResolvedRule> rules = new List<ResolvedRule>(ruleNames.Count);
            foreach (string ruleName in ruleNames)
            {
                SpecialRuleEntry entry = SpecialRuleEntryParser.Parse(ruleName);
                ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(
                    ruleResolver, entry, ERuleScope.Weapon, context);
                if (resolved != null)
                {
                    rules.Add(resolved);
                }
            }
            return rules;
        }
    }
}
