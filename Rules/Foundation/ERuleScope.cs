namespace FDG.Rules.Foundation;

/// <summary>
/// What kind of entity a special rule attaches to. The rulebook's own wording is
/// the classification source: "this weapon" / "weapons with this rule" ⇒
/// <see cref="Weapon"/>; "this model" / "models with this rule" ⇒ <see cref="Unit"/>.
/// Army-load enforces the scope (#027): a rule named at the wrong level is skipped
/// with a warning rather than attached where it doesn't belong.
/// </summary>
public enum ERuleScope
{
    /// <summary> Carried by a unit (or its models); applies to everything the unit does. </summary>
    Unit = 0,

    /// <summary> Carried by an individual weapon; applies only to attacks made with that weapon. </summary>
    Weapon = 1,
}
