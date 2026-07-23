namespace FDG.Rules.Foundation;

public enum ELifetime
{
    ThisAttack = 0,
    ThisActivation = 1,
    ThisRound = 2,
    NextTrigger = 3,
    Aura = 4,
    UntilEndOfGame = 5,

    /// <summary>
    /// Lasts until the bearing unit's NEXT activation starts, spanning the opponent's turns in between.
    /// Distinct from <see cref="ThisActivation"/> (which dies at the end of the activation that granted it)
    /// and from <see cref="ThisRound"/> (which dies at round end, too early or too late depending on turn
    /// order). The corpus shape is "when this unit is deployed or activated, pick one effect ... this
    /// effect lasts until the units' next activation" — a defensive buff that must still be live while the
    /// opponent shoots at it. Realized as a <c>TokenClearTrigger.CustomHook(Activation_OnActivationStart)</c>,
    /// swept by <c>ActivationStartStage</c> before it gathers that activation's offers, so the old choice is
    /// gone by the time the new one is made.
    /// </summary>
    UntilNextActivation = 6
}