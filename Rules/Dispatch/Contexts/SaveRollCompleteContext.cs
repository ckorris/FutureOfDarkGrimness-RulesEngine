using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnSaveRollComplete"/>: after defense
    /// rolls, before applying wounds. Carries the unmodified save-roll values so
    /// rules that react to natural results (Bane re-rolls unmodified Defense 6s)
    /// can decide. Attacker is the source of the attack; Defender holds the saves.
    /// </summary>
    public sealed record SaveRollCompleteContext(
        IUnit Attacker, IUnit Defender, IReadOnlyList<int> UnmodifiedSaveRolls) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnSaveRollComplete;
    }
}
