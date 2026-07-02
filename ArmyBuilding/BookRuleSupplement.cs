using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 — curated rule supplement: hand-authored SpecialRuleDefinitions for faction rules the OPR
    // importer emits as inert name refs. The supplement file is the durable source of truth (books are
    // regenerated artifacts of --import-opr, so definitions authored INTO a book would be wiped on
    // re-import); Apply() embeds the subset a book actually references into book.RuleDefinitions, from
    // where the existing #059 pipeline carries them: ListCompiler copies them onto every compiled army,
    // and army load registers them (RegisterOrReplace) so the rules fire in play — including for a peer
    // whose engine predates the supplement, since the definitions travel inside the file.

    /// <summary>
    /// Loads, validates, and merges a rule-supplement file (a JSON array of
    /// <see cref="SpecialRuleDefinition"/> in the <see cref="RuleJson"/> kind-schema) into a
    /// <see cref="BookFile"/>. Only definitions the book references (unit/weapon/item/upgrade rule names,
    /// spell-granted rule names, plus names granted by already-included definitions, transitively) are
    /// embedded. Validation is hard-fail: an unresolvable granted name or a hook/capability violation
    /// throws rather than shipping a definition that would fail the #059 load gate.
    /// </summary>
    public static class BookRuleSupplement
    {
        /// <summary>Deserializes a supplement file's content. Throws <see cref="JsonException"/> on
        /// malformed JSON or an unknown "kind" tag.</summary>
        public static List<SpecialRuleDefinition> LoadDefinitions(string supplementJson)
        {
            List<SpecialRuleDefinition>? definitions =
                JsonSerializer.Deserialize<List<SpecialRuleDefinition>>(supplementJson, RuleJson.Options);
            return definitions ?? new List<SpecialRuleDefinition>();
        }

        /// <summary>
        /// Standalone authoring check: validates EVERY definition in the supplement (not just a book's
        /// referenced subset) — hook/capability violations via <see cref="RuleValidator"/>, granted rule
        /// names that resolve neither in the core catalog nor within the supplement, and duplicate names.
        /// Returns human-readable problems; empty means clean.
        /// </summary>
        public static IReadOnlyList<string> ValidateAll(IReadOnlyList<SpecialRuleDefinition> supplement)
        {
            return CollectProblems(supplement, supplement);
        }

        /// <summary>
        /// Embeds into <paramref name="book"/> every supplement definition the book references, plus
        /// definitions those definitions grant (transitively). Existing embedded definitions with the same
        /// name are replaced, so re-applying is idempotent. Throws <see cref="InvalidOperationException"/>
        /// listing every validation problem if the included subset is invalid. Returns the names embedded.
        /// </summary>
        public static IReadOnlyList<string> Apply(BookFile book, IReadOnlyList<SpecialRuleDefinition> supplement,
            Action<string>? warn = null)
        {
            HashSet<string> referenced = ReferencedRuleNames(book);
            var included = new List<SpecialRuleDefinition>();
            var includedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Seed with directly-referenced definitions, then chase granted names into the supplement
            // (e.g. a book references "Highborn Boost Aura", whose Aura effect grants "Highborn Boost",
            // which must itself be embedded for the granted name to resolve at load).
            var pending = new Queue<SpecialRuleDefinition>(
                supplement.Where(d => referenced.Contains(d.Name)));
            while (pending.Count > 0)
            {
                SpecialRuleDefinition definition = pending.Dequeue();
                if (!includedNames.Add(definition.Name))
                {
                    continue;
                }

                included.Add(definition);
                foreach (string granted in GrantedRuleNames(definition))
                {
                    SpecialRuleDefinition? grantedDefinition = supplement.FirstOrDefault(d =>
                        string.Equals(d.Name, granted, StringComparison.OrdinalIgnoreCase));
                    if (grantedDefinition != null && !includedNames.Contains(grantedDefinition.Name))
                    {
                        pending.Enqueue(grantedDefinition);
                    }
                }
            }

            IReadOnlyList<string> problems = CollectProblems(included, supplement);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rule supplement failed validation:\n  " + string.Join("\n  ", problems));
            }

            RuleResolver coreResolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in included)
            {
                if (coreResolver.TryResolve(definition.Name, out _))
                {
                    warn?.Invoke($"Supplement rule '{definition.Name}' overrides a core catalog rule.");
                }

                book.RuleDefinitions.RemoveAll(existing =>
                    string.Equals(existing.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
                book.RuleDefinitions.Add(definition);
            }

            return included.Select(d => d.Name).ToList();
        }

        /// <summary>Every rule name the book's content refers to: unit rules, weapon rules, item rules,
        /// upgrade-option gains (rules, weapons' rules, items' rules), and rule names the book's spells
        /// grant. Alias entries contribute both the alias and the aliased name.</summary>
        public static HashSet<string> ReferencedRuleNames(BookFile book)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RosterUnit unit in book.Units)
            {
                AddRuleEntries(names, unit.Rules);
                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    AddRuleEntries(names, weapon.SpecialRules);
                }

                foreach (ItemEntry item in unit.Items)
                {
                    AddRuleEntries(names, item.Rules);
                }

                foreach (UpgradeOption option in unit.Sections.SelectMany(s => s.Options))
                {
                    AddRuleEntries(names, option.RulesGained);
                    foreach (WeaponFileEntry weapon in option.WeaponsGained)
                    {
                        AddRuleEntries(names, weapon.SpecialRules);
                    }

                    foreach (ItemEntry item in option.ItemsGained)
                    {
                        AddRuleEntries(names, item.Rules);
                    }
                }
            }

            foreach (SpellDefinition spell in book.Spells)
            {
                foreach (string granted in GrantedNamesIn(spell.Effect))
                {
                    names.Add(granted);
                }
            }

            return names;
        }

        private static void AddRuleEntries(HashSet<string> names, IEnumerable<SpecialRuleEntry> entries)
        {
            foreach (SpecialRuleEntry entry in entries)
            {
                switch (entry)
                {
                    case SpecialRuleEntry_Core core:
                        names.Add(core.Name);
                        break;
                    case SpecialRuleEntry_CoreNumeric numeric:
                        names.Add(numeric.Name);
                        break;
                    case SpecialRuleEntry_Alias alias:
                        names.Add(alias.Name);
                        AddRuleEntries(names, new[] { alias.AliasedRule });
                        break;
                }
            }
        }

        /// <summary>Rule names a definition grants through its passive and activated effects — the names
        /// that must resolve at load for the definition to function.</summary>
        private static IEnumerable<string> GrantedRuleNames(SpecialRuleDefinition definition)
        {
            return definition.Passive.SelectMany(entry => GrantedNamesIn(entry.Effect))
                .Concat(definition.Activated.SelectMany(ability => GrantedNamesIn(ability.Effect)));
        }

        private static IEnumerable<string> GrantedNamesIn(Effect effect)
        {
            switch (effect)
            {
                case Effect.AddRule addRule:
                    yield return addRule.RuleName;
                    break;
                case Effect.Aura aura:
                    yield return aura.RuleName;
                    break;
                case Effect.MarkTarget mark:
                    yield return mark.RuleName;
                    break;
                case Effect.DealHits dealHits:
                    // WithRules entries are weapon-rule refs, possibly argumented ("Blast(3)") — the bare
                    // name is what must resolve.
                    foreach (string withRule in dealHits.WithRules)
                    {
                        yield return StripArgument(withRule);
                    }

                    break;
                case Effect.MoraleTestThen moraleTest:
                    foreach (string nested in GrantedNamesIn(moraleTest.OnFailure))
                    {
                        yield return nested;
                    }

                    break;
            }
        }

        private static string StripArgument(string ruleRef)
        {
            int parenthesis = ruleRef.IndexOf('(');
            return parenthesis < 0 ? ruleRef.Trim() : ruleRef.Substring(0, parenthesis).Trim();
        }

        /// <summary>Problems in <paramref name="toValidate"/>: capability violations, granted names that
        /// resolve neither in the core catalog nor in <paramref name="resolutionUniverse"/>, and duplicate
        /// definition names.</summary>
        private static IReadOnlyList<string> CollectProblems(IReadOnlyList<SpecialRuleDefinition> toValidate,
            IReadOnlyList<SpecialRuleDefinition> resolutionUniverse)
        {
            var problems = new List<string>();

            var duplicates = toValidate.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);
            foreach (var duplicate in duplicates)
            {
                problems.Add($"Duplicate definition name '{duplicate.Key}'.");
            }

            var validator = new RuleValidator();
            RuleResolver coreResolver = CoreRuleCatalog.CreateResolver();
            var universeNames = new HashSet<string>(resolutionUniverse.Select(d => d.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (SpecialRuleDefinition definition in toValidate)
            {
                foreach (RuleViolation violation in validator.Validate(definition))
                {
                    problems.Add($"'{definition.Name}' at {violation.Hook}: {violation.Member} requires " +
                        $"{violation.MissingCapability.Name}, which that hook does not provide.");
                }

                foreach (string granted in GrantedRuleNames(definition).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!universeNames.Contains(granted) && !coreResolver.TryResolve(granted, out _))
                    {
                        problems.Add($"'{definition.Name}' grants '{granted}', which resolves neither in " +
                            "the core catalog nor in the supplement.");
                    }
                }
            }

            return problems;
        }
    }
}
