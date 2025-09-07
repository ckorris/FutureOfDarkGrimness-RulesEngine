using FutureOfDarkGrimness.TextInterface;

namespace FDG.TextInterface
{
    /// <summary>
    /// Simple wrapper for a player text relayer that logs messages to all players,
    /// and also logs it as a debug line.
    /// </summary>
    public class PlayerLogSender : ITextOutput
    {
        private IPlayerTextRelayer _playerTextRelayer;

        /// <summary>
        /// Constructor.
        /// </summary><remarks>
        /// This was written with the intention of the relayer to be <see cref="FDG.Players.PlayerSlotManager"/>.
        /// </remarks>
        /// <param name="relayer"></param>
        public PlayerLogSender(IPlayerTextRelayer relayer)
        {
            _playerTextRelayer = relayer;
        }

        public void Log(string message)
        {
            _playerTextRelayer.SendLogMessageToAll(message);

            System.Diagnostics.Debug.WriteLine($"LOG: {message}");
        }
    }
}
