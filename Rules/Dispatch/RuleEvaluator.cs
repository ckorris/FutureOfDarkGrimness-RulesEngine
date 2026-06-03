using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Evaluates a single unit's passive rules against a firing hook context, from a
/// given <see cref="ERuleSeat"/>, and returns the resolved <see cref="RuleOperation"/>
/// queue. This is the spine of #042 Phase 7.
///
/// Deliberately NOT a message bus: callers (stages) already know which units are
/// involved in an event and which role each plays, so they address those units
/// directly — once as the <see cref="ERuleSeat.Actor"/>, once as the
/// <see cref="ERuleSeat.Subject"/> — and apply the returned operations themselves.
/// There is no publish/subscribe, no registration, and the caller needs the queue
/// back synchronously, which makes this a query, not a broadcast.
///
/// A class rather than static functions because it will hold injected collaborators
/// as later sub-phases need them — a dice roller for random-amount effects (7c) and
/// the rule resolver for name-based references / auras (7e). They are absent now
/// because the only wired rule (Stealth) needs neither.
///
/// Only the conditions and effects exercised so far are handled; unhandled kinds
/// throw rather than silently produce nothing, so a gap surfaces loudly.
/// </summary>
public sealed class RuleEvaluator
{
    /// <summary>
    /// Operations produced by <paramref name="unit"/>'s passive rules whose hook
    /// matches the firing <paramref name="context"/> and whose seat matches
    /// <paramref name="seat"/> (the role this unit is playing in the event) and
    /// whose condition passes.
    /// </summary>
    public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context)
    {
        var ops = new List<RuleOperation>();

        foreach (ResolvedRule rule in unit.RuleDefinitions)
        {
            foreach (HookEntry entry in rule.Definition.Passive)
            {
                if (entry.HookID != context.Hook || entry.Seat != seat)
                {
                    continue;
                }

                if (!EvaluateCondition(entry.Condition, context))
                {
                    continue;
                }

                MapEffect(entry.Effect, context, ops);
            }
        }

        return ops;
    }

    private static bool EvaluateCondition(Condition condition, IHookContext context)
    {
        //I don't love that this is one big switch statement, but not yet sure how to keep it all data otherwise.
        switch (condition)
        {
            case Condition.DistanceGreaterThan distance:
                if (context is IHasDistance withDistance)
                {
                    return withDistance.DistanceInches > distance.DistanceInches;
                }

                throw new InvalidOperationException(
                    $"{nameof(Condition.DistanceGreaterThan)} fired against a context that " +
                    $"carries no distance ({context.GetType().Name}).");

            case Condition.UnmodifiedRollEquals unmodifiedRoll:
                if (context is IHasUnmodifiedHitRolls withRolls)
                {
                    return withRolls.UnmodifiedHitRolls.At(unmodifiedRoll.DieValue) > 0;
                }
                
                throw new InvalidOperationException(
                    $"{nameof(Condition.UnmodifiedRollEquals)} fired against a context that " +
                    $"carries no unmodified hit rolls ({context.GetType().Name}).");
            
            default:
                throw new NotImplementedException(
                    $"Condition '{condition.GetType().Name}' is not yet handled by RuleEvaluator.");
        }
    }

    private static void MapEffect(Effect effect, IHookContext context, List<RuleOperation> ops)
    {
        switch (effect)
        {
            case Effect.RollModifier rollModifier:
                ops.Add(new RuleOperation.ApplyRollModifier(rollModifier.RollKind, rollModifier.Delta));
                break;

            case Effect.AddExtraHit addExtraHit:
                if (context is IHasUnmodifiedHitRolls withRolls)
                {
                    float extraHits = withRolls.UnmodifiedHitRolls.At(addExtraHit.OnRollValue) * addExtraHit.Count;
                    ops.Add(new RuleOperation.InsertExtraHits(extraHits));
                    break;
                }
                
                throw new InvalidOperationException(
                    $"{nameof(Effect.AddExtraHit)} fired against a context that " +
                    $"carries no unmodified hit rolls ({context.GetType().Name}).");

            
            default:
                throw new NotImplementedException(
                    $"Effect '{effect.GetType().Name}' is not yet handled by RuleEvaluator.");
        }
    }
}
