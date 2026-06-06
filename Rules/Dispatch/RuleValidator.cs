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
                continue; //Unknown hook. Nothing to validate against.
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