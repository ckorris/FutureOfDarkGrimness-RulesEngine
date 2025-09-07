namespace FDG.Network.Messages
{
    public class LobbyChatMessage_FromClient
    {
        public string SendingPlayerName;

        public string Message;

        public LobbyChatMessage_FromClient(string sendingPlayerName, string message)
        {
            SendingPlayerName = sendingPlayerName;
            Message = message;
        }
    }
}
