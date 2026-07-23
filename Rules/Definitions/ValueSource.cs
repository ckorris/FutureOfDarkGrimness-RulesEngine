using System.Text.Json.Serialization;
using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Where an effect's numeric value comes from: a fixed literal authored on the effect, one of the
/// bearing rule's arguments by index, or something read off the firing itself. Lets a single
/// value-driven effect (Deadly's multiplier, Tough's wound count, Caster's token count) be authored
/// once and resolved per-instance against the firing rule's
/// <see cref="Dispatch.ResolvedRule.Arguments"/>.
///
/// Only new value-driven effects use <see cref="ValueSource"/>; existing fixed-value
/// effects (RollModifier, AddExtraHit, MovementBonus) keep their plain ints.
///
/// <para><see cref="Resolve"/> takes the whole <see cref="RuleInvocation"/>, not just the arguments,
/// because a value may depend on live game state (<see cref="RuleCarrierCount"/> counts the bearer's
/// models). Deliberately ONE resolution entry point: an arguments-only overload would be the easy thing
/// for a future call site to reach for, and would silently return a wrong answer for any state-reading
/// variant.</para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Literal), "literal")]
[JsonDerivedType(typeof(Arg), "arg")]
[JsonDerivedType(typeof(RuleCarrierCount), "ruleCarrierCount")]
public abstract record ValueSource
{
    public abstract int Resolve(RuleInvocation invocation);

    /// <summary> A fixed value authored directly on the effect. </summary>
    public sealed record Literal(int Value) : ValueSource
    {
        public override int Resolve(RuleInvocation invocation)
        {
            return Value;
        }
    }

    /// <summary> The bearing rule's argument at <see cref="Index"/> (e.g. Deadly's X is <c>Arg(0)</c>). </summary>
    public sealed record Arg(int Index) : ValueSource
    {
        public override int Resolve(RuleInvocation invocation)
        {
            IReadOnlyList<RuleArgument> arguments = invocation.Arguments;

            // Descriptive guard rather than a raw IndexOutOfRangeException: the usual way to get here
            // is granting an argumented (X) rule by name (AddRule/Aura/MarkTarget payloads carry no
            // argument slot), which the grant path is supposed to have screened out already.
            if (Index >= arguments.Count)
            {
                throw new InvalidOperationException(
                    $"Arg({Index}) is out of range: only {arguments.Count} argument(s) supplied. " +
                    "Argumented (X) rules cannot be granted by name.");
            }

            return arguments[Index] is RuleArgument.Int i
                ? i.Value
                : throw new InvalidOperationException($"Arg({Index}) expected an Int argument.");
        }
    }

    /// <summary>
    /// How many LIVING models of the bearer's unit carry the rule that is firing — Caster Group's
    /// "Caster(X), where X is the total number of models with this rule in this unit". Self-referential in
    /// the same way <see cref="Condition.AllModelsHaveThisRule"/> is: it reads the firing rule's own
    /// identity, so one value source serves every count-your-carriers rule.
    ///
    /// <para>Counts a model as a carrier if it holds the rule itself OR the unit does (a unit-scoped rule
    /// covers every model), and counts unit-held GRANTS for every model, matching
    /// <see cref="Condition.AllModelsHaveThisRule"/>'s accounting. Living only: the count is read at each
    /// firing, so a unit that has taken casualties funds a smaller pool next round, which is what
    /// "the total number of models with this rule" means once models start dying.</para>
    /// </summary>
    public sealed record RuleCarrierCount : ValueSource
    {
        public override int Resolve(RuleInvocation invocation)
        {
            SpecialRuleDefinition? definition = invocation.Definition;
            if (definition == null)
            {
                // No rule identity to count against. Not reached on the passive path (which sets it);
                // 0 rather than a throw so a mis-authored grant is inert instead of fatal mid-round.
                return 0;
            }

            IUnit unit = invocation.Bearer;
            bool unitWide = unit.RuleDefinitions.Any(rule => rule.Definition == definition)
                || RuleGrantQueries.UnitHasGrantedRule(unit, definition);

            return unit.Models.Count(model => model.GetIsAlive()
                && (unitWide || model.RuleDefinitions.Any(rule => rule.Definition == definition)));
        }
    }
}
