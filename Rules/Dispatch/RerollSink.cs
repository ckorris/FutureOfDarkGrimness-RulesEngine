using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds reroll operations into flags the stage acts on. Bane / Mischievous / Scrapper re-roll the
/// defender's unmodified maximum (6) save dice; their Boost variants widen that to 5-6; Fearless
/// re-rolls a failed morale test.
///
/// <para>Save rerolls compose by MINIMUM threshold, not by sum: a weapon carrying both Mischievous
/// (unmodified 6) and Mischievous Boost (unmodified 5-6) re-rolls at 5+, which is what the wider of the
/// two rules already says. That is why the Boosts are authored as the full band the corpus states rather
/// than as an increment - the opposite of the additive sinks, where a Boost must be the increment or it
/// double-counts with its base (#196/#197).</para>
/// </summary>
public sealed class RerollSink : IRerollSink
{
    private int? _rerollSavesAtOrAbove;
    private bool _rerollMoraleOnFailure;

    /// <summary>
    /// Write face (called by operations): records a reroll request, flagging the cases the stages know
    /// how to execute (save-on-unmodified-6 for Bane, morale-on-failure for Fearless).
    /// </summary>
    public void RequestReroll(ERollKind roll, RerollCondition condition)
    {
        if (roll == ERollKind.Save && condition is RerollCondition.OnUnmodifiedValue unmodified)
        {
            _rerollSavesAtOrAbove = _rerollSavesAtOrAbove is int current
                ? System.Math.Min(current, unmodified.MinValue)
                : unmodified.MinValue;
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

    /// <summary>
    /// Read face: the lowest unmodified save face that must be re-rolled, or null if no save reroll was
    /// requested. 6 for Bane / Mischievous / Scrapper, 5 once a Boost widens the band.
    /// </summary>
    public int? RerollSavesAtOrAbove => _rerollSavesAtOrAbove;

    /// <summary> Read face: whether a failed morale test gets a fresh-die second chance (Fearless). </summary>
    public bool RerollMoraleOnFailure => _rerollMoraleOnFailure;
}
