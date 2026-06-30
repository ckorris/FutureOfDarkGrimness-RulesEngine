namespace FDG.Rules.Foundation;

/// <summary>
/// Named shape slots for an authored token-shape override (#151). Like <see cref="ETokenColor"/>, the
/// engine stores intent and the app draws it; an un-overridden token derives its shape from a hash of its
/// display id. Shape is valence-independent.
/// </summary>
public enum ETokenShape
{
    Circle,
    Square,
    Triangle,
    Diamond,
    Pentagon,
    Hexagon,
    Star,
    Cross,
}
