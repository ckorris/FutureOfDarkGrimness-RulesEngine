using System;

namespace FDG.Presentation
{
    /// <summary>
    /// One presentable event in the engine's play-by-play stream: something that happened
    /// (a unit moved, a model died, dice were rolled) together with how long it should
    /// nominally take to present. Beats are the engine's semantic, paced narration — the
    /// structured successor to <see cref="ITextOutput.Log"/>; any front-end on this engine
    /// consumes the same beats and only differs in how it renders them.
    ///
    /// <para>
    /// The engine emits <em>semantic</em> beats with a <em>total</em> nominal duration; the
    /// front-end is free to sub-divide cosmetic timing (tracers, sparks, easing) however it
    /// likes <em>within</em> that envelope. The engine never models individual sub-effects.
    /// </para>
    ///
    /// <para>
    /// Beats cross the message bus to clients, so concrete beats must be serializable with
    /// the same polymorphic (<c>TypeNameHandling.Auto</c>) treatment as other bus messages,
    /// and any payload they carry (ids, positions, dice histograms) must round-trip.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class PresentationBeat
    {
        /// <summary>
        /// Engine-owned pacing envelope for this beat, <em>before</em> clock scaling.
        /// <see cref="TimeSpan.Zero"/> means instantaneous (e.g. a pure annotation).
        /// </summary>
        public abstract TimeSpan NominalDuration { get; }

        /// <summary>
        /// A "held" beat displays and then LINGERS on screen instead of clearing when done, so later
        /// action beats can play over it (e.g. save dice that stay visible while the wounds they
        /// caused land). The presenter paces only <see cref="HoldLeadIn"/> for a held beat -- enough
        /// for the display to appear/settle -- then lets the engine proceed while the front-end keeps
        /// the display up until it is superseded or lingers out. Non-held beats (the default) block for
        /// their full <see cref="NominalDuration"/> and clear when finished.
        /// </summary>
        public virtual bool Held => false;

        /// <summary>
        /// For a <see cref="Held"/> beat, how long the presenter paces before moving on. Ignored when
        /// <see cref="Held"/> is false; defaults to the full duration.
        /// </summary>
        public virtual TimeSpan HoldLeadIn => NominalDuration;

        /// <summary>
        /// Optional plain-text rendering for the CLI / a unified feed. <c>null</c> means the
        /// beat has no text form. This does <em>not</em> replace <see cref="ITextOutput.Log"/>:
        /// explicit log calls still fire independently; logs and beats are siblings, not
        /// parent/child.
        /// </summary>
        public virtual string? Text => null;
    }
}
