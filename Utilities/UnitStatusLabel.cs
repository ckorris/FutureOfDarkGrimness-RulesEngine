using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Utilities
{
    /// <summary>
    /// #337 — the status suffix an activation-picker row carries, in ONE place so both front ends say the
    /// same thing and the GUI can find the badge inside a finished label without re-deriving the rule.
    ///
    /// <para>A Shaken unit's activation is not a normal activation: <c>ChooseActionStage</c> sees
    /// <c>StartedActivationShaken</c>, skips the action menu entirely, and spends the whole activation
    /// recovering. Before this the picker listed it exactly like any other unit, so the only warning was a
    /// Toast banner that had already fired by the time the player looked up — and a unit standing inside
    /// the 1" forced-charge band would silently decline to charge, which reads as the proximity rule being
    /// broken rather than as the unit being Shaken.</para>
    ///
    /// <para>The suffix goes in the engine-built option LABEL (the "(in Rhino)" precedent, #315) rather
    /// than in a front-end decoration, so the CLI picker carries it for free. The GUI locates
    /// <see cref="ShakenSuffix"/> inside the finished label and re-draws that run amber and hoverable —
    /// which is why the constant lives here and is never rebuilt from parts at the call site.</para>
    /// </summary>
    public static class UnitStatusLabel
    {
        /// <summary>The badge appended to a Shaken unit's picker label. Names the state AND what activating
        /// it will do, because the second half is the part that changes the player's decision.</summary>
        public const string ShakenSuffix = "(Shaken - recovers)";

        /// <summary>The hover body behind the badge — the token catalog's own wording, so the picker, the
        /// unit status HUD and the token tooltip cannot drift.</summary>
        public static string ShakenDescription =>
            TokenDefinitionCatalog.Lookup(TokenType.Shaken).Description;

        /// <summary>
        /// <paramref name="label"/> with the status badge appended when <paramref name="unit"/> carries it.
        /// Applied last, after any transport suffix, so a Shaken passenger reads
        /// "Warriors (in Rhino) (Shaken - recovers)".
        /// </summary>
        public static string Decorate(IUnit unit, string label) =>
            unit.Tokens.HasToken(TokenType.Shaken) ? $"{label} {ShakenSuffix}" : label;
    }
}
