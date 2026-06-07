using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Applies creation-time #042 rules to a freshly built unit. The lifecycle counterpart of the
/// stage integrations: instead of a stage firing a "when", army-load calls this once per unit after
/// its rules are attached. Currently drives Tough (sets each model's max wounds), but any future
/// <see cref="EHookID.Lifecycle_OnUnitCreated"/> rule folds in here the same way.
/// </summary>
public static class UnitCreationRules
{
    /// <summary>
    /// Fires <see cref="EHookID.Lifecycle_OnUnitCreated"/> for <paramref name="unit"/>, folds the
    /// max-wounds sink, and (if any Tough-style rule fired) sets every model's max wounds. A no-op
    /// for units with no creation rules.
    /// </summary>
    public static void Apply(IUnit unit, RuleEvaluator evaluator)
    {
        IReadOnlyList<RuleOperation> operations = evaluator.EvaluateAll(
            new UnitCreatedContext(unit), (unit, ERuleSeat.Actor));

        MaxWoundsSink maxWounds = new MaxWoundsSink();
        maxWounds.ApplyFrom(operations);
        if (!maxWounds.HasMax)
        {
            return;
        }

        foreach (IModel model in unit.Models)
        {
            model.SetMaxWounds(maxWounds.MaxWounds);
        }
    }
}
