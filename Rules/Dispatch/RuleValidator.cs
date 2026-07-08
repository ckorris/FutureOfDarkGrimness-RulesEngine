using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// A rule-authoring defect found by <see cref="RuleValidator"/>. A capability violation sets
/// <see cref="MissingCapability"/> (a Condition/Effect reads something its hook can't provide); a
/// semantic violation leaves it null and carries a human-readable <see cref="Detail"/> instead
/// (e.g. the #183 all-models gate check). Consumers render <see cref="Describe"/>.
/// </summary>
public sealed record RuleViolation(string RuleName, EHookID Hook, string Member, Type? MissingCapability,
    string? Detail = null)
{
    /// <summary> The member-level description, without the rule-name / hook prefix each consumer adds. </summary>
    public string Describe() => Detail
        ?? $"{Member} requires {MissingCapability?.Name}, which that hook's context does not provide";
}

public sealed class RuleValidator
{
    private const string CONDITION_MEMBER_NAME = "Condition";
    private const string EFFECT_MEMBER_NAME = "Effect";

    // #183 — Subject-seat hooks where a unit-targeted defensive rule must gate on
    // Condition.AllModelsHaveThisRule. These are the attack-interaction hooks whose Subject participant
    // is the unit being attacked/charged; the rulebook applies rules like Evasive/Resistance/Fortified
    // only "to units where all models have this rule", and slice-2 model visibility means an ungated
    // copy carried by a lone joined hero would otherwise fire for the whole unit. The gate is the single
    // mechanism enforcing the RAW reading in both directions (host-has-hero-lacks and hero-has-host-lacks).
    private static readonly IReadOnlySet<EHookID> AllModelsGatedSubjectHooks = new HashSet<EHookID>
    {
        EHookID.Shooting_OnRangeCheck,
        EHookID.Shooting_OnHitRollModifier,
        EHookID.Shooting_OnHitRollComplete,
        EHookID.Shooting_OnSaveRollComplete,
        EHookID.Movement_OnChargeDeclared,
        EHookID.Melee_OnCounterTrigger,
        EHookID.Melee_OnChargeContact,
    };

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
            CheckAllModelsGate(rule, entry, violations);

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

    // #183 — a unit-scoped rule with a Subject-seat entry at a defensive attack-interaction hook must gate
    // its condition on AllModelsHaveThisRule (weapon-scoped rules ride their weapon and are exempt; the
    // gate is meaningless per-weapon). Enforced here so both the catalog (via RuleFireLint) and any
    // embedded/imported supplement rule (via army-load + BookRuleSupplement) reject an ungated one.
    private static void CheckAllModelsGate(SpecialRuleDefinition rule, HookEntry entry,
        List<RuleViolation> violations)
    {
        if (rule.Scope != ERuleScope.Unit
            || entry.Seat != ERuleSeat.Subject
            || !AllModelsGatedSubjectHooks.Contains(entry.HookID)
            || GatesOnAllModels(entry.Condition))
        {
            return;
        }

        violations.Add(new RuleViolation(rule.Name, entry.HookID, CONDITION_MEMBER_NAME, MissingCapability: null,
            "a unit-scoped defensive rule firing at the Subject seat must gate its condition on " +
            "AllModelsHaveThisRule - RAW, the unit benefits only if every model has the rule, and per-model " +
            "dispatch would otherwise let a lone joined hero's copy fire for the whole unit"));
    }

    // True if AllModelsHaveThisRule appears in a CONJUNCTIVE position (top-level or inside an And chain),
    // so it actually gates. Under an Or/Not it wouldn't, so those don't count.
    private static bool GatesOnAllModels(Condition condition) => condition switch
    {
        Condition.AllModelsHaveThisRule => true,
        Condition.And and => GatesOnAllModels(and.Left) || GatesOnAllModels(and.Right),
        _ => false,
    };
}