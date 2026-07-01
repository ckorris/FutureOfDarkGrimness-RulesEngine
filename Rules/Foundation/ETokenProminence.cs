namespace FDG.Rules.Foundation;

/// <summary>
/// How prominently a token is shown, bundling visibility and prominence into one axis (#151) so the
/// nonsensical "invisible and first-class" combination can't be expressed.
/// </summary>
public enum ETokenProminence
{
    /// <summary>The default: a small generic chip in the token row.</summary>
    Normal = 0,

    /// <summary>Drawn large in a dedicated status tier with bespoke iconography (Shaken, Fatigued, Spell Tokens).</summary>
    FirstClass = 1,

    /// <summary>
    /// Not drawn at all — engine-bookkeeping tokens (cost gates, reserve/embark markers) whose presence
    /// would only confuse a player. A dev "show all tokens" toggle may reveal these for rules debugging.
    /// </summary>
    Invisible = 2,
}
