using FDG.Presentation;
using FDG.StageResolution;

namespace FDG.Replay
{
    /// <summary>
    /// Watches the host's decision chokepoint (<c>RequestMessageSender</c>). Every call is made OUTSIDE
    /// any lock the sender holds; implementations must not throw and must return quickly - the request
    /// side runs on the engine thread, the reply side on the bus / network read thread.
    /// </summary>
    public interface IRequestObserver
    {
        /// <summary>A request was built and is about to be sent. <paramref name="requestJson"/> is the
        /// exact wire body.</summary>
        void OnRequested(TaskID taskId, PlayerID playerId, string requestType, string replyType,
            string displayName, string requestJson);

        /// <summary>The reply for <paramref name="taskId"/> was claimed (exactly once per task), before the
        /// awaiting stage resumes.</summary>
        void OnReplied(TaskID taskId, string replyJson);

        /// <summary>The target's resolver answered with an error instead of a reply.</summary>
        void OnFailed(TaskID taskId, string message);
    }

    /// <summary>Watches the host's presentation fan-out (<c>PresentationRelayer</c>): every beat, before it
    /// is relayed. Same rules as <see cref="IRequestObserver"/>: no throwing, no blocking.</summary>
    public interface IBeatObserver
    {
        void OnBeat(PresentationBeat beat);
    }
}
