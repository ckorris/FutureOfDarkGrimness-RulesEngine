namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of an impact-dice accumulator. A charge-impact operation
/// (<see cref="RuleOperation.ChargeImpactHits"/>, Impact) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the charge-contact stage folds the
/// total dice to roll without inspecting an operation's concrete type. Multiple
/// Impact(X) sources sum. The read face (the running total) lives on the concrete sink.
/// </summary>
public interface IImpactSink
{
    void AddDice(int count);

    /// <summary>
    /// #197 misc (Heavy Impact): the AP the impact hits carry. Core Impact contributes 0; Heavy Impact
    /// contributes 1. Folded as a MAX across sources (the single impact pool cannot separate per-source AP),
    /// so a unit that somehow carried both would apply the higher AP to every impact hit - an edge case no
    /// corpus unit hits (Heavy Impact replaces Impact rather than stacking).
    /// </summary>
    void AddArmorPenetration(int armorPenetration);
}
