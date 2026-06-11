using System;
using System.Threading;
using System.Threading.Tasks;

namespace FDG.Presentation
{
    /// <summary>
    /// Zero-scale clock: every wait completes immediately. Used in headless / automated /
    /// piped play and in tests, so the state machine runs instantly and deterministically
    /// while beats are still emitted (and can be printed as text) in order.
    /// </summary>
    public class InstantPresentationClock : IPresentationClock
    {
        public float Scale => 0f;

        public Task Wait(TimeSpan nominalDuration, CancellationToken ct = default)
        {
            return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
        }
    }
}
