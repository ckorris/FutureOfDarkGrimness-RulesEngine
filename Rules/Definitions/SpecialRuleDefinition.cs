namespace FDG.Rules.Definitions;

/// <summary>
/// Top-level definition of one rule. The unit of authoring — typically one
/// file (and eventually one JSON record) per rule.
///
/// Bundles:
/// <list type="bullet">
///   <item><see cref="Name"/> — the canonical identifier rules reference each
///         other by (e.g. "Regeneration", "Furious"). Stable; used as the
///         lookup key in the rule registry.</item>
///   <item><see cref="DisplayName"/> — optional player-facing label. Useful
///         for army-specific renames where mechanics match a core rule but the
///         flavor name differs (e.g. "Medical Training" displaying as such
///         while mechanically being Regeneration).</item>
///   <item><see cref="Passive"/> — all the hook attachments that fire
///         automatically when their conditions match. May be empty (e.g. a
///         pure spell rule has no passive entries).</item>
///   <item><see cref="Activated"/> — all player-triggered abilities the rule
///         provides. May be empty (e.g. Furious is purely passive).</item>
///   <item><see cref="Aliases"/> — alternative names that resolve to this same
///         definition. Lets multiple rule names (army-specific renames, legacy
///         names) point at the same definition without duplication.</item>
/// </list>
///
/// A rule may have only Passive entries (Furious, Stealth), only Activated
/// entries (most spells), or both (e.g. Battleborn's "passive recovery roll
/// at round start" combined with an activated piece).
/// </summary>
public record SpecialRuleDefinition(string Name, string? DisplayName, IReadOnlyList<HookEntry> Passive,
    IReadOnlyList<ActivatedAbility> Activated, IReadOnlyList<string>? Aliases);
