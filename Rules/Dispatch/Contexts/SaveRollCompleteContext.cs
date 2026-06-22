using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnSaveRollComplete"/>: after defense
    /// rolls, before applying wounds. Carries the unmodified save rolls as an
    /// <see cref="IDiceResults"/> histogram (so rules reacting to natural results,
    /// e.g. Bane re-rolling unmodified Defense 6s, read <c>UnmodifiedSaveRolls.At(6)</c>
    /// and stay correct under the probabilistic roller). Attacker is the source of
    /// the attack; Defender holds the saves.
    /// </summary>
    public sealed record SaveRollCompleteContext(
        IUnit Attacker, IUnit Defender, IDiceResults UnmodifiedSaveRolls) : IHookContext, IHasTarget
    {
        public EHookID Hook => EHookID.Shooting_OnSaveRollComplete;

        // The defender IS the target for target-keyed conditions (TargetMajorityHasTough etc.).
        IUnit IHasTarget.Target => Defender;
    }
}
