using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Morale_OnPreMoraleTest"/>: a unit is about to take a morale test,
    /// before the die is rolled. The hook the rulebook names for morale-roll modifiers (e.g. a Courage
    /// aura's +1 to morale tests); RollForMoraleStage folds any ApplyRollModifier(Morale) into the
    /// threshold. Carries the testing unit; grow it (e.g. the test's source) when a rule reads more.
    /// </summary>
    public sealed record PreMoraleTestContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Morale_OnPreMoraleTest;
    }
}
