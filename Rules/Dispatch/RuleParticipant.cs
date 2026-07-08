using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// One participant in a rule evaluation: a unit playing a seat (Actor/Subject), optionally contributing a
/// weapon's rules (#027) and/or specific models' per-model rules composed per <see cref="EModelRuleScope"/>
/// (#093/#183). A readonly struct so it costs exactly what the <c>ValueTuple</c> forms it replaces did —
/// inline in the <c>params</c> array, no heap allocation, no boxing.
/// </summary>
public readonly struct RuleParticipant
{
    public IUnit Unit { get; }
    public ERuleSeat Seat { get; }
    public IWeapon? Weapon { get; }
    public IReadOnlyList<IModel>? Models { get; }
    public EModelRuleScope ModelScope { get; }

    public RuleParticipant(IUnit unit, ERuleSeat seat, IWeapon? weapon = null,
        IReadOnlyList<IModel>? models = null, EModelRuleScope modelScope = EModelRuleScope.AnyOwner)
    {
        Unit = unit;
        Seat = seat;
        Weapon = weapon;
        Models = models;
        ModelScope = modelScope;
    }

    public static RuleParticipant Actor(IUnit unit, IWeapon? weapon = null,
        IReadOnlyList<IModel>? models = null, EModelRuleScope modelScope = EModelRuleScope.AnyOwner)
        => new(unit, ERuleSeat.Actor, weapon, models, modelScope);

    public static RuleParticipant Subject(IUnit unit, IWeapon? weapon = null,
        IReadOnlyList<IModel>? models = null, EModelRuleScope modelScope = EModelRuleScope.AnyOwner)
        => new(unit, ERuleSeat.Subject, weapon, models, modelScope);
}
