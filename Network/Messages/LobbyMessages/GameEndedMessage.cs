namespace FDG.Network.Messages
{
    /// <summary>
    /// Broadcast host → clients when the game finishes, carrying the result string
    /// (e.g. "Player X wins!" / "It's a tie!"). Lets a non-host client return to the menu.
    ///
    /// The host does not handle this message — it learns of game-end directly from
    /// <see cref="GameModel.FDGServer.OnGameEnded"/> and forwards that to its own front end; the wire
    /// message exists purely so remote clients (which have no FDGServer) get the same signal.
    /// </summary>
    internal class GameEndedMessage
    {
        public string Result { get; set; } = "";

        public GameEndedMessage() { }

        public GameEndedMessage(string result)
        {
            Result = result;
        }
    }
}
