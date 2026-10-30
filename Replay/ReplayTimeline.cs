using FDG.Data;
using FDG.StageResolution;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FDG.Replay
{
    /// <summary>One recorded decision request joined to how it ended: its reply, its error, or neither (the
    /// game ended, or the recording stopped, while it was open).</summary>
    public sealed class ReplayRequest
    {
        public RequestEvent Request { get; }

        public ReplyEvent? Reply { get; internal set; }

        public RequestErrorEvent? Error { get; internal set; }

        /// <summary>The round open when the request was made (null before round 1: setup and deployment).</summary>
        public int? Round { get; }

        /// <summary>The activation open when the request was made (null outside an activation).</summary>
        public int? Activation { get; }

        /// <summary>The unit the open activation named, once its ActivationStart repeat has named it.</summary>
        public DataReference? ActivatingUnit { get; }

        public TaskID TaskId => Request.TaskId;

        public bool IsAnswered => Reply != null;

        internal ReplayRequest(RequestEvent request, int? round, int? activation, DataReference? activatingUnit)
        {
            Request = request;
            Round = round;
            Activation = activation;
            ActivatingUnit = activatingUnit;
        }
    }

    /// <summary>
    /// One step of a replay (plan 4.4, D4): an answered decision the viewer stops on. The game state AT a step
    /// is the state the decision was asked in - before its effects; the effects (and any folded requests,
    /// beats and log lines they produced) play in the span that arrives at the NEXT step. #456's UI relies on
    /// this: selecting a step shows the question, stepping on shows the answer play out.
    /// </summary>
    public sealed class ReplayStep
    {
        public int Index { get; }

        /// <summary>The decision this step stops on (always answered).</summary>
        public ReplayRequest Decision { get; }

        /// <summary>The requests after this one, up to the next step, that the filter folded: one-option
        /// choices, failed requests, and anything still open when the recording ended. In <c>Seq</c> order.</summary>
        public IReadOnlyList<ReplayRequest> Folded { get; }

        /// <summary>Who answered: the slot's controller from the header, or null for a player the header does
        /// not list.</summary>
        public EReplayController? Controller { get; }

        /// <summary>
        /// One ASCII line saying what was decided ("P1 activates Warriors (5)"). Until a
        /// <c>ReplayPlayer</c> describes the steps (its constructor does) this is the request's
        /// <see cref="DisplayName"/>.
        /// </summary>
        public string Summary { get; internal set; }

        public long RequestSeq => Decision.Request.Seq;

        public long ReplySeq => Decision.Reply!.Seq;

        public int? Round => Decision.Round;

        public int? Activation => Decision.Activation;

        public PlayerID PlayerId => Decision.Request.PlayerId;

        /// <summary>The request's full CLR type name, as recorded.</summary>
        public string RequestType => Decision.Request.RequestType;

        public string DisplayName => Decision.Request.DisplayName;

        internal ReplayStep(int index, ReplayRequest decision, IReadOnlyList<ReplayRequest> folded, EReplayController? controller)
        {
            Index = index;
            Decision = decision;
            Folded = folded;
            Controller = controller;
            Summary = decision.Request.DisplayName;
        }
    }

    /// <summary>Decides which recorded requests are steps (plan 4.4). One class so the viewer can later offer
    /// "show folded steps" by swapping it.</summary>
    public interface IStepFilter
    {
        bool IsStep(ReplayRequest request);
    }

    /// <summary>
    /// D4's filter: every answered request is a step, human, AI or remote alike - except a choice that offered
    /// at most one option, which folds. Requests that failed or never got an answer fold too.
    /// <para>
    /// The option count is read off the recorded request JSON as a plain token tree, never by binding it to
    /// its CLR type: any request with a top-level <c>ValidOptions</c> list (<c>SelectionRequest&lt;T&gt;</c>,
    /// <c>CancellableSelectionRequest&lt;T&gt;</c>, <c>StringSelectionRequest</c>, <c>ChooseActionRequest</c> and
    /// their subclasses) counts its entries, plus one for a Back option - offered when the request says
    /// <c>AllowCancel</c> or its reply type is a <c>CancellableResult</c>. A request without such a list
    /// (yes/no, movement, wounds, placement) is always a real decision.
    /// </para>
    /// </summary>
    public sealed class DefaultStepFilter : IStepFilter
    {
        public static DefaultStepFilter Instance { get; } = new DefaultStepFilter();

        private static readonly string CancellableReplyPrefix = typeof(CancellableResult<>).FullName!;

        public bool IsStep(ReplayRequest request)
        {
            if (request.IsAnswered == false)
            {
                return false;
            }

            int? options = CountOptions(request.Request);
            return options == null || options > 1;
        }

        /// <summary>The number of choices the request offered (Back included), or null when it is not a pick
        /// from a list.</summary>
        public static int? CountOptions(RequestEvent request)
        {
            if (RecordedJson.TryParse(request.RequestJson) is not JObject root)
            {
                return null;
            }

            JArray? validOptions = RecordedJson.AsArray(root["ValidOptions"]);
            if (validOptions == null)
            {
                return null;
            }

            bool canGoBack = root.Value<bool?>("AllowCancel") == true
                || request.ReplyType.StartsWith(CancellableReplyPrefix, StringComparison.Ordinal);
            return validOptions.Count + (canGoBack ? 1 : 0);
        }
    }

    /// <summary>Reads recorded request / reply JSON as plain tokens - no <c>$type</c> is ever resolved.</summary>
    internal static class RecordedJson
    {
        public static JToken? TryParse(string json)
        {
            try
            {
                using JsonTextReader reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                };
                return JToken.ReadFrom(reader);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>A list as the wire writes it: a bare array, or a <c>{"$type":...,"$values":[...]}</c>
        /// wrapper when the declared type is an interface.</summary>
        public static JArray? AsArray(JToken? token) => token switch
        {
            JArray array => array,
            JObject wrapper when wrapper["$values"] is JArray values => values,
            _ => null,
        };

        /// <summary>A serialized <c>DataReference</c> / <c>DataBinding</c> (<c>{"TypeID":{"ID":n},"Index":i,
        /// "Generation":g}</c>), or null.</summary>
        public static DataReference? AsReference(JToken? token)
        {
            if (token is not JObject reference
                || reference["TypeID"] is not JObject typeId
                || typeId.Value<int?>("ID") is not int id
                || reference.Value<int?>("Index") is not int index
                || reference.Value<int?>("Generation") is not int generation)
            {
                return null;
            }

            return new DataReference { TypeID = new TypeID(id), Index = index, Generation = generation };
        }
    }

    /// <summary>
    /// The index over a <see cref="ReplayDocument"/> (plan 4.3): snapshots, markers, requests joined to their
    /// replies and errors, and the step list. Pure - no store, nothing is applied; <c>ReplayPlayer</c>
    /// does that.
    /// </summary>
    public sealed class ReplayTimeline
    {
        public ReplayDocument Document { get; }

        public ReplayHeader Header => Document.Header;

        public IReadOnlyList<ReplayEvent> Events => Document.Events;

        public IReadOnlyList<SnapshotEvent> Snapshots { get; }

        public IReadOnlyList<MarkerEvent> Markers { get; }

        /// <summary>Every request, in the order it was made.</summary>
        public IReadOnlyList<ReplayRequest> Requests { get; }

        public IReadOnlyList<ReplayStep> Steps { get; }

        /// <summary>Folded requests made before the first step (a forced first deployment pick, say).</summary>
        public IReadOnlyList<ReplayRequest> FoldedBeforeFirstStep { get; }

        private readonly Dictionary<TaskID, ReplayRequest> _requestsByTask = new Dictionary<TaskID, ReplayRequest>();

        public ReplayTimeline(ReplayDocument document, IStepFilter? filter = null)
        {
            Document = document;
            filter ??= DefaultStepFilter.Instance;

            var snapshots = new List<SnapshotEvent>();
            var markers = new List<MarkerEvent>();
            var requests = new List<ReplayRequest>();

            int? round = null;
            int? activation = null;
            DataReference? activatingUnit = null;
            long previousSeq = long.MinValue;

            foreach (ReplayEvent replayEvent in document.Events)
            {
                if (replayEvent.Seq <= previousSeq)
                {
                    throw new ReplayFormatException(
                        $"Replay events are out of order: Seq {replayEvent.Seq} follows {previousSeq}.");
                }
                previousSeq = replayEvent.Seq;

                switch (replayEvent)
                {
                    case SnapshotEvent snapshot:
                        snapshots.Add(snapshot);
                        break;

                    case MarkerEvent marker:
                        markers.Add(marker);
                        switch (marker.Marker)
                        {
                            case EReplayMarker.GameStart:
                            case EReplayMarker.RoundStart:
                            case EReplayMarker.Resumed:
                                round = marker.Round ?? round;
                                activation = null;
                                activatingUnit = null;
                                break;
                            case EReplayMarker.ActivationStart:
                                // A repeat (same round and activation) names the unit; it is not a new activation.
                                round = marker.Round ?? round;
                                activation = marker.Activation;
                                activatingUnit = marker.UnitRef;
                                break;
                        }
                        break;

                    case RequestEvent request:
                        if (_requestsByTask.ContainsKey(request.TaskId))
                        {
                            throw new ReplayFormatException($"Replay requests task {request.TaskId.ID} twice (Seq {request.Seq}).");
                        }
                        var joined = new ReplayRequest(request, round, activation, activatingUnit);
                        _requestsByTask.Add(request.TaskId, joined);
                        requests.Add(joined);
                        break;

                    // A reply or error for a task the recording never saw requested (it started mid-request)
                    // has nothing to join to and is ignored; a second one for the same task keeps the first.
                    case ReplyEvent reply:
                        if (_requestsByTask.TryGetValue(reply.TaskId, out ReplayRequest? answered)
                            && answered.Reply == null && answered.Error == null)
                        {
                            answered.Reply = reply;
                        }
                        break;

                    case RequestErrorEvent error:
                        if (_requestsByTask.TryGetValue(error.TaskId, out ReplayRequest? failed)
                            && failed.Reply == null && failed.Error == null)
                        {
                            failed.Error = error;
                        }
                        break;
                }
            }

            Snapshots = snapshots;
            Markers = markers;
            Requests = requests;

            // Steps need every reply joined first: a reply can land after later requests were made.
            var steps = new List<ReplayStep>();
            var leading = new List<ReplayRequest>();
            List<ReplayRequest>? folded = null;
            ReplayRequest? decision = null;
            foreach (ReplayRequest request in requests)
            {
                if (filter.IsStep(request) == false)
                {
                    (folded ?? leading).Add(request);
                    continue;
                }

                if (decision != null)
                {
                    steps.Add(new ReplayStep(steps.Count, decision, folded!, ControllerOf(decision)));
                }
                decision = request;
                folded = new List<ReplayRequest>();
            }
            if (decision != null)
            {
                steps.Add(new ReplayStep(steps.Count, decision, folded!, ControllerOf(decision)));
            }

            Steps = steps;
            FoldedBeforeFirstStep = leading;
        }

        public ReplayRequest? FindRequest(TaskID taskId) =>
            _requestsByTask.TryGetValue(taskId, out ReplayRequest? request) ? request : null;

        /// <summary>The position in <see cref="Events"/> of the event with <paramref name="seq"/>, or of the
        /// first event after it when none has exactly that Seq (Seq is ascending, not required dense).</summary>
        public int IndexOfSeq(long seq)
        {
            int low = 0;
            int high = Events.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (Events[middle].Seq < seq) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        /// <summary>
        /// The snapshot to rebuild from to reach <paramref name="seq"/> (plan 4.3): the last one at or before it,
        /// provided no <c>Resumed</c> marker lies between them - a resume replaces state, so a snapshot from
        /// before it cannot reach anything after it. Null when nothing qualifies (a recording cut short before
        /// its first snapshot, or a resume whose snapshot is missing).
        /// </summary>
        public SnapshotEvent? SnapshotFor(long seq)
        {
            SnapshotEvent? candidate = null;
            foreach (SnapshotEvent snapshot in Snapshots)
            {
                if (snapshot.Seq > seq) break;
                candidate = snapshot;
            }

            if (candidate == null)
            {
                return null;
            }

            foreach (MarkerEvent marker in Markers)
            {
                if (marker.Seq > seq) break;
                if (marker.Marker == EReplayMarker.Resumed && marker.Seq > candidate.Seq)
                {
                    return null;
                }
            }

            return candidate;
        }

        private EReplayController? ControllerOf(ReplayRequest request)
        {
            foreach (ReplaySlot slot in Header.Slots)
            {
                if (slot.PlayerId == request.Request.PlayerId)
                {
                    return slot.Controller;
                }
            }
            return null;
        }
    }
}
