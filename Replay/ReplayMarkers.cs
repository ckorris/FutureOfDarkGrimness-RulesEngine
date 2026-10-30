using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Messages;

namespace FDG.Replay
{
    /// <summary>
    /// Where the state machine reports its flow boundaries for the replay (#450, plan 4.2):
    /// <c>IGameContext.ReplayMarkers</c>. A no-op (<see cref="NullReplayMarkerSink"/>) unless the launch
    /// path gave <c>FDGServer</c> a recorder. Implementations never throw and return quickly - every call
    /// is on the engine thread, in the middle of a stage.
    /// </summary>
    public interface IReplayMarkerSink
    {
        /// <summary>A main-phase round begins (<c>ReconcileNewRoundStage</c>, before the round banner and
        /// start-of-round arrivals).</summary>
        void RoundStart(int round);

        /// <summary>The activation boundary: the acting player is known, the unit is not
        /// (<c>DeterminePlayerTurnStage</c>, right after the #052 rolling snapshot).</summary>
        void ActivationStart(int round, PlayerID player);

        /// <summary>The activating unit was committed (<c>MainUnitActionStage.Enter</c>; again after a #248
        /// back-out re-pick).</summary>
        void ActivationUnitChosen(DataReference unit);
    }

    /// <summary>The default sink: records nothing.</summary>
    public sealed class NullReplayMarkerSink : IReplayMarkerSink
    {
        public static readonly NullReplayMarkerSink Instance = new NullReplayMarkerSink();

        private NullReplayMarkerSink() { }

        public void RoundStart(int round) { }

        public void ActivationStart(int round, PlayerID player) { }

        public void ActivationUnitChosen(DataReference unit) { }
    }

    /// <summary>
    /// <c>FDGServer</c>'s marker sink: turns boundaries into <see cref="MarkerEvent"/>s (numbering the
    /// activations), takes the snapshots that follow GameStart and RoundStart, and broadcasts every marker
    /// and snapshot cue to clients as a <see cref="ReplayMarkerMessage"/> so their recordings (#451) carry
    /// the same markers. The broadcast continues even if the host recorder has disabled itself.
    /// </summary>
    internal sealed class ReplayMarkerRelay : IReplayMarkerSink
    {
        private readonly object _lock = new object();
        private readonly ReplayRecorder _recorder;
        private readonly IMessageBusHost _bus;
        private readonly GameDataStore? _store;
        private readonly ITextOutput _failureOutput;

        private int? _round;
        private int _activation;
        private PlayerID? _activePlayer;
        private bool _ended;
        private bool _failureLogged;

        public ReplayMarkerRelay(ReplayRecorder recorder, IMessageBusHost bus, GameDataStore? store, ITextOutput failureOutput)
        {
            _recorder = recorder;
            _bus = bus;
            _store = store;
            _failureOutput = failureOutput;
        }

        /// <summary>The recording begins: <paramref name="round"/> is null for a fresh game (setup has not
        /// run yet) or the round a resumed game re-enters. Followed by snapshot 0.</summary>
        public void GameStart(int? round) => Guard(() =>
        {
            lock (_lock)
            {
                _round = round;
                _activation = 0;
                _activePlayer = null;
            }

            Emit(new MarkerEvent(EReplayMarker.GameStart) { Round = round });
            TakeSnapshot();
        });

        public void RoundStart(int round) => Guard(() =>
        {
            lock (_lock)
            {
                _round = round;
                _activation = 0;
                _activePlayer = null;
            }

            Emit(new MarkerEvent(EReplayMarker.RoundStart) { Round = round });
            TakeSnapshot();
        });

        public void ActivationStart(int round, PlayerID player) => Guard(() =>
        {
            MarkerEvent marker;
            lock (_lock)
            {
                if (_round != round)
                {
                    // A resumed game's first round has no RoundStart of its own; adopt it here.
                    _round = round;
                    _activation = 0;
                }

                _activation++;
                _activePlayer = player;
                marker = new MarkerEvent(EReplayMarker.ActivationStart)
                {
                    Round = round,
                    Activation = _activation,
                    PlayerId = player,
                };
            }

            Emit(marker);
        });

        public void ActivationUnitChosen(DataReference unit) => Guard(() =>
        {
            MarkerEvent marker;
            lock (_lock)
            {
                // The repeat of the open activation, now naming its unit (see EReplayMarker.ActivationStart).
                marker = new MarkerEvent(EReplayMarker.ActivationStart)
                {
                    Round = _round,
                    Activation = _activation == 0 ? null : _activation,
                    PlayerId = _activePlayer,
                    UnitRef = unit,
                };
            }

            Emit(marker);
        });

        /// <summary>Records the game's end, exactly once however many paths report it.</summary>
        public void GameEnded(GameResult result) => Guard(() =>
        {
            lock (_lock)
            {
                if (_ended)
                {
                    return;
                }

                _ended = true;
            }

            Emit(new MarkerEvent(EReplayMarker.GameEnded)
            {
                Outcome = result.Outcome.ToString(),
                Message = result.Message,
            });
        });

        private void Emit(MarkerEvent marker)
        {
            _recorder.RecordMarker(marker);
            _ = _bus.SendCommandToAllAsync(ReplayMarkerMessage.For(marker));
        }

        private void TakeSnapshot()
        {
            if (_store == null)
            {
                return;
            }

            int index = _recorder.Snapshot(_store);
            _ = _bus.SendCommandToAllAsync(ReplayMarkerMessage.ForSnapshot(index));
        }

        // Nothing here may throw into a stage. The recorder already swallows its own failures; this catches
        // the rest (a bus send that throws synchronously) and says so once.
        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                bool first;
                lock (_lock)
                {
                    first = _failureLogged == false;
                    _failureLogged = true;
                }

                if (first)
                {
                    try
                    {
                        _failureOutput.Log($"[replay] A replay marker could not be sent ({exception.GetType().Name}).");
                    }
                    catch
                    {
                        // Best effort.
                    }
                }
            }
        }
    }
}
