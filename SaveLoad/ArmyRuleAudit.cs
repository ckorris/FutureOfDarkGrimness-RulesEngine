using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.SaveLoad
{
    /// <summary>
    /// The result of <see cref="ArmyRuleAudit.Audit"/>: every rule reference the list would drop at
    /// launch, plus the one failure mode that is not a drop — embedded definitions that fail
    /// validation, which reject the whole list instead.
    /// </summary>
    public sealed class ArmyRuleAuditResult
    {
        public ArmyRuleAuditResult(IReadOnlyList<RuleDrop> drops, string? embeddedDefinitionError)
        {
            Drops = drops;
            EmbeddedDefinitionError = embeddedDefinitionError;
        }

        public IReadOnlyList<RuleDrop> Drops { get; }

        /// <summary>
        /// Non-null when the army's embedded #059 rule definitions fail
        /// <see cref="RuleValidator"/> validation. At launch this throws
        /// <see cref="RuleValidationException"/> and rejects the list outright — a harder failure
        /// than any drop, so it is reported separately rather than folded into <see cref="Drops"/>.
        /// </summary>
        public string? EmbeddedDefinitionError { get; }
    }

    /// <summary>
    /// Answers "which of this list's rule references would do nothing in a game?" without building a
    /// store or launching anything (#168) — the army-builder screen shows the result next to its
    /// force-org warnings. Walks the same references the launch path attaches, in the same order
    /// (per unit: weapon entries as the UnitData ctor does, then unit-level names as
    /// GameBootstrap.AttachRulesFromArmyList does; then spell WithRules names as
    /// ArmyListSpellResolution does), classifying each through the shared
    /// <see cref="ArmyListRuleResolution.ResolveOrDescribeDrop"/> ladder so this audit cannot drift
    /// from what actually attaches. Parity is pinned by ArmyRuleAuditParityTests.
    /// <para>
    /// Scoped to one list: the resolver is the core catalog plus THIS army's embedded definitions.
    /// (At a real launch every player's embedded definitions share one resolver, so another list's
    /// custom definition could in principle implement a name this audit reports as unimplemented —
    /// an acceptable imprecision for an advisory pane.)
    /// </para>
    /// </summary>
    public static class ArmyRuleAudit
    {
        public static ArmyRuleAuditResult Audit(ArmyListFile armyListFile)
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();

            string? embeddedDefinitionError = null;
            try
            {
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, armyListFile);
            }
            catch (RuleValidationException exception)
            {
                // Core-catalog-only audit still runs: the drops it finds are real regardless.
                embeddedDefinitionError = exception.Message;
            }

            List<RuleDrop> drops = new List<RuleDrop>();

            foreach (UnitFileEntry unit in armyListFile.Units)
            {
                // Weapon-level rules: the UnitData ctor resolves each weapon entry's rule names once
                // (shared across the entry's quantity), scope-gated to Weapon.
                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    foreach (SpecialRuleEntry ruleEntry in weapon.SpecialRules)
                    {
                        Classify(resolver, ruleEntry, ERuleScope.Weapon,
                            $"weapon '{weapon.Name}' of unit '{unit.Name}'", drops);
                    }
                }

                // Unit-level rules resolve any-scope: a weapon-scoped rule here is legitimate wargear
                // (#197 slice 0) that re-homes onto every weapon the unit carries — it only drops when
                // there is no weapon to receive it.
                bool hasWeapons = unit.ModelCount > 0 && unit.Weapons.Any(weapon => weapon.Quantity > 0);
                foreach (SpecialRuleEntry ruleEntry in unit.SpecialRules)
                {
                    string owner = $"unit '{unit.Name}'";
                    ResolvedRule? resolved = Classify(resolver, ruleEntry, attachmentScope: null, owner, drops);

                    if (resolved != null && resolved.Definition.Scope == ERuleScope.Weapon && !hasWeapons)
                    {
                        drops.Add(new RuleDrop(ruleEntry.PrintableName, owner,
                            ERuleDropReason.NoWeaponsToAttach,
                            $"Skipping weapon rule '{ruleEntry.PrintableName}' on unit '{unit.Name}': " +
                            "it is granted at unit level but the unit carries no weapons to attach it to."));
                    }
                }
            }

            // Spell-carried weapon rules: a damage spell's WithRules names resolve at Weapon scope
            // (ArmyListSpellResolution.ResolveWeaponRules).
            foreach (SpellDefinition spell in armyListFile.Spells)
            {
                if (spell.Effect is not Effect.DealHits dealHits)
                {
                    continue;
                }

                foreach (string ruleName in dealHits.WithRules)
                {
                    SpecialRuleEntry entry = SpecialRuleEntryParser.Parse(ruleName);
                    Classify(resolver, entry, ERuleScope.Weapon, $"spell '{spell.Name}'", drops);
                }
            }

            return new ArmyRuleAuditResult(drops, embeddedDefinitionError);
        }

        private static ResolvedRule? Classify(IRuleResolver resolver, SpecialRuleEntry ruleEntry,
            ERuleScope? attachmentScope, string ownerDescription, List<RuleDrop> drops)
        {
            ResolvedRule? resolved = ArmyListRuleResolution.ResolveOrDescribeDrop(resolver, ruleEntry,
                attachmentScope, ownerDescription, out RuleDrop? drop);
            if (drop != null)
            {
                drops.Add(drop.Value);
            }

            return resolved;
        }
    }
}
