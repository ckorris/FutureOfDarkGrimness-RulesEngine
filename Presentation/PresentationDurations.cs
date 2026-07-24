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
        public static readonly TimeSpan UnitMove     = TimeSpan.FromMilliseconds(600);
        public static readonly TimeSpan ModelDeath   = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan ModelWounded = TimeSpan.FromMilliseconds(300);
        // #232 casualty cascade: the engine-paced gap between overlapped casualty beats in one volley.
        // Each death/flinch still animates its full duration front-end-side; only the LAST casualty
        // paces in full, so N kills read as a rapid-fire "BE-BE-BE-BEEW" instead of N serial deaths.
        public static readonly TimeSpan CasualtyStagger = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan Saves        = TimeSpan.FromMilliseconds(350);
        // #274 spell visuals. The cast outcome is the beat of the whole action, so it gets the most
        // room; the per-target landing follows it immediately and is kept shorter so a multi-target
        // spell doesn't drag. Assists are batched into at most two beats right before the roll.
        public static readonly TimeSpan SpellCast   = TimeSpan.FromMilliseconds(700);
        public static readonly TimeSpan SpellTarget = TimeSpan.FromMilliseconds(650);
        public static readonly TimeSpan SpellAssist = TimeSpan.FromMilliseconds(600);
        // Long enough that the result lingers after the faces lock (the front-end spends only the
        // first fraction "rolling", the rest settled), so players can read the dice — and now the
        // settled "what it means" summary line too, so a touch longer.
        public static readonly TimeSpan DiceRoll     = TimeSpan.FromMilliseconds(1800);
        public static readonly TimeSpan Banner      = TimeSpan.FromMilliseconds(1300);
        // A volley = a weapon group's weapons all firing once, simultaneously; a weapon's Attacks
        // value is how many volleys it fires, played one after another. Total time scales with the
        // volley count (not shots-per-volley, which fire together), capped so it stays prompt.
        public static readonly TimeSpan PerVolley = TimeSpan.FromMilliseconds(400);
        public static readonly TimeSpan VolleyMax = TimeSpan.FromMilliseconds(1600);

        public static TimeSpan ForVolleys(int volleyCount)
        {
            int n = Math.Max(1, volleyCount);
            double ms = Math.Min(PerVolley.TotalMilliseconds * n, VolleyMax.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(ms);
        }

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
