using FDG.Data;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #420 - the one place a 3.6 Dangerous terrain test is rolled and its wounds worked out. Core Rules
/// v3.6.0: "Units with models moving across Dangerous terrain, or that activate in it, must roll as many
/// dice as the number of wounds it would take to fully destroy them, and for each roll of 1 the unit takes
/// one wound." The movement crossing, the forced Dangerous Terrain Debuff and a destroyed transport's
/// spillout ("must take a Dangerous terrain test") all come through <see cref="Roll"/>; they differ only in
/// WHEN the test is owed, never in how it is rolled.
/// <para>
/// ROLL ONLY: the wounds come back pending, not applied, because a batched test must let its dice be read
/// and each casualty animate as it falls (<c>CasualtyPresentation</c>, #299). The caller lands them.
/// </para>
/// <para>
/// The dice are the unit's <see cref="IUnitExtensions.GetWoundsToDestroy"/> - one batched roll, so the
/// probabilistic roller yields the expected number of 1s rather than N decisive dice.
/// </para>
/// </summary>
public static class DangerousTerrainBatch
{
    /// <summary>One model's share of the wounds a test owes, computed but NOT applied.</summary>
    public readonly record struct ModelWound(IModel Model, float Wounds);

    /// <summary>The rolled test: the single roll, its size, the wounds it owes in total (fractional under
    /// the probabilistic roller) and where they land, still pending.</summary>
    public readonly struct Outcome
    {
        public readonly IDiceResults? Roll;
        /// <summary>The dice rolled: the unit's wounds to destroy at the moment of the test.</summary>
        public readonly int DiceCount;
        public readonly float Wounds;
        public readonly IReadOnlyList<ModelWound> PendingWounds;

        public Outcome(IDiceResults? roll, int diceCount, float wounds, IReadOnlyList<ModelWound> pendingWounds)
        {
            Roll = roll; DiceCount = diceCount; Wounds = wounds; PendingWounds = pendingWounds;
        }

        public static Outcome None => new Outcome(null, 0, 0f, Array.Empty<ModelWound>());
    }

    /// <summary>
    /// Roll <paramref name="unit"/>'s Dangerous terrain test: one d6 per wound it would take to fully
    /// destroy it, each 1 one wound to the UNIT. A unit with nothing left to destroy rolls nothing
    /// (<see cref="Outcome.None"/>).
    /// <para>
    /// Realistic: a whole number of 1s came up, and those wounds are assigned the normal way, run to
    /// completion with no player choice (<see cref="AssignWoundsResults.AutoAssign"/>: an already-wounded
    /// model first, a joined hero last). The wound is the UNIT's, not "that model's" - a die does not belong
    /// to any model any more, so a unit of three Tough(3) models rolls nine and its first 1s fill the first
    /// model before the second is touched.
    /// </para>
    /// <para>
    /// Probabilistic: the roll is the expected-value distribution, so the wounds are fractional and spread
    /// across the living models in proportion to the share of the unit's wounds each one holds (the
    /// expected-value analog of every wound-slot carrying its own 1-in-6 chance; for a unit of 1-wound
    /// models it is exactly the old even split). The share never exceeds a model's remaining wounds, since
    /// the expected 1s are at most the dice rolled and the dice never exceed the unit's remaining wounds.
    /// </para>
    /// </summary>
    public static Outcome Roll(IUnit unit, IDiceRoller diceRoller, ERandomnessType randomness)
    {
        int dice = unit.GetWoundsToDestroy();
        if (dice <= 0) return Outcome.None;

        IDiceResults roll = diceRoller.Roll(6, dice);
        float ones = roll.At(1); // 1s are wounds (fractional under the probabilistic roller)

        List<ModelWound> pending = new List<ModelWound>();
        float woundsRolled;
        if (randomness == ERandomnessType.Probabilistic)
        {
            float totalRemaining = 0f;
            foreach (IModel model in unit.Models)
                if (model.GetIsAlive()) totalRemaining += Math.Max(0f, model.TotalWounds - model.WoundsDealt);

            if (ones > 0f && totalRemaining > 0f)
            {
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    float remaining = Math.Max(0f, model.TotalWounds - model.WoundsDealt);
                    float share = ones * remaining / totalRemaining;
                    if (share > 0f) pending.Add(new ModelWound(model, share));
                }
            }
            woundsRolled = ones;
        }
        else
        {
            int wounds = (int)MathF.Round(ones);
            if (wounds > 0)
            {
                // Every live IUnit is a UnitData (the model-binding list the assignment fills lives there).
                UnitData data = unit as UnitData
                    ?? throw new InvalidOperationException(
                        $"Dangerous terrain wounds need a live UnitData, got {unit.GetType().Name}.");
                foreach (PendingWounds entry in AssignWoundsResults.AutoAssign(data, wounds))
                    pending.Add(new ModelWound(entry.Model.GetValue(), entry.Wounds));
            }
            woundsRolled = wounds;
        }

        return new Outcome(roll, dice, woundsRolled, pending);
    }
}
