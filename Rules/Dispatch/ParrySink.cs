using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #423: the Parry dice pool one <see cref="Contexts.MeleeHitsTakenContext"/> evaluation produces - the
/// <see cref="ImpactSink"/> shape, read by <c>ParryHitsStage</c> (and by the AI's CombatMath / the Combat
/// Calculator, so their dice match the stage's). Per model, the highest X any firing named it with.
/// </summary>
public sealed class ParrySink : IParrySink
{
    private readonly Dictionary<ModelID, int> _dicePerCarrier = new();

    /// <summary> Write face (called by operations): <paramref name="carriers"/> may each roll
    /// <paramref name="dicePerModel"/> Parry dice; a model already named keeps the higher value. </summary>
    public void AddDice(int dicePerModel, IReadOnlyList<ModelID> carriers)
    {
        if (dicePerModel <= 0) return;
        foreach (ModelID carrier in carriers)
        {
            if (!_dicePerCarrier.TryGetValue(carrier, out int current) || dicePerModel > current)
            {
                _dicePerCarrier[carrier] = dicePerModel;
            }
        }
    }

    /// <summary> Applies every Parry operation in <paramref name="operations"/> to this sink. </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IParrySink> operation in operations.OfType<SinkOperation<IParrySink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary> Read face: X summed over the carriers - the whole budget for one enemy melee attack (#423 B1). </summary>
    public int TotalDice => _dicePerCarrier.Values.Sum();

    /// <summary> Read face: how many living models carry a Parry rule. </summary>
    public int Carriers => _dicePerCarrier.Count;

    /// <summary>
    /// The X every carrier shares, for "5 models x Parry(1)" presentation; null when the carriers' ratings
    /// differ (a mixed unit - none in 3.6.0 - is described by its total instead) or nobody carries the rule.
    /// </summary>
    public int? UniformDicePerModel
    {
        get
        {
            if (_dicePerCarrier.Count == 0) return null;
            int first = _dicePerCarrier.Values.First();
            return _dicePerCarrier.Values.All(x => x == first) ? first : null;
        }
    }
}
