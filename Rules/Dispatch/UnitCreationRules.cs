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

        // Auras (and any other creation-time grants): an Effect.Aura at Lifecycle_OnUnitCreated emits a
        // GrantTokenToUnit; apply it here so the granted rule projects unit-wide for the rest of the game
        // (read back by RuleEvaluator.CollectGrantedRules). ApplyTokenOperations only touches token ops,
        // so the SetMaxWounds ops the MaxWoundsSink folds below are untouched — applying both over the one
        // queue is safe. Without this the grant op was produced and dropped, leaving auras inert in-game.
        OperationApplier.ApplyTokenOperations(operations);

        MaxWoundsSink maxWounds = new MaxWoundsSink();
        maxWounds.ApplyFrom(operations);

        // #006: a joined hero keeps its OWN max wounds (its Tough), not the host unit's. The hero's
        // standalone unit is never registered, so the creation-rules pass never runs for it directly —
        // its wound count rides on the host's HeroAttachment and is applied to the hero model here.
        HeroAttachment? hero = (unit as UnitData)?.HeroAttachment;

        if (!maxWounds.HasMax && hero == null)
        {
            return;
        }

        foreach (IModel model in unit.Models)
        {
            if (hero != null && model.ID == hero.HeroModelId)
            {
                model.SetMaxWounds(hero.HeroWounds);
                continue;
            }

            if (maxWounds.HasMax)
            {
                model.SetMaxWounds(maxWounds.MaxWounds);
            }
        }
    }
}
