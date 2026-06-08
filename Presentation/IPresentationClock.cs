using System;
using System.Threading;
using System.Threading.Tasks;

namespace FDG.Presentation
{
    /// <summary>
    /// Maps a beat's nominal duration to real elapsed time. This is the one place the
    /// engine spends wall-clock time on presentation pacing — see <see cref="IPresenter"/>.
    ///
    /// <para>
    /// The engine self-paces against this clock; it never waits on a renderer
    /// acknowledgement, so play is free-running by construction. A GUI front-end uses a
    /// real-time clock (<see cref="RealtimePresentationClock"/>); headless / automated /
    /// test runs use a zero-scale clock (<see cref="InstantPresentationClock"/>) so the
    /// state machine runs instantly and stays deterministic.
    /// </para>
    ///
    /// <para>
    /// Crucially, only <em>emission</em> sleeps here — state computation is always instant
    /// and free of any real-time dependency, so the rules remain testable and deterministic.
    /// </para>
    /// </summary>
    public interface IPresentationClock
    {
        /// <summary>
        /// Global multiplier applied to every beat's nominal duration. <c>0</c> means
        /// instantaneous (no waiting). <c>1</c> is real time; larger values play slower
        /// (the existing "slow mode" knob maps onto this).
        /// </summary>
        float Scale { get; }

        /// <summary>
        /// Completes after <paramref name="nominalDuration"/> scaled by <see cref="Scale"/>.
        /// Cancellation unwinds the paced engine task cleanly (e.g. the game ends or the
        /// window closes mid-beat).
        /// </summary>
        Task Wait(TimeSpan nominalDuration, CancellationToken ct = default);
    }
}
