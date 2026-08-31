using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Movement_OnMoveResolved"/>: the unit has finished a CHOSEN move and
    /// its position is committed. Lit for #381 (AoF Retreating Strike) at exactly two seams - the end of
    /// the unit's own move action (<c>RetreatingStrikeMoveStage</c>) and the end of a post-combat
    /// Harassing-family move (<c>RetreatingStrikePostCombatStage</c>, both the melee and the shoot
    /// funnel).
    ///
    /// <para>Deliberately NOT fired for forced or scripted movement - in particular the charger's
    /// mandatory 1" post-melee move-back (<c>ConsolidateStage</c>'s disengage), per the #381 owner
    /// ruling (2026-08-31): "ends its move" means a move the unit CHOOSES to make; counting the forced
    /// move-back would make a move-end strike free on every charge. Also not yet fired for wipe-out
    /// consolidation, teleport/reposition placement, or disembark - recorded deferrals on the #381
    /// ledger, extend here if a rule ever needs them.</para>
    ///
    /// <see cref="Unit"/> is the mover; an ability's target (e.g. the enemy within 3") is supplied at
    /// resolution time via its <see cref="TargetSelector"/>, so it isn't carried here.
    /// </summary>
    public sealed record MoveResolvedContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Movement_OnMoveResolved;

        public IUnit ActingUnit => Unit;
    }
}
