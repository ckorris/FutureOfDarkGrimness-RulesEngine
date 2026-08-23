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
            => ResolveSpells(armyListFile.Spells, ruleResolver);

        /// <summary>
        /// Resolves a bare spell list, for the #095 resume path: there the army file is vestigial and the
        /// definitions come from the blob persisted on <see cref="ArmyData"/> instead.
        /// </summary>
        public static IReadOnlyList<RuntimeSpell> ResolveSpells(IReadOnlyList<SpellDefinition> definitions,
            IRuleResolver ruleResolver)
        {
            List<RuntimeSpell> spells = new List<RuntimeSpell>(definitions.Count);
            foreach (SpellDefinition definition in definitions)
            {
                spells.Add(new RuntimeSpell(definition, ResolveWeaponRules(definition, ruleResolver)));
                WarnUnresolvableGrants(definition, ruleResolver);
            }
            return spells;
        }

        /// <summary>
        /// Pre-flights the names a spell grants as rules (#377): dispatch resolves a granted name lazily
        /// — RuleEvaluator.CollectGrantedRules skips an unresolvable or argument-reading definition with
        /// only a WarnOnce at cast time — so without this check a buff spell that can never do anything
        /// looks healthy at load, in the army builder, and in the cast menu. Purely diagnostic: the
        /// spell still loads and casts (matching the dispatch-time tolerance), but the drop is surfaced
        /// through <see cref="RuleDiagnostics"/> where the #168 audit and the lobby can report it.
        /// A bare-name entry with no arguments mirrors the grant path exactly: grants carry no argument
        /// slot, so an argument-reading definition classifies as <see cref="ERuleDropReason.MissingArgument"/>.
        /// </summary>
        private static void WarnUnresolvableGrants(SpellDefinition spell, IRuleResolver ruleResolver)
        {
            foreach (string ruleName in SpellRuleReferences.GrantedRuleNames(spell.Effect))
            {
                ArmyListRuleResolution.ResolveOrDescribeDrop(ruleResolver,
                    new SpecialRuleEntry_Core(ruleName), attachmentScope: null,
                    $"spell '{spell.Name}'", out RuleDrop? drop);
                if (drop != null)
                {
                    RuleDiagnostics.WarnDropped(drop.Value);
                }
            }
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
