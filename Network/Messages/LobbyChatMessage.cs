
namespace FDG.Network.Messages
{
    public class LobbyChatMessage
    {
        public string SendingPlayerName;

        public string Message;

        public LobbyChatMessage(string sendingPlayerName, string message)
        {
            SendingPlayerName = sendingPlayerName;
            Message = message;
        }
    }
}
