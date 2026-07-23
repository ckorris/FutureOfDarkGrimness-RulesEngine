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

    /// <summary>
    /// The shared army-load resolver, exposed so a STAGE can resolve rule names that are only known at
    /// dispatch time — an ability's <c>DealHits.WithRules</c> (#164), which unlike a spell's has no
    /// army-load site to pre-resolve at because the ability may itself have been conferred at runtime by
    /// an aura or grant. Null under the same conditions the read-back is skipped (see above), so callers
    /// must tolerate it and degrade to "no weapon rules" rather than throwing.
    /// </summary>
    public IRuleResolver? RuleResolver => _ruleResolver;

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
        IWeapon? weapon = null, IReadOnlyList<IModel>? models = null,
        EModelRuleScope modelScope = EModelRuleScope.AnyOwner)
    {
        var tagged = new List<TaggedOperation>();
        // Single-participant Evaluate does NOT run the consume-on-fire pass — grantsToConsume stays null —
        // so a one-shot (NextTrigger) granted rule firing here is projected but never spent. Correct today:
        // 3 of its 4 call sites are read-only queries (TryGet*Defer) that MUST NOT consume, and no corpus
        // rule grants a one-shot buff firing only at a round-start/deployment/activation hook. If one ever
        // does, add a `consumeGrants` opt-in and set it on the apply site (GrantRoundStartTokens) only — see #104.
        CollectTagged(unit, seat, weapon, models, modelScope, context, tagged, new DedupState(),
            grantsToConsume: null, trace: TraceEnabled);

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

    /// <summary>
    /// Collects each participant's matching operations (weapon rules per #027, per-model rules per
    /// #093/#183 when a participant names models), runs the suppression first-pass, logs each surviving
    /// op, and returns the resolved queue. Identical rules reached through several carriers fire once
    /// (the rulebook's "multiple instances of the same rule don't stack"), except argumented (X) rules,
    /// which stack by the book.
    /// </summary>
    public IReadOnlyList<RuleOperation> EvaluateAll(IHookContext context, params RuleParticipant[] participants)
    {
        return CollectSurviving(context, log: true, participants).Select(t => t.Op).ToList();
    }

    /// <summary>
    /// Like <see cref="EvaluateAll"/>, but pairs each surviving operation with the alias-aware display
    /// name (<see cref="ResolvedRule.RequestedName"/>) of the rule that produced it — so presentation
    /// queries (e.g. <see cref="SightRuleQueries"/>) can attribute an effect to its rule. Does NOT log:
    /// it's a read-only query callers may run per-frame while building UI, so it must not spam the game
    /// log, and it does not spend one-shot (NextTrigger) grants.
    /// </summary>
    public IReadOnlyList<(RuleOperation Op, string RuleName)> EvaluateAllNamed(IHookContext context,
        params RuleParticipant[] participants)
    {
        return CollectSurviving(context, log: false, participants)
            .Select(t => (t.Op, t.Origin.RequestedName)).ToList();
    }

    /// <summary>
    /// The live twin of <see cref="EvaluateAllNamed"/>: identical to <see cref="EvaluateAll"/> (it LOGS
    /// and spends one-shot NextTrigger grants - a real evaluation, not a read-only query), but also pairs
    /// each surviving operation with the alias-aware display name of the rule that produced it. Used by
    /// the hit-roll stage (#204) to attribute each extra hit / multiplier / per-hit-AP effect to its rule
    /// ("+1 (Furious)", "x3 (Blast)") in the save-roll presentation, without re-evaluating (which would
    /// double-spend grants).
    /// </summary>
    public IReadOnlyList<(RuleOperation Op, string RuleName)> EvaluateAllNamedLive(IHookContext context,
        params RuleParticipant[] participants)
    {
        return CollectSurviving(context, log: true, participants)
            .Select(t => (t.Op, t.Origin.RequestedName)).ToList();
    }

    /// <summary>
    /// Collects every participant's matching operations, runs the suppression first-pass (a queued
    /// <see cref="RuleOperation.SuppressRule"/>("X") removes every op whose origin rule's canonical name
    /// is "X" — alias-renamed victims included, since it matches <c>Definition.Name</c>), and returns the
    /// surviving tagged operations in order. Logs each kept op (and each suppressor's "X ignored Y") only
    /// when <paramref name="log"/> is true.
    /// </summary>
    private List<TaggedOperation> CollectSurviving(IHookContext context, bool log,
        params RuleParticipant[] participants)
    {
        var tagged = new List<TaggedOperation>();

        // #163 — only live evaluations narrate; the read-only named queries (log == false) run per-frame
        // while building UI and must stay silent even with tracing on.
        bool trace = log && TraceEnabled;
        if (trace)
        {
            TraceLine($"{context.Hook} fires - " + string.Join(", ", participants.Select(p =>
                p.Weapon == null ? $"{p.Unit.Name} ({p.Seat})" : $"{p.Unit.Name} ({p.Seat}, {p.Weapon.Name})")));
        }

        // Shared across the whole event so the same rule reached through several carriers
        // (unit + weapon, two identical weapons, or the unit walked once per weapon
        // participant) fires once per bearer.
        var seen = new DedupState();

        // #101 — only the live apply path (log == true) consumes one-shot grants; the read-only named
        // query (log == false) must not mutate token state. Collects the FirstTrigger grant TOKENS whose
        // rule has an entry for this hook+seat — the "next time it would apply" occurrence — to spend after
        // the walk, regardless of whether the rule's condition passed or its op survived suppression.
        List<(IUnit Unit, Token Grant)>? grantsToConsume = log ? new List<(IUnit, Token)>() : null;

        foreach (RuleParticipant p in participants)
        {
            CollectTagged(p.Unit, p.Seat, p.Weapon, p.Models, p.ModelScope, context, tagged, seen,
                grantsToConsume, trace);
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
                if (trace)
                {
                    string suppressors = string.Join(", ", tagged
                        .Where(s => s.Op is RuleOperation.SuppressRule sup
                            && sup.RuleName == t.Origin.Definition.Name)
                        .Select(s => s.Origin.RequestedName).Distinct());
                    TraceLine($"{t.Bearer.Name}'s {t.Origin.RequestedName} " +
                        $"{t.Op.GetType().Name} suppressed by {suppressors}");
                }
                continue;
            }

            if (log) Log(t);
            result.Add(t);
        }

        // #101 consume-on-occurrence: spend each one-shot grant whose hook+seat fired this event, removing
        // its token DIRECTLY (not via a returned op). This is robust — the hit/save/melee pipeline runs
        // sinks, not OperationApplier, so a returned consume op would be silently dropped exactly where
        // most combat buffs apply.
        if (grantsToConsume != null)
        {
            SpendGrants(grantsToConsume);
        }

        return result;
    }

    /// <summary>
    /// Explicitly spends the one-shot (FirstTrigger) granted rules that key on <paramref name="context"/>'s
    /// hook + the given seats, without applying or logging any operations. The movement flow uses this
    /// (#153): declaration-time budget projection and per-frame validation queries are read-only, so a
    /// granted movement/terrain rule stays visible for the WHOLE move and is spent exactly once here, when
    /// the move resolves. Mirrors the live-path consume-on-occurrence pass — conditions are ignored; having
    /// an entry at the hook+seat is the "next time it would apply" occurrence.
    /// </summary>
    public void ConsumeOneShotGrants(IHookContext context, params (IUnit Unit, ERuleSeat Seat)[] participants)
    {
        var grantsToConsume = new List<(IUnit Unit, Token Grant)>();
        var tagged = new List<TaggedOperation>();
        var seen = new DedupState();
        foreach ((IUnit unit, ERuleSeat seat) in participants)
        {
            // trace: false — this walk only exists to find spendable grants; the live evaluation that
            // preceded it already narrated the hook.
            CollectTagged(unit, seat, weapon: null, models: null, EModelRuleScope.AnyOwner, context, tagged,
                seen, grantsToConsume, trace: false);
        }

        SpendGrants(grantsToConsume);
    }

    // Deduped per (bearer, payload) so one occurrence spends one charge even when a grant fired through
    // several entries.
    private void SpendGrants(List<(IUnit Unit, Token Grant)> grantsToConsume)
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
        EModelRuleScope modelScope, IHookContext context, List<TaggedOperation> sink, DedupState seen,
        List<(IUnit Unit, Token Grant)>? grantsToConsume, bool trace)
    {
        CollectFromRules(unit.RuleDefinitions, unit, carryingWeapon: null, seat, context, sink, seen, trace);

        // Token-granted rules (auras, "gains rule X" buffs): the unit behaves as if it has each rule
        // named by a RuleGrant token. Walked through the same per-rule path as static attachments, so
        // they honour the firing seat/condition and share the dedup (an argument-less rule granted on
        // top of a static copy fires once) and suppression first-pass. Bearer stays the unit, never a
        // weapon — grants live on unit token containers.
        CollectGrantedRules(unit, seat, context, sink, seen, grantsToConsume, trace);

        if (weapon != null)
        {
            CollectFromRules(weapon.RuleDefinitions, unit, weapon, seat, context, sink, seen, trace);
        }

        // #006 slice F / #093: per-model rules for the model(s) this hook involves, composed per the
        // participant's EModelRuleScope. AnyOwner unions each model's rules (any involved model brings it —
        // a joined Caster's token grant). AllOwners fires only rules EVERY listed model shares, so a pooled
        // combat batch's per-model rule applies only when all its living owners carry it (no leak onto the
        // batch's shared dice; a single-owner list is the joined-hero case, unchanged). Bearer stays the
        // unit either way, so dedup + logging are unchanged.
        if (models != null && models.Count > 0)
        {
            if (modelScope == EModelRuleScope.AllOwners)
            {
                CollectFromRules(RulesSharedByAll(models), unit, carryingWeapon: null, seat, context, sink,
                    seen, trace);
            }
            else
            {
                foreach (IModel model in models)
                {
                    CollectFromRules(model.RuleDefinitions, unit, carryingWeapon: null, seat, context, sink,
                        seen, trace);
                }
            }
        }
    }

    /// <summary>
    /// The rules carried by EVERY model in <paramref name="models"/> — matched by definition and arguments
    /// (an (X) rule matches only at the same X) — one representative per distinct rule. The intersection
    /// backing <see cref="EModelRuleScope.AllOwners"/> dispatch: a pooled batch's per-model rule fires only
    /// when all its living owners share it. A single-model list returns that model's rules (the joined-hero
    /// sole-owner case, unchanged from slice F). Caller guarantees a non-empty list.
    /// </summary>
    private static IReadOnlyList<ResolvedRule> RulesSharedByAll(IReadOnlyList<IModel> models)
    {
        var shared = new List<ResolvedRule>();
        foreach (ResolvedRule candidate in models[0].RuleDefinitions)
        {
            if (shared.Any(existing => RulesMatch(existing, candidate)))
            {
                continue; // a duplicate of a rule already recorded from an earlier entry on the first model
            }

            bool onEveryModel = true;
            for (int i = 1; i < models.Count; i++)
            {
                if (!models[i].RuleDefinitions.Any(other => RulesMatch(other, candidate)))
                {
                    onEveryModel = false;
                    break;
                }
            }

            if (onEveryModel)
            {
                shared.Add(candidate);
            }
        }

        return shared;
    }

    /// <summary> Two attachments are the same rule when they share a definition and identical arguments
    /// (<see cref="RuleArgument"/> is a value record), so Deadly(3) matches Deadly(3) but not Deadly(2). </summary>
    private static bool RulesMatch(ResolvedRule a, ResolvedRule b) =>
        a.Definition == b.Definition && a.Arguments.SequenceEqual(b.Arguments);

    private void CollectFromRules(IReadOnlyList<ResolvedRule> rules, IUnit unit, IWeapon? carryingWeapon,
        ERuleSeat seat, IHookContext context, List<TaggedOperation> sink, DedupState seen, bool trace)
    {
        foreach (ResolvedRule rule in rules)
        {
            // #163 — narrate only rules that actually listen at this hook+seat; walking every rule
            // past every hook would drown the trace in non-events.
            bool traceThisRule = trace && rule.Definition.Passive
                .Any(e => e.HookID == context.Hook && e.Seat == seat);

            if (!seen.ShouldFire(unit, rule))
            {
                if (traceThisRule)
                {
                    TraceLine($"{unit.Name}'s {rule.RequestedName}: duplicate instance skipped " +
                        "(multiple instances of the same rule do not stack)");
                }
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
                    if (traceThisRule)
                    {
                        TraceLine($"{unit.Name}'s {rule.RequestedName} at {context.Hook}/{seat}: " +
                            $"condition {DescribeCondition(entry.Condition)} not met");
                    }
                    continue;
                }

                var produced = new List<RuleOperation>();
                entry.Effect.Apply(invocation, produced);

                if (traceThisRule)
                {
                    TraceLine($"{unit.Name}'s {rule.RequestedName} at {context.Hook}/{seat}: " +
                        (produced.Count == 0
                            ? $"condition passed but {entry.Effect.GetType().Name} produced no operations"
                            : $"fired -> {string.Join(", ", produced.Select(op => op.GetType().Name))}"));
                }

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
        List<(IUnit Unit, Token Grant)>? grantsToConsume, bool trace)
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
                RuleDiagnostics.WarnOnce($"grant:{grant.RuleName}",
                    $"Granted rule '{grant.RuleName}' on {unit.Name} has no definition in the registry - " +
                    "the grant does nothing.");
                continue;
            }

            // A granted rule arrives with no arguments (RuleGrant payloads have no argument slot), so an
            // argumented (X) rule would throw in ValueSource.Arg.Resolve mid-dispatch. Screen it out
            // here, mirroring army-load's arity gate in ArmyListRuleResolution.ResolveForScope.
            if (RuleArgumentArity.MaxReferencedArgIndex(resolved.Definition) >= 0)
            {
                RuleDiagnostics.WarnOnce($"grant-arity:{grant.RuleName}",
                    $"Granted rule '{grant.RuleName}' on {unit.Name} reads arguments, but grants carry " +
                    "none - skipped.");
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
            CollectFromRules(granted, unit, carryingWeapon: null, seat, context, sink, seen, trace);
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

    // #163 — dispatch tracing. Lines ride the Debug log channel (hidden in the GUI unless the console's
    // Debug toggle is on; printed as normal [LOG] lines headless), gated by the process-wide
    // RuleTrace.Enabled switch the app sets via --trace-rules or the GUI Debug toggle.
    private bool TraceEnabled => RuleTrace.Enabled && _log != null;

    private void TraceLine(string message) => _log?.LogDebug($"trace: {message}");

    private static string DescribeCondition(Condition condition) => condition switch
    {
        Condition.And and => $"And({DescribeCondition(and.Left)}, {DescribeCondition(and.Right)})",
        Condition.Or or => $"Or({DescribeCondition(or.Left)}, {DescribeCondition(or.Right)})",
        Condition.Not not => $"Not({DescribeCondition(not.Inner)})",
        _ => condition.GetType().Name,
    };

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

        // The acting unit's own rules, plus each LIVING model's per-model rules (#093): a joined hero's
        // activated ability (Vanguard, Martial Prowess) is offered for the host unit even though the merge
        // relocated it onto the hero model. AnyOwner — any model bringing an ability offers it; the `seen`
        // set keys each distinct ability (definition + ability) so an ability carried by both the unit and
        // a model (or by several models) is offered once, not once per carrier.
        var seen = new HashSet<(SpecialRuleDefinition, ActivatedAbility)>();
        GatherOffersFromRules(unit.RuleDefinitions, unit, context, offers, seen);
        foreach (IModel model in unit.Models)
        {
            if (model.GetIsAlive())
            {
                GatherOffersFromRules(model.RuleDefinitions, unit, context, offers, seen);
            }
        }

        // #197 P5a: a GRANTED rule's abilities are offered too, mirroring CollectGrantedRules on the passive
        // side. Without this an aura that confers an ability-only rule (Versatile Reach Aura -> Versatile
        // Reach) grants a token nothing ever reads — the Breath Attack failure mode, one level up. No shipped
        // aura granted an ability-bearing rule before now, which is why the asymmetry went unnoticed.
        GatherOffersFromRules(GrantedRules(unit), unit, context, offers, seen);

        return offers;
    }

    /// <summary>
    /// The unit's <see cref="TokenType.RuleGrant"/> tokens resolved back to definitions, with the same
    /// screening <see cref="CollectGrantedRules"/> applies: no resolver means no granted rules, an unknown
    /// name is skipped with a warning, and an argumented (X) rule is skipped because a grant payload carries
    /// no arguments to feed it. Never consumes a one-shot grant — offering an ability is not firing it.
    /// </summary>
    private IReadOnlyList<ResolvedRule> GrantedRules(IUnit unit)
    {
        if (_ruleResolver == null)
        {
            return Array.Empty<ResolvedRule>();
        }

        var granted = new List<ResolvedRule>();
        foreach (Token token in unit.Tokens.GetAllTokens(TokenType.RuleGrant))
        {
            if (token.Payload is not TokenPayload.RuleGrant grant)
            {
                continue;
            }

            if (!_ruleResolver.TryResolve(grant.RuleName, out ResolvedRule resolved))
            {
                RuleDiagnostics.WarnOnce($"grant:{grant.RuleName}",
                    $"Granted rule '{grant.RuleName}' on {unit.Name} has no definition in the registry - " +
                    "the grant does nothing.");
                continue;
            }

            if (RuleArgumentArity.MaxReferencedArgIndex(resolved.Definition) >= 0)
            {
                RuleDiagnostics.WarnOnce($"grant-arity:{grant.RuleName}",
                    $"Granted rule '{grant.RuleName}' on {unit.Name} reads arguments, but grants carry " +
                    "none - skipped.");
                continue;
            }

            granted.Add(resolved);
        }

        return granted;
    }

    private void GatherOffersFromRules(IReadOnlyList<ResolvedRule> rules, IUnit unit, IHookContext context,
        List<AbilityOffer> offers, HashSet<(SpecialRuleDefinition, ActivatedAbility)> seen)
    {
        foreach (ResolvedRule rule in rules)
        {
            // Definition is what makes a self-referential condition work (Condition.AllModelsHaveThisRule
            // asks "does every model have the rule that is firing?"). Without it that condition takes its
            // "no rule identity to check" arm and returns true, so an ability gated on it was silently
            // ungated. Versatile Defense is the first rule whose text puts the all-models gate on the
            // CHOICE ("when a unit where all models have this rule is deployed or activated, pick one
            // effect") rather than on the effect, which is why nothing surfaced it before.
            var invocation = new RuleInvocation(context, unit, rule.Arguments, DiceRoller: _diceRoller,
                Definition: rule.Definition);

            foreach (ActivatedAbility ability in rule.Definition.Activated)
            {
                if (ability.TriggerHook != context.Hook)
                {
                    continue;
                }

                if (!ability.AvailableWhen.Evaluate(invocation))
                {
                    if (TraceEnabled)
                    {
                        TraceLine($"{unit.Name}'s {rule.RequestedName} ability at {context.Hook}: " +
                            $"not offered (availability {DescribeCondition(ability.AvailableWhen)} not met)");
                    }
                    continue;
                }

                if (!IsAffordable(ability.Cost, unit, rule.RequestedName))
                {
                    if (TraceEnabled)
                    {
                        TraceLine($"{unit.Name}'s {rule.RequestedName} ability at {context.Hook}: " +
                            $"not offered (cannot pay {ability.Cost.GetType().Name})");
                    }
                    continue;
                }

                if (!seen.Add((rule.Definition, ability)))
                {
                    continue;
                }

                if (TraceEnabled)
                {
                    TraceLine($"{unit.Name}'s {rule.RequestedName} ability at {context.Hook}: offered");
                }

                offers.Add(new AbilityOffer(unit, rule.RequestedName, ability, rule.Arguments));
            }
        }
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
            // The offer carries the bearing rule's arguments (Crossing Attack's (1)), so an effect reading
            // ValueSource.Arg resolves against the real value rather than throwing on an empty list.
            var invocation = new RuleInvocation(
                Hook: null, offer.Bearer, offer.ResolvedArguments, target, _diceRoller);
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
