using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// In-memory registry mapping rule names to <see cref="SpecialRuleDefinition"/>
/// instances. Both canonical names and aliases live in the same dictionary —
/// an alias is just an additional key pointing at an already-registered
/// definition, so multiple names share one definition instance.
///
/// Because identity-based comparisons (e.g. <c>Effect.IgnoreRule</c>) compare
/// attached rules by <see cref="SpecialRuleDefinition"/> reference, an alias is
/// indistinguishable from its canonical rule at dispatch time — which is exactly
/// what callers want (ignoring Regeneration also ignores army-flavored aliases
/// like "Healing Pods"). The original requested name is preserved on
/// <see cref="ResolvedRule.RequestedName"/> for display purposes.
///
/// Intended usage: populated once at lobby/army-load time and treated as frozen
/// thereafter. There is intentionally no <c>Unregister</c>.
/// </summary>
public sealed class RuleResolver : IRuleResolver
{
    private readonly Dictionary<string, SpecialRuleDefinition> _rules = new();

    /// <summary>
    /// Registers <paramref name="definition"/> under its canonical
    /// <see cref="SpecialRuleDefinition.Name"/>.
    /// </summary>
    /// <exception cref="Exception">A rule is already registered under this name.</exception>
    public void Register(SpecialRuleDefinition definition)
    {
        if (!_rules.TryAdd(definition.Name, definition))
        {
            throw new Exception($"{nameof(RuleResolver)} already had a rule named {definition.Name}.");
        }
    }

    /// <summary>
    /// Registers <paramref name="definition"/> under its canonical name, REPLACING any rule already
    /// registered under that name. The override primitive for army-embedded rules (#059): core rules
    /// register first via <see cref="Register"/>, then a loaded army's definitions replace by name so a
    /// template can retune a core rule from data. Unlike <see cref="Register"/>, this never throws on a
    /// duplicate. Existing aliases keep pointing at the prior definition instance (they captured the
    /// reference at alias time), so override-then-alias ordering matters if both are used for one name.
    /// </summary>
    public void RegisterOrReplace(SpecialRuleDefinition definition)
    {
        _rules[definition.Name] = definition;
    }

    /// <summary>
    /// Registers <paramref name="alias"/> as an additional name for the rule
    /// already registered under <paramref name="existingRuleName"/>. After this
    /// call, <see cref="Resolve"/> with either name returns the same
    /// <see cref="SpecialRuleDefinition"/> instance.
    ///
    /// Aliases-of-aliases work transparently: aliasing "Cellular Mend" to
    /// "Healing Pods" (which is itself aliased to "Regeneration") resolves to the
    /// Regeneration definition, because the alias lookup returns whatever the
    /// intermediate key already points at.
    /// </summary>
    /// <exception cref="Exception">
    /// No rule is registered under <paramref name="existingRuleName"/>, or
    /// <paramref name="alias"/> is already in use.
    /// </exception>
    public void RegisterAlias(string alias, string existingRuleName)
    {
        if (_rules.TryGetValue(existingRuleName, out SpecialRuleDefinition? result) == false)
        {
            throw new Exception(
                $"Tried to register special rule {alias} as alias of {existingRuleName}, but that rule wasn't registered.");
        }

        if (_rules.TryAdd(alias, result) == false)
        {
            throw new Exception($"Already registered an alias named {alias} to {nameof(RuleResolver)}.)");
        }
    }

    /// <inheritdoc />
    public ResolvedRule Resolve(string rule)
    {
        return _rules.TryGetValue(rule, out SpecialRuleDefinition? result)
            ? new ResolvedRule(rule, result)
            : throw new KeyNotFoundException($"No rule named {rule} was registered in {nameof(RuleResolver)}.");
    }

    /// <inheritdoc />
    public bool TryResolve(string rule, out ResolvedRule resolved)
    {
        if (_rules.TryGetValue(rule, out SpecialRuleDefinition? result))
        {
            resolved = new ResolvedRule(rule, result);
            return true;
        }

        resolved = null!;
        return false;
    }
}