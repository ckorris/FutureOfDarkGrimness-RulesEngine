using FDG.Rules.Definitions;
using System;
using System.Collections.Generic;

namespace FDG.SaveLoad
{
    /// <summary>
    /// The host's current rulebook data, as army load sees it (#354). The engine has no idea where
    /// rulebooks live — they are app-side assets (<c>FdgRaylib/Assets/Books</c>) — so the app installs
    /// an implementation at startup and the engine asks it two questions during army load.
    /// </summary>
    public interface ICurrentRulebook
    {
        /// <summary>
        /// Every special-rule definition the book for <paramref name="faction"/> ships today, or empty
        /// when no book matches (a hand-authored or freeform list). Called once per army load, so an
        /// implementation is expected to cache.
        /// </summary>
        IReadOnlyList<SpecialRuleDefinition> DefinitionsForFaction(string faction);

        /// <summary>
        /// Whether the current rulebook data defines <paramref name="ruleName"/> at all, regardless of
        /// faction. Answers "is this name implemented today?" for a list whose own faction matched no
        /// book, which is the difference between a stale list and a genuinely unimplemented rule.
        /// </summary>
        bool Defines(string ruleName);
    }

    /// <summary>
    /// Install point for <see cref="ICurrentRulebook"/> (#354), mirroring how the app installs itself
    /// on <see cref="Rules.Dispatch.RuleDiagnostics"/>. Nothing installed means the engine behaves
    /// exactly as it did before this item: an army resolves against the core catalog plus its own
    /// embedded definitions and nothing else. Tests install a stub and reset it in teardown.
    ///
    /// <para>WHY this exists: a compiled <c>.fdgarmy</c> embeds a COPY of its book's definitions at save
    /// time (<c>ListCompiler</c>), so a list saved before a rule was implemented keeps naming that rule
    /// with nothing behind it forever — the rule silently does nothing in every game the list is ever
    /// fielded in. Consulting the bundled book at load lets an old list pick up rules the engine has
    /// since learned.</para>
    /// </summary>
    public static class CurrentRulebook
    {
        private static readonly SpecialRuleDefinition[] None = Array.Empty<SpecialRuleDefinition>();

        /// <summary>The installed source, or null when the host installed none. Set once at startup.</summary>
        public static ICurrentRulebook? Installed { get; set; }

        /// <inheritdoc cref="ICurrentRulebook.DefinitionsForFaction"/>
        public static IReadOnlyList<SpecialRuleDefinition> DefinitionsForFaction(string? faction)
        {
            if (Installed == null || string.IsNullOrWhiteSpace(faction))
            {
                return None;
            }

            return Installed.DefinitionsForFaction(faction!);
        }

        /// <inheritdoc cref="ICurrentRulebook.Defines"/>
        public static bool Defines(string ruleName) =>
            Installed != null && !string.IsNullOrWhiteSpace(ruleName) && Installed.Defines(ruleName);
    }
}
