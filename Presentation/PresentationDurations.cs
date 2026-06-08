using System;

namespace FDG.Presentation
{
    /// <summary>
    /// Engine-owned nominal beat durations (before clock scaling). Pacing is a domain concern, so
    /// these live in the engine; a front-end only scales them via the <see cref="IPresentationClock"/>.
    /// Constants for now (work item 052); a per-beat pacing profile can come later.
    /// </summary>
    public static class PresentationDurations
    {
        public static readonly TimeSpan UnitMove   = TimeSpan.FromMilliseconds(600);
        public static readonly TimeSpan ModelDeath = TimeSpan.FromMilliseconds(500);
        public static readonly TimeSpan DiceRoll   = TimeSpan.FromMilliseconds(700);
        public static readonly TimeSpan Banner     = TimeSpan.FromMilliseconds(1300);
    }
}
