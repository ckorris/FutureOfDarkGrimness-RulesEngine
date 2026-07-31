using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Capability for the destruction hook, where a rule borne by the DESTROYED unit has to act on the unit
/// that killed it — #197 Vengeance ("place markers on the unit that destroyed this one"). The dead unit
/// is already evaluated at <see cref="EHookID.Shooting_OnUnitDestroyed"/> in the
/// <see cref="ERuleSeat.Subject"/> seat (<c>UnitDestructionNotifier</c> passes it alongside the killer),
/// but nothing could reach the killer from there: on the passive path
/// <see cref="RuleInvocation.EffectiveTarget"/> is always the bearer, so every token-granting effect
/// lands on the dying unit itself.
///
/// <para>Deliberately its own capability rather than reusing <see cref="IHasTarget"/>: that one means
/// "the defender this attack resolves against", and the killer is neither a defender nor a unit the
/// bearer chose. Keeping them apart also keeps <see cref="Condition.TargetHasRule"/> and friends from
/// silently becoming authorable at the destruction hook, where "target" would mean something else.</para>
///
/// Lives in Definitions rather than Foundation because it references <see cref="IUnit"/> (an engine
/// type), same rationale as <see cref="IHasActingUnit"/>; Foundation stays free of game-object
/// dependencies.
/// </summary>
public interface IHasKillerUnit : ICapability
{
    /// <summary> The unit credited with the kill. Never null — the hook only fires with an attributable
    /// killer (a rout or dangerous-terrain death goes to <see cref="EHookID.Lifecycle_OnSelfDestroyed"/>
    /// instead). </summary>
    IUnit KillerUnit { get; }
}
