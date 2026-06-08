using System.Threading;
using System.Threading.Tasks;
using FDG.Players;

namespace FDG.Presentation
{
    /// <summary>
    /// Server-side <see cref="IPresenter"/> assigned to the game context: fans each beat out to
    /// every player's <see cref="IPresentationSink"/> (local players render in-process; remote
    /// players receive a <c>PresentBeatMessage</c>), then awaits the beat's scaled nominal
    /// duration <em>once</em> on the host clock. The single host-side wait is what gives every
    /// front-end a shared tempo and what keeps remote clients from building a backlog. The
    /// presentation analogue of <c>TempVisualRelayer</c>.
    /// </summary>
    internal class PresentationRelayer : IPresenter
    {
        private readonly PlayerSlotManager _playerSlotManager;
        private readonly IPresentationClock _clock;

        public PresentationRelayer(PlayerSlotManager playerSlotManager, IPresentationClock clock)
        {
            _playerSlotManager = playerSlotManager;
            _clock = clock;
        }

        public Task Present(PresentationBeat beat, CancellationToken ct = default)
        {
            for (int i = 0; i < _playerSlotManager._playerSlots.Length; i++)
            {
                IPlayerController? controller = _playerSlotManager._playerSlots[i].Controller;
                controller?.PresentationSink?.OnBeat(beat);
            }

            return _clock.Wait(beat.NominalDuration, ct);
        }
    }
}
