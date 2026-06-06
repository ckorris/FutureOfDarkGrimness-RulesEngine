namespace FDG.Rules.Foundation;

/// <summary>
/// Marker for a context capability — a small interface (IHasDistance,
/// IHasUnmodifiedHitRolls, ...) that an IHookContext opts into to expose one
/// piece of data a Condition/Effect may require. Lets HookContextCatalog and
/// RuleValidator identify capability interfaces precisely rather than by name.
/// </summary>
public interface ICapability
{
    
}