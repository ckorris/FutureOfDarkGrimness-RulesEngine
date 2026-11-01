using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Morale_OnPreMoraleTest"/>: a unit is about to take a morale test,
    /// before the die is rolled. The hook the rulebook names for morale-roll modifiers (e.g. a Courage
    /// aura's +1 to morale tests); RollForMoraleStage folds any ApplyRollModifier(Morale) into the
    /// threshold. Carries the testing unit and, since #432, the attack that triggered the test.
    /// </summary>
    /// <param name="ShotFromInches">#432: the shooting distance of the attack whose wounds triggered this test
    /// (<c>ResolveRangedMoraleStage</c>; captured when the shooter first targeted the unit). Null when no shooting
    /// attack triggered it.</param>
    /// <param name="ChargedFromInches">#432: the charge distance of the charge this unit RECEIVED, when the test is
    /// the melee loser's and the loser was the charged unit (<c>RollForMoraleStage</c>). Null otherwise - a charger
    /// that loses was not charged.</param>
    /// <param name="Cause">#435: what triggered the test - shooting (the wound-driven test), melee (the loser's test)
    /// or anything else (a spell's or a rule's demand, the default). Read through <see cref="IHasMoraleTestCause"/> by
    /// OPR 3.6 Banner ("+1 to Morale Test rolls triggered by shooting/melee").</param>
    /// <remarks>Read through <see cref="IHasShootChargeDistance"/> by OPR 3.6's morale rules (Hive Bond, Hold the
    /// Line, Valiant: "+1 to Morale Test rolls when shot/charged from over 9"/6" away"). Spell, rule-driven
    /// (Mind Control) and Strafe/Crossing Strike tests carry neither, so those rules do not fire there.</remarks>
    public sealed record PreMoraleTestContext(IUnit Unit, float? ShotFromInches = null,
        float? ChargedFromInches = null, EMoraleTestCause Cause = EMoraleTestCause.Other)
        : IHookContext, IHasShootChargeDistance, IHasMoraleTestCause
    {
        public EHookID Hook => EHookID.Morale_OnPreMoraleTest;

        public float? ShootingDistanceInches => ShotFromInches;

        public float? ChargeDistanceInches => ChargedFromInches;

        public EMoraleTestCause MoraleTestCause => Cause;
    }
}
