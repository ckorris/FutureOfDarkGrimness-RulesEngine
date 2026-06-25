using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Evaluates a unit's rules against a firing hook context and returns the resolved
/// <see cref="RuleOperation"/> queue. The spine of #042 Phase 7 — passive rules via
/// <see cref="Evaluate"/>, activated abilities via <see cref="GatherOffers"/> +
/// <see cref="ResolveAbility"/>.
///
/// Deliberately NOT a message bus: callers (stages) already know which units are
/// involved in an event and which role each plays, so they address those units
/// directly — once as the <see cref="ERuleSeat.Actor"/>, once as the
/// <see cref="ERuleSeat.Subject"/> — and apply the returned operations themselves.
/// There is no publish/subscribe, no registration, and the caller needs the queue
/// back synchronously, which makes this a query, not a broadcast.
///
/// Holds an injected <see cref="IDiceRoller"/> for effects with random amounts
/// (Mend's D3 heal), passed into every <see cref="RuleInvocation"/> it builds.
/// </summary>
public sealed class RuleEvaluator
{
    private readonly IDiceRoller _diceRoller;
    private readonly ITextOutput? _log;

    /// <summary>
    /// Resolves the rule NAME carried by a <see cref="TokenType.RuleGrant"/> token back to its
    /// <see cref="SpecialRuleDefinition"/> so granted rules (auras, "gains rule X" buffs) actually
    /// fire — the read-back half of <c>Effect.Aura</c>/<c>Effect.AddRule</c>, which only write the
    /// token. The same shared army-load resolver registered the unit's static rules, so aliases
    /// resolve identically. Optional: null (resume before #095 rehydration, or bare-evaluator tests)
    /// simply skips granted-rule read-back — granted tokens contribute nothing rather than throwing.
    /// </summary>
    private readonly IRuleResolver? _ruleResolver;

    public RuleEvaluator(IDiceRoller diceRoller, ITextOutput? log = null, IRuleResolver? ruleResolver = null)
    {
        _diceRoller = diceRoller;
        _log = log;
        _ruleResolver = ruleResolver;
    }

    /// <summary>
    /// Operations produced by <paramref name="unit"/>'s passive rules whose hook
    /// matches the firing <paramref name="context"/> and whose seat matches
    /// <paramref name="seat"/> (the role this unit is playing in the event) and
    /// whose condition passes. When <paramref name="weapon"/> is supplied (#027),
    /// that weapon's own rules are evaluated too — the per-weapon scoping that makes
    /// a Blast cannon's rules apply to its shots and not its bearer's other weapons.
    /// </summary>
    public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context,
        IWeapon? weapon = null)
    {
        var tagged = new List<TaggedOperation>();
        // Single-participant Evaluate does NOT run the consume-on-fire pass — grantsToConsume stays null —
        // so a one-shot (NextTrigger) granted rule firing here is projected but never spent. Correct today:
        // 3 of its 4 call sites are read-only queries (TryGet*Defer) that MUST NOT consume, and no corpus
        // rule grants a one-shot buff firing only at a round-start/deployment/activation hook. If one ever
        // does, add a `consumeGrants` opt-in and set it on the apply site (GrantSpellTokens) only — see #104.
        CollectTagged(unit, seat, weapon, models: null, context, tagged, new DedupState(), grantsToConsume: null);

        // Per-unit Evaluate does NOT run the suppression first-pass — cross-unit suppression
        // (an attacker's Unstoppable cancelling a defender's Regeneration) only exists once the
        // participants are combined in EvaluateAll. So here every produced op is logged and
        // returned as-is, including any SuppressRule (the queue-level tests assert on it).
        foreach (TaggedOperation t in tagged)
        {
            Log(t);
        }

        return tagged.Select(t => t.Op).ToList();
    }

    public IReadOnlyList<RuleOperation> EvaluateAll(IHookContext context,
        params (IUnit Unit, ERuleSeat Seat)[] participants)
    {
        return EvaluateAll(context, WithoutWeapons(participants));
    }

    /// <summary>
    /// Weapon-aware participant form (#027): a participant with a weapon contributes that
    /// weapon's rules alongside its unit rules — the attacker's firing weapon in the shoot
    /// pipeline, each defender melee weapon at strike-order time. Identical rules reached
    /// through several carriers fire once (the rulebook's "multiple instances of the same
    /// rule don't stack"), except argumented (X) rules, which stack by the book.
    /// </summary>
    public IReadOnlyList<RuleOperation> EvaluateAll(IHookContext context,
        params (IUnit Unit, ERuleSeat Seat, IWeapon? Weapon)[] participants)
    {
        return CollectSurviving(context, log: true, WithModels(participants)).Select(t => t.Op).ToList();
    }

    /// <summary>
    /// Model-aware participant form (#006 slice F): a participant may also name the specific model(s) the
    /// hook involves, and those models' own rules (<see cref="IModel.RuleDefinitions"/>) are evaluated
    /// alongside the unit's and weapon's. Opt-in — only stages that pass models (currently the hit stages,
    /// for a weapon batch's sole owner) see per-model rules; every other call site is unaffected. This is
    /// how a joined hero's own rules fire for the hero alone rather than the whole host unit.
    /// </summary>
    public IReadOnlyList<RuleOperation> EvaluateAll(IHookContext context,
        params (IUnit Unit, ERuleSeat Seat, IWeapon? Weapon, IReadOnlyList<IModel>? Models)[] participants)
    {
        return CollectSurviving(context, log: true, participants).Select(t => t.Op).ToList();
    }

    /// <summary>
    /// Like <see cref="EvaluateAll"/>, but pairs each surviving operation with the alias-aware display
    /// name (<see cref="ResolvedRule.RequestedName"/>) of the rule that produced it — so presentation
    /// queries (e.g. <see cref="SightRuleQueries"/>) can attribute an effect to its rule. Does NOT log:
    /// it's a read-only query callers may run per-frame while building UI, so it must not spam the game log.
    /// </summary>
    public IReadOnlyList<(RuleOperation Op, string RuleName)> EvaluateAllNamed(IHookContext context,
        params (IUnit Unit, ERuleSeat Seat)[] participants)
    {
        return EvaluateAllNamed(context, WithoutWeapons(participants));
    }

    /// <summary> Weapon-aware form of <see cref="EvaluateAllNamed"/> (#027). </summary>
    public IReadOnlyList<(RuleOperation Op, string RuleName)> EvaluateAllNamed(IHookContext context,
        params (IUnit Unit, ERuleSeat Seat, IWeapon? Weapon)[] participants)
    {
        return CollectSurviving(context, log: false, WithModels(participants))
            .Select(t => (t.Op, t.Origin.RequestedName)).ToList();
    }

    private static (IUnit, ERuleSeat, IWeapon?)[] WithoutWeapons((IUnit Unit, ERuleSeat Seat)[] participants)
    {
        var expanded = new (IUnit, ERuleSeat, IWeapon?)[participants.Length];
        for (int i = 0; i < participants.Length; i++)
        {
            expanded[i] = (participants[i].Unit, participants[i].Seat, null);
        }
        return expanded;
    }

    /// <summary> Widens weaponed participants to the model-aware tuple with no per-model rules (the
    /// non-slice-F default), so the original call sites keep their exact behavior. </summary>
    private static (IUnit, ERuleSeat, IWeapon?, IReadOnlyList<IModel>?)[] WithModels(
        (IUnit Unit, ERuleSeat Seat, IWeapon? Weapon)[] participants)
    {
        var expanded = new (IUnit, ERuleSeat, IWeapon?, IReadOnlyList<IModel>?)[participants.Length];
        for (int i = 0; i < participants.Length; i++)
        {
            expanded[i] = (participants[i].Unit, participants[i].Seat, participants[i].Weapon, null);
        }
        return expanded;
    }

    /// <summary>
    /// Collects every participant's matching operations, runs the suppression first-pass (a queued
    /// <see cref="RuleOperation.SuppressRule"/>("X") removes every op whose origin rule's canonical name
    /// is "X" — alias-renamed victims included, since it matches <c>Definition.Name</c>), and returns the
    /// surviving tagged operations in order. Logs each kept op (and each suppressor's "X ignored Y") only
    /// when <paramref name="log"/> is true.
    /// </summary>
    private List<TaggedOperation> CollectSurviving(IHookContext context, bool log,
        params (IUnit Unit, ERuleSeat Seat, IWeapon? Weapon, IReadOnlyList<IModel>? Models)[] participants)
    {
        var tagged = new List<TaggedOperation>();

        // Shared across the whole event so the same rule reached through several carriers
        // (unit + weapon, two identical weapons, or the unit walked once per weapon
        // participant) fires once per bearer.
        var seen = new DedupState();

        // #101 — only the live apply path (log == true) consumes one-shot grants; the read-only named
        // query (log == false) must not mutate token state. Collects the FirstTrigger grant TOKENS whose
        // rule has an entry for this hook+seat — the "next time it would apply" occurrence — to spend after
        // the walk, regardless of whether the rule's condition passed or its op survived suppression.
        List<(IUnit Unit, Token Grant)>? grantsToConsume = log ? new List<(IUnit, Token)>() : null;

        foreach ((IUnit unit, ERuleSeat seat, IWeapon? weapon, IReadOnlyList<IModel>? models) in participants)
        {
            CollectTagged(unit, seat, weapon, models, context, tagged, seen, grantsToConsume);
        }

        var suppressedRuleNames = tagged
            .Select(t => t.Op)
            .OfType<RuleOperation.SuppressRule>()
            .Select(s => s.RuleName)
            .ToHashSet();

        var result = new List<TaggedOperation>(tagged.Count);
        foreach (TaggedOperation t in tagged)
        {
            if (t.Op is RuleOperation.SuppressRule)
            {
                // The suppressor is internal machinery a stage never applies. Log the action
                // ("X ignored Y"), then drop it from the queue handed back.
                if (log) Log(t);
                continue;
            }

            if (suppressedRuleNames.Contains(t.Origin.Definition.Name))
            {
                // Dropped by a suppressor — emit no log line (logging happens post-suppression,
                // so a cancelled op never prints).
                continue;
            }

            if (log) Log(t);
            result.Add(t);
        }

        // #101 consume-on-occurrence: spend each one-shot grant whose hook+seat fired this event, removing
        // its token DIRECTLY (not via a returned op). This is robust — the hit/save/melee pipeline runs
        // sinks, not OperationApplier, so a returned consume op would be silently dropped exactly where
        // most combat buffs apply. Deduped per (bearer, payload) so one occurrence spends one charge even
        // when a grant fired through several entries.
        if (grantsToConsume != null)
        {
            var spent = new HashSet<(UnitID, TokenPayload?)>();
            foreach ((IUnit unit, Token grant) in grantsToConsume)
            {
                if (!spent.Add((unit.ID, grant.Payload)))
                {
                    continue;
                }

                unit.Tokens.RemoveTokensWithPayload(grant.Type, grant.OwnerUnitID, grant.Payload, count: 1);
                if (grant.Payload is TokenPayload.RuleGrant spentGrant)
                {
                    _log?.Log($"{unit.Name}'s {spentGrant.RuleName} grant is spent.");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Walks <paramref name="unit"/>'s passive rules — and, when <paramref name="weapon"/> is
    /// supplied, that weapon's rules (#027) — for entries matching the firing
    /// <paramref name="context"/> and <paramref name="seat"/> whose condition passes, and appends
    /// each produced operation to <paramref name="sink"/> paired with its origin rule + bearer.
    /// <paramref name="seen"/> dedupes argument-less rules per bearer across carriers (the
    /// rulebook's "effects from multiple instances of the same special rule don't stack, unless
    /// it is a rule with (X) in its name"). Does not log — callers log after deciding which
    /// operations survive.
    /// </summary>
    private void CollectTagged(IUnit unit, ERuleSeat seat, IWeapon? weapon, IReadOnlyList<IModel>? models,
        IHookContext context, List<TaggedOperation> sink, DedupState seen,
        List<(IUnit Unit, Token Grant)>? grantsToConsume)
    {
        CollectFromRules(unit.RuleDefinitions, unit, carryingWeapon: null, seat, context, sink, seen);

        // Token-granted rules (auras, "gains rule X" buffs): the unit behaves as if it has each rule
        // named by a RuleGrant token. Walked through the same per-rule path as static attachments, so
        // they honour the firing seat/condition and share the dedup (an argument-less rule granted on
        // top of a static copy fires once) and suppression first-pass. Bearer stays the unit, never a
        // weapon — grants live on unit token containers.
        CollectGrantedRules(unit, seat, context, sink, seen, grantsToConsume);

        if (weapon != null)
        {
            CollectFromRules(weapon.RuleDefinitions, unit, weapon, seat, context, sink, seen);
        }

        // #006 slice F: per-model rules for the model(s) this hook actually involves (e.g. a weapon
        // batch's sole owner on the hit hooks). Bearer stays the unit, so dedup + logging are unchanged.
        if (models != null)
        {
            foreach (IModel model in models)
            {
                CollectFromRules(model.RuleDefinitions, unit, carryingWeapon: null, seat, context, sink, seen);
            }
        }
    }

    private void CollectFromRules(IReadOnlyList<ResolvedRule> rules, IUnit unit, IWeapon? carryingWeapon,
        ERuleSeat seat, IHookContext context, List<TaggedOperation> sink, DedupState seen)
    {
        foreach (ResolvedRule rule in rules)
        {
            if (!seen.ShouldFire(unit, rule))
            {
                continue;
            }

            var invocation = new RuleInvocation(context, unit, rule.Arguments, DiceRoller: _diceRoller,
                Weapon: carryingWeapon, Definition: rule.Definition);

            foreach (HookEntry entry in rule.Definition.Passive)
            {
                if (entry.HookID != context.Hook || entry.Seat != seat)
                {
                    continue;
                }

                if (!entry.Condition.Evaluate(invocation))
                {
                    continue;
                }

                var produced = new List<RuleOperation>();
                entry.Effect.Apply(invocation, produced);

                foreach (RuleOperation op in produced)
                {
                    sink.Add(new TaggedOperation(op, rule, unit, carryingWeapon));
                }
            }
        }
    }

    /// <summary>
    /// Resolves each of <paramref name="unit"/>'s <see cref="TokenType.RuleGrant"/> tokens back to a
    /// <see cref="ResolvedRule"/> and walks them like static attachments (same seat/condition/dedup).
    /// This is the read side of <c>Effect.Aura</c>/<c>Effect.AddRule</c>. No resolver (resume before
    /// #095 rehydration, or a bare-evaluator test) ⇒ no granted rules contribute. An unknown grant name
    /// is skipped, not thrown — a granted rule the registry doesn't carry just does nothing, matching
    /// army-load's skip-and-warn for unimplemented rule names.
    /// </summary>
    private void CollectGrantedRules(IUnit unit, ERuleSeat seat, IHookContext context,
        List<TaggedOperation> sink, DedupState seen,
        List<(IUnit Unit, Token Grant)>? grantsToConsume)
    {
        if (_ruleResolver == null)
        {
            return;
        }

        List<ResolvedRule>? granted = null;
        foreach (Token token in unit.Tokens.GetAllTokens(TokenType.RuleGrant))
        {
            if (token.Payload is not TokenPayload.RuleGrant grant)
            {
                continue;
            }

            if (!_ruleResolver.TryResolve(grant.RuleName, out ResolvedRule resolved))
            {
                continue;
            }

            (granted ??= new List<ResolvedRule>()).Add(resolved);

            // #101 — record this one-shot ("next time") grant for consumption iff THIS hook+seat is one its
            // rule actually listens on (the occurrence). It's then spent in CollectSurviving regardless of
            // whether the condition passed or the op survived suppression — forcing a unit to waste a buff
            // by entering a situation it can't benefit from is a legitimate tactic.
            if (grantsToConsume != null && token.ClearTrigger is TokenClearTrigger.FirstTrigger
                && RuleHasEntryForHook(resolved.Definition, context.Hook, seat))
            {
                grantsToConsume.Add((unit, token));
            }
        }

        if (granted != null)
        {
            CollectFromRules(granted, unit, carryingWeapon: null, seat, context, sink, seen);
        }
    }

    /// <summary>
    /// Whether <paramref name="definition"/> has any passive entry for the firing <paramref name="hook"/>
    /// in the <paramref name="seat"/> the bearer is playing — i.e. this event is an occurrence the rule
    /// listens on. Used to decide when a one-shot ("next time") grant is spent (#101), independent of the
    /// entry's <see cref="Condition"/> or whether its op survives.
    /// </summary>
    private static bool RuleHasEntryForHook(SpecialRuleDefinition definition, EHookID hook, ERuleSeat seat)
    {
        foreach (HookEntry entry in definition.Passive)
        {
            if (entry.HookID == hook && entry.Seat == seat)
            {
                return true;
            }
        }
        return false;
    }

    private void Log(TaggedOperation t)
    {
        string carrier = t.Weapon == null
            ? $"{t.Bearer.Name}'s {t.Origin.RequestedName}"
            : $"{t.Bearer.Name}'s {t.Weapon.Name}'s {t.Origin.RequestedName}";
        _log?.Log($"{carrier} {t.Op.Describe()}.");
    }

    /// <summary>
    /// A produced operation paired with the rule that produced it, the unit carrying that rule,
    /// and — for weapon-attached rules (#027) — the carrying weapon. Origin tracking is dispatcher
    /// sidecar metadata — not a field on <see cref="RuleOperation"/> — so the suppression
    /// first-pass can match ops by their origin rule's canonical name and the game-log can name
    /// the bearer and rule. The public API still returns bare operations.
    /// </summary>
    private readonly record struct TaggedOperation(RuleOperation Op, ResolvedRule Origin, IUnit Bearer,
        IWeapon? Weapon = null);

    /// <summary>
    /// Per-event dedup deciding whether a rule attachment fires (#027). Two layers:
    /// the same <see cref="ResolvedRule"/> attachment instance never fires twice in one
    /// event (a unit's rules are walked once per weapon participant; duplicate weapons
    /// from one army-file entry share their attachment instances); and argument-less
    /// rules additionally fire at most once per bearer across DISTINCT attachments —
    /// the rulebook's "effects from multiple instances of the same special rule don't
    /// stack, unless it is a rule with (X) in its name".
    /// </summary>
    private sealed class DedupState
    {
        private readonly HashSet<(UnitID, ResolvedRule)> _firedAttachments = new();
        private readonly HashSet<(UnitID, SpecialRuleDefinition)> _firedArglessDefinitions = new();

        public bool ShouldFire(IUnit bearer, ResolvedRule rule)
        {
            if (!_firedAttachments.Add((bearer.ID, rule)))
            {
                return false;
            }

            if (rule.Arguments.Count == 0 && !_firedArglessDefinitions.Add((bearer.ID, rule.Definition)))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The player-triggered abilities available at this hook: abilities whose
    /// <see cref="ActivatedAbility.TriggerHook"/> matches the firing context, whose
    /// <see cref="ActivatedAbility.AvailableWhen"/> passes, and whose
    /// <see cref="Cost"/> the acting unit can currently afford. Returns offers, not
    /// operations — nothing is resolved until the player accepts (see
    /// <see cref="ResolveAbility"/>). The acting unit is read straight off the
    /// context via <see cref="IHasActingUnit"/>; contexts without it offer nothing.
    /// </summary>
    public IReadOnlyList<AbilityOffer> GatherOffers(IHookContext context)
    {
        var offers = new List<AbilityOffer>();

        if (context is not IHasActingUnit acting)
        {
            return offers;
        }

        IUnit unit = acting.ActingUnit;

        foreach (ResolvedRule rule in unit.RuleDefinitions)
        {
            var invocation = new RuleInvocation(context, unit, rule.Arguments, DiceRoller: _diceRoller);

            foreach (ActivatedAbility ability in rule.Definition.Activated)
            {
                if (ability.TriggerHook != context.Hook)
                {
                    continue;
                }

                if (!ability.AvailableWhen.Evaluate(invocation))
                {
                    continue;
                }

                if (!IsAffordable(ability.Cost, unit, rule.RequestedName))
                {
                    continue;
                }

                offers.Add(new AbilityOffer(unit, rule.RequestedName, ability));
            }
        }

        return offers;
    }

    /// <summary>
    /// Resolves an accepted ability against the chosen <paramref name="targets"/>:
    /// emits the cost-consumption operations the bearer pays, then applies the
    /// ability's effect once per target (effects land on the target via
    /// <see cref="RuleInvocation.EffectiveTarget"/>). Returns the combined queue —
    /// cost operations first, then effect operations.
    /// </summary>
    public IReadOnlyList<RuleOperation> ResolveAbility(AbilityOffer offer, IReadOnlyList<IUnit> targets)
    {
        var operations = new List<RuleOperation>();

        EmitCostOps(offer.Ability.Cost, offer.Bearer, offer.RuleName, operations);

        foreach (IUnit target in targets)
        {
            // Activated-ability args aren't carried on the offer (no corpus ability uses
            // ValueSource.Arg); thread the bearer's ResolvedRule.Arguments here when one does.
            var invocation = new RuleInvocation(
                Hook: null, offer.Bearer, Array.Empty<RuleArgument>(), target, _diceRoller);
            offer.Ability.Effect.Apply(invocation, operations);
        }

        return operations;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> can currently pay <paramref name="cost"/>.
    /// Once-per-X gates are tracked by a per-ability "used" marker token keyed on
    /// <paramref name="ruleName"/>; the gate is open while no marker is present.
    /// </summary>
    private static bool IsAffordable(Cost cost, IUnit unit, string ruleName) => cost switch
    {
        Cost.SpellTokens st => unit.Tokens.GetTokenCount(TokenType.SpellTokens) >= st.Count,
        Cost.ConsumesToken ct => unit.Tokens.GetTokenCount(ct.TType) >= ct.Count,
        Cost.OncePerActivation => !unit.Tokens.HasToken(UsedMarker(ruleName)),
        Cost.OncePerRound => !unit.Tokens.HasToken(UsedMarker(ruleName)),
        Cost.OncePerGame => !unit.Tokens.HasToken(UsedMarker(ruleName)),
        _ => true,
    };

    /// <summary>
    /// Emits the operations that pay <paramref name="cost"/>: token consumption for
    /// resource costs, or granting the per-ability "used" marker (with the clear
    /// trigger that defines its window) for once-per-X gates.
    /// </summary>
    private static void EmitCostOps(Cost cost, IUnit bearer, string ruleName, List<RuleOperation> operations)
    {
        switch (cost)
        {
            case Cost.SpellTokens st:
                operations.Add(new RuleOperation.ConsumeTokensFromUnit(bearer, TokenType.SpellTokens, st.Count));
                break;
            case Cost.ConsumesToken ct:
                operations.Add(new RuleOperation.ConsumeTokensFromUnit(bearer, ct.TType, ct.Count));
                break;
            case Cost.OncePerActivation:
                operations.Add(new RuleOperation.GrantTokenToUnit(bearer,
                    new Token(UsedMarker(ruleName), 1, new TokenClearTrigger.ActivationEnd())));
                break;
            case Cost.OncePerRound:
                operations.Add(new RuleOperation.GrantTokenToUnit(bearer,
                    new Token(UsedMarker(ruleName), 1, new TokenClearTrigger.RoundEnd())));
                break;
            case Cost.OncePerGame:
                operations.Add(new RuleOperation.GrantTokenToUnit(bearer,
                    new Token(UsedMarker(ruleName), 1, new TokenClearTrigger.ManualOnly())));
                break;
        }
    }

    private static TokenType UsedMarker(string ruleName) => new("AbilityUsed:" + ruleName);
}
