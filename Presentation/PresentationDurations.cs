using System;

namespace FDG.Presentation
{
    /// <summary>
    /// Engine-owned nominal beat durations (before clock scaling). Pacing is a domain concern, so
    /// these live in the engine; a front-end only scales them via the <see cref="IPresentationClock"/>.
    /// </summary>
    public static class PresentationDurations
    {
        /// <summary>Fallback move duration (a ~6" Advance); see <see cref="ForMoveDistance"/>.</summary>
        public static readonly TimeSpan UnitMove   = TimeSpan.FromMilliseconds(600);
        public static readonly TimeSpan ModelDeath = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan DiceRoll   = TimeSpan.FromMilliseconds(700);
        public static readonly TimeSpan Banner      = TimeSpan.FromMilliseconds(1300);
        public static readonly TimeSpan Projectiles = TimeSpan.FromMilliseconds(450);
        public static readonly TimeSpan MeleeClash  = TimeSpan.FromMilliseconds(400);

        // Movement plays at a roughly constant speed, so a longer move takes longer (a 6" Advance
        // ≈ 600ms, a 12" Rush ≈ 1200ms), clamped so tiny moves aren't instant and big ones don't
        // drag. Speed could later vary by action type (Advance vs Rush vs Charge) — pass a
        // per-action inches/second here when that's wanted.
        public const float MoveInchesPerSecond = 10f;
        public static readonly TimeSpan MoveMin = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan MoveMax = TimeSpan.FromMilliseconds(1500);

        /// <summary>Move-beat duration for a path of <paramref name="distanceInches"/> total length.</summary>
        public static TimeSpan ForMoveDistance(float distanceInches)
        {
            double ms = distanceInches / MoveInchesPerSecond * 1000.0;
            ms = Math.Clamp(ms, MoveMin.TotalMilliseconds, MoveMax.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
