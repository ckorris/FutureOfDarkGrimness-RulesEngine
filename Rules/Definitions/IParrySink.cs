using System.Collections.Generic;

namespace FDG.Rules.Definitions;

/// <summary>
/// #423 Parry(X): the write face <see cref="RuleOperation.ParryDice"/> folds into. Each firing names the
/// living models carrying ITS copy of the rule and the X they roll; the sink keeps the HIGHEST X per model,
/// so a model that somehow carried two Parry weapons (no 3.6.0 unit does) rolls X once, not twice -
/// "X dice per model with this rule" is a per-model count, not a per-weapon one.
/// </summary>
public interface IParrySink
{
    void AddDice(int dicePerModel, IReadOnlyList<ModelID> carriers);
}
