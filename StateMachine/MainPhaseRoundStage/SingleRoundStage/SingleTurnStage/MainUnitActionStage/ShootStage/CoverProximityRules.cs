namespace FDG.Stages
{
    /// <summary>
    /// #201 house-rule cover proximity exceptions, toggled by
    /// <see cref="GameSettings.CoverProximityExceptions"/> (lobby setting, default ON). The official
    /// rules grant +1 defense for ANY cover piece crossing the sight line; these two exceptions void
    /// pieces that don't meaningfully screen the defender. Both only demote Cover to Clear for a
    /// single shooter-to-target sight line - Blocking pieces are never affected.
    ///
    /// Kept as a cheap pure predicate over positions/bases (one segment-exit query plus at most
    /// three base-distance calls, no allocation, no GameContext) so UI instruments can sample it at
    /// hypothetical shooter positions - e.g. a future "stand here to shoot over this wall" area
    /// (see WorkItems/201). Note the verdict depends on BOTH endpoints: any such visual is per
    /// target, not a target-independent band.
    /// </summary>
    public static class CoverProximityRules
    {
        /// <summary>
        /// Rule 1: a cover piece whose sight-line exit lies closer than this to the shooter's base
        /// is hugged by the shooter (a unit lined up along sandbags) and grants nothing - unless the
        /// target's base is also within this distance of the exit (both units using the same wall:
        /// the defender legitimately keeps its cover; owner amendment 2026-07-21).
        /// </summary>
        public const float AttackerExitIgnoreInches = 2f;

        /// <summary>
        /// Rule 2: a shooter and target inside the SAME cover piece see each other plainly under
        /// this base-to-base distance (two units brawling in one forest) - the shared piece grants
        /// nothing. At or beyond it, the intervening growth is real and cover stands.
        /// </summary>
        public const float SharedCoverMinDistanceInches = 6f;

        /// <summary>
        /// True if <paramref name="piece"/>'s Cover effect is voided for this shooter-to-target
        /// sight line. Only meaningful for pieces already evaluating to Cover; never call it to
        /// second-guess Blocking.
        /// </summary>
        public static bool VoidsCover(ITerrain piece, in CoverContext ctx)
        {
            Float2 shooter = new Float2(ctx.ShooterPos.x, ctx.ShooterPos.z);
            Float2 target = new Float2(ctx.TargetPos.x, ctx.TargetPos.z);

            // Rule 2 - containment is cheaper than the exit intersection, so it goes first.
            if (piece.IsPointWithinZone(shooter) && piece.IsPointWithinZone(target))
            {
                float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                    ctx.ShooterPos, ctx.TargetPos, ctx.ShooterBase, ctx.ShooterFacing,
                    ctx.TargetBase, ctx.TargetFacing);
                if (distance < SharedCoverMinDistanceInches) return true;
            }

            // Rule 1. A null exit means the target stands inside the piece (rule 2's territory) or
            // the piece never touches the segment at all - either way rule 1 must not fire.
            Float2? exit = piece.GetLastSegmentExit(shooter, target);
            if (!exit.HasValue) return false;

            Position exitPos = new Position(exit.Value.X, exit.Value.Y);
            if (BaseShapeGeometry.SurfaceDistanceToPoint2D(
                    ctx.ShooterBase, ctx.ShooterPos, ctx.ShooterFacing, exitPos)
                >= AttackerExitIgnoreInches)
            {
                return false;
            }

            // Owner amendment: the defender hugging the far side of the same wall keeps its cover.
            return BaseShapeGeometry.SurfaceDistanceToPoint2D(
                    ctx.TargetBase, ctx.TargetPos, ctx.TargetFacing, exitPos)
                >= AttackerExitIgnoreInches;
        }
    }

    /// <summary>
    /// The endpoint-model facts <see cref="CoverProximityRules"/> needs, built once per
    /// shooter/target model pair. Positions are model centers; bases/facings give the true
    /// oriented footprints (#150).
    /// </summary>
    public readonly struct CoverContext
    {
        public readonly Position ShooterPos;
        public readonly IBaseShape ShooterBase;
        public readonly Float2 ShooterFacing;
        public readonly Position TargetPos;
        public readonly IBaseShape TargetBase;
        public readonly Float2 TargetFacing;

        public CoverContext(Position shooterPos, IBaseShape shooterBase, Float2 shooterFacing,
            Position targetPos, IBaseShape targetBase, Float2 targetFacing)
        {
            ShooterPos = shooterPos;
            ShooterBase = shooterBase;
            ShooterFacing = shooterFacing;
            TargetPos = targetPos;
            TargetBase = targetBase;
            TargetFacing = targetFacing;
        }

        /// <summary>Convenience for the common "both endpoints are models" case.</summary>
        public static CoverContext ForModels(IModel shooter, IModel target)
            => new CoverContext(shooter.Position, shooter.BaseShape, shooter.Facing,
                target.Position, target.BaseShape, target.Facing);
    }
}
