using FDG.Data;
using FDG.MessageBus;
using FDG.Network;
using FDG.Network.Messages;
using FDG.Network.Messages.DataMessages;
using FDG.Network.Messages.StageRequestMessages;
using FDG.Presentation;
using FDG.Presentation.Messages;
using FDG.StageResolution;
using Newtonsoft.Json;

namespace FDG.Replay
{
    /// <summary>
    /// A remote client's replay recorder (#451, plan 4.2 "Client"). The host's <see cref="ReplayRecorder"/> watches
    /// the engine; a client has no engine, so this one watches what the host sends it - which is the whole game -
    /// and records it, in arrival order, into the same container:
    /// <list type="bullet">
    /// <item>store deltas -> <c>Store*</c> events (the host's JSON strings, verbatim);</item>
    /// <item>the join-time full sync -> <c>Snapshot</c> 0, a save of this client's store once the sync is applied
    /// (one snapshot shape, whatever its source); each of the host's snapshot cues (every <c>RoundStart</c>) ->
    /// another save of this store under the host's index - the store is a faithful mirror (see
    /// <see cref="SaveLoad.StoreReplay"/>), so it holds the same state the host saved at that point;</item>
    /// <item>this player's requests on arrival, everyone's decisions from the resolved notice (D2): a request
    /// not seen before is recorded from the notice, then its reply or error;</item>
    /// <item>beats, log lines and the host's markers. <c>GameEnded</c> closes the recording, as on the host.</item>
    /// </list>
    /// <para>
    /// <b>Header.</b> The host's own header rides the launch message (<see cref="LaunchGameMessage.Replay"/>), so
    /// the game ID, seed and settings are known from the first event. Slots are relabelled from this client's
    /// point of view: its own is <see cref="EReplayController.Human"/>, every other <see cref="EReplayController.Remote"/>
    /// - whether a person or the host's AI answered it, it was answered somewhere else.
    /// </para>
    /// <para>
    /// <b>Order.</b> Every message arrives on the client's one network read thread, in the host's send order per
    /// connection (#086's write lock). Another player's request is recorded when its resolved notice arrives, i.e.
    /// next to its reply rather than where the host sent it; deltas made while that decision was open (rare - two
    /// decisions in flight at once, as in army setup) land before it here and after it on the host.
    /// </para>
    /// <para>
    /// Same rules as the host recorder: invisible to the game, never throws into bus dispatch (the first failure is
    /// logged once and stops the recording), what was recorded stays exportable after <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    public sealed class ClientReplayRecorder : IReplaySource, IDisposable
    {
        private readonly ReplayRecorder _recording = new ReplayRecorder();
        private readonly IMessageBusClient _bus;
        private readonly GameDataStore? _store;
        private readonly JsonSerializerSettings _beatSettings;

        // Decision bookkeeping. Under _lock, which also covers the append, so a request is always recorded before
        // its reply even when the two arrive on different threads (the in-process loopback test transport).
        private readonly object _lock = new object();
        private readonly Dictionary<TaskID, string> _displayNames = new Dictionary<TaskID, string>();
        private readonly HashSet<TaskID> _requested = new HashSet<TaskID>();
        private readonly HashSet<TaskID> _closed = new HashSet<TaskID>();
        private readonly PlayerID _thisPlayer;
        private bool _joined;
        private bool _disposed;

        /// <param name="store">This client's store - the one the sync writes into. Snapshots need it to be a
        /// <see cref="GameDataStore"/>; any other store records everything but them.</param>
        /// <param name="hostHeader">The host's header from the launch message, or null when the host sent none
        /// (its own recording failed to start): the game ID is then empty and seed, settings and slots unknown.</param>
        internal ClientReplayRecorder(IMessageBusClient bus, IReadableGameDataStore store, PlayerID thisPlayer,
            ReplayHeader? hostHeader, ITextOutput? failureOutput = null)
        {
            _bus = bus;
            _store = store as GameDataStore;
            _thisPlayer = thisPlayer;
            _beatSettings = WireJsonSettings.For(store);
            _recording.Start(ClientHeader(hostHeader, thisPlayer), failureOutput);

            _bus.RegisterForMessageEvent<AddSingleDataMessage>(OnStoreAdd);
            _bus.RegisterForMessageEvent<UpdateSingleDataMessage>(OnStoreUpdate);
            _bus.RegisterForMessageEvent<RemoveSingleDataMessage>(OnStoreRemove);
            _bus.RegisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnAwaiting);
            _bus.RegisterForMessageEvent<StageTaskRequestMessage>(OnOwnRequest);
            _bus.RegisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnResolved);
            _bus.RegisterForMessageEvent<PresentBeatMessage>(OnBeat);
            _bus.RegisterForMessageEvent<LogChatNetworkMessage>(OnLog);
            _bus.RegisterForMessageEvent<ReplayMarkerMessage>(OnMarker);
        }

        public bool IsRecording => _recording.IsRecording;

        public byte[] ExportGzip() => _recording.ExportGzip();

        public ReplaySummary Summary => _recording.Summary;

        /// <summary>The header as recorded (tests).</summary>
        internal ReplayHeader? Header => _recording.Header;

        /// <summary>
        /// The join-time sync has been applied to the store (<c>GameDataUpdateReceiver.OnFullStateApplied</c>):
        /// snapshot 0 - or, should the client ever re-sync, the next one.
        /// </summary>
        internal void OnFullStateApplied() => Guard("join snapshot", () =>
        {
            if (_store == null)
            {
                return;
            }

            lock (_lock)
            {
                _joined = true;
            }

            _recording.Snapshot(_store);
        });

        internal static ReplayHeader ClientHeader(ReplayHeader? host, PlayerID thisPlayer) => new ReplayHeader
        {
            BuildVersion = ReplayFormat.BuildVersion,
            EngineVersion = ReplayFormat.EngineVersion,
            GameId = host?.GameId ?? Guid.Empty,
            Seed = host?.Seed,
            RandomnessType = host?.RandomnessType ?? default,
            Settings = host?.Settings,
            Slots = (host?.Slots ?? new List<ReplaySlot>()).Select(slot => new ReplaySlot
            {
                SlotId = slot.SlotId,
                PlayerId = slot.PlayerId,
                Name = slot.Name,
                ArmyName = slot.ArmyName,
                Team = slot.Team,
                Controller = slot.PlayerId == thisPlayer ? EReplayController.Human : EReplayController.Remote,
            }).ToList(),
            RecordedBy = EReplayRecordedBy.Client,
            RecordedByPlayerId = thisPlayer,
            StartedUtc = DateTime.UtcNow,
        };

        // --- Store ------------------------------------------------------------------------------------

        private void OnStoreAdd(AddSingleDataMessage message) =>
            _recording.RecordStoreAdd(message.DataReference, message.InitialValueAsJson);

        private void OnStoreUpdate(UpdateSingleDataMessage message) =>
            _recording.RecordStoreUpdate(message.DataReference, message.ValueAsJson);

        private void OnStoreRemove(RemoveSingleDataMessage message) =>
            _recording.RecordStoreRemove(message.DataReference);

        // --- Decisions ----------------------------------------------------------------------------------

        // Broadcast just ahead of every request: the only place this client learns its own request's display name.
        private void OnAwaiting(StageTaskNotifyAwaitingMessage message) => Guard("request", () =>
        {
            lock (_lock)
            {
                if (_closed.Contains(message.TaskID) == false)
                {
                    _displayNames[message.TaskID] = message.UserFriendlyTaskName;
                }
            }
        });

        private void OnOwnRequest(StageTaskRequestMessage message) => Guard("request", () =>
        {
            if (message.PlayerID != _thisPlayer)
            {
                return;
            }

            lock (_lock)
            {
                if (_requested.Add(message.TaskID) == false)
                {
                    return;
                }

                _displayNames.TryGetValue(message.TaskID, out string? displayName);
                _recording.OnRequested(message.TaskID, message.PlayerID, message.RequestFullTypeName,
                    message.ReplyFullTypeName, displayName ?? "", message.RequestJson);
            }
        });

        private void OnResolved(StageTaskNotifyResolvedMessage message) => Guard("reply", () =>
        {
            lock (_lock)
            {
                _displayNames.Remove(message.TaskID);
                if (_closed.Add(message.TaskID) == false)
                {
                    return;
                }

                // Every other player's request, and this player's should its request message ever be missed.
                if (_requested.Add(message.TaskID))
                {
                    _recording.OnRequested(message.TaskID, message.PlayerID, message.RequestFullTypeName,
                        message.ReplyFullTypeName, message.DisplayName, message.RequestJson);
                }

                if (message.ReplyJson != null)
                {
                    _recording.OnReplied(message.TaskID, message.ReplyJson);
                }
                else
                {
                    _recording.OnFailed(message.TaskID, message.ErrorMessage ?? "(no reply)");
                }
            }
        });

        // --- Beats, log, markers ------------------------------------------------------------------------

        // The bus already turned the wire JSON into a beat; serialize it back the way the host recorder does
        // (wire settings, declared as the base type so the root carries its $type).
        private void OnBeat(PresentBeatMessage message) => Guard("beat", () =>
        {
            if (IsRecording)
            {
                _recording.RecordBeatJson(JsonConvert.SerializeObject(message.Beat, typeof(PresentationBeat), _beatSettings));
            }
        });

        private void OnLog(LogChatNetworkMessage message) => _recording.RecordLog(message.LogMessage, message.IsDebug);

        private void OnMarker(ReplayMarkerMessage message) => Guard("marker", () =>
        {
            if (message.SnapshotIndex is int index)
            {
                // A cue before the join snapshot has nothing to snapshot yet (the sync is what fills the store).
                bool joined;
                lock (_lock)
                {
                    joined = _joined;
                }

                if (joined && _store != null)
                {
                    _recording.Snapshot(_store, index);
                }

                return;
            }

            MarkerEvent? marker = message.ToMarkerEvent();
            if (marker == null)
            {
                return;
            }

            _recording.RecordMarker(marker);
            if (marker.Marker == EReplayMarker.GameEnded)
            {
                // The host's end of the game ends this recording too; anything after it belongs to no step.
                Dispose();
            }
        });

        /// <summary>Stops recording and leaves the bus. What was recorded stays exportable.</summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _bus.DeregisterForMessageEvent<AddSingleDataMessage>(OnStoreAdd);
            _bus.DeregisterForMessageEvent<UpdateSingleDataMessage>(OnStoreUpdate);
            _bus.DeregisterForMessageEvent<RemoveSingleDataMessage>(OnStoreRemove);
            _bus.DeregisterForMessageEvent<StageTaskNotifyAwaitingMessage>(OnAwaiting);
            _bus.DeregisterForMessageEvent<StageTaskRequestMessage>(OnOwnRequest);
            _bus.DeregisterForMessageEvent<StageTaskNotifyResolvedMessage>(OnResolved);
            _bus.DeregisterForMessageEvent<PresentBeatMessage>(OnBeat);
            _bus.DeregisterForMessageEvent<LogChatNetworkMessage>(OnLog);
            _bus.DeregisterForMessageEvent<ReplayMarkerMessage>(OnMarker);
            _recording.Dispose();
        }

        // Nothing here may throw into bus dispatch, where an exception would tear down the connection. The inner
        // recorder already swallows its own failures; this catches the rest (a beat that will not serialize).
        private void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                _recording.Fail(what, exception);
            }
        }
    }
}
