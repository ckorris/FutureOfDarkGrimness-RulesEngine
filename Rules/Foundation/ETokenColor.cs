namespace FDG.Rules.Foundation;

/// <summary>
/// Named color slots for an authored token-color override (#151). Enum-valued, NOT RGB: the engine stores
/// authored intent ("spell tokens are blue") while the app maps each slot to actual pixels. A token with
/// no override derives its color from a hash of its display id within its valence band.
/// </summary>
public enum ETokenColor
{
    Red,
    Orange,
    Yellow,
    Pink,
    Green,
    Blue,
    Purple,
    Teal,
    Gray,
}
