namespace FDG.Rules.Definitions;

/// <summary>
/// Discriminator for which numeric stat a <see cref="Condition.StatGreaterOrEqualTo"/>
/// (or similar stat-querying condition) is reading against. Lifts the choice of
/// stat out of the rule code into the data record so a single comparison subtype
/// can serve all three stats.
/// </summary>
public enum EStatKind
{
    /// <summary>
    /// The unit/model's Quality value (the target score for hit and morale rolls).
    /// </summary>
    Quality = 1,

    /// <summary>
    /// The unit/model's Defense value (the target score for blocking hits).
    /// </summary>
    Defense = 2,

    /// <summary>
    /// The model's Tough value — number of wounds it can take before being killed.
    /// Modelled here as a stat (even though it's also a special rule internally)
    /// because rules genuinely want to compare against it as a number, e.g.
    /// "most models in target have Tough &gt;= 3."
    /// </summary>
    Tough = 3,
}
