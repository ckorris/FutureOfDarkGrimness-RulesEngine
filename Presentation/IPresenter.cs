using System.Threading;
using System.Threading.Tasks;

namespace FDG.Presentation
{
    /// <summary>
    /// The engine's outbound presentation channel, reached from a stage via
    /// <c>context.Presenter</c> the same way narration is reached via <c>context.TextOutput</c>.
    /// Stages emit beats inline at the point of resolution.
    /// </summary>
    public interface IPresenter
    {
        /// <summary>
        /// Emit <paramref name="beat"/> to all consumers, then await its (clock-scaled)
        /// nominal duration. This is the only place the engine self-paces. It does
        /// <em>not</em> await any renderer acknowledgement — consumers render asynchronously
        /// on their own clock, so the engine stays free-running.
        /// </summary>
        Task Present(PresentationBeat beat, CancellationToken ct = default);
    }
}
