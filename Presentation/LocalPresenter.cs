using System.Threading;
using System.Threading.Tasks;

namespace FDG.Presentation
{
    /// <summary>
    /// In-process presenter for single-machine play: hands each beat to a local
    /// <see cref="IPresentationSink"/>, then awaits its scaled duration on the clock. The
    /// networked host equivalent (fan-out to every player over the message bus) is a separate
    /// implementation; both share this emit-then-pace shape.
    /// </summary>
    public class LocalPresenter : IPresenter
    {
        private readonly IPresentationSink? _sink;
        private readonly IPresentationClock _clock;

        /// <param name="sink">
        /// Local consumer. May be <c>null</c> (e.g. fully headless with no text echo), in which
        /// case beats are still paced but not rendered.
        /// </param>
        /// <param name="clock">Pacing clock. Use <see cref="InstantPresentationClock"/> for headless/tests.</param>
        public LocalPresenter(IPresentationSink? sink, IPresentationClock clock)
        {
            _sink = sink;
            _clock = clock;
        }

        public Task Present(PresentationBeat beat, CancellationToken ct = default)
        {
            _sink?.OnBeat(beat);
            return _clock.Wait(beat.NominalDuration, ct);
        }
    }
}
