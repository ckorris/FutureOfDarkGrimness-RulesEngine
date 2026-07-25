using FDG.MessageBus;
using FDG.Network.Messages.StagePreviewMessages;
using FDG.Network.Messages.StageRequestMessages;

namespace FDG.StageResolution.Previews
{
    /// <summary>
    /// Default <see cref="IPreviewFeed"/>: folds the preview broadcast stream into a lock-guarded
    /// latest-wins map keyed (player, slot). Handlers run on bus/network read threads; the drawer
    /// reads on the main thread - everything shares one lock, same discipline as the GUI resolvers'
    /// request state.
    ///
    /// <para>
    /// Besides the publisher's explicit clear, previews expire when their player's LAST outstanding
    /// request resolves (tracked via the same notify-awaiting/resolved broadcasts that drive
    /// <see cref="OutstandingTaskLister"/>). That safety net is what cleans up after a publisher
    /// that can never send its clear - a crashed or disconnected client - because the disconnect
    /// path already broadcasts a resolved notification for each failed task (#076). Scoped to the
    /// last task so a concurrent second request's preview isn't wiped mid-stream.
    /// </para>
    /// </summary>
    public class PreviewFeed : IPreviewFeed, IDisposable
    {
        private readonly object _lock = new object();

        private readonly IMessageBusClient _messageBusClient;

        // Live reference, not a snapshot: the host game's local-player list is populated after
        // construction (AddLocalPlayerID), and previews only flow post-launch, well after both.
        private readonly IReadOnlyList<PlayerID> _localPlayerIDs;

        private readonly Dictionary<PlayerID, Dictionary<string, PreviewEntry>> _previewsByPlayer
            = new Dictionary<PlayerID, Dictionary<string, PreviewEntry>>();

        // TaskID -> owning player, mirroring OutstandingTaskLister, for the last-task expiry above.
        private readonly Dictionary<TaskID, PlayerID> _outstandingTaskOwners
            = new Dictionary<TaskID, PlayerID>();

        private int _version;

        public int Version { get { lock (_lock) return _version; } }

        public PreviewFeed(IMessageBusClient messageBusClient, IReadOnlyList<PlayerID> localPlayerIDs)
        {
            _messageBusClient = messageBusClient;
            _localPlayerIDs = localPlayerIDs;

            _messageBusClient.RegisterForMessageEvent<StagePreviewMessage>(OnPreviewUpdate);
            _messageBusClient.RegisterForMessageEvent<StagePreviewClearMessage>(OnPreviewClear);
            _messageBusClient.RegisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnTaskAwaiting);
            _messageBusClient.RegisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnTaskResolved);
        }

        public IReadOnlyList<PreviewEntry> GetSnapshot()
        {
            lock (_lock)
            {
                List<PreviewEntry> snapshot = new List<PreviewEntry>();
                foreach (Dictionary<string, PreviewEntry> slots in _previewsByPlayer.Values)
                {
                    snapshot.AddRange(slots.Values);
                }
                return snapshot;
            }
        }

        private void OnPreviewUpdate(StagePreviewMessage message)
        {
            // A local player's preview renders live through their own resolver overlay; the
            // broadcast loopback (SendCommandToAllAsync also dispatches in-process) must not
            // double-draw it. In single-machine play everyone is local, so the feed stays empty.
            if (_localPlayerIDs.Contains(message.SourcePlayerID))
            {
                return;
            }

            // Wire strings can be null despite the record's annotations; drop malformed quietly
            // (cosmetic channel - the next update supersedes anyway).
            if (message.Slot == null || message.PreviewTypeName == null || message.PreviewJson == null)
            {
                return;
            }

            lock (_lock)
            {
                if (_previewsByPlayer.TryGetValue(message.SourcePlayerID, out Dictionary<string, PreviewEntry>? slots) == false)
                {
                    slots = new Dictionary<string, PreviewEntry>();
                    _previewsByPlayer[message.SourcePlayerID] = slots;
                }

                slots[message.Slot] = new PreviewEntry(message.SourcePlayerID, message.Slot,
                    message.PreviewTypeName, message.PreviewJson);
                _version++;
            }
        }

        private void OnPreviewClear(StagePreviewClearMessage message)
        {
            lock (_lock)
            {
                if (_previewsByPlayer.Remove(message.SourcePlayerID))
                {
                    _version++;
                }
            }
        }

        private void OnTaskAwaiting(StageTaskNotifyAwaitingMessage message)
        {
            lock (_lock)
            {
                _outstandingTaskOwners[message.TaskID] = message.PlayerInfo.PlayerID;
            }
        }

        private void OnTaskResolved(StageTaskNotifyResolvedMessage message)
        {
            lock (_lock)
            {
                if (_outstandingTaskOwners.Remove(message.TaskID, out PlayerID owner) == false)
                {
                    return;
                }

                if (_outstandingTaskOwners.ContainsValue(owner))
                {
                    return; // The player still has another request pending - keep their preview.
                }

                if (_previewsByPlayer.Remove(owner))
                {
                    _version++;
                }
            }
        }

        public void Dispose()
        {
            _messageBusClient.DeregisterForMessageEvent<StagePreviewMessage>(OnPreviewUpdate);
            _messageBusClient.DeregisterForMessageEvent<StagePreviewClearMessage>(OnPreviewClear);
            _messageBusClient.DeregisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnTaskAwaiting);
            _messageBusClient.DeregisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnTaskResolved);
        }
    }
}
