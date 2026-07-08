using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

public sealed record RuleViolation(string RuleName, EHookID Hook, string Member, Type MissingCapability);

public sealed class RuleValidator
{
    private const string CONDITION_MEMBER_NAME = "Condition";
    private const string EFFECT_MEMBER_NAME = "Effect";
    
    private readonly HookContextCatalog _catalog;

    public RuleValidator(HookContextCatalog? catalog = null)
    {
        _catalog = catalog ?? new HookContextCatalog();
    }

    public IReadOnlyList<RuleViolation> Validate(SpecialRuleDefinition rule)
    {
        var violations = new List<RuleViolation>();

        foreach (HookEntry entry in rule.Passive)
        {
            if (!_catalog.TryGetContextType(entry.HookID, out _))
            {
                // Unknown hook: nothing to validate against — but that means NO stage constructs a
                // context for it, so the entry can never fire. Registering it is legal (the hook may be
                // reserved for future wiring), but silently is how inert rules ship, so warn loudly.
                RuleDiagnostics.WarnOnce($"dead-hook:{rule.Name}:{entry.HookID}",
                    $"Rule '{rule.Name}' has a passive entry on hook '{entry.HookID}', which no engine " +
                    "stage currently fires - that entry will never trigger in a game.");
                continue;
            }

            IReadOnlyCollection<Type> provided = _catalog.CapabilitiesOf(entry.HookID);
            Check(rule.Name, entry.HookID, CONDITION_MEMBER_NAME, entry.Condition.RequiredCapabilities, provided, violations);
            Check(rule.Name, entry.HookID, EFFECT_MEMBER_NAME, entry.Effect.RequiredCapabilities, provided, violations);
        }

        return violations;
    }

    private static void Check(string ruleName, EHookID hook, string member,
        IReadOnlyCollection<Type> required, IReadOnlyCollection<Type> provided, List<RuleViolation> violations)
    {
        foreach (Type capability in required)
        {
            if (provided.Contains(capability) == false)
            {
                violations.Add(new RuleViolation(ruleName, hook, member, capability));
            }
        }
    }
}