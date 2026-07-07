using FDG.TextInterface;

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

        public void Log(string message, TextColor? color = null)
        {
            // Resolve the default once here; everything downstream carries a concrete TextColor.
            _playerTextRelayer.SendLogMessageToAll(message, color ?? TextColor.White);

            System.Diagnostics.Debug.WriteLine($"LOG: {message}");
        }

        // Debug-category line: same relay path, flagged so the front end can route it to its Debug view.
        public void LogDebug(string message, TextColor? color = null)
        {
            _playerTextRelayer.SendLogMessageToAll(message, color ?? TextColor.White, isDebug: true);

            System.Diagnostics.Debug.WriteLine($"DEBUG: {message}");
        }
    }
}
