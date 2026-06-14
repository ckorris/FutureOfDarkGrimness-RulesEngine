using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds reroll operations into flags the stage acts on. The corpus has two: Bane, which re-rolls the
/// defender's unmodified maximum (6) save dice; and Fearless, which re-rolls a failed morale test.
/// <see cref="RerollCondition.OnUnmodifiedValue"/> is interpreted as "the unmodified max face (6)" — the
/// only such case today; add a value field to the condition if a non-6 reroll rule ever appears.
/// </summary>
public sealed class RerollSink : IRerollSink
{
    private bool _rerollSavesOnUnmodifiedMax;
    private bool _rerollMoraleOnFailure;

    /// <summary>
    /// Write face (called by operations): records a reroll request, flagging the cases the stages know
    /// how to execute (save-on-unmodified-6 for Bane, morale-on-failure for Fearless).
    /// </summary>
    public void RequestReroll(ERollKind roll, RerollCondition condition)
    {
        if (roll == ERollKind.Save && condition is RerollCondition.OnUnmodifiedValue)
        {
            _rerollSavesOnUnmodifiedMax = true;
        }
        else if (roll == ERollKind.Morale && condition is RerollCondition.AllFailures)
        {
            _rerollMoraleOnFailure = true;
        }
    }

    /// <summary>
    /// Applies every reroll operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IRerollSink> operation in operations.OfType<SinkOperation<IRerollSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary> Read face: whether the defender's unmodified-6 save dice must be re-rolled (Bane). </summary>
    public bool RerollSavesOnUnmodifiedMax => _rerollSavesOnUnmodifiedMax;

    /// <summary> Read face: whether a failed morale test gets a fresh-die second chance (Fearless). </summary>
    public bool RerollMoraleOnFailure => _rerollMoraleOnFailure;
}
