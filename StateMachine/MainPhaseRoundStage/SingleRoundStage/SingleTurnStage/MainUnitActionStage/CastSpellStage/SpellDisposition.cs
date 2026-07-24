using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #274 — is a spell trying to HELP its targets or HURT them? Presentation-only: it picks which
    /// variant of the per-target landing effect the front-end plays (boon or bane). Nothing in the
    /// rules reads it, so a wrong answer costs a mismatched colour, never legality.
    ///
    /// <para>
    /// Affinity decides it, because that is what the spell author already had to state and it is
    /// right for the whole corpus: a spell that may only be cast at friendlies is helping them, one
    /// that may only be cast at enemies is hurting them — including the very common "enemy unit gets
    /// a bad rule", which effect-kind alone would misread as a buff.
    /// </para>
    ///
    /// <para>
    /// <see cref="ETargetAffinity.Any"/> is the only case affinity cannot answer, so it falls through
    /// to the effect: the three effects that are unambiguously an attack on the target regardless of
    /// who is holding it (damage, a morale test with a penalty attached, fatigue) read as harmful and
    /// everything else reads as beneficial. Add to that list when a new hostile effect joins the
    /// vocabulary AND an Any-affinity spell in the corpus uses it.
    /// </para>
    /// </summary>
    internal static class SpellDisposition
    {
        /// <summary>True when the spell is meant to benefit its targets; false when it is meant to harm them.</summary>
        internal static bool IsBeneficial(RuntimeSpell spell) =>
            IsBeneficial(spell.Target.TargetAffinity, spell.Effect);

        internal static bool IsBeneficial(ETargetAffinity affinity, Effect effect) => affinity switch
        {
            ETargetAffinity.Friend => true,
            ETargetAffinity.Self   => true,
            ETargetAffinity.Foe    => false,
            // Any: affinity says nothing, so read the effect.
            _ => effect switch
            {
                Effect.DealHits       => false,
                Effect.DealAutoWounds => false,
                Effect.MoraleTestThen => false,
                Effect.ApplyFatigue   => false,
                _                     => true,
            },
        };
    }
}
