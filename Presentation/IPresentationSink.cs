namespace FDG.Presentation
{
    /// <summary>
    /// A consumer of the presentation-beat stream, implemented by each front-end (a Raylib
    /// renderer enqueues beats onto its animation timeline; a CLI prints
    /// <see cref="PresentationBeat.Text"/>; headless may no-op).
    ///
    /// <para>
    /// <see cref="OnBeat"/> is called on the engine thread — both for locally produced beats
    /// and for beats received from the host over the network — and the engine does not await
    /// it. Implementations must return promptly and marshal to their render thread using the
    /// same lock-and-handoff discipline the GUI resolvers use for their request/TCS fields.
    /// </para>
    /// </summary>
    public interface IPresentationSink
    {
        void OnBeat(PresentationBeat beat);
    }
}
