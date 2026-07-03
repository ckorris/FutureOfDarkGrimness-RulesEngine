namespace FDG.Rules.Foundation;

/// <summary>
/// How a token reads for the unit it is ON — bearer-relative, not viewer-relative (#151): something the
/// bearer wants (<see cref="Positive"/>), something it doesn't (<see cref="Negative"/>), or neither
/// (<see cref="Neutral"/>). Drives the display color band (Positive = vivid cool, Negative = vivid warm,
/// Neutral = muted hue) and the on-model sort. A positive token on an enemy reads — correctly — as bad
/// for you, with no per-viewer logic, precisely because valence is a static property of the token.
/// </summary>
public enum EValence
{
    Neutral = 0,
    Positive = 1,
    Negative = 2,
}
