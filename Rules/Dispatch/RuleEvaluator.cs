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

    public RuleEvaluator(IDiceRoller diceRoller)
    {
        _diceRoller = diceRoller;
    }

    /// <summary>
    /// Operations produced by <paramref name="unit"/>'s passive rules whose hook
    /// matches the firing <paramref name="context"/> and whose seat matches
    /// <paramref name="seat"/> (the role this unit is playing in the event) and
    /// whose condition passes.
    /// </summary>
    public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context)
    {
        var operations = new List<RuleOperation>();

        foreach (ResolvedRule rule in unit.RuleDefinitions)
        {
            var invocation = new RuleInvocation(context, unit, rule.Arguments, DiceRoller: _diceRoller);

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

                entry.Effect.Apply(invocation, operations);
            }
        }

        return operations;
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
