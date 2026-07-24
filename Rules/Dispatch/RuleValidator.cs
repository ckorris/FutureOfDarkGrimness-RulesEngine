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
    private const string AVAILABLE_WHEN_MEMBER_NAME = "AvailableWhen";

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

    /// <summary>
    /// #265 — the AUTHORING check: everything <see cref="Validate"/> covers, plus the whole-unit-ability
    /// all-models gate (<see cref="CheckUnitWideSelfEffectGate"/>).
    ///
    /// <para>Deliberately separate from <see cref="Validate"/>, which is the army-LOAD gate and hard-fails
    /// the whole load. Supplement definitions travel inside saved <c>.fdgarmy</c> files, so folding the new
    /// check into the load gate would make every army file exported before this change fail to open. New
    /// data cannot get past this one: it runs at supplement validation, at book-embed time, and over the
    /// core catalog + shipped supplement in the test suite.</para>
    /// </summary>
    public IReadOnlyList<RuleViolation> ValidateAuthoring(SpecialRuleDefinition rule)
    {
        var violations = new List<RuleViolation>(Validate(rule));

        foreach (HookEntry entry in rule.Passive)
        {
            // A passive entry's effect applies to the bearer, so it is always "self-targeted".
            CheckUnitWideSelfEffectGate(rule, entry.HookID, entry.Effect, entry.Condition,
                targetsSelf: true, CONDITION_MEMBER_NAME, violations);
        }

        foreach (ActivatedAbility ability in rule.Activated)
        {
            CheckUnitWideSelfEffectGate(rule, ability.TriggerHook, ability.Effect, ability.AvailableWhen,
                ability.TargetSelector.TargetAffinity == ETargetAffinity.Self,
                AVAILABLE_WHEN_MEMBER_NAME, violations);
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

    // #265 — a Unit-scoped rule that repositions or re-activates the WHOLE bearer unit must gate on
    // AllModelsHaveThisRule. The rulebook convention is that a unit-wide ability works only if every model
    // has it, and per-model dispatch means a single joined hero carrying Teleport would otherwise let the
    // entire unit teleport. This is the activated-ability sibling of the #183 defensive-rule gate above,
    // which only covers passive Subject-seat entries and so never saw Teleport / Fanatic / Wolfborn.
    private static void CheckUnitWideSelfEffectGate(SpecialRuleDefinition rule, EHookID hook, Effect effect,
        Condition condition, bool targetsSelf, string member, List<RuleViolation> violations)
    {
        if (rule.Scope != ERuleScope.Unit
            || !targetsSelf
            || !IsWholeUnitSelfEffect(effect, hook)
            || GatesOnAllModels(condition))
        {
            return;
        }

        violations.Add(new RuleViolation(rule.Name, hook, member, MissingCapability: null,
            "a rule that repositions or re-activates the WHOLE bearer unit must gate on " +
            "AllModelsHaveThisRule - RAW the unit gets the ability only if every model has it, and per-model " +
            "dispatch would otherwise let one joined hero's copy move or reactivate the entire unit"));
    }

    // The effects that move or re-activate every model of the bearer unit at once.
    private static bool IsWholeUnitSelfEffect(Effect effect, EHookID hook) => effect switch
    {
        Effect.Teleport => true,
        Effect.RepositionAtActivation => true,
        Effect.RepositionOnDeploy => true,
        Effect.Reactivate => true,
        // TriggeredMove is shared with the Harassing family (a 3-6" nudge after shooting or being attacked),
        // which is a different class of rule and out of scope here. Only the pre-game Scout/Vanguard
        // reposition - a self-targeted triggered move at the deploy hook - is covered.
        Effect.TriggeredMove => hook == EHookID.Deployment_OnUnitDeployed,
        _ => false,
    };

    // True if AllModelsHaveThisRule appears in a CONJUNCTIVE position (top-level or inside an And chain),
    // so it actually gates. Under an Or/Not it wouldn't, so those don't count.
    private static bool GatesOnAllModels(Condition condition) => condition switch
    {
        Condition.AllModelsHaveThisRule => true,
        Condition.And and => GatesOnAllModels(and.Left) || GatesOnAllModels(and.Right),
        _ => false,
    };
}