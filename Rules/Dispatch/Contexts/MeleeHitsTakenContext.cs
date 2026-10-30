using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Melee_OnHitsTaken"/>: the defending unit has just taken hits in melee - a
    /// swing's hit roll has resolved (RollToHitStage, with every extra-hit and per-hit-AP rule folded) or the
    /// charge's Impact hits have landed - and no save has been rolled yet. The Parry window (#423, Core Rules
    /// v3.6.0: "When this unit takes hits in melee, roll X dice per model with this rule in it, up to as many
    /// dice as hits taken. For each 6+ one hit is ignored (defender picks which)."). <see cref="Attacker"/> dealt
    /// the hits; <see cref="Defender"/> (the Subject seat) took them.
    ///
    /// Minimal, like <see cref="ChargeContactContext"/>: the per-model carrier count is read off the bearer by
    /// the effect itself (<c>Effect.ParryHits</c>). <see cref="HitsTaken"/> is informational - the cap "up to as
    /// many dice as hits taken" and the per-attack dice budget are <c>ParryHitsStage</c>'s business.
    /// </summary>
    /// <param name="HitsTaken">The hits landing on the defender (fractional under the probabilistic roller).</param>
    public sealed record MeleeHitsTakenContext(IUnit Attacker, IUnit Defender, float HitsTaken) : IHookContext
    {
        public EHookID Hook => EHookID.Melee_OnHitsTaken;
    }
}
